using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Deterministic monotonic clock. Elapsed time is simulated, never wall-calendar time.</summary>
    internal sealed class PlaygroundClock : IDeliberationClock
    {
        public long NowMillis { get; private set; }

        public void Advance(long millis)
        {
            if (millis > 0) NowMillis += millis;
        }
    }
}
