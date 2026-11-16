using System;
using System.Diagnostics;
using System.Threading;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Run-wide dispatch budget shared by every scenario of one <see cref="CsRunner.Run"/>. The
    /// stopwatch starts when the run starts, and <see cref="TryReserve"/> is atomic so two seats can
    /// never both consume the last request allowance. Replay uses an unbounded instance so it does
    /// not inherit a ceiling from the run being replayed.
    /// </summary>
    public sealed class CsRunBudget
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly int _maxRequests;
        private readonly long _maxMs;
        private int _dispatched;

        public CsRunBudget(int maxRunRequests, long maxRunMs)
        {
            _maxRequests = maxRunRequests < 0 ? 0 : maxRunRequests;
            _maxMs = maxRunMs < 0 ? 0 : maxRunMs;
        }

        public static CsRunBudget Unbounded() => new CsRunBudget(int.MaxValue, long.MaxValue);

        /// <summary>Requests reserved so far (a reservation precedes an actual inner call).</summary>
        public int Dispatched => Volatile.Read(ref _dispatched);

        public int MaxRequests => _maxRequests;
        public long MaxMs => _maxMs;
        public long ElapsedMs => _clock.ElapsedMilliseconds;

        /// <summary>Atomically consumes one request allowance; false when the run cap is exhausted.</summary>
        public bool TryReserve()
        {
            while (true)
            {
                int current = Volatile.Read(ref _dispatched);
                if (current >= _maxRequests) return false;
                if (Interlocked.CompareExchange(ref _dispatched, current + 1, current) == current)
                    return true;
            }
        }

        /// <summary>Whole-run wall allowance left in milliseconds, never negative.</summary>
        public long RemainingMs
        {
            get
            {
                long left = _maxMs - _clock.ElapsedMilliseconds;
                return left < 0 ? 0 : left;
            }
        }

        public bool DeadlineReached => RemainingMs <= 0;

        /// <summary>Whole-run request allowance left, never negative.</summary>
        public int RemainingRequests => Math.Max(0, _maxRequests - Dispatched);
    }
}
