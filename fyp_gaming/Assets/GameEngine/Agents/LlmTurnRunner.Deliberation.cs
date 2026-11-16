using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Runner-side wiring for the LS-019 ledger: bind the trusted (seat, turn token), admit each
    /// request/inquiry/action before dispatch, and settle or cancel the reservation exactly once.
    /// The ledger's counters are evidence; the runner's existing limits stay the model-facing caps.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        private bool DeliberationActive => _config.EnableDeliberation;

        private int RemainingInquiries()
            => DeliberationActive ? Math.Max(0, _config.MaxInquiriesPerTurn - _inquiriesThisTurn) : -1;

        /// <summary>
        /// One ledger per match, bound to the same (seat, turn token) that the host passed in. A
        /// host-provided match id keeps the session counters correct across seats; without that
        /// capability the run id is stable for one session and is used instead.
        /// </summary>
        private bool TryBindDeliberation()
        {
            string matchId = _host is IDeliberationHost host ? host.MatchId : null;
            if (string.IsNullOrWhiteSpace(matchId)) matchId = _runId;
            matchId = SafeLedgerId(matchId);

            if (_ledger == null
                || !string.Equals(_deliberationMatchId, matchId, StringComparison.Ordinal))
            {
                try
                {
                    _ledger = new DeliberationBudgetLedger(matchId,
                        DeliberationBudgetPolicies.FromDemoConfig(_config));
                }
                catch (Exception)
                {
                    _deliberationStopCode = "invalid_binding";
                    return false;
                }
                _deliberationMatchId = matchId;
            }

            DeliberationBindingResult bound = _ledger.BindTurn(_seatId, _turnToken);
            if (!bound.Ok)
            {
                _deliberationStopCode = bound.Code;
                return false;
            }
            return true;
        }

        private static string SafeLedgerId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "match";
            if (value.Length <= DeliberationBudgetLedger.MaxIdLength) return value;
            return value.Substring(0, 48) + "-" + Hash(value).Substring(0, 8);
        }

        /// <summary>Stops the turn without dispatching denied work. The stable code is evidence.</summary>
        private void FinishDeliberationDenied(DeliberationAdmission admission)
        {
            _deliberationStopCode = admission == null ? "invalid_request" : admission.Code;
            FinishLimit("deliberation budget: " + _deliberationStopCode,
                EndReason.DeliberationBudget);
        }

        private void CancelRequestReservation(InFlight flight, DeliberationCancelReason reason)
        {
            if (_ledger == null || flight == null
                || string.IsNullOrEmpty(flight.DeliberationReservationId))
                return;
            string id = flight.DeliberationReservationId;
            flight.DeliberationReservationId = null;
            _ledger.Cancel(id, reason);
        }

        private void SettleRequestReservation(InFlight flight, LlmResponse response)
        {
            if (_ledger == null || flight == null
                || string.IsNullOrEmpty(flight.DeliberationReservationId))
                return;
            string id = flight.DeliberationReservationId;
            flight.DeliberationReservationId = null;
            _ledger.Settle(id, new DeliberationSettlement
            {
                InputTokens = response == null ? null : response.InputTokens,
                OutputTokens = response == null ? null : response.OutputTokens,
                ElapsedMillis = response == null ? 0 : Math.Max(0, response.LatencyMs),
                Failed = response == null || !response.Ok,
            });
        }
    }
}
