using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// The finite limits one deliberation-budget policy allows. Every value is an <see cref="int"/>:
    ///
    /// <list type="bullet">
    /// <item><see cref="Unenforced"/> means "this dimension has no cap"; it is still counted and reported.</item>
    /// <item><c>0</c> means "zero allowance": every admission in that dimension is denied. That is a real
    /// experimental condition (for example "supplied options only, no inquiry"), not a missing value.</item>
    /// <item>A negative value, or a token bound above <see cref="MaxTokenBoundPerRequest"/>, is rejected by
    /// <see cref="DeliberationBudgetPolicy"/> instead of being repaired silently.</item>
    /// </list>
    ///
    /// Per-turn limits reset when the host binds a genuinely new (seat, turn token) pair. Session limits are
    /// session-wide across every seat, so they never reset per player. Instances are mutable DTOs for
    /// construction only; a policy takes a private copy.
    /// </summary>
    public sealed class DeliberationBudgetLimits
    {
        /// <summary>Sentinel for "no cap in this dimension". Zero is a zero allowance, not this value.</summary>
        public const int Unenforced = int.MaxValue;

        /// <summary>Defensive ceiling for one host-supplied token bound, to catch unit mistakes and overflow.</summary>
        public const int DefaultMaxTokenBoundPerRequest = 1000000;

        // ---- per turn -------------------------------------------------------
        public int MaxRequestsPerTurn = Unenforced;
        public int MaxInquiryPerTurn = Unenforced;
        public int MaxActionsPerTurn = Unenforced;
        public int MaxInputTokensPerTurn = Unenforced;
        public int MaxOutputTokensPerTurn = Unenforced;
        public int MaxWallMillisPerTurn = Unenforced;

        // ---- whole session (all seats together) -----------------------------
        public int MaxRequestsPerSession = Unenforced;
        public int MaxInquiryPerSession = Unenforced;
        public int MaxActionsPerSession = Unenforced;
        public int MaxInputTokensPerSession = Unenforced;
        public int MaxOutputTokensPerSession = Unenforced;
        public int MaxWallMillisPerSession = Unenforced;

        // ---- per-request shape ----------------------------------------------
        public int MaxTokenBoundPerRequest = DefaultMaxTokenBoundPerRequest;

        public DeliberationBudgetLimits Copy()
        {
            return new DeliberationBudgetLimits
            {
                MaxRequestsPerTurn = MaxRequestsPerTurn,
                MaxInquiryPerTurn = MaxInquiryPerTurn,
                MaxActionsPerTurn = MaxActionsPerTurn,
                MaxInputTokensPerTurn = MaxInputTokensPerTurn,
                MaxOutputTokensPerTurn = MaxOutputTokensPerTurn,
                MaxWallMillisPerTurn = MaxWallMillisPerTurn,
                MaxRequestsPerSession = MaxRequestsPerSession,
                MaxInquiryPerSession = MaxInquiryPerSession,
                MaxActionsPerSession = MaxActionsPerSession,
                MaxInputTokensPerSession = MaxInputTokensPerSession,
                MaxOutputTokensPerSession = MaxOutputTokensPerSession,
                MaxWallMillisPerSession = MaxWallMillisPerSession,
                MaxTokenBoundPerRequest = MaxTokenBoundPerRequest,
            };
        }

        /// <summary>True for a finite cap. Zero is enforced (as a zero allowance); only the sentinel is not.</summary>
        public static bool IsEnforced(int limit) => limit >= 0 && limit != Unenforced;
    }

    /// <summary>
    /// An immutable, validated deliberation budget. The constructor copies the supplied
    /// <see cref="DeliberationBudgetLimits"/> and <see cref="Limits"/> hands out copies, so neither the
    /// caller's DTO nor a later reader can change a policy that a ledger is already enforcing.
    ///
    /// Two modes exist because they answer different research questions:
    /// <see cref="DeliberationBudgetMode.RequestLimited"/> bounds requests (and wall time) and reports
    /// that the token budget is <em>not</em> enforced; <see cref="DeliberationBudgetMode.TokenStrict"/>
    /// enforces token reservations and settlements and fails closed when usage is unknown. Mixing
    /// enforced token caps into request-limited mode is rejected rather than silently ignored.
    /// </summary>
    public sealed class DeliberationBudgetPolicy
    {
        /// <summary>Serialization version for experiment records. Not a live-game spend policy.</summary>
        public const string SchemaVersion = "ls019-v1";

        private readonly string _profileId;
        private readonly string _profileVersion;
        private readonly DeliberationBudgetMode _mode;
        private readonly DeliberationBudgetLimits _limits;

        public string ProfileId => _profileId;
        public string ProfileVersion => _profileVersion;
        public DeliberationBudgetMode Mode => _mode;

        /// <summary>True only in token-strict mode; a request-limited policy reports tokens as not enforced.</summary>
        public bool TokenBudgetEnforced => _mode == DeliberationBudgetMode.TokenStrict;

        /// <summary>A detached copy of the limits this policy enforces.</summary>
        public DeliberationBudgetLimits Limits => _limits.Copy();

        public DeliberationBudgetPolicy(string profileId, string profileVersion,
            DeliberationBudgetMode mode, DeliberationBudgetLimits limits)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("a profile id is required for experiment records", nameof(profileId));
            if (string.IsNullOrWhiteSpace(profileVersion))
                throw new ArgumentException("a profile version is required for experiment records", nameof(profileVersion));
            if (mode != DeliberationBudgetMode.RequestLimited && mode != DeliberationBudgetMode.TokenStrict)
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown deliberation budget mode");
            if (limits == null) throw new ArgumentNullException(nameof(limits));

            ValidateLimits(mode, limits);
            _profileId = profileId.Trim();
            _profileVersion = profileVersion.Trim();
            _mode = mode;
            _limits = limits.Copy();
        }

        /// <summary>Wire-mode label used in records.</summary>
        public string ModeCode => _mode == DeliberationBudgetMode.TokenStrict ? "token_strict" : "request_limited";

        /// <summary>Dimensions this policy actually enforces, in a stable order.</summary>
        public IReadOnlyList<string> EnforcedDimensions()
        {
            var enforced = new List<string>();
            AddIfEnforced(enforced, "requests.turn", _limits.MaxRequestsPerTurn);
            AddIfEnforced(enforced, "requests.session", _limits.MaxRequestsPerSession);
            AddIfEnforced(enforced, "inquiry.turn", _limits.MaxInquiryPerTurn);
            AddIfEnforced(enforced, "inquiry.session", _limits.MaxInquiryPerSession);
            AddIfEnforced(enforced, "actions.turn", _limits.MaxActionsPerTurn);
            AddIfEnforced(enforced, "actions.session", _limits.MaxActionsPerSession);
            AddIfEnforced(enforced, "input_tokens.turn", _limits.MaxInputTokensPerTurn);
            AddIfEnforced(enforced, "input_tokens.session", _limits.MaxInputTokensPerSession);
            AddIfEnforced(enforced, "output_tokens.turn", _limits.MaxOutputTokensPerTurn);
            AddIfEnforced(enforced, "output_tokens.session", _limits.MaxOutputTokensPerSession);
            AddIfEnforced(enforced, "wall_time.turn", _limits.MaxWallMillisPerTurn);
            AddIfEnforced(enforced, "wall_time.session", _limits.MaxWallMillisPerSession);
            return enforced.AsReadOnly();
        }

        /// <summary>Dimensions with no cap. Tokens are unenforced in request-limited mode, and that is reported.</summary>
        public IReadOnlyList<string> UnenforcedDimensions()
        {
            var unenforced = new List<string>();
            AddIfUnenforced(unenforced, "requests.turn", _limits.MaxRequestsPerTurn);
            AddIfUnenforced(unenforced, "requests.session", _limits.MaxRequestsPerSession);
            AddIfUnenforced(unenforced, "inquiry.turn", _limits.MaxInquiryPerTurn);
            AddIfUnenforced(unenforced, "inquiry.session", _limits.MaxInquiryPerSession);
            AddIfUnenforced(unenforced, "actions.turn", _limits.MaxActionsPerTurn);
            AddIfUnenforced(unenforced, "actions.session", _limits.MaxActionsPerSession);
            AddIfUnenforced(unenforced, "input_tokens.turn", _limits.MaxInputTokensPerTurn);
            AddIfUnenforced(unenforced, "input_tokens.session", _limits.MaxInputTokensPerSession);
            AddIfUnenforced(unenforced, "output_tokens.turn", _limits.MaxOutputTokensPerTurn);
            AddIfUnenforced(unenforced, "output_tokens.session", _limits.MaxOutputTokensPerSession);
            AddIfUnenforced(unenforced, "wall_time.turn", _limits.MaxWallMillisPerTurn);
            AddIfUnenforced(unenforced, "wall_time.session", _limits.MaxWallMillisPerSession);
            return unenforced.AsReadOnly();
        }

        /// <summary>Serializable profile record for experiment logs: no keys, no prices, no provider data.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(DeliberationJson.Str(SchemaVersion));
            sb.Append(",\"profile_id\":").Append(DeliberationJson.Str(_profileId));
            sb.Append(",\"profile_version\":").Append(DeliberationJson.Str(_profileVersion));
            sb.Append(",\"mode\":").Append(DeliberationJson.Str(ModeCode));
            sb.Append(",\"token_budget_enforced\":").Append(DeliberationJson.Bool(TokenBudgetEnforced));
            sb.Append(",\"limits\":{");
            sb.Append("\"requests_turn\":").Append(DeliberationJson.Limit(_limits.MaxRequestsPerTurn));
            sb.Append(",\"requests_session\":").Append(DeliberationJson.Limit(_limits.MaxRequestsPerSession));
            sb.Append(",\"inquiry_turn\":").Append(DeliberationJson.Limit(_limits.MaxInquiryPerTurn));
            sb.Append(",\"inquiry_session\":").Append(DeliberationJson.Limit(_limits.MaxInquiryPerSession));
            sb.Append(",\"actions_turn\":").Append(DeliberationJson.Limit(_limits.MaxActionsPerTurn));
            sb.Append(",\"actions_session\":").Append(DeliberationJson.Limit(_limits.MaxActionsPerSession));
            sb.Append(",\"input_tokens_turn\":").Append(DeliberationJson.Limit(_limits.MaxInputTokensPerTurn));
            sb.Append(",\"input_tokens_session\":").Append(DeliberationJson.Limit(_limits.MaxInputTokensPerSession));
            sb.Append(",\"output_tokens_turn\":").Append(DeliberationJson.Limit(_limits.MaxOutputTokensPerTurn));
            sb.Append(",\"output_tokens_session\":").Append(DeliberationJson.Limit(_limits.MaxOutputTokensPerSession));
            sb.Append(",\"wall_ms_turn\":").Append(DeliberationJson.Limit(_limits.MaxWallMillisPerTurn));
            sb.Append(",\"wall_ms_session\":").Append(DeliberationJson.Limit(_limits.MaxWallMillisPerSession));
            sb.Append(",\"max_token_bound_per_request\":").Append(DeliberationJson.Num(_limits.MaxTokenBoundPerRequest));
            sb.Append('}');
            sb.Append(",\"enforced_dimensions\":").Append(DeliberationJsonArray(EnforcedDimensions()));
            sb.Append(",\"unenforced_dimensions\":").Append(DeliberationJsonArray(UnenforcedDimensions()));
            sb.Append('}');
            return sb.ToString();
        }

        public override string ToString()
            => "DeliberationBudgetPolicy(" + _profileId + "/" + _profileVersion + ", " + ModeCode
               + ", token_budget_enforced=" + (TokenBudgetEnforced ? "true" : "false") + ")";

        private static string DeliberationJsonArray(IReadOnlyList<string> values)
        {
            var items = new List<string>(values.Count);
            foreach (string value in values) items.Add(MiniJson.Str(value));
            return MiniJson.Array(items);
        }

        private static void AddIfEnforced(List<string> names, string name, int limit)
        {
            if (DeliberationBudgetLimits.IsEnforced(limit)) names.Add(name);
        }

        private static void AddIfUnenforced(List<string> names, string name, int limit)
        {
            if (!DeliberationBudgetLimits.IsEnforced(limit)) names.Add(name);
        }

        /// <summary>
        /// Rejects limits that cannot be enforced coherently. Throwing here is deliberate: a silently
        /// enlarged or ignored limit would misreport an experiment's budget.
        /// </summary>
        private static void ValidateLimits(DeliberationBudgetMode mode, DeliberationBudgetLimits limits)
        {
            RequireNonNegative(limits.MaxRequestsPerTurn, nameof(limits.MaxRequestsPerTurn));
            RequireNonNegative(limits.MaxInquiryPerTurn, nameof(limits.MaxInquiryPerTurn));
            RequireNonNegative(limits.MaxActionsPerTurn, nameof(limits.MaxActionsPerTurn));
            RequireNonNegative(limits.MaxInputTokensPerTurn, nameof(limits.MaxInputTokensPerTurn));
            RequireNonNegative(limits.MaxOutputTokensPerTurn, nameof(limits.MaxOutputTokensPerTurn));
            RequireNonNegative(limits.MaxWallMillisPerTurn, nameof(limits.MaxWallMillisPerTurn));
            RequireNonNegative(limits.MaxRequestsPerSession, nameof(limits.MaxRequestsPerSession));
            RequireNonNegative(limits.MaxInquiryPerSession, nameof(limits.MaxInquiryPerSession));
            RequireNonNegative(limits.MaxActionsPerSession, nameof(limits.MaxActionsPerSession));
            RequireNonNegative(limits.MaxInputTokensPerSession, nameof(limits.MaxInputTokensPerSession));
            RequireNonNegative(limits.MaxOutputTokensPerSession, nameof(limits.MaxOutputTokensPerSession));
            RequireNonNegative(limits.MaxWallMillisPerSession, nameof(limits.MaxWallMillisPerSession));

            if (limits.MaxTokenBoundPerRequest < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(limits.MaxTokenBoundPerRequest),
                    limits.MaxTokenBoundPerRequest,
                    "MaxTokenBoundPerRequest must be at least 1 so a host token bound can be validated.");
            }

            bool inputEnforced = DeliberationBudgetLimits.IsEnforced(limits.MaxInputTokensPerTurn)
                                 || DeliberationBudgetLimits.IsEnforced(limits.MaxInputTokensPerSession);
            bool outputEnforced = DeliberationBudgetLimits.IsEnforced(limits.MaxOutputTokensPerTurn)
                                  || DeliberationBudgetLimits.IsEnforced(limits.MaxOutputTokensPerSession);

            if (mode == DeliberationBudgetMode.TokenStrict)
            {
                // Fail closed: a token-strict policy that cannot bound tokens is not token-strict.
                if (!inputEnforced)
                    throw new ArgumentException(
                        "a token-strict policy must enforce an input-token limit", nameof(limits));
                if (!outputEnforced)
                    throw new ArgumentException(
                        "a token-strict policy must enforce an output-token limit", nameof(limits));
            }
            else
            {
                // Explicitly refuse to carry a cap the mode cannot enforce, so no experiment records
                // an enforced-looking token budget that the ledger never checks.
                if (inputEnforced || outputEnforced)
                    throw new ArgumentException(
                        "a request-limited policy does not enforce token budgets; leave token limits unenforced",
                        nameof(limits));
                if (!DeliberationBudgetLimits.IsEnforced(limits.MaxRequestsPerTurn)
                    && !DeliberationBudgetLimits.IsEnforced(limits.MaxRequestsPerSession))
                    throw new ArgumentException(
                        "a request-limited policy must enforce a per-turn or session request limit",
                        nameof(limits));
            }
        }

        private static void RequireNonNegative(int value, string name)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(name, value, "budget limits cannot be negative");
        }
    }
}
