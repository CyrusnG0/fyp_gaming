using System.Diagnostics;

namespace LuanShi.Engine
{
    /// <summary>
    /// Monotonic millisecond source. Wall-time budgets are measured between admissions, so the module
    /// needs a clock that never jumps backwards and never depends on the wall calendar. Tests inject a
    /// deterministic implementation; the default wraps <see cref="Stopwatch"/>, which is monotonic.
    /// </summary>
    public interface IDeliberationClock
    {
        long NowMillis { get; }
    }

    /// <summary>Default monotonic clock. It is not a UTC clock and must never be used as one.</summary>
    public sealed class SystemDeliberationClock : IDeliberationClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public long NowMillis => _stopwatch.ElapsedMilliseconds;
    }
}
