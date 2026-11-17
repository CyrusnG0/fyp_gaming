using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Rich-deliberation experiment support for the production runner (opt-in; see
    /// <see cref="LlmConfig.ExperimentActive"/>). Everything here is inert for a config without the
    /// experiment profile: no extra record, no extra observation call and no prompt change.
    ///
    /// Ceilings: request, accepted-action and inquiry counts and the per-turn/session wall clock are
    /// enforced by the shared ledger; provider token ceilings are enforced here before each dispatch.
    /// Whichever one ends a turn is named by <see cref="StopCeiling"/> and written to the turn-end
    /// exchange record. Inquiries and notes never reach the engine, so they cost no command point.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        private readonly Dictionary<string, List<RichInquiryEntry>> _inquiryLog =
            new Dictionary<string, List<RichInquiryEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _inquiryLogDropped =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _historyDropped =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Stopwatch _fallbackSessionClock = Stopwatch.StartNew();
        private long _sessionStartMillis = -1;
        private bool _settingsRecorded;
        private bool _turnEndRecorded;
        private string _stopDetail;
        private string _pendingDeliberationEvent;

        /// <summary>True only for the opt-in rich-deliberation experiment.</summary>
        public bool ExperimentActive => _config != null && _config.ExperimentActive;

        /// <summary>Notes saved in the current turn. Notes never cost command points or action slots.</summary>
        public int NotesSavedThisTurn { get; private set; }

        /// <summary>Inquiries answered in the current turn (also in the ledger as admissions).</summary>
        public int InquiriesThisTurn { get; private set; }

        /// <summary>JSON completeness report of the most recently dispatched rich prompt, or null.</summary>
        public string LastContextReportJson { get; private set; }

        /// <summary>
        /// Stable code of the ceiling or condition that ended the turn (for example
        /// <c>turn_requests_exhausted</c>, <c>session_output_tokens</c>, <c>session_seconds</c>), or
        /// null while running or when the model ended its own turn.
        /// </summary>
        public string StopCeiling
        {
            get
            {
                switch (EndReason)
                {
                    case EndReason.None:
                    case EndReason.ModelEndedTurn: return null;
                    case EndReason.ActionLimit: return _stopDetail ?? "turn_actions";
                    case EndReason.RequestLimit: return _stopDetail ?? "turn_requests";
                    case EndReason.SessionRequestLimit: return _stopDetail ?? "session_requests";
                    case EndReason.InquiryLimit: return _stopDetail ?? "turn_inquiries";
                    case EndReason.TurnTimeLimit: return _stopDetail ?? "turn_seconds";
                    case EndReason.InvalidResponseLimit:
                        return FailureReason != null && (FailureReason.StartsWith("output budget",
                                    StringComparison.Ordinal)
                                || FailureReason.StartsWith("response truncated", StringComparison.Ordinal))
                            ? "output_tokens_per_reply" : "invalid_responses";
                    case EndReason.UsdBudgetLimit:
                    case EndReason.UsdBudgetUnknown: return "usd_budget";
                    case EndReason.RequestTimeout: return "request_timeout_ms";
                    case EndReason.PromptTooLarge: return "prompt_chars";
                    case EndReason.ProviderError: return "provider_error";
                    case EndReason.TransportError: return "transport_error";
                    case EndReason.HostError: return "host_error";
                    case EndReason.GameOver: return "game_over";
                    case EndReason.Cancelled: return "cancelled";
                    case EndReason.DispatchDenied: return _stopDetail ?? "dispatch_denied";
                    case EndReason.DeliberationLimit: return _stopDetail ?? "deliberation_limit";
                    default: return null;
                }
            }
        }

        private void ResetExperimentTurnState()
        {
            NotesSavedThisTurn = 0;
            InquiriesThisTurn = 0;
            LastContextReportJson = null;
            ResetPromptSizeState();
            _stopDetail = null;
            _pendingDeliberationEvent = null;
            _turnEndRecorded = false;
        }

        // Provider token ceilings are per match: a reused runner keeps its lifetime totals for reporting
        // (TotalInputTokens/TotalOutputTokens) but each match starts with its own allowance and its own
        // known/unknown usage state.
        private string _tokenMatchId;
        private long _matchInput;
        private long _matchOutput;
        private bool _matchUsageUnknown;

        private void EnterTokenMatch(string gameId)
        {
            if (string.Equals(_tokenMatchId, gameId, StringComparison.Ordinal)) return;
            _tokenMatchId = gameId;
            _matchInput = 0;
            _matchOutput = 0;
            _matchUsageUnknown = false;
        }

        private void ClearExperimentMatchState()
        {
            _tokenMatchId = null;
            _matchInput = 0;
            _matchOutput = 0;
            _matchUsageUnknown = false;
            _inquiryLog.Clear();
            _inquiryLogDropped.Clear();
            _historyDropped.Clear();
        }

        /// <summary>Returns a stable failure text when a set experiment profile cannot run safely.</summary>
        private bool ExperimentConfigInvalid(out string failure)
        {
            failure = null;
            if (string.IsNullOrEmpty(_config.ExperimentProfile)) return false;
            IReadOnlyList<string> errors = _config.ValidateExperiment();
            if (errors.Count == 0) return false;
            _stopDetail = "experiment_configuration";
            failure = "experiment configuration invalid: " + string.Join(",", errors);
            return true;
        }

        private long SessionNowMillis()
            => _deliberationClock != null ? _deliberationClock.NowMillis : _fallbackSessionClock.ElapsedMilliseconds;

        private void MarkSessionStart()
        {
            // Reads the injected clock only when a session ceiling needs it, so a config without one
            // consumes no extra clock reads (a scripted test clock sees the historical sequence).
            if (_sessionStartMillis < 0 && _config.MaxSessionSeconds > 0)
                _sessionStartMillis = SessionNowMillis();
        }

        private bool SessionSecondsHit()
            => DeliberationActive && _config.MaxSessionSeconds > 0 && _sessionStartMillis >= 0
               && SessionNowMillis() - _sessionStartMillis >= _config.MaxSessionSeconds * 1000L;

        /// <summary>
        /// The wall-clock ceiling that has been reached, if any. The session ceiling is polled in
        /// flight only for the experiment; the other two are the historical checks.
        /// </summary>
        private string TimeCeilingHit()
        {
            if (ExperimentActive && SessionSecondsHit()) return "session_seconds";
            if (DeliberationWallExpired()) return "turn_deliberation_millis";
            if (TurnTimeExpired()) return "turn_seconds";
            return null;
        }

        /// <summary>
        /// Provider ceilings, checked before every dispatch. Output is strict (the next reply's own
        /// output bound must still fit); input can only be checked against usage already reported,
        /// so one reply may overshoot an input ceiling. Missing usage fails closed.
        /// </summary>
        private bool TokenCeilingHit(out string failure, out string ceiling)
        {
            failure = null;
            ceiling = null;
            if (!ExperimentActive) return false;
            bool inputCapped = _config.MaxSessionInputTokens > 0;
            bool outputCapped = _config.MaxSessionOutputTokens > 0;
            if ((inputCapped || outputCapped) && _matchUsageUnknown)
            {
                failure = "provider usage unknown with a token ceiling configured";
                ceiling = "session_usage_unknown";
                return true;
            }
            if (inputCapped && _matchInput >= _config.MaxSessionInputTokens)
            {
                failure = "session input token ceiling reached";
                ceiling = "session_input_tokens";
                return true;
            }
            int perReply = _config.MaxOutputTokens > 0 ? _config.MaxOutputTokens : 400;
            if (outputCapped && _matchOutput + perReply > _config.MaxSessionOutputTokens)
            {
                failure = "session output token ceiling reached";
                ceiling = "session_output_tokens";
                return true;
            }
            return false;
        }

        // ---- per-seat private inquiry log (cross-turn, rich mode only) ------------------

        private void LogInquiryResult(string tool, string json)
        {
            if (!ExperimentActive || string.IsNullOrEmpty(json) || string.IsNullOrEmpty(_seatId)) return;
            if (!_inquiryLog.TryGetValue(_seatId, out List<RichInquiryEntry> log))
            {
                log = new List<RichInquiryEntry>();
                _inquiryLog[_seatId] = log;
            }
            log.Add(new RichInquiryEntry { Season = _season, Tool = tool, Json = json });
            // One extra slot: the newest entry is shown as PREVIOUS INQUIRY RESULT, the rest as prior.
            int keep = Math.Max(0, _config.ContextCrossTurnInquiryResults) + 1;
            while (log.Count > keep)
            {
                log.RemoveAt(0);
                _inquiryLogDropped[_seatId] = (_inquiryLogDropped.TryGetValue(_seatId, out int d) ? d : 0) + 1;
            }
        }

        private IReadOnlyList<RichInquiryEntry> PriorInquiryEntries(string latest, out int dropped)
        {
            dropped = _inquiryLogDropped.TryGetValue(_seatId ?? "", out int d) ? d : 0;
            var prior = new List<RichInquiryEntry>();
            if (_seatId != null && _inquiryLog.TryGetValue(_seatId, out List<RichInquiryEntry> log))
            {
                prior.AddRange(log);
                if (!string.IsNullOrEmpty(latest) && prior.Count > 0
                    && string.Equals(prior[prior.Count - 1].Json, latest, StringComparison.Ordinal))
                    prior.RemoveAt(prior.Count - 1);
            }
            return prior;
        }

        private int HistoryLimit => _config.EffectiveHistoryItems;

        private void CountHistoryDrop()
        {
            if (!ExperimentActive || string.IsNullOrEmpty(_seatId)) return;
            _historyDropped[_seatId] = (_historyDropped.TryGetValue(_seatId, out int d) ? d : 0) + 1;
        }

        // ---- rich prompt assembly -------------------------------------------------------

        private PromptBuilder.RichAllowances BuildAllowances()
        {
            DeliberationBudgetSnapshot snap = _deliberationLedger?.Snapshot();
            long turnReq = snap?.Turn?.Requests ?? RequestsThisTurn;
            long sessReq = snap?.Session?.Requests ?? TotalRequests;
            long turnInq = snap?.Turn?.Inquiries ?? 0;
            long sessInq = snap?.Session?.Inquiries ?? 0;
            long turnAct = snap?.Turn?.AcceptedActions ?? _acceptedActionsThisTurn;
            long sessAct = snap?.Session?.AcceptedActions ?? 0;
            var a = new PromptBuilder.RichAllowances
            {
                RequestsLeftTurn = (int)Math.Max(0, _config.MaxRequestsPerTurn - turnReq),
                RequestsLeftSession = (int)Math.Max(0, _config.MaxTotalRequests - sessReq),
                InquiriesLeftTurn = (int)Math.Max(0, _config.MaxInquiriesPerTurn - turnInq),
                InquiriesLeftSession = (int)Math.Max(0, _config.MaxInquiriesPerSession - sessInq),
                ActionsLeftTurn = (int)Math.Max(0, _config.MaxActionsPerTurn - turnAct),
                ActionsLeftSession = _config.MaxAcceptedActionsPerSession > 0
                    ? (int)Math.Max(0, _config.MaxAcceptedActionsPerSession - sessAct) : -1,
                OutputTokensPerReply = _config.MaxOutputTokens,
                NoteMaxChars = Math.Min(_config.MaxNoteChars, LlmStrategicNoteRules.MaxNoteChars),
            };
            long turnMs = _clock == null ? 0 : _clock.ElapsedMilliseconds;
            long turnCap = Math.Max(0, _config.MaxTurnSeconds) * 1000L;
            if (_config.MaxDeliberationTurnMillis > 0) turnCap = Math.Min(turnCap, _config.MaxDeliberationTurnMillis);
            a.SecondsLeftTurn = (int)Math.Max(0, (turnCap - turnMs) / 1000);
            a.SecondsLeftSession = _config.MaxSessionSeconds > 0 && _sessionStartMillis >= 0
                ? (int)Math.Max(0, (_config.MaxSessionSeconds * 1000L - (SessionNowMillis() - _sessionStartMillis)) / 1000)
                : -1;
            a.InputTokensLeftSession = TokensLeft(_config.MaxSessionInputTokens, _matchInput, _matchUsageUnknown);
            a.OutputTokensLeftSession = TokensLeft(_config.MaxSessionOutputTokens, _matchOutput, _matchUsageUnknown);
            return a;
        }

        private static long TokensLeft(int cap, long used, bool unknown)
        {
            if (cap <= 0) return -1;
            if (unknown) return -2;
            return Math.Max(0, cap - used);
        }

        private RichContextResult AssembleRichContext(Observation observation,
            List<ActionOption> options, MovementView movement, AttackView attacks, LlmPersona persona,
            string inquiryResult, string version, int systemChars, int requestsLeft, int actionsLeft,
            int reservedChars)
        {
            IReadOnlyList<RichInquiryEntry> prior = PriorInquiryEntries(inquiryResult, out int priorDropped);
            var input = new RichContextInputs
            {
                Observation = observation,
                Options = options,
                Movement = movement,
                History = _history,
                HistoryDropped = _historyDropped.TryGetValue(_seatId, out int hd) ? hd : 0,
                Memories = _memories,
                PriorResults = prior,
                PriorDropped = priorDropped,
                LatestInquiryResult = inquiryResult,
                Note = _strategicNote,
                Persona = persona,
                AttackEnabled = AttackActive,
                Attacks = attacks,
                Version = version,
                SystemChars = systemChars,
                RequestsLeft = requestsLeft,
                ActionsLeft = actionsLeft,
                Allowances = BuildAllowances(),
                SpatialOptions = _config?.SpatialContext,
                ReservedChars = reservedChars,
            };
            return RichContextAssembler.Assemble(input, _config);
        }

        /// <summary>Config validity and the session wall clock, checked before every request.</summary>
        private bool ExperimentPreflight()
        {
            if (ExperimentConfigInvalid(out string failure))
            {
                FinishFailed(failure, EndReason.DeliberationLimit);
                return false;
            }
            if (ExperimentActive && SessionSecondsHit())
            {
                _stopDetail = "session_seconds";
                FinishLimit("session time limit reached", EndReason.TurnTimeLimit);
                return false;
            }
            return true;
        }

        /// <summary>Provider token ceilings, checked after the ledger is bound and before dispatch.</summary>
        private bool ExperimentTokenGate()
        {
            if (!TokenCeilingHit(out string failure, out string ceiling)) return true;
            _stopDetail = ceiling;
            FinishLimit(failure, EndReason.DeliberationLimit);
            return false;
        }

        private bool TryBuildRichPrompt(Observation observation, List<ActionOption> options,
            MovementView movement, AttackView attacks, LlmPersona persona, string inquiryResult,
            string version, int systemChars, int requestsLeft, int actionsLeft, int reservedChars,
            out string user, out string observationJson)
        {
            RichContextResult assembled = AssembleRichContext(observation, options, movement, attacks,
                persona, inquiryResult, version, systemChars, requestsLeft, actionsLeft, reservedChars);
            user = assembled.User;
            observationJson = assembled.ObservationJson;
            LastSpatialTruncation = assembled.SpatialTruncation;
            LastContextReportJson = assembled.Report.ToJson();
            _lastRichSizeReport = assembled.SizeReport;
            if (assembled.Report.Fits) return true;
            LastPromptSizeJson = assembled.SizeReport != null
                ? assembled.SizeReport.ToJson()
                : PromptSizeReport.FinalOnly(systemChars, user.Length, assembled.Report.MaxPromptChars,
                    reservedChars, "mandatory_context_exceeds_limit").ToJson();
            _stopDetail = "prompt_chars";
            FinishFailed("prompt too large", EndReason.PromptTooLarge);
            return false;
        }
    }
}
