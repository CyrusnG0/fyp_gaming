using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// One accounting bucket (a turn, the whole session, or one seat's session total). Values are
    /// <see cref="long"/> so cumulative usage cannot overflow; policy limits stay <see cref="int"/>.
    ///
    /// The three token figures are deliberately separate: <see cref="InputTokens"/> is known settled
    /// usage, <see cref="ReservedInputTokens"/> is the upper bound still held by outstanding
    /// reservations, and <see cref="UnknownUsageEvents"/> counts settlements whose provider usage was
    /// missing. A record can therefore never imply that an estimate is a measurement.
    /// </summary>
    public sealed class DeliberationBudgetTotals
    {
        public long Requests { get; internal set; }
        public long Inquiries { get; internal set; }
        public long Failures { get; internal set; }
        public long ActionAdmissions { get; internal set; }
        public long AcceptedActions { get; internal set; }
        public long InputTokens { get; internal set; }
        public long OutputTokens { get; internal set; }
        public long ReservedInputTokens { get; internal set; }
        public long ReservedOutputTokens { get; internal set; }
        public long UnknownUsageEvents { get; internal set; }
        public long OverrunEvents { get; internal set; }
        public long MeasuredElapsedMillis { get; internal set; }
        /// <summary>Elapsed milliseconds observed by the injected clock when work was last admitted.</summary>
        public long WallClockMillis { get; internal set; }

        public DeliberationBudgetTotals Copy()
        {
            return new DeliberationBudgetTotals
            {
                Requests = Requests,
                Inquiries = Inquiries,
                Failures = Failures,
                ActionAdmissions = ActionAdmissions,
                AcceptedActions = AcceptedActions,
                InputTokens = InputTokens,
                OutputTokens = OutputTokens,
                ReservedInputTokens = ReservedInputTokens,
                ReservedOutputTokens = ReservedOutputTokens,
                UnknownUsageEvents = UnknownUsageEvents,
                OverrunEvents = OverrunEvents,
                MeasuredElapsedMillis = MeasuredElapsedMillis,
                WallClockMillis = WallClockMillis,
            };
        }

        public string ToJson()
        {
            var sb = new StringBuilder(384);
            sb.Append('{');
            sb.Append("\"requests\":").Append(DeliberationJson.Num(Requests));
            sb.Append(",\"inquiries\":").Append(DeliberationJson.Num(Inquiries));
            sb.Append(",\"failures\":").Append(DeliberationJson.Num(Failures));
            sb.Append(",\"action_admissions\":").Append(DeliberationJson.Num(ActionAdmissions));
            sb.Append(",\"accepted_actions\":").Append(DeliberationJson.Num(AcceptedActions));
            sb.Append(",\"input_tokens\":").Append(DeliberationJson.Num(InputTokens));
            sb.Append(",\"output_tokens\":").Append(DeliberationJson.Num(OutputTokens));
            sb.Append(",\"reserved_input_tokens\":").Append(DeliberationJson.Num(ReservedInputTokens));
            sb.Append(",\"reserved_output_tokens\":").Append(DeliberationJson.Num(ReservedOutputTokens));
            sb.Append(",\"unknown_usage_events\":").Append(DeliberationJson.Num(UnknownUsageEvents));
            sb.Append(",\"overrun_events\":").Append(DeliberationJson.Num(OverrunEvents));
            sb.Append(",\"measured_elapsed_ms\":").Append(DeliberationJson.Num(MeasuredElapsedMillis));
            sb.Append(",\"wall_clock_ms\":").Append(DeliberationJson.Num(WallClockMillis));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Per-seat session totals. Evidence only: the enforced caps are the session-wide ones.</summary>
    public sealed class DeliberationSeatTotals
    {
        public string SeatId { get; internal set; }
        public long Requests { get; internal set; }
        public long Inquiries { get; internal set; }
        public long Failures { get; internal set; }
        public long ActionAdmissions { get; internal set; }
        public long AcceptedActions { get; internal set; }
        public long InputTokens { get; internal set; }
        public long OutputTokens { get; internal set; }
        public long UnknownUsageEvents { get; internal set; }
        public long OverrunEvents { get; internal set; }

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(DeliberationJson.Str(SeatId));
            sb.Append(",\"requests\":").Append(DeliberationJson.Num(Requests));
            sb.Append(",\"inquiries\":").Append(DeliberationJson.Num(Inquiries));
            sb.Append(",\"failures\":").Append(DeliberationJson.Num(Failures));
            sb.Append(",\"action_admissions\":").Append(DeliberationJson.Num(ActionAdmissions));
            sb.Append(",\"accepted_actions\":").Append(DeliberationJson.Num(AcceptedActions));
            sb.Append(",\"input_tokens\":").Append(DeliberationJson.Num(InputTokens));
            sb.Append(",\"output_tokens\":").Append(DeliberationJson.Num(OutputTokens));
            sb.Append(",\"unknown_usage_events\":").Append(DeliberationJson.Num(UnknownUsageEvents));
            sb.Append(",\"overrun_events\":").Append(DeliberationJson.Num(OverrunEvents));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One recorded stop or accounting problem. Deduplicated per reason code by the ledger.</summary>
    public sealed class DeliberationHalt
    {
        public DeliberationDenyReason Reason { get; internal set; }
        public string Code => DeliberationBudgetReasons.Code(Reason);
        public string Message { get; internal set; }
        public string SeatId { get; internal set; }
        public int TurnToken { get; internal set; }
        public long AtSessionMillis { get; internal set; }

        public string ToJson()
        {
            var sb = new StringBuilder(288);
            sb.Append('{');
            sb.Append("\"reason\":").Append(DeliberationJson.Str(Code));
            sb.Append(",\"message\":").Append(DeliberationJson.Str(Message));
            sb.Append(",\"seat_id\":").Append(DeliberationJson.Str(SeatId));
            sb.Append(",\"turn_token\":").Append(DeliberationJson.Num(TurnToken));
            sb.Append(",\"at_session_ms\":").Append(DeliberationJson.Num(AtSessionMillis));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Result of binding the host's active match/player/turn, or of marking a call dispatched.</summary>
    public sealed class DeliberationBindingResult
    {
        public bool Ok { get; internal set; }
        public DeliberationDenyReason Reason { get; internal set; }
        public string Code => DeliberationBudgetReasons.Code(Reason);
        public string Message { get; internal set; }
    }

    /// <summary>
    /// Result of one admission attempt. <see cref="Allowed"/> false means nothing was dispatched and no
    /// counter changed. A denial carries a stable typed <see cref="Reason"/>; a sentinel-free
    /// <see cref="Blocked"/> flag tells the host that the budget has stopped for this session.
    /// </summary>
    public sealed class DeliberationAdmission
    {
        public bool Allowed { get; internal set; }
        public DeliberationDenyReason Reason { get; internal set; }
        public string Code => Allowed ? "allowed" : DeliberationBudgetReasons.Code(Reason);
        public string Message { get; internal set; }
        public DeliberationWorkKind Kind { get; internal set; }

        /// <summary>Opaque identity used to settle or cancel; null on denial.</summary>
        public string ReservationId { get; internal set; }
        public string MatchId { get; internal set; }
        public string SeatId { get; internal set; }
        public int TurnToken { get; internal set; }
        public int ReservedInputTokens { get; internal set; }
        public int ReservedOutputTokens { get; internal set; }

        /// <summary>True when a further admission of this same kind (with these bounds) would be denied.</summary>
        public bool Exhausted { get; internal set; }

        /// <summary>Explicit end-of-budget signal: the ledger will refuse all further admissions.</summary>
        public bool Blocked { get; internal set; }
        public string StopCode { get; internal set; }
        public bool TokenBudgetEnforced { get; internal set; }
    }

    /// <summary>
    /// What the host reports after dispatched work finishes. Null token values mean the provider
    /// reported no usage; the ledger then charges the reservation and records unknown usage instead of
    /// pretending the spend was measured.
    /// </summary>
    public sealed class DeliberationSettlement
    {
        public int? InputTokens;
        public int? OutputTokens;
        public long ElapsedMillis;

        /// <summary>Engine accepted the admitted game action (evidence only; the admission already counted).</summary>
        public bool Accepted;

        /// <summary>The attempt failed (transport, parse, tool error). The admission is not refunded.</summary>
        public bool Failed;

        public static DeliberationSettlement Known(int input, int output, long elapsedMillis = 0)
            => new DeliberationSettlement { InputTokens = input, OutputTokens = output, ElapsedMillis = elapsedMillis };

        public static DeliberationSettlement Unknown(long elapsedMillis = 0)
            => new DeliberationSettlement { ElapsedMillis = elapsedMillis };
    }

    public enum DeliberationSettlementOutcome
    {
        /// <summary>The settlement was applied once and its usage is now in the ledger.</summary>
        Applied = 0,

        /// <summary>The reservation was already settled or released; nothing changed.</summary>
        AlreadySettled = 1,

        /// <summary>No reservation with that identity exists; nothing changed.</summary>
        UnknownReservation = 2,

        /// <summary>The settlement itself was malformed (negative or out-of-range values); the reservation stays open.</summary>
        Rejected = 3,
    }

    /// <summary>One settlement attempt. <see cref="AppliedToTurn"/> is false for a late completion of an obsolete turn.</summary>
    public sealed class DeliberationSettlementResult
    {
        public DeliberationSettlementOutcome Outcome { get; internal set; }
        public bool Applied => Outcome == DeliberationSettlementOutcome.Applied;
        public DeliberationDenyReason Reason { get; internal set; }
        public string Code => DeliberationBudgetReasons.Code(Reason);
        public string Message { get; internal set; }
        public string ReservationId { get; internal set; }
        /// <summary>Tokens charged: reported usage, or the reserved upper bound when usage was missing.</summary>
        public long InputTokens { get; internal set; }
        public long OutputTokens { get; internal set; }
        public bool InputUsageKnown { get; internal set; }
        public bool OutputUsageKnown { get; internal set; }
        public bool UsageUnknown { get; internal set; }
        /// <summary>Reported usage exceeded the reservation; the excess is recorded, never clamped.</summary>
        public bool Overrun { get; internal set; }
        public bool Accepted { get; internal set; }
        /// <summary>False when the reservation belonged to a turn that is no longer bound.</summary>
        public bool AppliedToTurn { get; internal set; }
        public bool Blocked { get; internal set; }
        public string StopCode { get; internal set; }
        public bool TokenBudgetEnforced { get; internal set; }
    }

    public enum DeliberationCancelOutcome
    {
        /// <summary>Abandoned before dispatch: the token reservation was released and nothing was charged.</summary>
        Released = 0,

        /// <summary>Abandoned after dispatch: the reserved bounds were charged as unknown usage.</summary>
        Charged = 1,

        AlreadySettled = 2,
        UnknownReservation = 3,
    }

    /// <summary>
    /// Result of abandoning work. Only the reservation is released; the request/inquiry/action slot was
    /// already consumed at admission, so retrying cannot buy a free attempt.
    /// </summary>
    public sealed class DeliberationCancelResult
    {
        public DeliberationCancelOutcome Outcome { get; internal set; }
        public bool Ok => Outcome == DeliberationCancelOutcome.Released
                          || Outcome == DeliberationCancelOutcome.Charged;
        public DeliberationCancelReason CancelReason { get; internal set; }
        public DeliberationDenyReason Reason { get; internal set; }
        public string Code => DeliberationBudgetReasons.Code(Reason);
        public string Message { get; internal set; }
        public string ReservationId { get; internal set; }
        public int ReleasedInputTokens { get; internal set; }
        public int ReleasedOutputTokens { get; internal set; }
        public long ChargedInputTokens { get; internal set; }
        public long ChargedOutputTokens { get; internal set; }
        public bool UsageUnknown { get; internal set; }
        public bool Blocked { get; internal set; }
        public string StopCode { get; internal set; }
        public bool TokenBudgetEnforced { get; internal set; }
    }
}
