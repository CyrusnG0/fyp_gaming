using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in "rich deliberation" experiment settings (default OFF). Every value here is inert unless
    /// <see cref="ExperimentProfile"/> equals <see cref="RichDeliberationProfile"/> AND
    /// <see cref="EnableDeliberation"/> is true, so a config file without these keys behaves exactly
    /// as before. Request, accepted-action, inquiry, context, output and time/provider ceilings are
    /// separate fields; <see cref="ValidateExperiment"/> refuses an unset, non-finite or absurd value
    /// instead of silently repairing it, so a run can never be started without finite ceilings.
    /// </summary>
    public sealed partial class LlmConfig
    {
        public const string RichDeliberationProfile = "rich-deliberation-v1";

        // Defensive hard maxima. These are not recommended values: they only stop a unit mistake
        // (for example milliseconds written as seconds) from becoming an effectively open-ended run.
        public const int ExperimentHardMaxRequestsPerTurn = 200;
        public const int ExperimentHardMaxTotalRequests = 5000;
        public const int ExperimentHardMaxActionsPerTurn = 100;
        public const int ExperimentHardMaxActionsPerSession = 2000;
        public const int ExperimentHardMaxInquiriesPerTurn = 150;
        public const int ExperimentHardMaxInquiriesPerSession = 4000;
        public const int ExperimentHardMaxTurnSeconds = 1800;
        public const int ExperimentHardMaxSessionSeconds = 4 * 3600;
        public const int ExperimentHardMaxRequestTimeoutMs = 600000;
        public const int ExperimentHardMaxOutputTokens = 64000;
        public const int ExperimentHardMaxPromptChars = 300000;
        public const int ExperimentHardMaxSessionTokens = 2000000000;
        public const int ExperimentHardMaxContextItems = 500;
        public const int ExperimentHardMaxContextChars = 200000;

        /// <summary>Empty/null = experiment off. Only <see cref="RichDeliberationProfile"/> is recognised.</summary>
        public string ExperimentProfile;

        // ---- time / provider ceilings -------------------------------------------------
        /// <summary>
        /// Whole-session wall-clock ceiling in seconds across every seat sharing one ledger. Zero means
        /// "none". Read whenever deliberation is on; the experiment requires a positive value.
        /// </summary>
        public int MaxSessionSeconds;

        /// <summary>
        /// Provider ceilings on total reported usage across the session. Zero means "none". When one
        /// is set and the provider omits usage, the runner stops with "usage unknown" instead of
        /// counting the missing figure as zero.
        /// </summary>
        public int MaxSessionInputTokens;
        public int MaxSessionOutputTokens;

        // ---- context bounds (separate from request/action/inquiry/output bounds) ------------
        /// <summary>Whole-prompt character ceiling (system + user). Zero = the historical default.</summary>
        public int MaxPromptChars;

        /// <summary>Own-seat feedback lines shown and kept. Zero = historical 12.</summary>
        public int ContextHistoryItems;

        /// <summary>Character budget for retrieved long-term memories. Zero = historical default.</summary>
        public int ContextMemoryChars;

        /// <summary>Most recent observation events included in the prompt. Zero = historical 30.</summary>
        public int ContextEventItems;

        /// <summary>
        /// Earlier inquiry results kept per seat ACROSS turns of one match (rich mode). Zero keeps the
        /// historical behaviour: results live only inside the turn that asked for them.
        /// </summary>
        public int ContextCrossTurnInquiryResults;

        /// <summary>Character budget for the prior-inquiry-result section. Zero = no section.</summary>
        public int ContextInquiryChars;

        /// <summary>True only for a recognised profile together with deliberation.</summary>
        public bool ExperimentActive
            => EnableDeliberation
               && string.Equals(ExperimentProfile, RichDeliberationProfile, StringComparison.Ordinal);

        internal int EffectiveMaxPromptChars
            => ExperimentActive && MaxPromptChars > 0 ? MaxPromptChars : PromptBuilder.MaxPromptChars;

        internal int EffectiveHistoryItems
            => ExperimentActive && ContextHistoryItems > 0 ? ContextHistoryItems : LlmTurnRunner.MaxRecentHistory;

        internal int EffectiveMemoryChars
            => ExperimentActive && ContextMemoryChars > 0 ? ContextMemoryChars : LlmMemoryRetriever.DefaultMaxChars;

        internal int EffectiveEventItems
            => ExperimentActive && ContextEventItems > 0
                ? ContextEventItems : PromptBuilder.MaxPromptObservationEvents;

        /// <summary>
        /// A ready-to-use large-budget configuration for the rich-deliberation experiment. Nothing
        /// loads it automatically; a host or test opts in explicitly. Values are finite and below
        /// every hard maximum, and provider/key fields are left to the caller.
        /// </summary>
        public static LlmConfig RichDeliberation()
        {
            return new LlmConfig
            {
                ExperimentProfile = RichDeliberationProfile,
                EnableDeliberation = true,
                EnableManualLookup = false,
                MaxRequestsPerTurn = 40,
                MaxTotalRequests = 400,
                MaxActionsPerTurn = 12,
                MaxAcceptedActionsPerSession = 120,
                MaxInquiriesPerTurn = 30,
                MaxInquiriesPerSession = 300,
                MaxInvalidPerTurn = 3,
                MaxTurnSeconds = 600,
                MaxDeliberationTurnMillis = 600000,
                MaxSessionSeconds = 5400,
                RequestTimeoutMs = 180000,
                MaxOutputTokens = 16000,
                MaxSessionInputTokens = 12000000,
                MaxSessionOutputTokens = 2000000,
                MaxPromptChars = 200000,
                ContextHistoryItems = 60,
                ContextMemoryChars = 12000,
                ContextEventItems = 100,
                ContextCrossTurnInquiryResults = 12,
                ContextInquiryChars = 40000,
                MaxRetainedInquiryResults = 12,
                InquiryMaxResponseChars = 16000,
                MaxNoteChars = 2000,
            };
        }

        /// <summary>
        /// Stable error codes for an invalid experiment configuration; empty when the profile is off or
        /// every ceiling is explicit, finite and within its hard maximum. Never contains secrets.
        /// </summary>
        public IReadOnlyList<string> ValidateExperiment()
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(ExperimentProfile)) return errors;
            if (!string.Equals(ExperimentProfile, RichDeliberationProfile, StringComparison.Ordinal))
                errors.Add("experiment_profile_unknown");
            if (!EnableDeliberation) errors.Add("experiment_requires_deliberation");

            Bound(errors, "max_requests_per_turn", MaxRequestsPerTurn, ExperimentHardMaxRequestsPerTurn);
            Bound(errors, "max_total_requests", MaxTotalRequests, ExperimentHardMaxTotalRequests);
            Bound(errors, "max_actions_per_turn", MaxActionsPerTurn, ExperimentHardMaxActionsPerTurn);
            Bound(errors, "max_accepted_actions_per_session", MaxAcceptedActionsPerSession,
                ExperimentHardMaxActionsPerSession);
            Bound(errors, "max_inquiries_per_turn", MaxInquiriesPerTurn, ExperimentHardMaxInquiriesPerTurn);
            Bound(errors, "max_inquiries_per_session", MaxInquiriesPerSession,
                ExperimentHardMaxInquiriesPerSession);
            Bound(errors, "max_turn_seconds", MaxTurnSeconds, ExperimentHardMaxTurnSeconds);
            Bound(errors, "max_session_seconds", MaxSessionSeconds, ExperimentHardMaxSessionSeconds);
            Bound(errors, "request_timeout_ms", RequestTimeoutMs, ExperimentHardMaxRequestTimeoutMs);
            Bound(errors, "max_output_tokens", MaxOutputTokens, ExperimentHardMaxOutputTokens);
            Bound(errors, "max_session_input_tokens", MaxSessionInputTokens, ExperimentHardMaxSessionTokens);
            Bound(errors, "max_session_output_tokens", MaxSessionOutputTokens, ExperimentHardMaxSessionTokens);
            Bound(errors, "max_prompt_chars", MaxPromptChars, ExperimentHardMaxPromptChars);
            // Context knobs may stay at their historical default (zero) but never exceed a maximum.
            Optional(errors, "context_history_items", ContextHistoryItems, ExperimentHardMaxContextItems);
            Optional(errors, "context_event_items", ContextEventItems, ExperimentHardMaxContextItems);
            Optional(errors, "context_cross_turn_inquiry_results", ContextCrossTurnInquiryResults,
                ExperimentHardMaxContextItems);
            Optional(errors, "context_memory_chars", ContextMemoryChars, ExperimentHardMaxContextChars);
            Optional(errors, "context_inquiry_chars", ContextInquiryChars, ExperimentHardMaxContextChars);
            if (MaxSessionSeconds > 0 && MaxTurnSeconds > MaxSessionSeconds)
                errors.Add("max_turn_seconds_exceeds_session");
            if (MaxRequestsPerTurn > MaxTotalRequests) errors.Add("max_requests_per_turn_exceeds_session");
            if (MaxInquiriesPerTurn > MaxInquiriesPerSession) errors.Add("max_inquiries_per_turn_exceeds_session");
            if (MaxActionsPerTurn > MaxAcceptedActionsPerSession)
                errors.Add("max_actions_per_turn_exceeds_session");
            if (InquiryMaxResponseChars < InvestigationOptions.MinimumResponseChars)
                errors.Add("inquiry_max_response_chars_too_small");
            return errors;
        }

        private static void Bound(List<string> errors, string name, int value, int hardMax)
        {
            if (value <= 0) errors.Add(name + "_not_set");
            else if (value > hardMax) errors.Add(name + "_above_hard_max");
        }

        private static void Optional(List<string> errors, string name, int value, int hardMax)
        {
            if (value < 0 || value > hardMax) errors.Add(name + "_out_of_range");
        }

        private bool ApplyExperimentKey(string key, JsonNode value)
        {
            switch (key)
            {
                case "experiment_profile": ExperimentProfile = Text(value, ExperimentProfile); return true;
                case "max_session_seconds": MaxSessionSeconds = Int(value, MaxSessionSeconds); return true;
                case "max_session_input_tokens": MaxSessionInputTokens = Int(value, MaxSessionInputTokens); return true;
                case "max_session_output_tokens": MaxSessionOutputTokens = Int(value, MaxSessionOutputTokens); return true;
                case "max_prompt_chars": MaxPromptChars = Int(value, MaxPromptChars); return true;
                case "context_history_items": ContextHistoryItems = Int(value, ContextHistoryItems); return true;
                case "context_memory_chars": ContextMemoryChars = Int(value, ContextMemoryChars); return true;
                case "context_event_items": ContextEventItems = Int(value, ContextEventItems); return true;
                case "context_cross_turn_inquiry_results":
                    ContextCrossTurnInquiryResults = Int(value, ContextCrossTurnInquiryResults); return true;
                case "context_inquiry_chars": ContextInquiryChars = Int(value, ContextInquiryChars); return true;
                default: return false;
            }
        }

        private void AppendExperimentRedacted(StringBuilder sb)
        {
            // Appended only when the profile is set, so the legacy diagnostic stays byte-identical.
            if (string.IsNullOrEmpty(ExperimentProfile)) return;
            sb.Append("; experiment=").Append(ExperimentProfile);
            sb.Append("; session_seconds=").Append(Inv(MaxSessionSeconds));
            sb.Append("; session_input_tokens=").Append(Inv(MaxSessionInputTokens));
            sb.Append("; session_output_tokens=").Append(Inv(MaxSessionOutputTokens));
            sb.Append("; max_prompt_chars=").Append(Inv(MaxPromptChars));
            sb.Append("; context_history=").Append(Inv(ContextHistoryItems));
            sb.Append("; context_memory_chars=").Append(Inv(ContextMemoryChars));
            sb.Append("; context_events=").Append(Inv(ContextEventItems));
            sb.Append("; cross_turn_results=").Append(Inv(ContextCrossTurnInquiryResults));
            sb.Append("; inquiry_chars=").Append(Inv(ContextInquiryChars));
        }

        private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Secret-free JSON of every ceiling and context bound in force, written once per session so a
        /// run record states exactly which limits applied. Never includes the key or provider URL.
        /// </summary>
        public string ExperimentSettingsJson()
        {
            var sb = new StringBuilder(768);
            sb.Append("{\"experiment_profile\":").Append(MiniJson.Str(ExperimentProfile));
            sb.Append(",\"active\":").Append(ExperimentActive ? "true" : "false");
            AppendInt(sb, "max_requests_per_turn", MaxRequestsPerTurn);
            AppendInt(sb, "max_total_requests", MaxTotalRequests);
            AppendInt(sb, "max_actions_per_turn", MaxActionsPerTurn);
            AppendInt(sb, "max_accepted_actions_per_session", MaxAcceptedActionsPerSession);
            AppendInt(sb, "max_inquiries_per_turn", MaxInquiriesPerTurn);
            AppendInt(sb, "max_inquiries_per_session", MaxInquiriesPerSession);
            AppendInt(sb, "max_invalid_per_turn", MaxInvalidPerTurn);
            AppendInt(sb, "max_turn_seconds", MaxTurnSeconds);
            AppendInt(sb, "max_deliberation_turn_millis", MaxDeliberationTurnMillis);
            AppendInt(sb, "max_session_seconds", MaxSessionSeconds);
            AppendInt(sb, "request_timeout_ms", RequestTimeoutMs);
            AppendInt(sb, "max_output_tokens", MaxOutputTokens);
            AppendInt(sb, "max_session_input_tokens", MaxSessionInputTokens);
            AppendInt(sb, "max_session_output_tokens", MaxSessionOutputTokens);
            AppendInt(sb, "max_prompt_chars", EffectiveMaxPromptChars);
            AppendInt(sb, "context_history_items", EffectiveHistoryItems);
            AppendInt(sb, "context_memory_chars", EffectiveMemoryChars);
            AppendInt(sb, "context_event_items", EffectiveEventItems);
            AppendInt(sb, "context_cross_turn_inquiry_results", ContextCrossTurnInquiryResults);
            AppendInt(sb, "context_inquiry_chars", ContextInquiryChars);
            AppendInt(sb, "max_retained_inquiry_results", MaxRetainedInquiryResults);
            AppendInt(sb, "inquiry_max_response_chars", InquiryMaxResponseChars);
            AppendInt(sb, "max_note_chars", MaxNoteChars);
            sb.Append(",\"token_budget_enforced_by\":\"runner_provider_ceiling\"");
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendInt(StringBuilder sb, string key, int value)
            => sb.Append(",\"").Append(key).Append("\":").Append(Inv(value));
    }
}
