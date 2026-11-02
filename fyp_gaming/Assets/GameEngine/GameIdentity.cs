using System;
using System.Globalization;

namespace LuanShi.Engine
{
    /// <summary>
    /// Identity for one played match. A genuinely new match gets a fresh id; recreating a runner for
    /// the same engine keeps the engine's explicit id, which is what scopes durable seat memory.
    /// The id deliberately does not derive from the random seed: two new matches may use the same
    /// seed and must still not share memory.
    /// </summary>
    public static class GameIdentity
    {
        private const string Prefix = "match-";

        public static string NewMatchId()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
            return Prefix + timestamp + "-" + Guid.NewGuid().ToString("N");
        }
    }
}
