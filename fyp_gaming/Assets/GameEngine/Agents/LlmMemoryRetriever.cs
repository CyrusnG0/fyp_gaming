using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>Ranks active seat-private memories and fits them into a prompt character budget.</summary>
    public static class LlmMemoryRetriever
    {
        public const int DefaultMaxChars = 3000;

        public static IReadOnlyList<LlmMemory> Select(IReadOnlyList<LlmMemory> memories,
            Observation observation, IReadOnlyList<ActionOption> options, int maxChars = DefaultMaxChars)
        {
            var selected = new List<LlmMemory>();
            if (memories == null || maxChars <= 0) return selected.AsReadOnly();

            var terms = new List<string>();
            if (options != null)
            {
                foreach (ActionOption option in options)
                {
                    AddTerm(terms, option == null ? null : option.Type);
                    AddTerm(terms, option == null ? null : option.TargetId);
                }
            }
            int season = observation == null ? 0 : observation.Season;
            string gameId = observation == null ? null : observation.GameId;
            string seatId = observation == null ? null : observation.SeatId;
            var ranked = new List<RankedMemory>();
            foreach (LlmMemory memory in memories)
            {
                if (memory == null || string.IsNullOrWhiteSpace(memory.Content)
                    || string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(seatId)
                    || memory.GameId != gameId || memory.SeatId != seatId
                    || memory.Visibility != LlmMemoryVisibility.Seat
                    || memory.Scope != LlmMemoryScope.Game
                    || !string.IsNullOrEmpty(memory.SupersededBy))
                    continue;
                int score = memory.Importance * 10 + memory.Confidence * 4;
                score += Math.Max(0, 10 - Math.Abs(season - memory.Season));
                string lower = memory.Content.ToLowerInvariant();
                foreach (string term in terms)
                    if (lower.Contains(term)) score += 12;
                ranked.Add(new RankedMemory(memory, score));
            }
            ranked.Sort((a, b) =>
            {
                int result = b.Score.CompareTo(a.Score);
                return result != 0 ? result : string.CompareOrdinal(a.Memory.Id, b.Memory.Id);
            });

            int used = 0;
            foreach (RankedMemory item in ranked)
            {
                string line = Format(item.Memory);
                int extra = line.Length + 2;
                if (used + extra > maxChars) continue;
                selected.Add(item.Memory.Copy());
                used += extra;
            }
            return selected.AsReadOnly();
        }

        private static string Format(LlmMemory memory)
            => "[" + memory.Type + "] " + memory.Content;

        private static void AddTerm(List<string> terms, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string term = value.Trim().ToLowerInvariant();
            if (term.Length >= 3 && !terms.Contains(term)) terms.Add(term);
        }

        private sealed class RankedMemory
        {
            public readonly LlmMemory Memory;
            public readonly int Score;
            public RankedMemory(LlmMemory memory, int score) { Memory = memory; Score = score; }
        }
    }
}
