using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// In-memory durable-memory backend for deterministic sessions and self-tests. It mirrors
    /// <see cref="LocalLlmMemoryStore"/>'s (game, seat) filtering and lifecycle consolidation without
    /// touching the filesystem.
    /// </summary>
    internal sealed class InMemoryLlmMemoryStore : ILlmMemoryStore
    {
        private readonly Dictionary<string, LlmMemory> _byId =
            new Dictionary<string, LlmMemory>(StringComparer.Ordinal);

        public string LastError { get; private set; }

        public IReadOnlyList<LlmMemory> Load(string gameId, string seatId)
        {
            LastError = null;
            var result = new List<LlmMemory>();
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId))
                return result.AsReadOnly();
            foreach (LlmMemory memory in _byId.Values)
                if (memory.GameId == gameId && memory.SeatId == seatId) result.Add(memory.Copy());
            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result.AsReadOnly();
        }

        public bool Append(LlmMemory memory)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.Id)
                || string.IsNullOrWhiteSpace(memory.GameId) || string.IsNullOrWhiteSpace(memory.SeatId)
                || string.IsNullOrWhiteSpace(memory.Type) || string.IsNullOrWhiteSpace(memory.Content))
            {
                LastError = "invalid memory";
                return false;
            }
            _byId[Key(memory.GameId, memory.SeatId, memory.Id)] = memory.Copy();
            LastError = null;
            return true;
        }

        public bool Consolidate(string gameId, string seatId, int currentSeason,
            int retentionSeasons, int maxItems)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId)
                || currentSeason < 0 || retentionSeasons < 0 || maxItems <= 0)
            {
                LastError = "invalid memory consolidation";
                return false;
            }

            var prefix = (gameId ?? "") + "\n" + (seatId ?? "") + "\n";
            var existing = new List<LlmMemory>();
            var remove = new List<string>();
            foreach (KeyValuePair<string, LlmMemory> pair in _byId)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                existing.Add(pair.Value);
                remove.Add(pair.Key);
            }
            foreach (string key in remove) _byId.Remove(key);

            IReadOnlyList<LlmMemory> consolidated = LlmMemoryLifecycle.Consolidate(
                existing, currentSeason, retentionSeasons, maxItems);
            foreach (LlmMemory memory in consolidated)
                _byId[Key(memory.GameId, memory.SeatId, memory.Id)] = memory.Copy();
            LastError = null;
            return true;
        }

        public void Reset()
        {
            _byId.Clear();
            LastError = null;
        }

        private static string Key(string gameId, string seatId, string id)
            => (gameId ?? "") + "\n" + (seatId ?? "") + "\n" + (id ?? "");
    }
}
