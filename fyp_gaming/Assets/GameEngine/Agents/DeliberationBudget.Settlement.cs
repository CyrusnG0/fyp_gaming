using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Settlement half of the ledger: mark dispatch, settle reported usage once, or cancel. Admission
    /// already consumed the request/inquiry/action slot, so none of these methods can grant new work.
    /// </summary>
    public sealed partial class DeliberationBudgetLedger
    {
        /// <summary>
        /// Record that the admitted work was handed to the provider (or tool). Only an undispatched
        /// reservation can be released by <see cref="Cancel"/>; after this call the reservation's upper
        /// bound counts even if the call is cancelled or times out.
        /// </summary>
        public DeliberationBindingResult MarkDispatched(string reservationId)
        {
            lock (_gate)
            {
                Reservation reservation;
                if (string.IsNullOrEmpty(reservationId) || !_reservations.TryGetValue(reservationId, out reservation))
                    return Binding(false, DeliberationDenyReason.UnknownReservation,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.UnknownReservation));
                if (reservation.Finalized)
                    return Binding(false, DeliberationDenyReason.AlreadySettled,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.AlreadySettled));

                reservation.Dispatched = true;
                return Binding(true, DeliberationDenyReason.None, "dispatched");
            }
        }

        /// <summary>
        /// Account one reservation exactly once. Reported usage is charged as measured; a missing input or
        /// output usage charge falls back to that part's reserved upper bound and is recorded as unknown.
        /// Usage above the reservation is recorded in full and halts further admission. A malformed
        /// settlement is rejected without changing any counter, so the host can retry with valid values.
        /// </summary>
        public DeliberationSettlementResult Settle(string reservationId, DeliberationSettlement usage)
        {
            lock (_gate)
            {
                DeliberationSettlementResult result = NewSettlementResult(reservationId);
                Reservation reservation;
                if (string.IsNullOrEmpty(reservationId) || !_reservations.TryGetValue(reservationId, out reservation))
                    return SettlementFailed(result, DeliberationSettlementOutcome.UnknownReservation,
                        DeliberationDenyReason.UnknownReservation);
                if (reservation.Finalized)
                    return SettlementFailed(result, DeliberationSettlementOutcome.AlreadySettled,
                        DeliberationDenyReason.AlreadySettled);

                string invalid = ValidateUsage(usage);
                if (invalid != null)
                {
                    result.Message = invalid;
                    return SettlementFailed(result, DeliberationSettlementOutcome.Rejected,
                        DeliberationDenyReason.InvalidUsage);
                }

                // Only a model request reserves tokens. Inquiry and game-action settlements carry no
                // token usage, so a missing usage field there is normal and never an accounting gap.
                // Request-limited mode may dispatch with no token bound at all; measured usage is then
                // recorded without an overrun, because nothing was reserved for it to exceed.
                bool tokenBearing = reservation.Kind == DeliberationWorkKind.ModelRequest;
                bool knownInput = tokenBearing && usage.InputTokens.HasValue;
                bool knownOutput = tokenBearing && usage.OutputTokens.HasValue;
                long chargeInput = tokenBearing
                    ? (knownInput ? usage.InputTokens.Value
                        : (reservation.InputBounded ? reservation.ReservedInputTokens : 0)) : 0;
                long chargeOutput = tokenBearing
                    ? (knownOutput ? usage.OutputTokens.Value
                        : (reservation.OutputBounded ? reservation.ReservedOutputTokens : 0)) : 0;
                bool overrun = tokenBearing
                               && ((knownInput && reservation.InputBounded
                                    && chargeInput > reservation.ReservedInputTokens)
                                   || (knownOutput && reservation.OutputBounded
                                       && chargeOutput > reservation.ReservedOutputTokens));
                bool usageUnknown = tokenBearing && (!knownInput || !knownOutput);
                bool appliedToTurn = ReferenceEquals(reservation.TurnCounters, _turn);

                Apply(reservation, chargeInput, chargeOutput, usage.ElapsedMillis, usage.Accepted,
                    usage.Failed, usageUnknown, overrun);

                if (overrun)
                {
                    Block(DeliberationDenyReason.TokenOverrun,
                        "reported usage " + chargeInput + "/" + chargeOutput
                        + " exceeded the reservation " + reservation.ReservedInputTokens + "/"
                        + reservation.ReservedOutputTokens
                        + "; the excess is recorded and further admission is halted.",
                        reservation.SeatId, reservation.TurnToken);
                }
                if (usageUnknown)
                {
                    string message = "the provider reported no token usage for " + reservation.Kind
                                     + "; the missing side was charged at its reserved upper bound.";
                    if (_policy.TokenBudgetEnforced)
                        Block(DeliberationDenyReason.UsageUnknown, message,
                            reservation.SeatId, reservation.TurnToken);
                    else
                        RecordHalt(DeliberationDenyReason.UsageUnknown, message,
                            reservation.SeatId, reservation.TurnToken);
                }

                result.Outcome = DeliberationSettlementOutcome.Applied;
                result.Reason = DeliberationDenyReason.None;
                result.Message = "settled";
                result.InputTokens = chargeInput;
                result.OutputTokens = chargeOutput;
                result.InputUsageKnown = knownInput;
                result.OutputUsageKnown = knownOutput;
                result.UsageUnknown = usageUnknown;
                result.Overrun = overrun;
                result.Accepted = usage.Accepted && reservation.Kind == DeliberationWorkKind.GameAction;
                result.AppliedToTurn = appliedToTurn;
                result.Blocked = _blocked;
                result.StopCode = _blocked ? DeliberationBudgetReasons.Code(_blockReason) : null;
                return result;
            }
        }

        /// <summary>
        /// Abandon an admission. Before dispatch only this reservation's token holds are released; the
        /// admission slot stays consumed, so repeated admit/cancel cycles cannot buy extra requests.
        /// After dispatch the reserved upper bound is charged as unknown usage and, for a token-strict
        /// policy, admission stops because the real usage is unknowable.
        /// </summary>
        public DeliberationCancelResult Cancel(string reservationId, DeliberationCancelReason reason)
        {
            lock (_gate)
            {
                var result = new DeliberationCancelResult
                {
                    ReservationId = reservationId,
                    CancelReason = reason,
                    TokenBudgetEnforced = _policy.TokenBudgetEnforced,
                };

                Reservation reservation;
                if (string.IsNullOrEmpty(reservationId) || !_reservations.TryGetValue(reservationId, out reservation))
                {
                    result.Outcome = DeliberationCancelOutcome.UnknownReservation;
                    result.Reason = DeliberationDenyReason.UnknownReservation;
                    result.Message = DeliberationBudgetReasons.Message(DeliberationDenyReason.UnknownReservation);
                    return result;
                }
                if (reservation.Finalized)
                {
                    result.Outcome = DeliberationCancelOutcome.AlreadySettled;
                    result.Reason = DeliberationDenyReason.AlreadySettled;
                    result.Message = DeliberationBudgetReasons.Message(DeliberationDenyReason.AlreadySettled);
                    return result;
                }

                if (!reservation.Dispatched)
                {
                    int releasedInput = reservation.ReservedInputTokens;
                    int releasedOutput = reservation.ReservedOutputTokens;
                    ReleaseReservation(reservation);
                    result.Outcome = DeliberationCancelOutcome.Released;
                    result.Reason = DeliberationDenyReason.None;
                    result.Message = "released before dispatch; the admission slot stays consumed.";
                    result.ReleasedInputTokens = releasedInput;
                    result.ReleasedOutputTokens = releasedOutput;
                }
                else
                {
                    bool tokenBearing = reservation.Kind == DeliberationWorkKind.ModelRequest;
                    long chargeInput = tokenBearing
                        ? (reservation.InputBounded ? reservation.ReservedInputTokens : 0) : 0;
                    long chargeOutput = tokenBearing
                        ? (reservation.OutputBounded ? reservation.ReservedOutputTokens : 0) : 0;
                    Apply(reservation, chargeInput, chargeOutput, 0, false, false, tokenBearing, false);
                    result.Outcome = DeliberationCancelOutcome.Charged;
                    result.Reason = DeliberationDenyReason.None;
                    result.Message = tokenBearing
                        ? "dispatched work was abandoned; the reserved upper bound counts as unknown usage."
                        : "dispatched inquiry or action was abandoned; its admission slot stays consumed.";
                    result.ChargedInputTokens = chargeInput;
                    result.ChargedOutputTokens = chargeOutput;
                    result.UsageUnknown = tokenBearing;

                    if (tokenBearing)
                    {
                        string message = "dispatched " + reservation.Kind.ToString()
                                         + " ended as " + reason.ToString()
                                         + " without provider usage; the reservation was charged.";
                        if (_policy.TokenBudgetEnforced)
                            Block(DeliberationDenyReason.UsageUnknown, message,
                                reservation.SeatId, reservation.TurnToken);
                        else
                            RecordHalt(DeliberationDenyReason.UsageUnknown, message,
                                reservation.SeatId, reservation.TurnToken);
                    }
                }

                result.Blocked = _blocked;
                result.StopCode = _blocked ? DeliberationBudgetReasons.Code(_blockReason) : null;
                return result;
            }
        }

        // ---- internals ---------------------------------------------------------

        private static string ValidateUsage(DeliberationSettlement usage)
        {
            if (usage == null) return "a settlement payload is required.";
            if (usage.InputTokens.HasValue && usage.InputTokens.Value < 0)
                return "reported input tokens cannot be negative.";
            if (usage.OutputTokens.HasValue && usage.OutputTokens.Value < 0)
                return "reported output tokens cannot be negative.";
            if (usage.ElapsedMillis < 0)
                return "measured elapsed milliseconds cannot be negative.";
            return null;
        }

        /// <summary>
        /// Books one final charge. Token usage is always charged (known or reserved fallback) and only
        /// this reservation's holds are released. Counters of a closed turn still absorb the charge so
        /// the session total stays exact, but they are no longer visible in the current turn view.
        /// </summary>
        private void Apply(Reservation reservation, long input, long output, long elapsedMillis,
            bool accepted, bool failed, bool usageUnknown, bool overrun)
        {
            Counters turn = reservation.TurnCounters;
            Counters seat = Seat(reservation.SeatId);

            turn.InputTokens += input; _session.InputTokens += input; seat.InputTokens += input;
            turn.OutputTokens += output; _session.OutputTokens += output; seat.OutputTokens += output;

            ReleaseReservation(reservation);

            if (elapsedMillis > 0)
            {
                if (turn.MeasuredElapsedMillis > long.MaxValue - elapsedMillis
                    || _session.MeasuredElapsedMillis > long.MaxValue - elapsedMillis)
                {
                    Block(DeliberationDenyReason.AccountingOverflow,
                        "measured elapsed time would overflow the ledger counters; it was not added.",
                        reservation.SeatId, reservation.TurnToken);
                }
                else
                {
                    turn.MeasuredElapsedMillis += elapsedMillis;
                    _session.MeasuredElapsedMillis += elapsedMillis;
                }
            }
            if (failed)
            {
                turn.Failures++; _session.Failures++; seat.Failures++;
            }
            if (usageUnknown)
            {
                turn.UnknownUsageEvents++; _session.UnknownUsageEvents++; seat.UnknownUsageEvents++;
            }
            if (overrun)
            {
                turn.OverrunEvents++; _session.OverrunEvents++; seat.OverrunEvents++;
            }
            if (accepted && reservation.Kind == DeliberationWorkKind.GameAction)
            {
                turn.AcceptedActions++; _session.AcceptedActions++; seat.AcceptedActions++;
            }
        }

        /// <summary>
        /// Releases exactly this reservation's holds. An action admission reserves one accepted-action
        /// slot; a rejected or cancelled action returns it, while an accepted one is converted to a
        /// permanent accepted-action charge by <see cref="Apply"/>.
        /// </summary>
        private void ReleaseReservation(Reservation reservation)
        {
            Counters turn = reservation.TurnCounters;
            turn.ReservedInputTokens -= reservation.ReservedInputTokens;
            _session.ReservedInputTokens -= reservation.ReservedInputTokens;
            turn.ReservedOutputTokens -= reservation.ReservedOutputTokens;
            _session.ReservedOutputTokens -= reservation.ReservedOutputTokens;

            if (reservation.Kind == DeliberationWorkKind.GameAction)
            {
                turn.ReservedActions--;
                _session.ReservedActions--;
                Seat(reservation.SeatId).ReservedActions--;
            }

            reservation.Finalized = true;
        }

        private DeliberationSettlementResult NewSettlementResult(string reservationId)
        {
            return new DeliberationSettlementResult
            {
                ReservationId = reservationId,
                TokenBudgetEnforced = _policy.TokenBudgetEnforced,
                Blocked = _blocked,
                StopCode = _blocked ? DeliberationBudgetReasons.Code(_blockReason) : null,
            };
        }

        private static DeliberationSettlementResult SettlementFailed(DeliberationSettlementResult result,
            DeliberationSettlementOutcome outcome, DeliberationDenyReason reason)
        {
            result.Outcome = outcome;
            result.Reason = reason;
            if (string.IsNullOrEmpty(result.Message)) result.Message = DeliberationBudgetReasons.Message(reason);
            return result;
        }
    }
}
