// KingdomDemo (partial) — input: map clicks, selection, march orders and the test hooks.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------------ input
        // All clicks are handled in OnGUI via Event.current, which works regardless of
        // the project's Active Input Handling setting (TBTK's legacy-input Update loops
        // are broken until the editor restarts; IMGUI is not).

        void HandleClicks()
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            // a concept tab is modal over the map, exactly like the court report
            if (!ready || reportShowing || handoverShowing || activeTab != "map") return;
            if (PointerOverUI(e.mousePosition)) return;

            if (e.button == 1) { ClearArmyMoveTargets(); selCity = null; selArmy = null; e.Use(); return; }
            if (e.button != 0) return;

            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var node = ResolveClickedNode(ray);
            if (node == null)
            {
                locationChoiceOpen = false;
                ClearArmyMoveTargets();
                selCity = null;
                selArmy = null;
                e.Use();
                return;
            }

            var city = engine.State.CityAt(node.idxX, node.idxZ);
            var army = engine.State.ArmiesOf(currentSeat).Find(a => a.X == node.idxX && a.Z == node.idxZ);
            bool citySelectable = city != null && (city.OwnerSeatId == currentSeat
                || (obs != null && obs.FindCity(city.Id) != null));

            if (locationChoiceOpen && node.idxX == locationChoiceX && node.idxZ == locationChoiceZ)
            {
                locationChoiceOpen = false;
                e.Use();
                return;
            }

            locationChoiceOpen = false;

            if (selCity != null && city == null && army == null)
            {
                selCity = null;
                e.Use();
                return;
            }

            // clicking the selected army again drops it (works on a city hex too)
            if (army != null && selArmy != null && army.Id == selArmy.Id)
            {
                selArmy = null;
                ClearArmyMoveTargets();
                e.Use();
                return;
            }

            // An army is selected: the next click is a march order, on whatever hex it lands — an empty
            // hex, a neutral city (capture) or our own city (return home). This MUST be decided before
            // the city/army popup below, or a click aimed at a city hex opens the popup instead and the
            // march never happens (the demo's headline move — taking 南城 by marching onto it — was
            // impossible, and so was marching home).
            if (selArmy != null && army == null)
            {
                locationChoiceOpen = false;
                TryMarch(selArmy, node.idxX, node.idxZ);
                e.Use();
                return;
            }

            if (army != null && citySelectable)
            {
                locationChoiceOpen = true;
                locationChoiceX = node.idxX;
                locationChoiceZ = node.idxZ;
                selCity = null;
                selArmy = null;
                ClearArmyMoveTargets();
                e.Use();
                return;
            }

            if (army != null)
            {
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(TBTK.GridManager.GetNode(army.X, army.Z));
                ShowArmyMoveTargets(army);
                statusMsg = "Selected " + army.Name + "（" + UnitTypes.Label(army.UnitType) + "） — choose a destination";
                e.Use();
                return;
            }
            if (city != null && city.OwnerSeatId == currentSeat)
            {
                ClearArmyMoveTargets();
                if (selCity != null && selCity.Id == city.Id)
                {
                    selCity = null;
                    e.Use();
                    return;
                }
                selCity = city; selArmy = null; e.Use(); return;
            }
            // a foreign city can only be selected while the current seat can actually see it (fog)
            if (city != null && obs != null && obs.FindCity(city.Id) != null)
            {
                ClearArmyMoveTargets();
                if (selCity != null && selCity.Id == city.Id)
                {
                    selCity = null;
                    e.Use();
                    return;
                }
                selCity = city; selArmy = null;
                statusMsg = city.IsNeutral ? "Neutral city — send an army to occupy it" : "Enemy city";
                e.Use();
                return;
            }

            e.Use();
        }

        TBTK.Node ResolveClickedNode(Ray ray)
        {
            int nodeLayerMask = 1 << TBTK.TBTK.GetLayerNode();
            if (Physics.Raycast(ray, out var nodeHit, 500f, nodeLayerMask))
                return TBTK.GridManager.GetNode(nodeHit.point, nodeHit.collider.gameObject);

            if (Physics.Raycast(ray, out var hit, 500f))
                return TBTK.GridManager.GetNode(hit.point, null);

            return null;
        }

        void ShowArmyMoveTargets(ArmyState army)
        {
            var targets = new List<TBTK.Node>();
            // the highlight and the validator must agree on the season's budget, or 冬 (floor(6 × 0.7) = 4)
            // highlights hexes the engine then refuses (P6 §8b; ACTIONS.md §2)
            int budget = BalanceConfig.MarchBudget(engine.State.Season, army.UnitType);
            if (!army.MarchedThisSeason)
                for (int x = 0; x < engine.State.Map.Width; x++)
                    for (int z = 0; z < engine.State.Map.Height; z++)
                    {
                        if (x == army.X && z == army.Z) continue;
                        if (!engine.State.Map.IsWalkable(x, z)) continue;

                        // the highlight follows the seat's observation: units it cannot see do not
                        // block a destination it can click (the engine still has the last word)
                        var blocker = engine.State.ArmyAt(x, z);
                        if (blocker != null && blocker.OwnerSeatId != currentSeat
                            && (obs == null || obs.FindArmy(blocker.Id) == null)) blocker = null;
                        if (blocker != null) continue;

                        var city = engine.State.CityAt(x, z);
                        if (city != null && !city.IsNeutral && city.OwnerSeatId != currentSeat
                            && obs != null && obs.FindCity(city.Id) != null) continue;
                        if (engine.State.Map.PathCost(army.X, army.Z, x, z, budget) < 0) continue;

                        var node = TBTK.GridManager.GetNode(x, z);
                        if (node != null) targets.Add(node);
                    }

            TBTK.GridIndicator.ShowMovable(targets);
        }

        void ClearArmyMoveTargets()
        {
            TBTK.GridIndicator.HideAll();
        }

        bool PointerOverUI(Vector2 m)
        {
            float s = Screen.width / 1600f;
            foreach (var r in uiRects)
                if (new Rect(r.x * s, r.y * s, r.width * s, r.height * s).Contains(m)) return true;
            return false;
        }

        void TryMarch(ArmyState army, int x, int z)
            => SubmitHuman(new ActionCommand { Type = ActionType.March, TargetId = army.Id, ParamA = x, ParamB = z });

        // ---- test hooks -------------------------------------------------------
        // The smoke-test harness drives the running game through Unity's SendMessage, which can only
        // reach public methods with at most one argument. These two hooks exist only for that: they do
        // the same things a mouse would, nothing more.

        /// <summary>
        /// Test hook. Selects <c>"city:&lt;id&gt;"</c> or <c>"army:&lt;id&gt;"</c> exactly as a map
        /// click would, fog included: anything the current seat cannot see is refused, and a foreign
        /// army cannot be selected (only its own panel exists).
        /// </summary>
        public void DebugSelect(string kindAndId)
        {
            if (!ready || string.IsNullOrEmpty(kindAndId)) return;

            int split = kindAndId.IndexOf(':');
            if (split <= 0) return;
            string kind = kindAndId.Substring(0, split);
            string id = kindAndId.Substring(split + 1);

            if (kind == "city")
            {
                var city = obs != null ? obs.FindCity(id) : null;          // fog rules apply
                if (city == null)
                {
                    statusMsg = $"DebugSelect: city '{id}' is not visible to {currentSeat}";
                    return;
                }
                ClearArmyMoveTargets();
                selCity = city;
                selArmy = null;
                statusMsg = city.OwnerSeatId == currentSeat ? "Selected " + city.Name
                    : city.IsNeutral ? "Neutral city — send an army to occupy it" : "Enemy city";
                return;
            }

            if (kind == "army")
            {
                var army = obs != null ? obs.FindArmy(id) : null;
                if (army == null)
                {
                    statusMsg = $"DebugSelect: army '{id}' is not visible to {currentSeat}";
                    return;
                }
                if (army.OwnerSeatId != currentSeat)
                {
                    statusMsg = $"DebugSelect: '{id}' is not {currentSeat}'s army";
                    return;
                }
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(TBTK.GridManager.GetNode(army.X, army.Z));
                ShowArmyMoveTargets(army);
                statusMsg = "Selected " + army.Name + "（" + UnitTypes.Label(army.UnitType) + "） — choose a destination";
            }
        }

        /// <summary>
        /// Test hook. Submits <c>"&lt;action_type&gt;|&lt;target_id&gt;"</c>, with an optional third
        /// segment <c>"x,z"</c> for the hex-parameterised actions (<c>march</c>, <c>attack_army</c>,
        /// <c>attack_city</c>), e.g. <c>"march|army-wei-1|4,3"</c>. Goes through the same
        /// <see cref="SubmitHuman"/> path as the buttons, so the engine validates it identically.
        /// </summary>
        public void DebugOrder(string spec)
        {
            if (!ready || string.IsNullOrEmpty(spec)) return;

            string[] parts = spec.Split('|');
            if (parts.Length < 2 || string.IsNullOrEmpty(parts[0])) return;

            var cmd = new ActionCommand
            {
                Type = parts[0].Trim(),
                TargetId = parts[1].Trim(),
                Reason = "debug hook",
            };
            if (parts.Length > 2)
            {
                string[] coords = parts[2].Split(',');
                if (coords.Length == 2
                    && int.TryParse(coords[0], out int x) && int.TryParse(coords[1], out int z))
                {
                    cmd.ParamA = x;
                    cmd.ParamB = z;
                }
            }
            SubmitHuman(cmd);
        }
    }
}
