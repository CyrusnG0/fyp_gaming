// KingdomDemo (partial) — offline match replay controller.
//
// Replays recorded matches with ZERO live LLM or network calls.
// ReplaySession owns the authoritative replay stepping; this file connects it to
// KingdomDemo's lifecycle, updates visuals and fog-of-war, and handles UI input locks.

using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private const string ReplayLockReason = "Offline match replay (zero model calls)";

        private ReplaySession _replaySession;
        private ReplayBundle _replayBundle;
        private string _replayInitError;
        private string _replayLastViewedSeat;
        private int _replaySyncedRequestIndex = -1;
        private bool _replayFinalReportLogged;

        void InitializeReplay()
        {
            if (!replayMode) return;

            uiInputLocked = true;
            uiLockReason = ReplayLockReason;
            selCity = null;
            selArmy = null;
            locationChoiceOpen = false;
            statusMsg = "Offline match replay — zero model calls; controls are docked on the right";

            string runKey = !string.IsNullOrEmpty(replayRunId) ? replayRunId : ReplayLoader.VerifiedDemoRunId;
            string searchRoot = !string.IsNullOrWhiteSpace(replaySearchRoot) ? replaySearchRoot.Trim() : null;
            if (!ReplayLoader.TryResolveRunFiles(runKey, searchRoot, out string sPath, out string jPath, out string ePath, out string bPath, out string err))
            {
                _replaySession = null;
                _replayBundle = null;
                _replayInitError = "Run files not found: " + err;
                statusMsg = _replayInitError;
                Debug.LogWarning("[Replay] " + _replayInitError);
                return;
            }

            ReplaySession candidateSession = null;
            GameEngine previousEngine = engine;
            bool visualsModified = false;
            try
            {
                _replayBundle = ReplayLoader.Load(sPath, jPath, ePath, runKey, bPath);
                if (_replayBundle.Summary != null)
                {
                    randomSeed = _replayBundle.Summary.Seed;
                    if (!string.IsNullOrEmpty(_replayBundle.Summary.BalanceProfileKey))
                        balanceProfile = _replayBundle.Summary.BalanceProfileKey;
                }
                candidateSession = new ReplaySession(_replayBundle, engine != null ? engine.State.Map : null);
                engine = candidateSession.Engine;
                if (_replayBundle.Bootstrap != null || _replayBundle.ScenarioSetup != null)
                    RebuildReplayVisuals(ref visualsModified);
                candidateSession.OnEventReplayed += HandleReplayEvent;
                _replaySession = candidateSession;
                _replayLastViewedSeat = SeatRotation.FirstSeat(engine.State) ?? "seat-0";
                RefreshReplayView();
                Debug.Log("[Replay] initialized with " + _replayBundle.Requests.Count + " requests from " + runKey);
            }
            catch (Exception ex)
            {
                if (candidateSession != null) candidateSession.OnEventReplayed -= HandleReplayEvent;
                if (visualsModified) ClearReplayVisuals();
                _replaySession = null;
                _replayBundle = null;
                selCity = null;
                selArmy = null;
                obs = null;
                engine = previousEngine;
                ApplyFog();
                _replayInitError = "Failed to load replay: " + ex.Message;
                statusMsg = _replayInitError;
                Debug.LogWarning("[Replay] " + _replayInitError);
            }
        }

        void ValidateReplayGridAndPlacements()
        {
            if (engine == null || engine.State == null || engine.State.Map == null)
                throw new InvalidOperationException("replay engine or map is missing");

            int gridW = TBTK.GridManager.DimensionX();
            int gridH = TBTK.GridManager.DimensionZ();
            MapData map = engine.State.Map;
            if (map.Width != gridW || map.Height != gridH)
                throw new InvalidOperationException(
                    $"replay map {map.Width}x{map.Height} does not match scene grid {gridW}x{gridH}");

            for (int x = 0; x < gridW; x++)
            {
                for (int z = 0; z < gridH; z++)
                {
                    var node = TBTK.GridManager.GetNode(x, z);
                    bool recWalkable = map.IsWalkable(x, z);
                    int recCost = map.Cost != null ? map.Cost[map.Index(x, z)] : 1;
                    if (node == null)
                    {
                        if (recWalkable)
                            throw new InvalidOperationException(
                                $"replay terrain mismatch at ({x},{z}): recorded walkable=true but scene grid node is missing");
                        continue;
                    }
                    if (node.walkable != recWalkable)
                        throw new InvalidOperationException(
                            $"replay terrain mismatch at ({x},{z}): recorded walkable={recWalkable} differs from scene walkable={node.walkable}");
                    int sceneCost = Mathf.Max(1, Mathf.RoundToInt(node.cost));
                    if (sceneCost != recCost)
                        throw new InvalidOperationException(
                            $"replay terrain mismatch at ({x},{z}): recorded cost={recCost} differs from scene cost={sceneCost}");
                }
            }

            foreach (var city in engine.State.Cities)
                if (TBTK.GridManager.GetNode(city.X, city.Z) == null)
                    throw new InvalidOperationException($"city '{city.Id}' at ({city.X},{city.Z}) is not on a grid node");
            foreach (var tower in engine.State.WatchTowers)
                if (TBTK.GridManager.GetNode(tower.X, tower.Z) == null)
                    throw new InvalidOperationException($"watchtower '{tower.Id}' at ({tower.X},{tower.Z}) is not on a grid node");
            foreach (var army in engine.State.Armies)
                if (TBTK.GridManager.GetNode(army.X, army.Z) == null)
                    throw new InvalidOperationException($"army '{army.Id}' at ({army.X},{army.Z}) is not on a grid node");
        }

        void RebuildReplayVisuals(ref bool visualsModified)
        {
            ValidateReplayGridAndPlacements();

            visualsModified = true;
            HideSceneRosterMarkers();
            ClearReplayVisuals();

            factionColors[SeatB] = new Color(0.8509804f, 0.3490196f, 0.1843137f, 1f);
            factionColors[SeatA] = new Color(0.3490196f, 0.5019608f, 0.8509804f, 1f);
            if (engine.State.ScenarioId == CompactScenario.CompactContactSwappedId)
            {
                Color weiColor = factionColors[SeatA];
                factionColors[SeatA] = factionColors[SeatB];
                factionColors[SeatB] = weiColor;
            }

            foreach (var city in engine.State.Cities)
            {
                var node = TBTK.GridManager.GetNode(city.X, city.Z);
                if (node == null)
                    throw new InvalidOperationException($"city '{city.Id}' at ({city.X},{city.Z}) is not on a grid node");
                var go = FindOrCreateMarker("ReplayCity_" + city.Id, city.X, city.Z, city.OwnerSeatId);
                if (go == null)
                    throw new InvalidOperationException("no marker position for city " + city.Id);
                go.transform.position = node.GetPos();
                cityObjs[city.Id] = go;
                TintCity(city.Id);
            }

            foreach (var tower in engine.State.WatchTowers)
            {
                var node = TBTK.GridManager.GetNode(tower.X, tower.Z);
                if (node == null)
                    throw new InvalidOperationException($"watchtower '{tower.Id}' at ({tower.X},{tower.Z}) is not on a grid node");
                var go = FindOrCreateMarker("ReplayTower_" + tower.Id, tower.X, tower.Z, tower.OwnerSeatId);
                if (go == null)
                    throw new InvalidOperationException("no marker position for watchtower " + tower.Id);
                go.transform.position = node.GetPos();
                watchTowerObjs[tower.Id] = go;
                TintWatchTower(tower.Id);
            }

            foreach (var army in engine.State.Armies)
            {
                if (TBTK.GridManager.GetNode(army.X, army.Z) == null)
                    throw new InvalidOperationException($"army '{army.Id}' at ({army.X},{army.Z}) is not on a grid node");
                armyObjs[army.Id] = CreateArmyBannerMarker(army);
                SyncArmyVisual(army.Id);
            }
        }

        void ClearReplayVisuals()
        {
            foreach (var kv in armyObjs)
            {
                if (kv.Value == null) continue;
                var unit = kv.Value.GetComponent<TBTK.Unit>();
                if (unit != null && unit.node != null && unit.node.unit == unit)
                    unit.node.unit = null;
                if (!IsSceneAuthoredObjectName(kv.Value.name)) Destroy(kv.Value);
            }
            foreach (var kv in cityObjs)
                if (kv.Value != null && !IsSceneAuthoredObjectName(kv.Value.name)) Destroy(kv.Value);
            foreach (var kv in watchTowerObjs)
                if (kv.Value != null && !IsSceneAuthoredObjectName(kv.Value.name)) Destroy(kv.Value);

            cityObjs.Clear();
            armyObjs.Clear();
            watchTowerObjs.Clear();
            armyBannerBases.Clear();
        }

        bool IsSceneAuthoredObjectName(string objName)
        {
            if (string.IsNullOrEmpty(objName)) return false;
            if (objName == weiCityObj || objName == shuCityObj || objName == southCityObj
                || objName == watchTowerObj || objName == weiArmyObj || objName == shuArmyObj)
                return true;
            if (extraArmyObjs != null)
                foreach (string extra in extraArmyObjs)
                    if (objName == extra) return true;
            return false;
        }

        void TickReplay()
        {
            if (!replayMode || _replaySession == null || !string.IsNullOrEmpty(_replayInitError)) return;

            HandleReplayKeyboardControls();

            if (_replaySession.IsPlaying)
            {
                _replaySession.Tick(Time.deltaTime);
            }

            if (_replaySession.CurrentRequestIndex != _replaySyncedRequestIndex)
            {
                _replaySyncedRequestIndex = _replaySession.CurrentRequestIndex;
                RefreshReplayView();
            }

            if (_replaySession.IsCompleted && !_replayFinalReportLogged)
            {
                _replayFinalReportLogged = true;
                if (_replaySession.FinalReport != null)
                {
                    Debug.Log(_replaySession.FinalReport.FormatReport());
                }
            }
        }

        void HandleReplayKeyboardControls()
        {
            if (_replaySession == null || !string.IsNullOrEmpty(_replayInitError) || pauseMenuOpen || reportShowing || battleResultShowing || handoverShowing) return;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (_replaySession.IsPlaying) ReplayPause();
                else ReplayPlay();
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Period))
            {
                ReplayStepRequest();
            }
            else if (Input.GetKeyDown(KeyCode.S))
            {
                ReplayStepSeason();
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                ReplayReset();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha1)) ReplaySetSpeed(0.5f);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) ReplaySetSpeed(1.0f);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) ReplaySetSpeed(2.0f);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) ReplaySetSpeed(4.0f);
        }

        void SyncReplayVisuals()
        {
            if (engine == null) return;
            SyncSpectatorArmyVisuals();
            SyncBattleVisuals();
            foreach (var c in engine.State.Cities) TintCity(c.Id);
        }

        void RefreshReplayView()
        {
            if (engine == null) return;
            string seat = _replaySession != null ? _replaySession.CurrentSeatId : null;
            if (string.IsNullOrEmpty(seat)) seat = _replayLastViewedSeat;
            if (string.IsNullOrEmpty(seat)) seat = currentSeat;
            if (string.IsNullOrEmpty(seat)) seat = "seat-0";
            _replayLastViewedSeat = seat;
            currentSeat = seat;
            RefreshObservation();
            SyncReplayVisuals();
            ApplyFog();
        }

        void DismissReplayModals()
        {
            if (!replayMode) return;
            handoverShowing = false;
            pendingReport = false;
            if (reportShowing)
            {
                reportShowing = false;
                reportScroll = Vector2.zero;
            }
        }

        string ReplayHeaderLine()
        {
            if (_replaySession == null)
            {
                return "OFFLINE REPLAY — " + (_replayInitError ?? "loading");
            }
            string viewed = ViewedFactionNameFromObservation();
            int season = _replaySession.CurrentSeason;
            int total = _replaySession.TotalSeasons;
            string state = _replaySession.IsCompleted
                ? (_replaySession.IsIncompleteRun ? $"Paused at S{season} (incomplete)" : "Completed")
                : (_replaySession.IsPlaying ? "Playing" : "Paused");
            string totalStr = total > 0 ? total.ToString() : "?";
            return "OFFLINE REPLAY  |  Season " + season + "/" + totalStr + "  |  " + state + " (viewing " + viewed + ")";
        }

        string ReplayDetailLine()
        {
            if (_replaySession == null) return "Zero API calls (offline verification)";
            int req = _replaySession.CurrentRequestIndex;
            int totalReq = _replaySession.TotalRequests;
            int matched = _replaySession.ObservationHashesMatched;
            return "Zero API calls  |  Req " + req + "/" + totalReq + "  |  Obs matched " + matched + "/" + req;
        }

        public string GetDisplayedSummarySnapshot()
            => _replaySession != null ? _replaySession.GetDisplayedSummarySnapshot() : ("OFFLINE REPLAY — " + (_replayInitError ?? "loading"));

        void ReplayPlay()
        {
            if (_replaySession != null && string.IsNullOrEmpty(_replayInitError)) _replaySession.Play();
        }

        void ReplayPause()
        {
            if (_replaySession != null && string.IsNullOrEmpty(_replayInitError)) _replaySession.Pause();
        }

        void ReplayStepSeason()
        {
            if (_replaySession == null || !string.IsNullOrEmpty(_replayInitError)) return;
            _replaySession.StepSeason();
            _replaySyncedRequestIndex = _replaySession.CurrentRequestIndex;
            RefreshReplayView();
        }

        void ReplayStepRequest()
        {
            if (_replaySession == null || !string.IsNullOrEmpty(_replayInitError)) return;
            _replaySession.StepRequest();
            _replaySyncedRequestIndex = _replaySession.CurrentRequestIndex;
            RefreshReplayView();
        }

        void ReplayReset()
        {
            if (_replaySession == null || !string.IsNullOrEmpty(_replayInitError)) return;
            jsonFeed.Clear();
            _replaySession.Reset();
            engine = _replaySession.Engine;
            if (_replayBundle != null && (_replayBundle.Bootstrap != null || _replayBundle.ScenarioSetup != null))
            {
                bool visualsModified = false;
                RebuildReplayVisuals(ref visualsModified);
            }
            _replaySyncedRequestIndex = -1;
            _replayFinalReportLogged = false;
            _replayLastViewedSeat = SeatRotation.FirstSeat(engine.State) ?? "seat-0";
            RefreshReplayView();
        }

        void HandleReplayEvent(ReplayFeedEvent evt)
        {
            if (evt == null) return;
            string okMark = evt.ResultOk ? "✓ " : "✗ ";
            string line = $"{okMark}[Replay S{evt.Season} {evt.Faction}] {evt.ActionSummary}";
            if (!string.IsNullOrEmpty(evt.Explanation))
                line += $" — \"{evt.Explanation}\"";
            jsonFeed.Add(line);
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }

        void ReplaySetSpeed(float multiplier)
        {
            if (_replaySession != null && string.IsNullOrEmpty(_replayInitError)) _replaySession.SetSpeed(multiplier);
        }
    }
}
