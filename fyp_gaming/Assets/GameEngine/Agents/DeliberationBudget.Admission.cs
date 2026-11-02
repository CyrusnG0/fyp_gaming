using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Admission half of the ledger. Every unit of work is admitted before dispatch: the check runs
    /// against the host-bound (seat, turn token) bucket, consumes the admission slot and holds any token
    /// reservation until <see cref="Settle"/> or <see cref="Cancel"/> finalizes it.
    /// </summary>
    public sealed partial class DeliberationBudgetLedger
    {
        /// <summary>Admit one provider round-trip with host-supplied input/output token bounds.</summary>
        public DeliberationAdmission TryAdmitRequest(DeliberationTokenBounds bounds)
            => TryAdmit(DeliberationWorkKind.ModelRequest, bounds);

        /// <summary>Admit one read-only inquiry. It consumes inquiry and wall-time budget only.</summary>
        public DeliberationAdmission TryAdmitInquiry()
            => TryAdmit(DeliberationWorkKind.Inquiry, DeliberationTokenBounds.None);

        /// <summary>Admit one candidate game action. It consumes the action allowance and wall time only.</summary>
        public DeliberationAdmission TryAdmitGameAction()
            => TryAdmit(DeliberationWorkKind.GameAction, DeliberationTokenBounds.None);

        /// <summary>
        /// The single admission path. Checks run in a documented, stable order: binding, stopped state,
        /// request shape, clock, wall time, per-turn counters, session counters, then token reservations.
        /// A denial changes no usage counter. An allowed admission consumes its counters and holds its
        /// token reservation until <see cref="Settle"/> or <see cref="Cancel"/> finalizes it.
        /// </summary>
        public DeliberationAdmission TryAdmit(DeliberationWorkKind kind, DeliberationTokenBounds bounds)
        {
            lock (_gate)
            {
                var admission = NewAdmission(kind);
                if (!IsKnownKind(kind))
                    return Deny(admission, DeliberationDenyReason.InvalidRequest,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.InvalidRequest));
                if (_turn == null)
                    return Deny(admission, DeliberationDenyReason.NotBound,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.NotBound));
                if (_blocked) return Deny(admission, _blockReason, _blockMessage);

                if (kind != DeliberationWorkKind.ModelRequest && !bounds.IsNone)
                    return Deny(admission, DeliberationDenyReason.InvalidTokenBounds,
                        "only a model request may reserve tokens; inquiry and game actions do not.");
                if (bounds.HasNegative || bounds.Input > _limits.MaxTokenBoundPerRequest
                    || bounds.Output > _limits.MaxTokenBoundPerRequest)
                    return Deny(admission, DeliberationDenyReason.InvalidTokenBounds,
                        "token bounds must be non-negative and within the configured per-request bound.");
                if (kind == DeliberationWorkKind.ModelRequest && _policy.TokenBudgetEnforced
                    && (bounds.Input <= 0 || bounds.Output <= 0))
                    return Deny(admission, DeliberationDenyReason.TokenBoundRequired,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.TokenBoundRequired));

                long now;
                if (!TryReadClock(out now))
                    return Deny(admission, DeliberationDenyReason.ClockInvalid, _blockMessage);

                long turnElapsed = now - _turn.StartMillis;
                long sessionElapsed = now - _sessionStartMillis;
                // Wall-clock fields are the observation behind a wall-time decision, not a usage
                // counter. Recording them here means an exhaustion denial can still report the exact
                // elapsed value; no request/inquiry/action/token counter changes on a denial.
                _turn.Counters.WallClockMillis = turnElapsed;
                _session.WallClockMillis = sessionElapsed;

                if (DeliberationBudgetLimits.IsEnforced(_limits.MaxWallMillisPerTurn)
                    && turnElapsed >= _limits.MaxWallMillisPerTurn)
                    return Deny(admission, DeliberationDenyReason.TurnWallTimeExhausted,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.TurnWallTimeExhausted));
                if (DeliberationBudgetLimits.IsEnforced(_limits.MaxWallMillisPerSession)
                    && sessionElapsed >= _limits.MaxWallMillisPerSession)
                    return Deny(admission, DeliberationDenyReason.SessionWallTimeExhausted,
                        DeliberationBudgetReasons.Message(DeliberationDenyReason.SessionWallTimeExhausted));

                DeliberationDenyReason denial = CheckAllowance(kind, bounds);
                if (denial != DeliberationDenyReason.None)
                    return Deny(admission, denial, DeliberationBudgetReasons.Message(denial));

                Consume(kind, bounds, out string reservationId);

                admission.Allowed = true;
                admission.Reason = DeliberationDenyReason.None;
                admission.Message = "allowed";
                admission.ReservationId = reservationId;
                admission.ReservedInputTokens = bounds.Input;
                admission.ReservedOutputTokens = bounds.Output;
                admission.Exhausted = CheckAllowance(kind, bounds) != DeliberationDenyReason.None;
                return admission;
            }
        }

        // ---- internals ---------------------------------------------------------

        private DeliberationDenyReason CheckAllowance(DeliberationWorkKind kind, DeliberationTokenBounds bounds)
        {
            switch (kind)
            {
                case DeliberationWorkKind.ModelRequest:
                    return FirstDenial(
                        CheckCounter(_limits.MaxRequestsPerTurn, _turn.Counters.Requests,
                            _limits.MaxRequestsPerSession, _session.Requests,
                            DeliberationDenyReason.TurnRequestsExhausted,
                            DeliberationDenyReason.SessionRequestsExhausted),
                        CheckReservation(_limits.MaxInputTokensPerTurn, _turn.Counters.CommittedInputTokens,
                            _limits.MaxInputTokensPerSession, _session.CommittedInputTokens, bounds.Input,
                            DeliberationDenyReason.TurnInputTokensExhausted,
                            DeliberationDenyReason.SessionInputTokensExhausted),
                        CheckReservation(_limits.MaxOutputTokensPerTurn, _turn.Counters.CommittedOutputTokens,
                            _limits.MaxOutputTokensPerSession, _session.CommittedOutputTokens, bounds.Output,
                            DeliberationDenyReason.TurnOutputTokensExhausted,
                            DeliberationDenyReason.SessionOutputTokensExhausted));
                case DeliberationWorkKind.Inquiry:
                    return CheckCounter(_limits.MaxInquiryPerTurn, _turn.Counters.Inquiries,
                        _limits.MaxInquiryPerSession, _session.Inquiries,
                        DeliberationDenyReason.TurnInquiryExhausted,
                        DeliberationDenyReason.SessionInquiryExhausted);
                case DeliberationWorkKind.GameAction:
                    // The enforced allowance is for accepted actions. A reservation is held between
                    // admission and engine validation, then released if the engine rejects the order.
                    return CheckCounter(_limits.MaxActionsPerTurn, _turn.Counters.CommittedActions,
                        _limits.MaxActionsPerSession, _session.CommittedActions,
                        DeliberationDenyReason.TurnActionsExhausted,
                        DeliberationDenyReason.SessionActionsExhausted);
                default:
                    return DeliberationDenyReason.InvalidRequest;
            }
        }

        private static DeliberationDenyReason FirstDenial(params DeliberationDenyReason[] reasons)
        {
            foreach (DeliberationDenyReason reason in reasons)
                if (reason != DeliberationDenyReason.None) return reason;
            return DeliberationDenyReason.None;
        }

        private static DeliberationDenyReason CheckCounter(int turnLimit, long turnUsed,
            int sessionLimit, long sessionUsed, DeliberationDenyReason turnReason,
            DeliberationDenyReason sessionReason)
        {
            if (DeliberationBudgetLimits.IsEnforced(turnLimit) && turnUsed >= turnLimit) return turnReason;
            if (DeliberationBudgetLimits.IsEnforced(sessionLimit) && sessionUsed >= sessionLimit) return sessionReason;
            return DeliberationDenyReason.None;
        }

        /// <summary>A token admission needs its whole upper bound, not just the next increment.</summary>
        private static DeliberationDenyReason CheckReservation(int turnLimit, long turnCommitted,
            int sessionLimit, long sessionCommitted, int requested, DeliberationDenyReason turnReason,
            DeliberationDenyReason sessionReason)
        {
            if (requested <= 0) return DeliberationDenyReason.None;
            if (turnCommitted > long.MaxValue - requested
                || sessionCommitted > long.MaxValue - requested)
                return DeliberationDenyReason.AccountingOverflow;
            if (DeliberationBudgetLimits.IsEnforced(turnLimit) && turnCommitted + requested > turnLimit)
                return turnReason;
            if (DeliberationBudgetLimits.IsEnforced(sessionLimit) && sessionCommitted + requested > sessionLimit)
                return sessionReason;
            return DeliberationDenyReason.None;
        }

        private void Consume(DeliberationWorkKind kind, DeliberationTokenBounds bounds,
            out string reservationId)
        {
            Counters seat = Seat(_turn.SeatId);

            switch (kind)
            {
                case DeliberationWorkKind.ModelRequest:
                    _turn.Counters.Requests++; _session.Requests++; seat.Requests++;
                    _turn.Counters.ReservedInputTokens += bounds.Input;
                    _session.ReservedInputTokens += bounds.Input;
                    _turn.Counters.ReservedOutputTokens += bounds.Output;
                    _session.ReservedOutputTokens += bounds.Output;
                    break;
                case DeliberationWorkKind.Inquiry:
                    _turn.Counters.Inquiries++; _session.Inquiries++; seat.Inquiries++;
                    break;
                default:
                    _turn.Counters.ActionAdmissions++; _session.ActionAdmissions++; seat.ActionAdmissions++;
                    _turn.Counters.ReservedActions++; _session.ReservedActions++; seat.ReservedActions++;
                    break;
            }

            _reservationSequence++;
            reservationId = "dr-" + _reservationSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _reservations[reservationId] = new Reservation
            {
                Id = reservationId,
                Kind = kind,
                SeatId = _turn.SeatId,
                TurnToken = _turn.TurnToken,
                TurnCounters = _turn.Counters,
                ReservedInputTokens = bounds.Input,
                ReservedOutputTokens = bounds.Output,
                InputBounded = bounds.Input > 0,
                OutputBounded = bounds.Output > 0,
                Dispatched = false,
                Finalized = false,
            };
        }
    }
}
