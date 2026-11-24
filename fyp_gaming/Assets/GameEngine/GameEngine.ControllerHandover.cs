using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>Result of a host handover call. <see cref="RequestId"/> is set when a request exists.</summary>
    public readonly struct HandoverResult
    {
        public readonly bool Ok;
        public readonly string Error;
        public readonly string RequestId;
        public HandoverResult(bool ok, string error, string requestId)
        { Ok = ok; Error = error; RequestId = requestId; }
    }

    /// <summary>Detached snapshot of a seat's handover state: the effective controller and any pending request.</summary>
    public sealed class HandoverStatus
    {
        public string SeatId;
        /// <summary>The effective controller right now. A pending request does not change this.</summary>
        public string Controller;
        public bool HasPending;
        public string PendingRequestId;
        public string PendingToController;
        public int PendingRequestedSeason;
    }

    /// <summary>
    /// The host capability for human/LLM handover (hotseat authority, not network identity). Only the object
    /// returned by <see cref="GameEngine.CreateHandoverOperator"/> can request or cancel a handover, and only
    /// for the seats the host named when creating it. It is never reachable from a model reply or prompt.
    /// </summary>
    public sealed class HandoverOperator
    {
        private readonly GameEngine _engine;
        private readonly HashSet<string> _eligible;

        internal HandoverOperator(GameEngine engine, IEnumerable<string> eligibleSeatIds)
        {
            _engine = engine;
            _eligible = new HashSet<string>(eligibleSeatIds, StringComparer.Ordinal);
        }

        public bool IsEligible(string seatId) => seatId != null && _eligible.Contains(seatId);

        /// <summary>Queues a request for <paramref name="seatId"/> to be controlled by <paramref name="toController"/>.</summary>
        public HandoverResult Request(string seatId, string toController)
            => _engine.HandoverRequest(this, seatId, toController);

        /// <summary>Cancels the seat's pending request explicitly.</summary>
        public HandoverResult Cancel(string seatId)
            => _engine.HandoverCancel(this, seatId);
    }

    /// <summary>
    /// Human to LLM and back, effective only between a seat's own turns. A request is stored outside
    /// <see cref="FactionState"/> (one per seat); <see cref="FactionState.Controller"/> changes only at the
    /// two boundary hooks below, never because another seat's turn ended and never mid-turn. The turn token
    /// and earlier records are never rewritten.
    /// </summary>
    public sealed partial class GameEngine
    {
        private sealed class PendingHandover
        {
            public string RequestId, ToController;
            public int Season;
        }

        private HandoverOperator handoverOperator;
        private readonly Dictionary<string, PendingHandover> pendingHandovers
            = new Dictionary<string, PendingHandover>(StringComparer.Ordinal);
        private int nextHandoverId = 1;

        /// <summary>Host-only, once per engine: mints the capability for <paramref name="eligibleSeatIds"/>.</summary>
        public HandoverOperator CreateHandoverOperator(IEnumerable<string> eligibleSeatIds)
        {
            if (eligibleSeatIds == null) throw new ArgumentNullException(nameof(eligibleSeatIds));
            if (handoverOperator != null)
                throw new InvalidOperationException("handover operator already created");
            var seats = new List<string>(eligibleSeatIds);
            foreach (string seat in seats)
                if (State.FindFaction(seat) == null)
                    throw new ArgumentException($"unknown seat '{seat}'", nameof(eligibleSeatIds));
            handoverOperator = new HandoverOperator(this, seats);
            return handoverOperator;
        }

        /// <summary>Read-only status of one seat; null for an unknown seat.</summary>
        public HandoverStatus GetHandoverStatus(string seatId)
        {
            var fac = State.FindFaction(seatId);
            if (fac == null) return null;
            pendingHandovers.TryGetValue(seatId, out var p);
            return new HandoverStatus
            {
                SeatId = seatId, Controller = fac.Controller, HasPending = p != null,
                PendingRequestId = p?.RequestId, PendingToController = p?.ToController,
                PendingRequestedSeason = p?.Season ?? 0,
            };
        }

        /// <summary>
        /// Host hook, call before planning <paramref name="seatId"/>'s turn: applies a pending request when the
        /// seat is not mid-turn. A no-op during that seat's own active turn and for other seats' boundaries.
        /// Returns true when the controller changed.
        /// </summary>
        public bool PrepareSeatTurn(string seatId)
        {
            if (seatId == null || string.Equals(seatId, activeSeatId, StringComparison.Ordinal)) return false;
            return ApplyPendingHandover(seatId, "before_turn");
        }

        internal HandoverResult HandoverRequest(HandoverOperator op, string seatId, string toController)
        {
            var fac = seatId == null ? null : State.FindFaction(seatId);
            if (op == null || !ReferenceEquals(op, handoverOperator) || fac == null || !op.IsEligible(seatId))
                return HandoverReject(null, seatId, toController, "handover not authorized");
            if (toController != ControllerType.Human && toController != ControllerType.Llm)
                return HandoverReject(fac, seatId, toController, "invalid target controller");
            if (fac.Controller != ControllerType.Human && fac.Controller != ControllerType.Llm)
                return HandoverReject(fac, seatId, toController, "seat is not human or llm controlled");
            if (State.WinnerSeatId != null) return HandoverReject(fac, seatId, toController, "game over");
            if (pendingHandovers.ContainsKey(seatId))
                return HandoverReject(fac, seatId, toController, "handover already pending");
            if (fac.Controller == toController)
                return HandoverReject(fac, seatId, toController, "already controlled by target");

            string id = "h-" + nextHandoverId++;
            pendingHandovers[seatId] = new PendingHandover { RequestId = id, ToController = toController, Season = State.Season };
            Emit(seatId, fac.Controller, "controller_handover_requested", "owner_only",
                P("seat_id", seatId, "request_id", id, "from_controller", fac.Controller,
                  "to_controller", toController,
                  "timing", string.Equals(activeSeatId, seatId, StringComparison.Ordinal)
                      ? "after_current_turn" : "before_next_turn"), null);
            return new HandoverResult(true, null, id);
        }

        internal HandoverResult HandoverCancel(HandoverOperator op, string seatId)
        {
            var fac = seatId == null ? null : State.FindFaction(seatId);
            if (op == null || !ReferenceEquals(op, handoverOperator) || fac == null || !op.IsEligible(seatId))
                return HandoverReject(null, seatId, null, "handover not authorized");
            if (!pendingHandovers.TryGetValue(seatId, out var p))
                return HandoverReject(fac, seatId, null, "no pending handover");
            pendingHandovers.Remove(seatId);
            Emit(seatId, fac.Controller, "controller_handover_cancelled", "owner_only",
                P("seat_id", seatId, "request_id", p.RequestId, "to_controller", p.ToController), null);
            return new HandoverResult(true, null, p.RequestId);
        }

        private HandoverResult HandoverReject(FactionState fac, string seatId, string to, string why)
        {
            // An unauthorized caller has no entitled seat: the record carries no actor and no seat_id party
            // key, so no seat receives it. Only the canonical log keeps it for audit.
            Emit(fac?.SeatId, fac?.Controller, "controller_handover_rejected", "owner_only",
                P(fac != null ? "seat_id" : "target_seat_id", seatId ?? "", "to_controller", to ?? "",
                  "reason", why), null);
            return new HandoverResult(false, why, null);
        }

        /// <summary>Applies the seat's pending request, if any. The caller guarantees the seat is between turns.</summary>
        private bool ApplyPendingHandover(string seatId, string via)
        {
            if (!pendingHandovers.TryGetValue(seatId, out var p)) return false;
            var fac = State.FindFaction(seatId);
            if (fac == null) return false;
            pendingHandovers.Remove(seatId);
            string from = fac.Controller;
            fac.Controller = p.ToController;
            Emit(seatId, fac.Controller, "controller_handover_effective", "public",
                P("seat_id", seatId, "request_id", p.RequestId, "from_controller", from,
                  "to_controller", p.ToController, "requested_season", p.Season.ToString(),
                  "applied_via", via), null);
            return true;
        }
    }
}
