using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>Turns trusted turn outcomes into deterministic, content-addressed memories.</summary>
    public static class LlmMemoryExtractor
    {
        public static LlmMemory FromAction(string gameId, string seatId, int season,
            LlmAction action, bool accepted, string result)
        {
            if (action == null || string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId))
                return null;
            string target = string.IsNullOrEmpty(action.TargetId) ? "" : " " + action.TargetId;
            string content = action.Type + target + (accepted ? " was accepted" : " was rejected: " + result);
            return Create(gameId, seatId, season, accepted ? LlmMemoryType.Fact : LlmMemoryType.Lesson,
                content, "engine_action", accepted ? 2 : 3);
        }

        public static LlmMemory FromSummary(string gameId, string seatId, int season, string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return null;
            string content = Clean(summary, 200);
            return Create(gameId, seatId, season, LlmMemoryType.Decision, content, "model_summary", 1);
        }

        private static LlmMemory Create(string gameId, string seatId, int season, string type,
            string content, string source, int importance)
        {
            content = Clean(content, 240);
            return new LlmMemory
            {
                Id = Hash(gameId + "\n" + seatId + "\n" + season + "\n" + type + "\n" + content),
                GameId = gameId, SeatId = seatId, Type = type, Content = content,
                Source = source, Season = Math.Max(0, season), Importance = importance, Confidence = 1,
            };
        }

        private static string Clean(string value, int max)
        {
            var sb = new StringBuilder(Math.Min(value.Length, max));
            foreach (char c in value)
            {
                if (sb.Length >= max) break;
                sb.Append(char.IsControl(c) ? ' ' : c);
            }
            return sb.ToString().Trim();
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
