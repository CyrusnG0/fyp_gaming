// HandoverUiModel.cs — pure-C# coordinator for pending between-turn handover/reclaim requests,
// operator seat eligibility, and seat-isolated LLM episode capture.

using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi
{
    /// <summary>
    /// Pure-C# coordinator bridging the UI to the single authoritative handover queue inside
    /// <see cref="GameEngine"/> and capturing seat-isolated LLM episode state across transitions.
    /// </summary>
    public sealed class HandoverCoordinatorState
    {
        private readonly HashSet<string> _eligibleSeats = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _eligibleSeatOrder = new List<string>();
        private readonly Dictionary<string, string> _lastKnownControllerBySeat =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Observation> _pendingPreBaselineBySeat =
            new Dictionary<string, Observation>(StringComparer.Ordinal);
        private readonly Dictionary<string, HandoverDelegationEpisode> _episodesBySeat =
            new Dictionary<string, HandoverDelegationEpisode>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lastActivitySeqBySeat =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private IHandoverEngineAdapter _adapter = new DisabledHandoverAdapter();

        public IReadOnlyList<string> EligibleSeats => _eligibleSeatOrder;
        public bool HasEligibleSeats => _eligibleSeatOrder.Count > 0;
        public bool HasAnyEpisode() => _episodesBySeat.Count > 0;

        /// <summary>
        /// Registers seats owned by the local human operator(s) and binds the authoritative
        /// <see cref="EngineHandoverAdapter"/> once per match.
        /// </summary>
        public void Initialize(GameEngine engine, string[] humanSeats, bool allowHandover = true,
            IHandoverEngineAdapter adapterOverride = null)
        {
            _eligibleSeats.Clear();
            _eligibleSeatOrder.Clear();
            _lastKnownControllerBySeat.Clear();
            _pendingPreBaselineBySeat.Clear();
            _episodesBySeat.Clear();
            _lastActivitySeqBySeat.Clear();

            if (!allowHandover || engine == null || engine.State == null)
            {
                _adapter = adapterOverride ?? new DisabledHandoverAdapter();
                return;
            }

            var configuredHumans = new HashSet<string>(StringComparer.Ordinal);
            if (humanSeats != null)
                foreach (string id in humanSeats)
                    if (!string.IsNullOrEmpty(id)) configuredHumans.Add(id);

            foreach (FactionState faction in engine.State.Factions)
            {
                if (faction == null || string.IsNullOrEmpty(faction.SeatId)) continue;
                if (faction.Controller == ControllerType.Human
                    || (configuredHumans.Contains(faction.SeatId) && faction.Controller != ControllerType.ScriptedAi))
                {
                    if (_eligibleSeats.Add(faction.SeatId))
                    {
                        _eligibleSeatOrder.Add(faction.SeatId);
                        _lastKnownControllerBySeat[faction.SeatId] = faction.Controller;
                    }
                }
            }

            _adapter = adapterOverride ?? (_eligibleSeatOrder.Count > 0
                ? (IHandoverEngineAdapter)new EngineHandoverAdapter(engine, _eligibleSeatOrder)
                : new DisabledHandoverAdapter("No operator-owned human seats in this match."));
        }

        public bool IsEligibleSeat(string seatId)
            => !string.IsNullOrEmpty(seatId) && _eligibleSeats.Contains(seatId) && _adapter.IsEligibleSeat(seatId);

        public bool TryGetPending(GameEngine engine, string seatId, out HandoverPendingRequest request)
        {
            request = null;
            if (string.IsNullOrEmpty(seatId)) return false;
            HandoverStatus status = _adapter.GetStatus(engine, seatId);
            if (status == null || !status.HasPending) return false;

            FactionState fac = engine?.State.FindFaction(seatId);
            request = new HandoverPendingRequest
            {
                RequestId = status.PendingRequestId,
                SeatId = seatId,
                FactionName = fac?.Name ?? seatId,
                TargetController = status.PendingToController,
                Kind = status.PendingToController == ControllerType.Llm
                    ? HandoverPendingKind.HandoverToAi
                    : HandoverPendingKind.ReclaimToHuman,
                RequestedSeason = status.PendingRequestedSeason,
            };
            return true;
        }

        public bool HasAnyPendingReclaim(GameEngine engine)
        {
            for (int i = 0; i < _eligibleSeatOrder.Count; i++)
            {
                HandoverStatus status = _adapter.GetStatus(engine, _eligibleSeatOrder[i]);
                if (status != null && status.HasPending && status.PendingToController == ControllerType.Human)
                    return true;
            }
            return false;
        }

        public bool RequestHandoverToAi(GameEngine engine, string seatId, out string error)
        {
            HandoverResult result = _adapter.Request(engine, seatId, ControllerType.Llm);
            error = result.Error;
            return result.Ok;
        }

        public bool RequestReclaimToHuman(GameEngine engine, string seatId, out string error)
        {
            HandoverResult result = _adapter.Request(engine, seatId, ControllerType.Human);
            error = result.Error;
            return result.Ok;
        }

        public bool CancelPending(GameEngine engine, string seatId, out string error)
        {
            HandoverResult result = _adapter.Cancel(engine, seatId);
            error = result.Error;
            if (result.Ok && !string.IsNullOrEmpty(seatId))
                _pendingPreBaselineBySeat.Remove(seatId);
            return result.Ok;
        }

        /// <summary>
        /// Snapshots the seat's pre-transfer observation right before <see cref="GameEngine.EndSeatTurn"/>
        /// or <see cref="GameEngine.PrepareSeatTurn"/> when a Human -> AI handover is pending, so the
        /// episode records exact pre-delegation resource baselines and sequence markers.
        /// </summary>
        public void CapturePreHandoverBaselineIfPending(GameEngine engine, string seatId, Observation seatObs)
        {
            if (engine == null || string.IsNullOrEmpty(seatId) || seatObs == null) return;
            HandoverStatus status = _adapter.GetStatus(engine, seatId);
            if (status != null && status.HasPending
                && status.Controller == ControllerType.Human
                && status.PendingToController == ControllerType.Llm)
            {
                _pendingPreBaselineBySeat[seatId] = seatObs;
            }
        }

        /// <summary>
        /// Calls the engine's between-turn prepare hook for <paramref name="boundarySeatId"/> when no turn
        /// gate is active, then synchronizes episode/briefing state if the engine transitioned controllers.
        /// </summary>
        public HandoverTransferOutcome PrepareAndSyncBoundary(
            GameEngine engine, string boundarySeatId, Observation seatObs)
        {
            if (engine == null || string.IsNullOrEmpty(boundarySeatId))
                return HandoverTransferOutcome.NonePending();

            HandoverStatus before = _adapter.GetStatus(engine, boundarySeatId);
            if (!string.IsNullOrEmpty(engine.ActiveSeatId))
            {
                return before != null && before.HasPending
                    ? HandoverTransferOutcome.Failed(
                        "Cannot transfer control mid-turn while " + engine.ActiveSeatId + " is active.")
                    : HandoverTransferOutcome.NonePending();
            }

            _adapter.PrepareSeatTurn(engine, boundarySeatId);
            return SyncBoundaryAfterEngineChange(engine, boundarySeatId, seatObs);
        }

        /// <summary>
        /// Updates delegation episode / reclaim-briefing state after an engine boundary hook
        /// (<see cref="GameEngine.EndSeatTurn"/> or <see cref="GameEngine.PrepareSeatTurn"/>) may
        /// have transitioned <paramref name="seatId"/>'s effective controller.
        /// </summary>
        public HandoverTransferOutcome SyncBoundaryAfterEngineChange(
            GameEngine engine, string seatId, Observation seatObs)
        {
            if (engine == null || string.IsNullOrEmpty(seatId))
                return HandoverTransferOutcome.NonePending();

            HandoverStatus status = _adapter.GetStatus(engine, seatId);
            if (status == null) return HandoverTransferOutcome.NonePending();

            if (!status.HasPending && status.Controller == ControllerType.Human)
                _pendingPreBaselineBySeat.Remove(seatId);

            if (!_lastKnownControllerBySeat.TryGetValue(seatId, out string previousController))
            {
                if (_eligibleSeats.Contains(seatId))
                    _lastKnownControllerBySeat[seatId] = status.Controller;
                _pendingPreBaselineBySeat.Remove(seatId);
                return HandoverTransferOutcome.NonePending();
            }

            if (string.Equals(previousController, status.Controller, StringComparison.Ordinal))
                return HandoverTransferOutcome.NonePending();

            _lastKnownControllerBySeat[seatId] = status.Controller;
            string factionName = seatObs?.Faction(seatId)?.Name ?? engine.State.FindFaction(seatId)?.Name ?? seatId;

            if (previousController == ControllerType.Human && status.Controller == ControllerType.Llm)
            {
                _pendingPreBaselineBySeat.TryGetValue(seatId, out Observation baselineObs);
                _pendingPreBaselineBySeat.Remove(seatId);
                StartDelegationEpisode(seatId, factionName, baselineObs ?? seatObs);
                return HandoverTransferOutcome.Succeeded($"{factionName} transferred to AI");
            }

            _pendingPreBaselineBySeat.Remove(seatId);
            if (previousController == ControllerType.Llm && status.Controller == ControllerType.Human)
            {
                MarkReclaimBriefingReady(seatId, factionName, seatObs);
                return HandoverTransferOutcome.Succeeded($"{factionName} reclaimed by Human");
            }

            return HandoverTransferOutcome.NonePending();
        }

        public void RecordLlmTurnForSeat(
            string seatId,
            int season,
            Observation seatObs,
            IReadOnlyList<LlmActivity> seatActivities,
            string endReasonText,
            string failureReason,
            int requestsThisTurn,
            int actionsThisTurn,
            string contextReportJson,
            SpatialTruncationReport spatialTruncation,
            int promptEventCap)
        {
            if (string.IsNullOrEmpty(seatId)) return;

            if (!_episodesBySeat.TryGetValue(seatId, out var episode))
            {
                episode = new HandoverDelegationEpisode
                {
                    SeatId = seatId,
                    FactionName = seatObs?.Faction(seatId)?.Name ?? seatId,
                    StartedSeason = season,
                    HasPreHandoverBaseline = false,
                };
                _episodesBySeat[seatId] = episode;
            }

            var capture = new HandoverSeatTurnCapture
            {
                SeatId = seatId,
                Season = season,
                EndReasonText = endReasonText,
                FailureReason = failureReason,
                RequestsThisTurn = requestsThisTurn,
                ActionsThisTurn = actionsThisTurn,
                ContextReportJson = contextReportJson,
                SpatialTruncation = spatialTruncation,
                PromptEventCap = promptEventCap,
                ObservationEventsAtTurnEnd = seatObs?.Events != null ? seatObs.Events.Count : 0,
            };

            _lastActivitySeqBySeat.TryGetValue(seatId, out int lastSeq);
            int maxSeq = lastSeq;
            if (seatActivities != null)
            {
                for (int i = 0; i < seatActivities.Count; i++)
                {
                    LlmActivity item = seatActivities[i];
                    if (item == null || !string.Equals(item.SeatId, seatId, StringComparison.Ordinal)) continue;
                    if (item.Sequence <= lastSeq) continue;

                    if (episode.TotalCapturedActivities >= HandoverDelegationEpisode.MaxCapturedActivities)
                    {
                        episode.ActivityRetentionDropped = true;
                        maxSeq = Math.Max(maxSeq, item.Sequence);
                        continue;
                    }
                    capture.Activities.Add(item);
                    maxSeq = Math.Max(maxSeq, item.Sequence);
                }
            }

            if (capture.Activities.Count == 0 && (requestsThisTurn > 0 || actionsThisTurn > 0))
                capture.ActivityGapDetected = true;

            _lastActivitySeqBySeat[seatId] = maxSeq;
            episode.Turns.Add(capture);
        }

        public HandoverDelegationEpisode GetEpisode(string seatId)
            => !string.IsNullOrEmpty(seatId) && _episodesBySeat.TryGetValue(seatId, out var ep) ? ep : null;

        public bool HasPendingBriefing(string seatId)
            => GetEpisode(seatId)?.PendingBriefing == true;

        public void ConsumePendingBriefing(string seatId)
        {
            HandoverDelegationEpisode ep = GetEpisode(seatId);
            if (ep != null) ep.PendingBriefing = false;
        }

        public IReadOnlyList<LlmActivity> GetEpisodeActivities(string seatId)
        {
            HandoverDelegationEpisode ep = GetEpisode(seatId);
            var list = new List<LlmActivity>();
            if (ep == null) return list;
            for (int t = 0; t < ep.Turns.Count; t++)
                list.AddRange(ep.Turns[t].Activities);
            return list;
        }

        private void StartDelegationEpisode(string seatId, string factionName, Observation seatObs)
        {
            int maxSeq = 0;
            int cities = 0, food = 0, gold = 0, pop = 0, prestige = 0;
            bool hasBaseline = seatObs != null;
            if (seatObs != null)
            {
                for (int i = 0; i < seatObs.Events.Count; i++)
                    if (seatObs.Events[i].Seq > maxSeq) maxSeq = seatObs.Events[i].Seq;
                foreach (var c in seatObs.CitiesOf(seatId))
                {
                    cities++;
                    food += c.Food;
                    gold += c.Gold;
                    pop += c.Population;
                }
                prestige = seatObs.Prestige;
            }

            _episodesBySeat[seatId] = new HandoverDelegationEpisode
            {
                SeatId = seatId,
                FactionName = string.IsNullOrEmpty(factionName) ? seatId : factionName,
                StartedSeason = seatObs != null ? seatObs.Season : 1,
                BaselineEventSeq = maxSeq,
                HasPreHandoverBaseline = hasBaseline,
                PreCitiesCount = cities,
                PreTotalFood = food,
                PreTotalGold = gold,
                PreTotalPopulation = pop,
                PrePrestige = prestige,
                PendingBriefing = false,
            };
        }

        private void MarkReclaimBriefingReady(string seatId, string factionName, Observation seatObs)
        {
            if (!_episodesBySeat.TryGetValue(seatId, out var ep))
            {
                ep = new HandoverDelegationEpisode
                {
                    SeatId = seatId,
                    FactionName = string.IsNullOrEmpty(factionName) ? seatId : factionName,
                    StartedSeason = seatObs != null ? seatObs.Season : 1,
                    HasPreHandoverBaseline = false,
                };
                _episodesBySeat[seatId] = ep;
            }
            ep.PendingBriefing = true;
        }
    }
}
