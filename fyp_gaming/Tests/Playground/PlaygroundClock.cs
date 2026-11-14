using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Deterministic monotonic clock. Elapsed time is simulated, never wall-calendar time.</summary>
    internal sealed class PlaygroundClock : IDeliberationClock
    {
        private long _nowMillis;
        private int _reads;

        // Test seam for expiring a reservation precisely between admission and dispatch.
        public int AdvanceOnRead { get; set; }
        public long AdvanceMillisOnRead { get; set; } = 1;

        public long NowMillis
        {
            get
            {
                _reads++;
                if (AdvanceOnRead > 0 && _reads == AdvanceOnRead)
                    _nowMillis += AdvanceMillisOnRead;
                return _nowMillis;
            }
        }

        public void Advance(long millis)
        {
            if (millis > 0) _nowMillis += millis;
        }
    }
}
