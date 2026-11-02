using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// LS-019 deliberation-budget ledger. The host binds one match, then the active (seat, turn token)
    /// pair; every unit of work must be admitted here before it is dispatched, and settled or cancelled
    /// exactly once afterwards.
    ///
    /// Authority is structural. Admission takes no seat/turn/match argument, so a model-supplied tool
    /// argument cannot rebind the ledger or pick a different allowance. Reservations carry the identity
    /// they were issued under, so a late completion settles against its own turn instead of whatever is
    /// bound now. Per-turn counters belong to one (seat, turn token) bucket: returning to that bucket
    /// restores the counters, a newer token starts a new allowance, and session counters are
    /// session-wide and survive every turn change.
    ///
    /// The ledger never calls a provider, never mutates game state and never ends a turn. It only
    /// decides whether more work may be admitted and records what was spent.
    /// </summary>
    public sealed partial class DeliberationBudgetLedger
    {
        /// <summary>Defensive bound for host-supplied identifiers; longer ids are rejected, not truncated.</summary>
        public const int MaxIdLength = 64;

        private sealed class Counters
        {
            public long Requests;
            public long Inquiries;
            public long Failures;
            public long ActionAdmissions;
            public long AcceptedActions;
            public long ReservedActions;
            public long InputTokens;
            public long OutputTokens;
            public long ReservedInputTokens;
            public long ReservedOutputTokens;
            public long UnknownUsageEvents;
            public long OverrunEvents;
            public long MeasuredElapsedMillis;
            public long WallClockMillis;

            /// <summary>Known usage plus the holds of unresolved reservations: what the caps must cover.</summary>
            public long CommittedInputTokens => InputTokens + ReservedInputTokens;
            public long CommittedOutputTokens => OutputTokens + ReservedOutputTokens;
            public long CommittedActions => AcceptedActions + ReservedActions;
        }

        /// <summary>One (seat, turn token) allowance bucket. Kept per seat until a newer token replaces it.</summary>
        private sealed class TurnBucket
        {
            public string SeatId;
            public int TurnToken;
            public long StartMillis;
            public readonly Counters Counters = new Counters();
        }

        private sealed class Reservation
        {
            public string Id;
            public DeliberationWorkKind Kind;
            public string SeatId;
            public int TurnToken;
            public Counters TurnCounters;
            public int ReservedInputTokens;
            public int ReservedOutputTokens;
            public bool InputBounded;
            public bool OutputBounded;
            public bool Dispatched;
            public bool Finalized;

            /// <summary>True when the host supplied a positive token bound for at least one side.</summary>
            public bool HasTokenReservation => InputBounded || OutputBounded;
        }

        private readonly object _gate = new object();
        private readonly string _matchId;
        private readonly DeliberationBudgetPolicy _policy;
        private readonly DeliberationBudgetLimits _limits;
        private readonly IDeliberationClock _clock;
        private readonly long _sessionStartMillis;
        private readonly Counters _session = new Counters();
        private readonly Dictionary<string, Counters> _seatCounters =
            new Dictionary<string, Counters>(StringComparer.Ordinal);
        private readonly Dictionary<string, TurnBucket> _turnsBySeat =
            new Dictionary<string, TurnBucket>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastTurnBySeat =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, Reservation> _reservations =
            new Dictionary<string, Reservation>(StringComparer.Ordinal);
        private readonly HashSet<string> _haltCodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<DeliberationHalt> _halts = new List<DeliberationHalt>();

        private TurnBucket _turn;
        private long _lastObservedMillis;
        private bool _blocked;
        private bool _accountingUnsafe;
        private DeliberationDenyReason _blockReason;
        private string _blockMessage;
        private int _reservationSequence;

        public string MatchId => _matchId;
        public DeliberationBudgetPolicy Policy => _policy;
        public bool Blocked { get { lock (_gate) return _blocked; } }

        /// <summary>
        /// One ledger per match. The match id is fixed for the ledger's lifetime so a later binding can
        /// never redirect session totals to a different game. The clock is monotonic; omit it only in
        /// production, where the default wraps <see cref="System.Diagnostics.Stopwatch"/>.
        /// </summary>
        public DeliberationBudgetLedger(string matchId, DeliberationBudgetPolicy policy,
            IDeliberationClock clock = null)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("a match id is required", nameof(matchId));
            if (matchId.Length > MaxIdLength)
                throw new ArgumentException("the match id exceeds the configured length bound", nameof(matchId));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            _matchId = matchId;
            _policy = policy;
            _limits = policy.Limits;
            _clock = clock ?? new SystemDeliberationClock();

            long start;
            if (!TryReadClock(out start))
                throw new ArgumentException("the injected clock is unavailable", nameof(clock));
            _sessionStartMillis = start;
            _lastObservedMillis = start;
        }

        /// <summary>
        /// Bind the host's active seat and turn token. Binding the already-bound pair is idempotent and
        /// does <em>not</em> refresh the per-turn counters. Returning to a seat's latest turn restores
        /// its bucket instead of resetting it; an older token is refused, so a new allowance needs a
        /// genuinely new turn. Session totals are untouched.
        /// </summary>
        public DeliberationBindingResult BindTurn(string seatId, int turnToken)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(seatId))
                    return Binding(false, DeliberationDenyReason.InvalidBinding, "a seat id is required.");
                if (seatId.Length > MaxIdLength)
                    return Binding(false, DeliberationDenyReason.InvalidBinding,
                        "the seat id exceeds the configured length bound.");
                if (turnToken < 0)
                    return Binding(false, DeliberationDenyReason.InvalidBinding,
                        "a non-negative turn token is required.");

                long last;
                bool hasLast = _lastTurnBySeat.TryGetValue(seatId, out last);
                if (hasLast && turnToken < last)
                    return Binding(false, DeliberationDenyReason.StaleTurn,
                        "the turn token is older than the last bound turn for this seat.");

                if (_turn != null
                    && string.Equals(_turn.SeatId, seatId, StringComparison.Ordinal)
                    && _turn.TurnToken == turnToken)
                    return Binding(true, DeliberationDenyReason.None, null);

                TurnBucket existing;
                if (hasLast && turnToken == last
                    && _turnsBySeat.TryGetValue(seatId, out existing))
                {
                    // Switching back within the same turn restores its counters; it never resets them.
                    _turn = existing;
                    return Binding(true, DeliberationDenyReason.None, null);
                }

                long now;
                if (!TryReadClock(out now))
                    return Binding(false, _blockReason, _blockMessage);

                var bucket = new TurnBucket
                {
                    SeatId = seatId,
                    TurnToken = turnToken,
                    StartMillis = now,
                };
                _turnsBySeat[seatId] = bucket;
                _lastTurnBySeat[seatId] = turnToken;
                if (!_seatCounters.ContainsKey(seatId)) _seatCounters[seatId] = new Counters();
                _turn = bucket;
                return Binding(true, DeliberationDenyReason.None, null);
            }
        }

        /// <summary>
        /// A detached copy of the current ledger state. Nothing in the returned object is live: later
        /// admissions, settlements or bindings cannot change it.
        /// </summary>
        public DeliberationBudgetSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new DeliberationBudgetSnapshot
                {
                    SchemaVersion = DeliberationBudgetPolicy.SchemaVersion,
                    ProfileId = _policy.ProfileId,
                    ProfileVersion = _policy.ProfileVersion,
                    ModeCode = _policy.ModeCode,
                    TokenBudgetEnforced = _policy.TokenBudgetEnforced,
                    TokenAccountingSafe = !_accountingUnsafe,
                    MatchId = _matchId,
                    Bound = _turn != null,
                    SeatId = _turn == null ? null : _turn.SeatId,
                    TurnToken = _turn == null ? -1 : _turn.TurnToken,
                    Turn = CopyTotals(_turn == null ? new Counters() : _turn.Counters),
                    Session = CopyTotals(_session),
                    Seats = SeatSnapshots(),
                    Halts = new List<DeliberationHalt>(_halts).AsReadOnly(),
                    OutstandingReservationIds = OutstandingReservationIds(),
                    EnforcedDimensions = _policy.EnforcedDimensions(),
                    UnenforcedDimensions = _policy.UnenforcedDimensions(),
                    Blocked = _blocked,
                    StopCode = _blocked ? DeliberationBudgetReasons.Code(_blockReason) : null,
                    StopMessage = _blocked ? _blockMessage : null,
                };
            }
        }

        // ---- internals ---------------------------------------------------------

        private static bool IsKnownKind(DeliberationWorkKind kind)
            => kind == DeliberationWorkKind.ModelRequest
               || kind == DeliberationWorkKind.Inquiry
               || kind == DeliberationWorkKind.GameAction;

        private Counters Seat(string seatId)
        {
            Counters counters;
            if (!_seatCounters.TryGetValue(seatId, out counters))
            {
                counters = new Counters();
                _seatCounters[seatId] = counters;
            }
            return counters;
        }

        private DeliberationAdmission NewAdmission(DeliberationWorkKind kind)
        {
            return new DeliberationAdmission
            {
                Kind = kind,
                MatchId = _matchId,
                SeatId = _turn == null ? null : _turn.SeatId,
                TurnToken = _turn == null ? -1 : _turn.TurnToken,
                TokenBudgetEnforced = _policy.TokenBudgetEnforced,
                Blocked = _blocked,
                StopCode = _blocked ? DeliberationBudgetReasons.Code(_blockReason) : null,
            };
        }

        private DeliberationAdmission Deny(DeliberationAdmission admission, DeliberationDenyReason reason,
            string message)
        {
            admission.Allowed = false;
            admission.Reason = reason;
            admission.Message = message;
            admission.ReservationId = null;
            admission.Blocked = _blocked;
            admission.StopCode = _blocked ? DeliberationBudgetReasons.Code(_blockReason) : null;
            return admission;
        }

        private static DeliberationBindingResult Binding(bool ok, DeliberationDenyReason reason, string message)
            => new DeliberationBindingResult { Ok = ok, Reason = reason, Message = message };

        /// <summary>Reads the injected clock and fails closed on an exception, a negative value or a backwards step.</summary>
        private bool TryReadClock(out long now)
        {
            now = 0;
            long value;
            try { value = _clock.NowMillis; }
            catch (Exception)
            {
                Block(DeliberationDenyReason.ClockInvalid, "the injected clock threw an exception.");
                return false;
            }
            if (value < 0)
            {
                Block(DeliberationDenyReason.ClockInvalid, "the injected clock returned a negative time.");
                return false;
            }
            if (value < _lastObservedMillis)
            {
                Block(DeliberationDenyReason.ClockInvalid, "the injected clock moved backwards.");
                return false;
            }
            _lastObservedMillis = value;
            now = value;
            return true;
        }

        /// <summary>Stops all further admission and records the first stop reason per code.</summary>
        private void Block(DeliberationDenyReason reason, string message)
            => Block(reason, message,
                _turn == null ? null : _turn.SeatId,
                _turn == null ? -1 : _turn.TurnToken);

        /// <summary>Stops admission and attributes the halt to the work's own seat/turn, even if it is late.</summary>
        private void Block(DeliberationDenyReason reason, string message, string seatId, int turnToken)
        {
            if (!_blocked)
            {
                _blocked = true;
                _blockReason = reason;
                _blockMessage = message;
            }
            RecordHalt(reason, message, seatId, turnToken);
        }

        /// <summary>
        /// Records a problem without necessarily stopping admission (unknown token usage in
        /// request-limited mode). Either way the snapshot stops claiming that token accounting is safe.
        /// </summary>
        private void RecordHalt(DeliberationDenyReason reason, string message)
            => RecordHalt(reason, message,
                _turn == null ? null : _turn.SeatId,
                _turn == null ? -1 : _turn.TurnToken);

        private void RecordHalt(DeliberationDenyReason reason, string message, string seatId, int turnToken)
        {
            if (reason == DeliberationDenyReason.UsageUnknown || reason == DeliberationDenyReason.TokenOverrun
                || reason == DeliberationDenyReason.AccountingOverflow)
                _accountingUnsafe = true;
            if (!_haltCodes.Add(DeliberationBudgetReasons.Code(reason))) return;
            _halts.Add(new DeliberationHalt
            {
                Reason = reason,
                Message = message,
                SeatId = seatId,
                TurnToken = turnToken,
                AtSessionMillis = _lastObservedMillis - _sessionStartMillis,
            });
        }

        private static DeliberationBudgetTotals CopyTotals(Counters counters)
        {
            return new DeliberationBudgetTotals
            {
                Requests = counters.Requests,
                Inquiries = counters.Inquiries,
                Failures = counters.Failures,
                ActionAdmissions = counters.ActionAdmissions,
                AcceptedActions = counters.AcceptedActions,
                InputTokens = counters.InputTokens,
                OutputTokens = counters.OutputTokens,
                ReservedInputTokens = counters.ReservedInputTokens,
                ReservedOutputTokens = counters.ReservedOutputTokens,
                UnknownUsageEvents = counters.UnknownUsageEvents,
                OverrunEvents = counters.OverrunEvents,
                MeasuredElapsedMillis = counters.MeasuredElapsedMillis,
                WallClockMillis = counters.WallClockMillis,
            };
        }

        private IReadOnlyList<DeliberationSeatTotals> SeatSnapshots()
        {
            var seats = new List<DeliberationSeatTotals>(_seatCounters.Count);
            var ids = new List<string>(_seatCounters.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                Counters counters = _seatCounters[id];
                seats.Add(new DeliberationSeatTotals
                {
                    SeatId = id,
                    Requests = counters.Requests,
                    Inquiries = counters.Inquiries,
                    Failures = counters.Failures,
                    ActionAdmissions = counters.ActionAdmissions,
                    AcceptedActions = counters.AcceptedActions,
                    InputTokens = counters.InputTokens,
                    OutputTokens = counters.OutputTokens,
                    UnknownUsageEvents = counters.UnknownUsageEvents,
                    OverrunEvents = counters.OverrunEvents,
                });
            }
            return seats.AsReadOnly();
        }

        private IReadOnlyList<string> OutstandingReservationIds()
        {
            var ids = new List<string>();
            foreach (KeyValuePair<string, Reservation> pair in _reservations)
                if (!pair.Value.Finalized) ids.Add(pair.Key);
            ids.Sort(StringComparer.Ordinal);
            return ids.AsReadOnly();
        }
    }
}
