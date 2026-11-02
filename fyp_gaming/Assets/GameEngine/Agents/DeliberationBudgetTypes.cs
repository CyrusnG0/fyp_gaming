using System;
using System.Globalization;

namespace LuanShi.Engine
{
    /*
     * LS-019 configurable deliberation budget — shared vocabulary.
     *
     * One ledger separates, per turn and for the whole session, the work a controller consumes:
     * model requests, read-only inquiry/tool invocations, game-action admissions, input tokens,
     * output tokens and elapsed/wall time. Reading the rules, the seat's own history or its own
     * memories spends an inquiry slot and (through the model request that asked for it) request,
     * token and time budget, but never a game command point or a game-action slot.
     *
     * Session counters are SESSION-WIDE: they cover every seat bound to the same ledger, so a
     * shared spectator runner cannot give each player a fresh global spending cap. Per-seat totals
     * exist only as experiment evidence; they are not separate caps. Per-turn counters belong to
     * the host-bound (seat, turn token) pair, so a caller — and therefore a model — cannot reset or
     * extend them by supplying different arguments.
     *
     * This module is deliberately pure C#: no provider calls, no pricing, no Unity types, no game
     * state mutation, no clock of its own beyond an injected monotonic source, and no implicit
     * EndTurn. It does not claim to stop a provider from ignoring its declared output bound, and it
     * cannot promise a dollar cap without prices.
     */

    /// <summary>One unit of work that has to be admitted before it is dispatched.</summary>
    public enum DeliberationWorkKind
    {
        /// <summary>A provider round-trip. Reserves input/output token bounds with the request.</summary>
        ModelRequest = 0,

        /// <summary>A read-only tool call (rules, own world, own history, own memory). No tokens of its own.</summary>
        Inquiry = 1,

        /// <summary>A candidate game action submitted to the engine validator. No tokens of its own.</summary>
        GameAction = 2,
    }

    /// <summary>Which spending bound a policy treats as authoritative.</summary>
    public enum DeliberationBudgetMode
    {
        /// <summary>
        /// Requests and wall time are the spend bound. Token usage is recorded and reserved for
        /// evidence, but the policy explicitly reports that the token budget is not enforced.
        /// </summary>
        RequestLimited = 0,

        /// <summary>
        /// Token reservations and settlements are enforced. Missing provider usage makes admission
        /// fail closed instead of silently under-counting spend.
        /// </summary>
        TokenStrict = 1,
    }

    /// <summary>Coarse reporting label attached to a denial or stop reason.</summary>
    public enum DeliberationBudgetDimension
    {
        None = 0,
        Binding,
        Requests,
        Inquiry,
        Actions,
        InputTokens,
        OutputTokens,
        WallTime,
        Accounting,
    }

    /// <summary>
    /// Stable typed reason for a denied admission, a rejected settlement or a stop recorded by the
    /// ledger. <see cref="DeliberationBudgetReasons.Code"/> is the wire form used in records; the
    /// numeric values are not part of the contract.
    /// </summary>
    public enum DeliberationDenyReason
    {
        None = 0,

        // ---- binding / request shape -------------------------------------
        NotBound,
        InvalidBinding,
        StaleTurn,
        InvalidRequest,
        InvalidTokenBounds,
        TokenBoundRequired,

        // ---- per-turn counters -------------------------------------------
        TurnRequestsExhausted,
        TurnInquiryExhausted,
        TurnActionsExhausted,
        TurnInputTokensExhausted,
        TurnOutputTokensExhausted,
        TurnWallTimeExhausted,

        // ---- session counters --------------------------------------------
        SessionRequestsExhausted,
        SessionInquiryExhausted,
        SessionActionsExhausted,
        SessionInputTokensExhausted,
        SessionOutputTokensExhausted,
        SessionWallTimeExhausted,

        // ---- accounting ---------------------------------------------------
        ClockInvalid,
        UsageUnknown,
        TokenOverrun,
        InvalidUsage,
        UnknownReservation,
        AlreadySettled,
        AccountingOverflow,
    }

    /// <summary>Why work was abandoned. Dispatch state, not this value, decides whether tokens count.</summary>
    public enum DeliberationCancelReason
    {
        Cancelled = 0,
        TimedOut = 1,
        Aborted = 2,
    }

