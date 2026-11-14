// KingdomDemo (partial) — opt-in scenario selection (host side).
//
// The scenario itself is pure data in LuanShi.Engine (CompactScenario). This file only turns the
// selected key into runtime markers: it deactivates the demo scene's authored city/army markers in
// memory, builds markers at the scenario's hexes, and never saves the scene. Both the single-agent
// and the spectator setup go through KingdomDemo.InitGame, so the same selection covers both modes.

using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        /// <summary>
        /// Replaces the hardcoded demo roster with the selected scenario. Throws on an unknown key so a
        /// typo cannot silently start a different match.
        /// </summary>
        void ApplyOptInScenario(string scenarioKey)
        {
            if (!CompactScenario.TryGet(scenarioKey, out CompactScenario chosen))
                throw new Exception("Unknown scenario: " + scenarioKey);

            chosen.Apply(engine, faction => ControlOf(faction.SeatId));
            if (chosen.ScenarioId == CompactScenario.CompactContactSwappedId)
            {
                Color weiColor = factionColors[SeatA];
                factionColors[SeatA] = factionColors[SeatB];
                factionColors[SeatB] = weiColor;
            }
            HideSceneRosterMarkers();
            BuildScenarioMarkers();

            Debug.Log("[KingdomDemo] scenario: " + engine.State.ScenarioId
                      + " (" + chosen.DisplayName + ") profile: " + engine.Balance.Key
                      + " cities: " + engine.State.Cities.Count + " armies: " + engine.State.Armies.Count);
        }

        /// <summary>
        /// The scene's authored markers belong to the built-in roster. A scenario places its own, so the
        /// originals are switched off for this play session only (no scene save, no asset change).
        /// </summary>
        void HideSceneRosterMarkers()
        {
            HideSceneObject(weiCityObj);
            HideSceneObject(shuCityObj);
            HideSceneObject(southCityObj);
            HideSceneObject(watchTowerObj);
            HideSceneObject(weiArmyObj);
            HideSceneObject(shuArmyObj);
            if (extraArmyObjs == null) return;
            foreach (string name in extraArmyObjs) HideSceneObject(name);
        }

        void HideSceneObject(string objName)
        {
            var go = FindSceneObject(objName);
            if (go != null && go.activeSelf) go.SetActive(false);
        }

        /// <summary>
        /// Runtime markers for every scenario entity. Cities and towers use the adapter's own marker
        /// helper; armies use the same banner builder a recruited army gets, so tinting, fog, selection
        /// and the battle visuals treat them exactly like the demo's own units.
        /// </summary>
        void BuildScenarioMarkers()
        {
            foreach (var city in engine.State.Cities)
            {
                var go = FindOrCreateMarker(city.Id, city.X, city.Z, city.OwnerSeatId);
                if (go == null) throw new Exception("no marker position for city " + city.Id);
                var node = TBTK.GridManager.GetNode(city.X, city.Z);
                if (node == null) throw new Exception("city " + city.Id + " is not on a grid node");
                go.transform.position = node.GetPos();
                cityObjs[city.Id] = go;
                TintCity(city.Id);
            }

            foreach (var army in engine.State.Armies)
            {
                armyObjs[army.Id] = CreateArmyBannerMarker(army);
                SyncArmyVisual(army.Id);
            }

            foreach (var tower in engine.State.WatchTowers)
            {
                var go = FindOrCreateMarker(tower.Id, tower.X, tower.Z, tower.OwnerSeatId);
                if (go == null) throw new Exception("no marker position for watchtower " + tower.Id);
                var node = TBTK.GridManager.GetNode(tower.X, tower.Z);
                if (node != null) go.transform.position = node.GetPos();
                go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                go.transform.localEulerAngles = new Vector3(45f, 0f, 0f);
                var towerPosition = go.transform.position;
                towerPosition.y = 0.55f;
                go.transform.position = towerPosition;
                watchTowerObjs[tower.Id] = go;
                TintWatchTower(tower.Id);
            }
        }
    }
}
