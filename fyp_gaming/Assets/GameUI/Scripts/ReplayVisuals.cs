// KingdomDemo (partial) - replay visuals: showing a different recording on the loaded scene.
//
// The scene's terrain is its fixed TBTK hex grid, which cannot be rebuilt while playing, so a recording on a
// different map is refused with the reason and the run already open stays untouched. For a recording on the
// same map the city, watch-tower and army markers are reconciled: created where the scene has none, moved when
// the recording puts them on another hex, hidden when the recording has no such entity, and runtime army
// banners are rebuilt so a new run never inherits another run's unit art. Presentation only: nothing here
// reads or changes authoritative state beyond the engine the replay session already owns.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // The terrain the scene was built with (captured in InitGame, before any replay engine replaces it).
        private MapData _replaySceneMap;
        // Last hex each city/tower marker was placed on; seeded from the scene's own engine on first use.
        private Dictionary<string, int[]> _replayCityHex;
        private Dictionary<string, int[]> _replayTowerHex;
        private ReplayVisualPlan _replayVisualPlan;

        private void EnsureReplayMarkerHexes()
        {
            if (_replayCityHex != null) return;
            _replayCityHex = new Dictionary<string, int[]>();
            _replayTowerHex = new Dictionary<string, int[]>();
            if (engine == null) return;
            foreach (CityState city in engine.State.Cities)
                if (cityObjs.ContainsKey(city.Id)) _replayCityHex[city.Id] = new[] { city.X, city.Z };
            foreach (WatchTowerState tower in engine.State.WatchTowers)
                if (watchTowerObjs.ContainsKey(tower.Id)) _replayTowerHex[tower.Id] = new[] { tower.X, tower.Z };
        }

        /// <summary>
        /// Null when <paramref name="open"/>'s recording can be drawn on this scene; otherwise the reason.
        /// Reads only, so a refused switch leaves everything as it was.
        /// </summary>
        string CheckReplayVisuals(ReplayOpenResult open)
        {
            if (_replaySceneMap == null) return "the scene's map was not captured, so the recording cannot be checked against it";
            EnsureReplayMarkerHexes();
            var surface = new ReplayVisualSurface { Map = _replaySceneMap };
            foreach (var kv in _replayCityHex) surface.Cities[kv.Key] = kv.Value;
            foreach (var kv in _replayTowerHex) surface.Towers[kv.Key] = kv.Value;
            _replayVisualPlan = ReplayVisualPlanner.Plan(surface, open.Session.Engine.State);
            return _replayVisualPlan.Compatible ? null : _replayVisualPlan.Reason;
        }

        /// <summary>Applies the plan for the engine now shown (after the session swap or a reset).</summary>
        void ApplyReplayVisuals()
        {
            if (engine == null || _replaySceneMap == null) return;
            EnsureReplayMarkerHexes();
            var surface = new ReplayVisualSurface { Map = _replaySceneMap };
            foreach (var kv in _replayCityHex) surface.Cities[kv.Key] = kv.Value;
            foreach (var kv in _replayTowerHex) surface.Towers[kv.Key] = kv.Value;
            ReplayVisualPlan plan = ReplayVisualPlanner.Plan(surface, engine.State);
            if (!plan.Compatible)
            {
                Debug.LogWarning("[Replay] visuals not reconciled: " + plan.Reason);
                return;
            }
            _replayVisualPlan = plan;

            foreach (CityState city in engine.State.Cities)
            {
                bool created = plan.CitiesToCreate.Contains(city.Id), moved = plan.CitiesToMove.Contains(city.Id);
                if (!cityObjs.TryGetValue(city.Id, out GameObject go) || go == null)
                {
                    go = FindOrCreateMarker(city.Id, city.X, city.Z, city.OwnerSeatId);
                    if (go == null) { Debug.LogWarning("[Replay] no marker could be made for city " + city.Id); continue; }
                    cityObjs[city.Id] = go;
                    created = true;
                }
                if (created || moved) PlaceMarkerOnHex(go, city.X, city.Z);
                go.SetActive(true);
                _replayCityHex[city.Id] = new[] { city.X, city.Z };
            }
            foreach (string id in plan.CitiesToHide)
                if (cityObjs.TryGetValue(id, out GameObject stale) && stale != null) stale.SetActive(false);

            foreach (WatchTowerState tower in engine.State.WatchTowers)
            {
                bool created = plan.TowersToCreate.Contains(tower.Id), moved = plan.TowersToMove.Contains(tower.Id);
                if (!watchTowerObjs.TryGetValue(tower.Id, out GameObject go) || go == null)
                {
                    go = FindOrCreateMarker(tower.Id, tower.X, tower.Z, tower.OwnerSeatId);
                    if (go == null) { Debug.LogWarning("[Replay] no marker could be made for watch tower " + tower.Id); continue; }
                    watchTowerObjs[tower.Id] = go;
                    created = true;
                }
                if (created || moved)
                {
                    PlaceMarkerOnHex(go, tower.X, tower.Z);
                    go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                    go.transform.localEulerAngles = new Vector3(45f, 0f, 0f);
                    Vector3 towerPosition = go.transform.position;
                    towerPosition.y = 0.55f;
                    go.transform.position = towerPosition;
                }
                go.SetActive(true);
                _replayTowerHex[tower.Id] = new[] { tower.X, tower.Z };
            }
            foreach (string id in plan.TowersToHide)
                if (watchTowerObjs.TryGetValue(id, out GameObject stale) && stale != null) stale.SetActive(false);

            RebuildReplayArmyVisuals();
            selCity = null;
            selArmy = null;
        }

        void PlaceMarkerOnHex(GameObject go, int x, int z)
        {
            var node = TBTK.GridManager.GetNode(x, z);
            if (node != null) go.transform.position = node.GetPos();
        }

        /// <summary>
        /// Runtime army banners belong to one engine's armies (their art follows the unit type), so they are
        /// destroyed and rebuilt for the engine now shown. Banners authored in the scene are kept, and hidden
        /// by the fog pass when this run has no such army.
        /// </summary>
        void RebuildReplayArmyVisuals()
        {
            var ids = new List<string>(armyObjs.Keys);
            foreach (string id in ids)
            {
                GameObject go = armyObjs[id];
                bool runtime = go != null && (go.name.StartsWith("ArmyBanner_", System.StringComparison.Ordinal)
                                              || go.name.StartsWith("ArmyMarker_", System.StringComparison.Ordinal));
                if (go == null || runtime)
                {
                    if (go != null) DestroyRuntimeArmyVisual(go);
                    armyObjs.Remove(id);
                    armyBannerBases.Remove(id);
                }
                else go.SetActive(false);
            }
            SyncSpectatorArmyVisuals();
        }

        void DestroyRuntimeArmyVisual(GameObject go)
        {
            var unit = go.GetComponent<TBTK.Unit>();
            if (unit != null)
            {
                if (unit.node != null && unit.node.unit == unit) unit.node.unit = null;
                foreach (var faction in TBTK.UnitManager.GetFactionList())
                    faction.unitList.Remove(unit);
            }
            Destroy(go);
        }
    }
}
