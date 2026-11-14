using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in production deliberation (default OFF). When <see cref="LlmConfig.EnableDeliberation"/>
    /// is true the runner accepts the four strict read-only inquiry tools and one bounded private
    /// strategic note, and enforces one shared per-match <see cref="DeliberationBudgetLedger"/>.
    ///
    /// Accounting invariants:
    /// - The ledger is created lazily for the observed match id (or supplied by the host) and is
    ///   never recreated per turn, so session limits are shared by every seat using this runner.
    /// - Every provider request and every tool/action dispatch is admitted, marked dispatched and
    ///   settled (or cancelled) exactly once. A denied reservation is cancelled, never dispatched.
    /// - Inquiries consume an inquiry slot, a request slot and wall time, but never a command point
    ///   or an accepted-action slot. The game state is read-only during an inquiry.
    /// - The action allowance counts actions the engine accepts; rejected attempts release their
    ///   reservation. Feature-off keeps the historical attempt-based counters unchanged.
    /// - Notes and inquiry results are bounded, per-match/per-seat untrusted data. They are loaded
    ///   for exactly the active seat and never cross seats or matches.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        private readonly DeliberationBudgetLedger _injectedDeliberationLedger;
        private readonly IDeliberationClock _deliberationClock;
        private ILlmStrategicNoteStore _strategicNoteStore;
        private DeliberationBudgetLedger _deliberationLedger;
        private string _deliberationMatchId;
        private string _strategicNote;
        private readonly List<string> _retainedInquiryResults = new List<string>();
        private int _acceptedActionsThisTurn;

        /// <summary>True only when the opt-in production deliberation mode is configured.</summary>
        public bool DeliberationActive => _config != null && _config.EnableDeliberation;

        /// <summary>
        /// The shared ledger for this runner's current match, or null before the first bound
        /// observation / when deliberation is off. A host may inject the same ledger into several
        /// runners to share one match's session budget.
        /// </summary>
        public DeliberationBudgetLedger DeliberationLedger => _deliberationLedger;

        /// <summary>Detached budget evidence for a researcher panel or a test.</summary>
        public DeliberationBudgetSnapshot DeliberationBudget => _deliberationLedger?.Snapshot();

        /// <summary>Accepted (engine-validated) actions in this turn. Never includes rejections.</summary>
        public int AcceptedActionsThisTurn => _acceptedActionsThisTurn;

        /// <summary>The note loaded for the active seat, or null. Never another seat's note.</summary>
        public string CurrentStrategicNote => _strategicNote;

        private ILlmStrategicNoteStore NoteStore
        {
            get
            {
                if (_strategicNoteStore == null) _strategicNoteStore = new MemoryLlmStrategicNoteStore();
                return _strategicNoteStore;
            }
        }

        private void ResetDeliberationTurnState()
        {
            _acceptedActionsThisTurn = 0;
            _retainedInquiryResults.Clear();
            _strategicNote = null;
        }

        /// <summary>
        /// Binds the shared ledger to this match and the active seat/turn, then loads only this
        /// seat's private note. Returns false with a bounded reason so the turn stops before any
        /// provider or tool work rather than silently using different accounting.
        /// </summary>
        private bool PrepareDeliberation(Observation observation, out string failure)
        {
            failure = null;
            if (!DeliberationActive) return true;
            if (observation == null || string.IsNullOrEmpty(observation.GameId)
                || string.IsNullOrEmpty(_seatId))
            {
                failure = "deliberation match identity unavailable";
                return false;
            }

            if (_injectedDeliberationLedger != null)
            {
                if (!string.Equals(_injectedDeliberationLedger.MatchId, observation.GameId,
                        StringComparison.Ordinal))
                {
                    failure = "deliberation ledger match mismatch";
                    return false;
                }
                _deliberationLedger = _injectedDeliberationLedger;
                _deliberationMatchId = observation.GameId;
            }
            else if (_deliberationLedger == null
                     || !string.Equals(_deliberationMatchId, observation.GameId, StringComparison.Ordinal))
            {
                DeliberationBudgetPolicy policy;
                try { policy = BuildDeliberationPolicy(); }
                catch (Exception) { failure = "deliberation budget configuration invalid"; return false; }
                try
                {
                    _deliberationLedger = new DeliberationBudgetLedger(observation.GameId, policy,
                        _deliberationClock);
                }
                catch (Exception) { failure = "deliberation ledger could not be created"; return false; }
                _deliberationMatchId = observation.GameId;
                _retainedInquiryResults.Clear();
                _strategicNote = null;
            }

            DeliberationBindingResult binding = _deliberationLedger.BindTurn(_seatId, _turnToken);
            if (!binding.Ok)
            {
                failure = "deliberation turn binding denied (" + binding.Code + ")";
                return false;
            }
            // Notes are read through a match+seat key, so a shared runner or a second seat can
            // never load another player's note.
            _strategicNote = NoteStore.Load(observation.GameId, _seatId);
            return true;
        }

        /// <summary>
        /// Maps the live configuration onto one request-limited ledger policy. Request and accepted
        /// action caps stay separate. Deliberation-only dimensions are read only in this mode.
        /// </summary>
        private DeliberationBudgetPolicy BuildDeliberationPolicy()
        {
            var limits = new DeliberationBudgetLimits
            {
                MaxRequestsPerTurn = NonNegative(_config.MaxRequestsPerTurn),
                MaxRequestsPerSession = NonNegative(_config.MaxTotalRequests),
                MaxActionsPerTurn = NonNegative(_config.MaxActionsPerTurn),
                MaxActionsPerSession = _config.MaxAcceptedActionsPerSession > 0
                    ? _config.MaxAcceptedActionsPerSession : DeliberationBudgetLimits.Unenforced,
                MaxInquiryPerTurn = NonNegative(_config.MaxInquiriesPerTurn),
                MaxInquiryPerSession = NonNegative(_config.MaxInquiriesPerSession),
                MaxWallMillisPerTurn = _config.MaxDeliberationTurnMillis > 0
                    ? _config.MaxDeliberationTurnMillis : DeliberationBudgetLimits.Unenforced,
                MaxWallMillisPerSession = DeliberationBudgetLimits.Unenforced,
                MaxInputTokensPerTurn = DeliberationBudgetLimits.Unenforced,
                MaxInputTokensPerSession = DeliberationBudgetLimits.Unenforced,
                MaxOutputTokensPerTurn = DeliberationBudgetLimits.Unenforced,
                MaxOutputTokensPerSession = DeliberationBudgetLimits.Unenforced,
            };
            return DeliberationBudgetPolicies.Research("production-deliberation",
                DeliberationBudgetMode.RequestLimited, limits);
        }

        private static int NonNegative(int value) => value < 0 ? 0 : value;

        /// <summary>
        /// Reservation hint only: <see cref="LlmConfig.DeliberationInputTokenBound"/> and the output cap
        /// size the ledger reservation. Neither is an enforced token cap; real usage is whatever the
        /// provider reports and is settled afterwards. Request counts and wall time are the limits.
        /// </summary>
        private DeliberationTokenBounds DeliberationRequestBounds()
        {
            int input = Math.Max(0, _config.DeliberationInputTokenBound);
            int output = Math.Max(0, _config.MaxOutputTokens);
            return input == 0 && output == 0
                ? DeliberationTokenBounds.None
                : DeliberationTokenBounds.Of(input, output);
        }

        private bool TryAdmitDeliberationRequest(out DeliberationAdmission admission,
            out string failure)
        {
            admission = null;
            failure = null;
            if (!DeliberationActive) return true;
            if (_deliberationLedger == null) { failure = "deliberation ledger unavailable"; return false; }
            admission = _deliberationLedger.TryAdmitRequest(DeliberationRequestBounds());
            if (admission.Allowed) return true;
            failure = admission.Code;
            return false;
        }

        /// <summary>
        /// Admits one provider request and marks it dispatched, before any provider work. On a denial
        /// the turn is finished here: an admission denial sends nothing, and a dispatch denial first
        /// cancels the undispatched reservation. Returns true only when the request may be sent.
        /// </summary>
        private bool TryDispatchDeliberationRequest(out DeliberationAdmission admission)
        {
            if (!TryAdmitDeliberationRequest(out admission, out string deniedReason))
            { FinishDeliberationRequestDenied(admission, deniedReason); return false; }
            DeliberationBindingResult dispatch = _deliberationLedger.MarkDispatched(admission.ReservationId);
            if (dispatch.Ok) return true;
            _deliberationLedger.Cancel(admission.ReservationId, DeliberationCancelReason.Aborted);
            FinishFailed("deliberation request dispatch denied: " + dispatch.Code, EndReason.DispatchDenied);
            return false;
        }

        private bool TryAdmitDeliberationAction(out DeliberationAdmission admission,
            out string failure)
        {
            admission = null;
            failure = null;
            if (!DeliberationActive) return true;
            if (_deliberationLedger == null) { failure = "deliberation ledger unavailable"; return false; }
            admission = _deliberationLedger.TryAdmitGameAction();
            if (admission.Allowed) return true;
            failure = admission.Code;
            return false;
        }

        /// <summary>Settles one completed provider round-trip, exactly once (null response = failed).</summary>
        private void SettleDeliberationProvider(InFlight flight, LlmResponse response)
        {
            if (flight == null || string.IsNullOrEmpty(flight.DeliberationReservationId)
                || _deliberationLedger == null) return;
            string reservationId = flight.DeliberationReservationId;
            flight.DeliberationReservationId = null;
            bool ok = response != null && response.Ok;
            _deliberationLedger.Settle(reservationId, new DeliberationSettlement
            {
                InputTokens = ok ? response.InputTokens : null,
                OutputTokens = ok ? response.OutputTokens : null,
                ElapsedMillis = ElapsedMs(flight),
                Failed = !ok,
            });
        }

        private void AbandonDeliberationProvider(InFlight flight, DeliberationCancelReason reason)
        {
            if (flight == null || string.IsNullOrEmpty(flight.DeliberationReservationId)) return;
            string reservationId = flight.DeliberationReservationId;
            flight.DeliberationReservationId = null;
            _deliberationLedger?.Cancel(reservationId, reason);
        }

        private void AbandonDeliberationAdmission(DeliberationAdmission admission)
        {
            if (admission == null || string.IsNullOrEmpty(admission.ReservationId)
                || _deliberationLedger == null) return;
            _deliberationLedger.Cancel(admission.ReservationId, DeliberationCancelReason.Aborted);
        }

        private void FinishDeliberationRequestDenied(DeliberationAdmission admission, string code)
        {
            FailureReason = "deliberation request denied (" + (code ?? "unknown") + ")";
            EndReason reason;
            switch (admission == null ? DeliberationDenyReason.None : admission.Reason)
            {
                case DeliberationDenyReason.TurnWallTimeExhausted:
                case DeliberationDenyReason.SessionWallTimeExhausted:
                    reason = EndReason.TurnTimeLimit; break;
                case DeliberationDenyReason.SessionRequestsExhausted:
                    reason = EndReason.SessionRequestLimit; break;
                case DeliberationDenyReason.TurnRequestsExhausted:
                    reason = EndReason.RequestLimit; break;
                default:
                    reason = EndReason.DeliberationLimit; break;
            }
            EndReason = reason;
            Status = "limit: " + FailureReason;
            FinishTerminal(RunnerState.Finished, Status);
        }

        private void FinishDeliberationActionDenied(DeliberationAdmission admission, string code)
        {
            FailureReason = "deliberation action denied (" + (code ?? "unknown") + ")";
            EndReason = EndReason.ActionLimit;
            Status = "limit: " + FailureReason;
            FinishTerminal(RunnerState.Finished, Status);
        }
    }
}