    /// <summary>Stable wire codes and human-readable messages for <see cref="DeliberationDenyReason"/>.</summary>
    public static class DeliberationBudgetReasons
    {
        public static string Code(DeliberationDenyReason reason)
        {
            switch (reason)
            {
                case DeliberationDenyReason.None: return "none";
                case DeliberationDenyReason.NotBound: return "not_bound";
                case DeliberationDenyReason.InvalidBinding: return "invalid_binding";
                case DeliberationDenyReason.StaleTurn: return "stale_turn";
                case DeliberationDenyReason.InvalidRequest: return "invalid_request";
                case DeliberationDenyReason.InvalidTokenBounds: return "invalid_token_bounds";
                case DeliberationDenyReason.TokenBoundRequired: return "token_bound_required";
                case DeliberationDenyReason.TurnRequestsExhausted: return "turn_requests_exhausted";
                case DeliberationDenyReason.TurnInquiryExhausted: return "turn_inquiry_exhausted";
                case DeliberationDenyReason.TurnActionsExhausted: return "turn_actions_exhausted";
                case DeliberationDenyReason.TurnInputTokensExhausted: return "turn_input_tokens_exhausted";
                case DeliberationDenyReason.TurnOutputTokensExhausted: return "turn_output_tokens_exhausted";
                case DeliberationDenyReason.TurnWallTimeExhausted: return "turn_wall_time_exhausted";
                case DeliberationDenyReason.SessionRequestsExhausted: return "session_requests_exhausted";
                case DeliberationDenyReason.SessionInquiryExhausted: return "session_inquiry_exhausted";
                case DeliberationDenyReason.SessionActionsExhausted: return "session_actions_exhausted";
                case DeliberationDenyReason.SessionInputTokensExhausted: return "session_input_tokens_exhausted";
                case DeliberationDenyReason.SessionOutputTokensExhausted: return "session_output_tokens_exhausted";
                case DeliberationDenyReason.SessionWallTimeExhausted: return "session_wall_time_exhausted";
                case DeliberationDenyReason.ClockInvalid: return "clock_invalid";
                case DeliberationDenyReason.UsageUnknown: return "usage_unknown";
                case DeliberationDenyReason.TokenOverrun: return "token_overrun";
                case DeliberationDenyReason.InvalidUsage: return "invalid_usage";
                case DeliberationDenyReason.UnknownReservation: return "unknown_reservation";
                case DeliberationDenyReason.AlreadySettled: return "already_settled";
                case DeliberationDenyReason.AccountingOverflow: return "accounting_overflow";
                default: return "unknown_reason";
            }
        }

        public static string Message(DeliberationDenyReason reason)
        {
            switch (reason)
            {
                case DeliberationDenyReason.None: return "allowed";
                case DeliberationDenyReason.NotBound: return "No seat/turn is bound, so no work can be admitted.";
                case DeliberationDenyReason.InvalidBinding: return "The seat id or turn token is not a valid binding.";
                case DeliberationDenyReason.StaleTurn: return "This seat/turn pair is closed or older than the last bound turn.";
                case DeliberationDenyReason.InvalidRequest: return "The admission request is not a known kind of work.";
                case DeliberationDenyReason.InvalidTokenBounds:
                    return "Token bounds are negative, exceed the configured bound, or were supplied for token-free work.";
                case DeliberationDenyReason.TokenBoundRequired:
                    return "A token-strict policy needs a positive input and output token bound before dispatch.";
                case DeliberationDenyReason.TurnRequestsExhausted: return "The per-turn model-request allowance is exhausted.";
                case DeliberationDenyReason.TurnInquiryExhausted: return "The per-turn inquiry allowance is exhausted.";
                case DeliberationDenyReason.TurnActionsExhausted: return "The per-turn game-action allowance is exhausted.";
                case DeliberationDenyReason.TurnInputTokensExhausted: return "The per-turn input-token allowance cannot cover the reservation.";
                case DeliberationDenyReason.TurnOutputTokensExhausted: return "The per-turn output-token allowance cannot cover the reservation.";
                case DeliberationDenyReason.TurnWallTimeExhausted: return "The per-turn wall-time budget is exhausted.";
                case DeliberationDenyReason.SessionRequestsExhausted: return "The whole-session model-request allowance is exhausted.";
                case DeliberationDenyReason.SessionInquiryExhausted: return "The whole-session inquiry allowance is exhausted.";
                case DeliberationDenyReason.SessionActionsExhausted: return "The whole-session game-action allowance is exhausted.";
                case DeliberationDenyReason.SessionInputTokensExhausted: return "The session input-token allowance cannot cover the reservation.";
                case DeliberationDenyReason.SessionOutputTokensExhausted: return "The session output-token allowance cannot cover the reservation.";
                case DeliberationDenyReason.SessionWallTimeExhausted: return "The whole-session wall-time budget is exhausted.";
                case DeliberationDenyReason.ClockInvalid: return "The injected clock is unavailable or not monotonic.";
                case DeliberationDenyReason.UsageUnknown: return "Token accounting is unsafe because provider usage was missing.";
                case DeliberationDenyReason.TokenOverrun: return "Reported usage exceeded the reservation, so admission is halted.";
                case DeliberationDenyReason.InvalidUsage: return "The settlement contains negative or missing values.";
                case DeliberationDenyReason.UnknownReservation: return "No reservation with this identity exists.";
                case DeliberationDenyReason.AlreadySettled: return "This reservation was already settled or released.";
                case DeliberationDenyReason.AccountingOverflow:
                    return "The ledger counters cannot represent this usage safely, so admission is halted.";
                default: return "Unknown reason.";
            }
        }

