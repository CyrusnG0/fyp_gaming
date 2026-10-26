// KingdomDemo (partial) — city and army order panels.
//
// Both panels are built from the current seat's Observation views only (CityView / ArmyView), and a
// city that is outside the observation is shown as "Unknown city" with no owner and no values. The
// V1 presentation pass keeps all text inside the panel: titles and order labels ellipsize, hints wrap.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        /// <summary>
        /// City details for the current seat. An own city shows everything and offers orders; a foreign or
        /// neutral city shows **only what this seat actually holds** — the observation when the city is in
        /// sight, otherwise a live 偵察 report (bands or exact numbers, P8), otherwise nothing at all. It
        /// never falls back to canonical state, so the panel cannot leak an unseen city's garrison.
        /// </summary>
        void DrawCityPanel(CityView city, GUIStyle body, GUIStyle btn)
        {
            bool mine = city.OwnerSeatId == currentSeat;
            bool conversionOpen = mine && garrisonConversionOpen && garrisonConversionCityId == city.Id && city.Garrison > 0;
            var inSight = obs != null ? obs.FindCity(city.Id) : null;
            var report = obs != null ? obs.FindScoutedCity(city.Id) : null;
            float height = mine ? (conversionOpen ? 520 : 480) : (inSight != null || report != null ? 230 : 120);
            var p = PlaceSelectionPanel(CityPanelWidth, height);
            uiRects.Add(p);
            DrawBackdrop(p, OwnerColor(city.OwnerSeatId));
            // the name and owner are themselves information: an unseen city with no report stays unnamed,
            // even if a debug hook selects it by id
            bool knowsCity = mine || inSight != null || report != null;
            string title = knowsCity ? $"{city.Name} ({OwnerName(city)}) — City values" : "Unknown city";
            GUI.Label(new Rect(p.x + 12f, p.y + 8f, p.width - 20f, 24f),
                EllipsizeToWidth(uiPanelTitle, title, p.width - 24f), uiPanelTitle);

            if (mine)
            {
                string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? " (yield halved)" : "";
                GUI.Label(new Rect(p.x + 12f, p.y + 34f, p.width - 20f, 116f),
                    $"Population {city.Population}\nFood {city.Food}\nGold {city.Gold}\nGarrison {city.Garrison} {UnitTypeName(city.GarrisonType)}\n" +
                    $"Morale {city.Morale}{moraleWarning}\n" +
                    $"Reclaimed {city.ReclaimPct}%  Defense {city.Defense}/{BalanceConfig.DefenseMax}  Training {city.Training}/{MyTrainingCap()}",
                    uiBodyWrap);

                float buttonLeft = p.x + 12f;
                float buttonTop = p.y + 160f;
                float buttonGap = 12f;
                float buttonWidth = (p.width - 24f - buttonGap) * 0.5f;
                float rightButtonLeft = buttonLeft + buttonWidth + buttonGap;
                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop, buttonWidth, 34), ActionType.LevyTax, city.Id, "Tax", null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop, buttonWidth, 34), ActionType.LightenLabor, city.Id, "Lighten labor",
                    city.Gold >= BalanceConfig.LightenLaborGoldCost ? null : "Not enough gold", 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 38f, buttonWidth, 34), ActionType.Reclaim, city.Id, "Reclaim",
                    city.ReclaimPct >= BalanceConfig.ReclaimPctCap ? "Limit reached"
                    : city.ReclaimedThisSeason ? "Already reclaimed this season" : null, 0, 0, btn));
                string recruitType = UnitTypes.Normalize(recruitUnitType);
                int recruitRequired = UnitTypes.RequiredMilitaryLevel(recruitType);
                int recruitCost = UnitTypes.RecruitGoldCost(recruitType, BalanceConfig.RecruitTroopGain);
                string recruitBlocked =
                    MilitaryLevel() < recruitRequired ? $"needs Military {recruitRequired}"
                    : city.Garrison > 0 && UnitTypes.Normalize(city.GarrisonType) != recruitType
                        ? $"garrison is {UnitTypeName(city.GarrisonType)}"
                    : city.Gold < recruitCost ? $"needs {recruitCost} gold"
                    : city.Population < BalanceConfig.RecruitPopCost ? "Not enough population" : null;
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop + 38f, buttonWidth, 34), ActionType.Recruit, city.Id, "Recruit",
                    recruitBlocked, 0, 0, btn,
                    new Dictionary<string, string> { { "unit_type", recruitType } }));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 76f, buttonWidth, 34), ActionType.Train, city.Id, "Train",
                    city.Training >= MyTrainingCap() ? "Limit reached" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop + 76f, buttonWidth, 34), ActionType.Fortify, city.Id, "Fortify",
                    city.Defense >= BalanceConfig.DefenseMax ? "Limit reached" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 114f, buttonWidth, 34), ActionType.Relief, city.Id, "Relief",
                    city.Food < BalanceConfig.ReliefFoodCost ? "Not enough food" : null, 0, 0, btn));
                var convertRect = new Rect(rightButtonLeft, buttonTop + 114f, buttonWidth, 34);
                var armyOnCity = obs != null ? obs.Armies.Find(a => a.X == city.X && a.Z == city.Z) : null;
                bool convertTypeMismatch = armyOnCity != null && armyOnCity.OwnerSeatId == currentSeat
                    && UnitTypes.Normalize(armyOnCity.UnitType) != UnitTypes.Normalize(city.GarrisonType);
                var previousEnabled = GUI.enabled;
                GUI.enabled = !uiInputLocked && city.Garrison > 0 && !convertTypeMismatch;
                if (GUI.Button(convertRect, EllipsizeToWidth(btn,
                        "Convert garrison " + OrderCostSuffix(ActionType.ConvertGarrison), convertRect.width - 12f), btn))
                {
                    garrisonConversionOpen = true;
                    garrisonConversionCityId = city.Id;
                    garrisonConversionAmount = city.Garrison;
                }
                GUI.enabled = previousEnabled;
                if (city.Garrison <= 0) hint = FirstReason(hint, "Convert garrison: No garrison");
                else if (convertTypeMismatch)
                    hint = FirstReason(hint,
                        $"Convert garrison: unit types differ (army is {UnitTypeName(armyOnCity.UnitType)})");

                // 兵種 picker: four toggles, research-gated types greyed with their reason (ADR-019)
                float typeLabelTop = p.y + 314f;
                string recruitLine =
                    $"Recruit type: {UnitTypeName(recruitType)} · {recruitCost} gold, {BalanceConfig.RecruitPopCost} pop";
                GUI.Label(new Rect(p.x + 12f, typeLabelTop, p.width - 20f, 20f),
                    EllipsizeToWidth(body, recruitLine, p.width - 24f), body);
                string gateHint = null;
                int military = MilitaryLevel();
                for (int i = 0; i < UnitTypes.All.Length; i++)
                {
                    string type = UnitTypes.All[i];
                    int required = UnitTypes.RequiredMilitaryLevel(type);
                    bool gated = military < required;
                    var typeRect = new Rect(p.x + 12f + i * 100f, typeLabelTop + 22f, 94f, 26f);
                    var prevBg = GUI.backgroundColor;
                    if (recruitType == type) GUI.backgroundColor = TabSelected;
                    var prevEnabledFlag = GUI.enabled;
                    GUI.enabled = !gated;
                    if (GUI.Button(typeRect, UnitTypeName(type), btn)) recruitUnitType = type;
                    GUI.enabled = prevEnabledFlag;
                    GUI.backgroundColor = prevBg;
                    if (gated)
                        gateHint = gateHint == null
                            ? $"{UnitTypeName(type)} needs Military {required}"
                            : gateHint + " · " + $"{UnitTypeName(type)} needs Military {required}";
                }
                if (gateHint != null)
                    GUI.Label(new Rect(p.x + 12f, typeLabelTop + 50f, p.width - 20f, 20f),
                              EllipsizeToWidth(uiReason, gateHint, p.width - 24f), uiReason);

                if (conversionOpen)
                {
                    string convertLine =
                        $"Convert {garrisonConversionAmount} of {city.Garrison} {UnitTypeName(city.GarrisonType)} garrison to army";
                    GUI.Label(new Rect(p.x + 12f, p.y + 390f, p.width - 20f, 22f),
                        EllipsizeToWidth(body, convertLine, p.width - 24f), body);
                    garrisonConversionAmount = Mathf.Clamp(garrisonConversionAmount, 1, city.Garrison);
                    garrisonConversionAmount = Mathf.RoundToInt(GUI.HorizontalSlider(
                        new Rect(p.x + 12f, p.y + 416f, 250f, 24f), garrisonConversionAmount, 1, city.Garrison));
                    var confirmEnabled = GUI.enabled;
                    GUI.enabled = !uiInputLocked;
                    if (GUI.Button(new Rect(p.x + 272f, p.y + 410f, 136f, 34f),
                                   "Confirm " + OrderCostSuffix(ActionType.ConvertGarrison), btn))
                        SubmitHuman(new ActionCommand
                        {
                            Type = ActionType.ConvertGarrison,
                            TargetId = city.Id,
                            ParamA = garrisonConversionAmount,
                            Reason = "UI: convert_garrison",
                        });
                    GUI.enabled = confirmEnabled;
                }

                if (hint != null)
                    GUI.Label(new Rect(p.x + 12f, p.y + (conversionOpen ? 456f : 390f), p.width - 20f, 40f),
                              hint, uiReasonWrap);
            }
            else
            {
                if (inSight != null)
                {
                    string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? " (yield halved)" : "";
                    GUI.Label(new Rect(p.x + 12f, p.y + 34f, p.width - 20f, 126f),
                        $"{city.Name} — in sight\n" +
                        $"Garrison {city.Garrison} {UnitTypeName(city.GarrisonType)}\nMorale {city.Morale}{moraleWarning}\n" +
                        $"Food {city.Food}  Gold {city.Gold}\n" +
                        $"Population {city.Population}\n" +
                        $"Defense {city.Defense}/{BalanceConfig.DefenseMax}  Training {city.Training}",
                        uiBodyWrap);
                }
                else if (report != null)
                {
                    string detail = report.Accuracy == "exact"
                        ? $"Garrison {report.Garrison}  Food {report.Food}\nMorale {report.Morale}  Gold {report.Gold}\nPopulation {report.Population}"
                        : $"Troops: {report.GarrisonBand}\nFood: {report.FoodBand}";
                    GUI.Label(new Rect(p.x + 12f, p.y + 34f, p.width - 20f, 126f),
                        $"{(city.IsNeutral ? "Neutral city" : "Enemy city")} — scout report ({report.Accuracy})\n" +
                        $"{detail}\nReport valid to season {report.UntilSeason}",
                        uiBodyWrap);
                }
                else
                {
                    GUI.Label(new Rect(p.x + 12f, p.y + 34f, p.width - 20f, 30f), "Outside our sight — nothing is known", body);
                }
            }
        }

        void DrawArmyPanel(ArmyView army, GUIStyle body, GUIStyle btn)
        {
            var enemyArmies = AttackableEnemyArmies(army);
            var enemyCities = AdjacentEnemyCities(army);
            int orders = enemyArmies.Count + enemyCities.Count;
            int attackRange = UnitTypes.AttackRange(army.UnitType);
            var occupiedCity = obs != null ? obs.Cities.Find(c => c.X == army.X && c.Z == army.Z) : null;
            bool canGarrison = occupiedCity != null && occupiedCity.OwnerSeatId == army.OwnerSeatId;
            if (canGarrison && garrisonArmyId != army.Id)
            {
                garrisonArmyId = army.Id;
                garrisonArmyAmount = army.Troops;
                garrisonArmyConversionOpen = false;
            }
            if (canGarrison) garrisonArmyAmount = Mathf.Clamp(garrisonArmyAmount, 1, army.Troops);

            float panelWidth = ArmyPanelWidth;
            float garrisonControlsHeight = garrisonArmyConversionOpen ? 90f : 52f;
            float contentHeight = (orders > 0 ? 236 + orders * 38 : 150) + (canGarrison ? garrisonControlsHeight : 0);
            var outer = PlaceSelectionPanel(panelWidth, contentHeight);
            uiRects.Add(outer);
            DrawBackdrop(outer, OwnerColor(army.OwnerSeatId));
            string title = $"{army.Name} · {UnitTypeName(army.UnitType)} · Troops {army.Troops}";
            GUI.Label(new Rect(outer.x + 12f, outer.y + 8f, outer.width - 20f, 24f),
                EllipsizeToWidth(uiPanelTitle, title, outer.width - 24f), uiPanelTitle);

            // PlaceSelectionPanel caps a panel that shares the side dock. If that cap bites, keep the
            // title fixed and scroll the body instead of drawing it under the side panel/status bar.
            Rect p = outer;
            bool scrolls = contentHeight > outer.height + 0.5f;
            if (scrolls)
            {
                var view = new Rect(outer.x + 2f, outer.y + 34f, outer.width - 4f,
                                    Mathf.Max(24f, outer.height - 34f));
                float bodyHeight = Mathf.Max(1f, contentHeight - 34f);
                TryWheelScroll(outer, view, bodyHeight, ref selectionScroll);
                selectionScroll.y = Mathf.Clamp(selectionScroll.y, 0f,
                                                Mathf.Max(0f, bodyHeight - view.height));
                selectionScroll = GUI.BeginScrollView(view, selectionScroll,
                    new Rect(0f, 0f, outer.width - 18f, bodyHeight), false, false);
                // Cancel the scroll view's offset so the body keeps the normal panel coordinates.
                p = new Rect(outer.x - view.x, outer.y - view.y, outer.width, contentHeight);
            }
            else
            {
                selectionScroll = Vector2.zero;
            }
            int foodPerSeason = (int)(army.Troops * UnitTypes.FoodPerTroop(army.UnitType));
            int factionFood = 0;
            if (obs != null)
                foreach (var city in obs.CitiesOf(army.OwnerSeatId)) factionFood += city.Food;
            GUI.Label(new Rect(p.x + 12f, p.y + 34f, p.width - 20f, 78f),
                $"Position ({army.X}, {army.Z})\nMorale {army.Morale}\n" +
                $"Faction food {factionFood} (army need {foodPerSeason} / season)\n" +
                (army.MarchCountThisSeason >= UnitTypes.MarchesPerSeason(army.UnitType)
                    ? "No marches left this season"
                    : $"Select a hex to march ({BalanceConfig.MarchBudget(engine.State.Season, army.UnitType)} points)"), uiBodyWrap);

            if (canGarrison)
            {
                if (!garrisonArmyConversionOpen)
                {
                    var convertEnabled = GUI.enabled;
                    GUI.enabled = !uiInputLocked;
                    if (GUI.Button(new Rect(p.x + 12f, p.y + 124f, p.width - 24f, 34),
                                   "Convert troops " + OrderCostSuffix(ActionType.GarrisonArmy), btn))
                        garrisonArmyConversionOpen = true;
                    GUI.enabled = convertEnabled;
                }
                else
                {
                    GUI.Label(new Rect(p.x + 12f, p.y + 120f, p.width - 24f, 22f),
                        $"Garrison troops: {garrisonArmyAmount}/{army.Troops}", body);
                    garrisonArmyAmount = Mathf.RoundToInt(GUI.HorizontalSlider(
                        new Rect(p.x + 12f, p.y + 142f, p.width - 24f, 20f),
                        garrisonArmyAmount, 1, army.Troops));
                    string garrisonBlocked = occupiedCity.Garrison > 0
                        && UnitTypes.Normalize(occupiedCity.GarrisonType) != UnitTypes.Normalize(army.UnitType)
                        ? $"unit types differ (garrison {UnitTypeName(occupiedCity.GarrisonType)})" : null;
                    OrderButton(new Rect(p.x + 12f, p.y + 162f, p.width - 24f, 34), ActionType.GarrisonArmy,
                        army.Id,
                        garrisonBlocked == null
                            ? $"Garrison in {occupiedCity.Name}"
                            : $"Garrison — {garrisonBlocked}",
                        garrisonBlocked, garrisonArmyAmount, 0, btn);
                }
            }

            if (orders == 0)
            {
                float noOrdersTop = canGarrison
                    ? p.y + (garrisonArmyConversionOpen ? 208f : 166f)
                    : p.y + 120f;
                GUI.Label(new Rect(p.x + 12f, noOrdersTop, p.width - 20f, 22f), "No enemy armies or cities nearby", body);
                if (uiInputLocked)
                    GUI.Label(new Rect(p.x + 12f, noOrdersTop + 24f, p.width - 20f, 38f),
                              "Orders locked: " + LockedReason(), uiReasonWrap);
                if (scrolls) GUI.EndScrollView();
                return;
            }

            string hint = null;
            int row = 0;
            float buttonLeft = p.x + 12f;
            float buttonTop = p.y + (canGarrison ? (garrisonArmyConversionOpen ? 212f : 174f) : 110f);
            float buttonWidth = p.width - 24f;
            foreach (var enemy in enemyArmies)
            {
                int distance = engine.State.Map.HexDistance(army.X, army.Z, enemy.X, enemy.Z, attackRange);
                string label = (distance > 1 ? "Attack (ranged) " : "Attack ") + enemy.Name + $" · Troops {enemy.Troops}";
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + row * 38f, buttonWidth, 34), ActionType.AttackArmy,
                    army.Id, label,
                    army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "Army routed and unable to fight" : null,
                    enemy.X, enemy.Z, btn));
                row++;
            }
            foreach (var city in enemyCities)
            {
                var defensiveArmy = obs != null ? obs.Armies.Find(a => a.X == city.X && a.Z == city.Z) : null;
                int defensiveArmyTroops = defensiveArmy != null && defensiveArmy.OwnerSeatId == city.OwnerSeatId
                    ? defensiveArmy.Troops : 0;
                int effectiveGarrison = city.Garrison + defensiveArmyTroops;
                int requiredTroops = BalanceConfig.AssaultTroopRatio * effectiveGarrison;
                string blocked = army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "Army routed and unable to fight"
                    : UnitTypes.WaivesAssaultRatio(army.UnitType) ? null
                    : army.Troops < requiredTroops
                        ? $"Requires {BalanceConfig.AssaultTroopRatio}:1 troop ratio ({requiredTroops} troops needed)" : null;
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + row * 38f, buttonWidth, 34), ActionType.AttackCity,
                    army.Id, $"Assault {city.Name} · {army.Troops}/{effectiveGarrison} troops", blocked, city.X, city.Z, btn));
                row++;
            }

            if (hint != null)
                GUI.Label(new Rect(p.x + 12f, buttonTop + row * 38f + 38f, p.width - 20f, 22f),
                          EllipsizeToWidth(uiReason, hint, p.width - 24f), uiReason);
            if (scrolls) GUI.EndScrollView();
        }

        /// <summary>
        /// Enemy armies the seat may actually act against: candidates come from this seat's observation, so
        /// the panel can never name or count a unit the fog hides (it used to read canonical state, which
        /// also let a player attack an army they could not see). P15: 弓兵 reaches 2 hexes, every other type
        /// 1 — the same <see cref="UnitTypes.AttackRange"/> the validator uses. The engine still has the last word.
        /// </summary>
        List<ArmyView> AttackableEnemyArmies(ArmyView army)
        {
            var list = new List<ArmyView>();
            if (obs == null) return list;
            int range = UnitTypes.AttackRange(army.UnitType);
            foreach (var other in obs.Armies)
                if (other.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.HexDistance(army.X, army.Z, other.X, other.Z, range) >= 0) list.Add(other);
            return list;
        }

        /// <summary>Enemy cities within reach, from this seat's observation only (see above).</summary>
        List<CityView> AdjacentEnemyCities(ArmyView army)
        {
            var list = new List<CityView>();
            if (obs == null) return list;
            foreach (var city in obs.Cities)
                if (!city.IsNeutral && city.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.AreAdjacent(army.X, army.Z, city.X, city.Z)) list.Add(city);
            return list;
        }
    }
}
