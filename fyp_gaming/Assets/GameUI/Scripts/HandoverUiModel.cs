// HandoverUiModel.cs — pure-C# coordinator for pending between-turn handover/reclaim requests,
// operator seat eligibility, and seat-isolated LLM episode capture.

using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi
{
    /// <summary>
    /// Pure-C# coordinator for pending between-turn handover/reclaim requests, operator seat
    /// eligibility, and seat-isolated LLM episode capture.
    /// </summary>
    public sealed class HandoverCoordinatorState
    {
        private readonly HashSet<string> _eligibleSeats = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _eligibleSeatOrder = new List<string>();
        private readonly Dictionary<string, HandoverPendingRequest> _pendingBySeat =
            new Dictionary<string, HandoverPendingRequest>(StringComparer.Ordinal);
        private readonly Dictionary<string, HandoverDelegationEpisode> _episodesBySeat =
            new Dictionary<string, HandoverDelegationEpisode>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lastActivitySeqBySeat =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<string> EligibleSeats => _eligibleSeatOrder;
        public bool HasEligibleSeats => _eligibleSeatOrder.Count > 0;

        /// <summary>
        /// Registers seats owned by the local human operator(s). Only seats explicitly listed in
        /// <paramref name="humanSeats"/> that began as Human (or match an initial human faction) are
        /// eligible; opponent AI/scripted seats cannot be hijacked.
        /// </summary>
        public void Initialize(IReadOnlyList<FactionState> factions, string[] humanSeats)
        {
            _eligibleSeats.Clear();
            _eligibleSeatOrder.Clear();
            _pendingBySeat.Clear();
            _episodesBySeat.Clear();
            _lastActivitySeqBySeat.Clear();

            if (factions == null) return;
            var configuredHumans = new HashSet<string>(StringComparer.Ordinal);
            if (humanSeats != null)
                foreach (string id in humanSeats)
                    if (!string.IsNullOrEmpty(id)) configuredHumans.Add(id);

            foreach (FactionState faction in factions)
            {
                if (faction == null || string.IsNullOrEmpty(faction.SeatId)) continue;
                if (faction.Controller == ControllerType.Human
                    || (configuredHumans.Contains(faction.SeatId) && faction.Controller != ControllerType.ScriptedAi))
                {
                    if (_eligibleSeats.Add(faction.SeatId))
                        _eligibleSeatOrder.Add(faction.SeatId);
                }
            }
        }

        public bool IsEligibleSeat(string seatId)
            => !string.IsNullOrEmpty(seatId) && _eligibleSeats.Contains(seatId);

        public bool TryGetPending(string seatId, out HandoverPendingRequest request)
        {
            if (string.IsNullOrEmpty(seatId))
            {
                request = null;
                return false;
            }
            return _pendingBySeat.TryGetValue(seatId, out request);
        }

        public bool HasAnyPendingReclaim()
        {
            foreach (var kv in _pendingBySeat)
                if (kv.Value.Kind == HandoverPendingKind.ReclaimToHuman) return true;
            return false;
        }

        public bool RequestHandoverToAi(string seatId, string factionName, string currentController,
            bool isSeatTurnActive, int season, out string error)
        {
            error = null;
            if (!IsEligibleSeat(seatId))
            {
                error = "Only human-owned factions can be handed over.";
                return false;
            }
            if (!string.Equals(currentController, ControllerType.Human, StringComparison.Ordinal))
            {
                error = "Faction is not currently human-controlled.";
                return false;
            }
            if (_pendingBySeat.ContainsKey(seatId))
            {
                error = "A controller transfer request is already pending for this faction; cancel it first.";
                return false;
            }

            _pendingBySeat[seatId] = new HandoverPendingRequest
            {
                SeatId = seatId,
                FactionName = string.IsNullOrEmpty(factionName) ? seatId : factionName,
                TargetController = ControllerType.Llm,
                Kind = HandoverPendingKind.HandoverToAi,
                Reason = "human_handover_to_ai",
                RequestedSeason = season,
                RequestedWhileActiveTurn = isSeatTurnActive,
            };
            return true;
        }

        public bool RequestReclaimToHuman(string seatId, string factionName, string currentController,
            bool isSeatTurnActive, int season, out string error)
        {
            error = null;
            if (!IsEligibleSeat(seatId))
            {
                error = "Only operator-owned factions can be reclaimed.";
                return false;
            }
            if (!string.Equals(currentController, ControllerType.Llm, StringComparison.Ordinal))
            {
                error = "Faction is not currently LLM-controlled.";
                return false;
            }
            if (_pendingBySeat.ContainsKey(seatId))
            {
                error = "A controller transfer request is already pending for this faction; cancel it first.";
                return false;
            }

            _pendingBySeat[seatId] = new HandoverPendingRequest
            {
                SeatId = seatId,
                FactionName = string.IsNullOrEmpty(factionName) ? seatId : factionName,
                TargetController = ControllerType.Human,
                Kind = HandoverPendingKind.ReclaimToHuman,
                Reason = "human_reclaim_control",
                RequestedSeason = season,
                RequestedWhileActiveTurn = isSeatTurnActive,
            };
            return true;
        }

        public bool CancelPending(string seatId, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(seatId) || !_pendingBySeat.Remove(seatId))
            {
                error = "No pending controller transfer to cancel.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Applies a pending transfer strictly for <paramref name="boundarySeatId"/> when no engine
        /// turn gate is open. Another seat's turn end must never trigger this seat's transfer.
        /// </summary>
        public HandoverTransferOutcome ApplyPendingAtSafeBoundary(
            GameEngine engine,
            string boundarySeatId,
            string activeEngineSeatId,
            IHandoverEngineAdapter adapter,
            Observation boundarySeatObs)
        {
            if (string.IsNullOrEmpty(boundarySeatId) || !_pendingBySeat.TryGetValue(boundarySeatId, out var req))
                return HandoverTransferOutcome.NonePending();

            if (!string.IsNullOrEmpty(activeEngineSeatId))
                return HandoverTransferOutcome.Failed(
                    "Cannot transfer control mid-turn while " + activeEngineSeatId + " is active.");

            IHandoverEngineAdapter effectiveAdapter = adapter ?? new BaselineDeferredHandoverAdapter();
            HandoverTransferOutcome outcome = effectiveAdapter.TryTransferBetweenTurns(
                engine, req.SeatId, req.TargetController, req.Reason);

            if (outcome.Unsupported)
            {
                _pendingBySeat.Remove(boundarySeatId);
                return outcome;
            }

            if (!outcome.Applied)
                return outcome;

            _pendingBySeat.Remove(boundarySeatId);
            if (req.Kind == HandoverPendingKind.HandoverToAi)
                StartDelegationEpisode(req.SeatId, req.FactionName, boundarySeatObs);
            else if (req.Kind == HandoverPendingKind.ReclaimToHuman)
                MarkReclaimBriefingReady(req.SeatId, req.FactionName, boundarySeatObs);

            return outcome;
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
