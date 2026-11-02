using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Deterministic retention, deduplication and supersession.
    ///
    /// Within one fact/lesson conflict group only the newest outcome stays active. "Newest" is
    /// season first, then the ordinal id as a stable tie-break, so the active winner does not depend
    /// on input order. Every other outcome in the group points at that winner through
    /// <see cref="LlmMemory.SupersededBy"/>; a later consolidation skips superseded input, which
    /// prevents an old version from becoming active again.
    /// </summary>
    public static class LlmMemoryLifecycle
    {
        public const int DefaultRetentionSeasons = 8;
        public const int DefaultMaxItems = 100;

        public static IReadOnlyList<LlmMemory> Consolidate(IReadOnlyList<LlmMemory> memories,
            int currentSeason, int retentionSeasons, int maxItems)
        {
            var deduped = new Dictionary<string, LlmMemory>(StringComparer.Ordinal);
            int oldestSeason = Math.Max(0, currentSeason - retentionSeasons);
            if (memories != null)
            {
                foreach (LlmMemory memory in memories)
                {
                    if (memory == null || string.IsNullOrWhiteSpace(memory.Content)
                        || memory.Season < oldestSeason
                        || !string.IsNullOrEmpty(memory.SupersededBy))
                        continue;

                    string key = memory.Type + "\n" + Normalize(memory.Content);
                    if (!deduped.TryGetValue(key, out LlmMemory existing))
                    {
                        deduped[key] = memory.Copy();
                        continue;
                    }

                    // Exact duplicates collapse to the newest record. Metadata is combined with
                    // commutative max rules so the result does not depend on iteration order.
                    LlmMemory newest = IsNewer(memory, existing) ? memory : existing;
                    LlmMemory older = ReferenceEquals(newest, memory) ? existing : memory;
                    LlmMemory merged = newest.Copy();
                    merged.Importance = Math.Max(merged.Importance, older.Importance);
                    merged.Confidence = Math.Min(100, Math.Max(merged.Confidence, older.Confidence));
                    deduped[key] = merged;
                }
            }

            // Choose each conflict group's winner from the complete group before marking anything.
            // Updating a running representative while iterating is what let a middle version stay
            // active for the A/B/C case.
            var winners = new Dictionary<string, LlmMemory>(StringComparer.Ordinal);
            foreach (LlmMemory memory in deduped.Values)
            {
                string conflictKey = ConflictKey(memory);
                if (conflictKey == null) continue;
                if (!winners.TryGetValue(conflictKey, out LlmMemory winner)
                    || IsNewer(memory, winner))
                    winners[conflictKey] = memory;
            }

            foreach (LlmMemory memory in deduped.Values)
            {
                string conflictKey = ConflictKey(memory);
                if (conflictKey == null) continue;
                LlmMemory winner = winners[conflictKey];
                if (!ReferenceEquals(memory, winner)) memory.SupersededBy = winner.Id;
            }

            var result = new List<LlmMemory>(deduped.Values);
            result.Sort((a, b) =>
            {
                bool aActive = string.IsNullOrEmpty(a.SupersededBy);
                bool bActive = string.IsNullOrEmpty(b.SupersededBy);
                int active = aActive.CompareTo(bActive);
                if (active != 0) return -active;
                int season = b.Season.CompareTo(a.Season);
                return season != 0 ? season : string.CompareOrdinal(a.Id, b.Id);
            });
            if (result.Count > maxItems) result.RemoveRange(maxItems, result.Count - maxItems);
            return result.AsReadOnly();
        }

        private static bool IsNewer(LlmMemory left, LlmMemory right)
            => left.Season > right.Season
                || (left.Season == right.Season && string.CompareOrdinal(left.Id, right.Id) > 0);

        private static string ConflictKey(LlmMemory memory)
        {
            if (memory.Type != LlmMemoryType.Fact && memory.Type != LlmMemoryType.Lesson)
                return null;
            int marker = memory.Content.IndexOf(" was ", StringComparison.Ordinal);
            return marker <= 0 ? null : memory.Content.Substring(0, marker).Trim().ToLowerInvariant();
        }

        private static string Normalize(string value)
            => value.Trim().ToLowerInvariant();
    }
}
