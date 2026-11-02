using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// A detached, read-only view of one ledger. Every collection and nested total is a copy taken at
    /// snapshot time, so a host can hand it to a recorder or a UI while the ledger keeps running.
    ///
    /// <see cref="Turn"/> covers the currently bound seat/turn pair only. <see cref="Session"/> and
    /// <see cref="Seats"/> cover every seat bound to the ledger; the policy caps are session-wide, so
    /// those totals — not per-seat totals — explain an exhausted session budget.
    /// </summary>
    public sealed class DeliberationBudgetSnapshot
    {
        public string SchemaVersion { get; internal set; }
        public string ProfileId { get; internal set; }
        public string ProfileVersion { get; internal set; }
        public string ModeCode { get; internal set; }
        public bool TokenBudgetEnforced { get; internal set; }
        public bool TokenAccountingSafe { get; internal set; }

        public string MatchId { get; internal set; }
        public bool Bound { get; internal set; }
        public string SeatId { get; internal set; }
        public int TurnToken { get; internal set; }

        public DeliberationBudgetTotals Turn { get; internal set; }
        public DeliberationBudgetTotals Session { get; internal set; }
        public IReadOnlyList<DeliberationSeatTotals> Seats { get; internal set; }
        public IReadOnlyList<DeliberationHalt> Halts { get; internal set; }
        public IReadOnlyList<string> OutstandingReservationIds { get; internal set; }
        public IReadOnlyList<string> EnforcedDimensions { get; internal set; }
        public IReadOnlyList<string> UnenforcedDimensions { get; internal set; }

        /// <summary>Explicit end-of-budget state. The host decides the game lifecycle; this module never ends a turn.</summary>
        public bool Blocked { get; internal set; }
        public string StopCode { get; internal set; }
        public string StopMessage { get; internal set; }

        public string ToJson()
        {
            var sb = new StringBuilder(2048);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(DeliberationJson.Str(SchemaVersion));
            sb.Append(",\"profile_id\":").Append(DeliberationJson.Str(ProfileId));
            sb.Append(",\"profile_version\":").Append(DeliberationJson.Str(ProfileVersion));
            sb.Append(",\"mode\":").Append(DeliberationJson.Str(ModeCode));
            sb.Append(",\"token_budget_enforced\":").Append(DeliberationJson.Bool(TokenBudgetEnforced));
            sb.Append(",\"token_accounting_safe\":").Append(DeliberationJson.Bool(TokenAccountingSafe));
            sb.Append(",\"match_id\":").Append(DeliberationJson.Str(MatchId));
            sb.Append(",\"bound\":").Append(DeliberationJson.Bool(Bound));
            sb.Append(",\"seat_id\":").Append(DeliberationJson.Str(SeatId));
            sb.Append(",\"turn_token\":").Append(DeliberationJson.Num(TurnToken));
            sb.Append(",\"turn\":").Append(Turn == null ? "null" : Turn.ToJson());
            sb.Append(",\"session\":").Append(Session == null ? "null" : Session.ToJson());
            sb.Append(",\"seats\":").Append(SeatArray());
            sb.Append(",\"halts\":").Append(HaltArray());
            sb.Append(",\"outstanding_reservations\":").Append(StringArray(OutstandingReservationIds));
            sb.Append(",\"enforced_dimensions\":").Append(StringArray(EnforcedDimensions));
            sb.Append(",\"unenforced_dimensions\":").Append(StringArray(UnenforcedDimensions));
            sb.Append(",\"blocked\":").Append(DeliberationJson.Bool(Blocked));
            sb.Append(",\"stop_code\":").Append(DeliberationJson.Str(StopCode));
            sb.Append(",\"stop_message\":").Append(DeliberationJson.Str(StopMessage));
            sb.Append('}');
            return sb.ToString();
        }

        private string SeatArray()
        {
            if (Seats == null) return "[]";
            var items = new List<string>(Seats.Count);
            foreach (DeliberationSeatTotals seat in Seats) items.Add(seat.ToJson());
            return MiniJson.Array(items);
        }

        private string HaltArray()
        {
            if (Halts == null) return "[]";
            var items = new List<string>(Halts.Count);
            foreach (DeliberationHalt halt in Halts) items.Add(halt.ToJson());
            return MiniJson.Array(items);
        }

        private static string StringArray(IReadOnlyList<string> values)
        {
            if (values == null) return "[]";
            var items = new List<string>(values.Count);
            foreach (string value in values) items.Add(MiniJson.Str(value));
            return MiniJson.Array(items);
        }
    }
}