        public static DeliberationBudgetDimension Dimension(DeliberationDenyReason reason)
        {
            switch (reason)
            {
                case DeliberationDenyReason.NotBound:
                case DeliberationDenyReason.InvalidBinding:
                case DeliberationDenyReason.StaleTurn:
                    return DeliberationBudgetDimension.Binding;
                case DeliberationDenyReason.TurnRequestsExhausted:
                case DeliberationDenyReason.SessionRequestsExhausted:
                    return DeliberationBudgetDimension.Requests;
                case DeliberationDenyReason.TurnInquiryExhausted:
                case DeliberationDenyReason.SessionInquiryExhausted:
                    return DeliberationBudgetDimension.Inquiry;
                case DeliberationDenyReason.TurnActionsExhausted:
                case DeliberationDenyReason.SessionActionsExhausted:
                    return DeliberationBudgetDimension.Actions;
                case DeliberationDenyReason.TurnInputTokensExhausted:
                case DeliberationDenyReason.SessionInputTokensExhausted:
                    return DeliberationBudgetDimension.InputTokens;
                case DeliberationDenyReason.TurnOutputTokensExhausted:
                case DeliberationDenyReason.SessionOutputTokensExhausted:
                    return DeliberationBudgetDimension.OutputTokens;
                case DeliberationDenyReason.TurnWallTimeExhausted:
                case DeliberationDenyReason.SessionWallTimeExhausted:
                case DeliberationDenyReason.ClockInvalid:
                    return DeliberationBudgetDimension.WallTime;
                case DeliberationDenyReason.None:
                    return DeliberationBudgetDimension.None;
                default:
                    return DeliberationBudgetDimension.Accounting;
            }
        }

        /// <summary>True for reasons that mean "this allowance is used up", not "the call was malformed".</summary>
        public static bool IsExhaustion(DeliberationDenyReason reason)
        {
            switch (reason)
            {
                case DeliberationDenyReason.TurnRequestsExhausted:
                case DeliberationDenyReason.TurnInquiryExhausted:
                case DeliberationDenyReason.TurnActionsExhausted:
                case DeliberationDenyReason.TurnInputTokensExhausted:
                case DeliberationDenyReason.TurnOutputTokensExhausted:
                case DeliberationDenyReason.TurnWallTimeExhausted:
                case DeliberationDenyReason.SessionRequestsExhausted:
                case DeliberationDenyReason.SessionInquiryExhausted:
                case DeliberationDenyReason.SessionActionsExhausted:
                case DeliberationDenyReason.SessionInputTokensExhausted:
                case DeliberationDenyReason.SessionOutputTokensExhausted:
                case DeliberationDenyReason.SessionWallTimeExhausted:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// A host-supplied upper bound for one request, in tokens. The module never derives tokens from
    /// characters: a caller that cannot state a bound must say so, and a token-strict policy then
    /// refuses to dispatch. Zero means "no bound supplied"; negative is rejected.
    /// </summary>
    public struct DeliberationTokenBounds
    {
        public int Input;
        public int Output;

        public static DeliberationTokenBounds None => default;

        public static DeliberationTokenBounds Of(int input, int output)
            => new DeliberationTokenBounds { Input = input, Output = output };

        public bool IsNone => Input == 0 && Output == 0;

        public bool HasNegative => Input < 0 || Output < 0;

        public override string ToString()
            => Input.ToString(CultureInfo.InvariantCulture) + "/" + Output.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>JSON helpers shared by the budget records. Integers only, like the rest of the engine schema.</summary>
    internal static class DeliberationJson
    {
        public static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Bool(bool value) => value ? "true" : "false";

        public static string Str(string value) => MiniJson.Str(value);

        /// <summary>An unenforced limit is rendered as null so a record shows "no cap" rather than a sentinel.</summary>
        public static string Limit(int value)
            => value == DeliberationBudgetLimits.Unenforced ? "null" : Num(value);
    }
}
