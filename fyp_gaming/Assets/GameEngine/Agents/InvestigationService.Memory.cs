using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    // recall_memory: bounded deterministic recall over injected, detached own-seat memory records.
    // The service never opens a store, reads disk or trusts a record's visibility without rechecking it.
    public sealed partial class InvestigationService
    {
        /// <summary>
        /// Search the injected memory snapshot. Foreign game/seat records, unsupported scope or
        /// visibility, superseded records and unknown schema/types are dropped. Empty query returns
        /// the newest/strongest own memories. Beliefs are returned with an explicit untrusted status.
        /// </summary>
        public MemoryRecallResult RecallMemory(string query, int limit)
        {
            var result = new MemoryRecallResult();
            try
            {
                if (_contextError != null)
                {
                    result.Fail("context_mismatch", ContextMismatchMessage);
                    return result;
                }

                string effectiveQuery = query ?? "";
                if (effectiveQuery.Length > _options.MaxQueryLength)
                {
                    result.Fail("query_too_long", "The query exceeds the configured length bound.");
                    return result;
                }

                int take;
                if (limit < 0)
                {
                    result.Fail("invalid_limit", "The result limit cannot be negative.");
                    return result;
                }
                if (limit == 0)
                {
                    take = Math.Min(_options.DefaultMemoryLimit, _options.EffectiveMemoryLimit);
                    take = Math.Max(1, take);
                }
                else if (limit > _options.EffectiveMemoryLimit)
                {
                    result.Fail("limit_too_large", "The result limit exceeds the configured bound.");
                    return result;
                }
                else
                {
                    take = limit;
                }

                List<string> tokens = InvestigationText.Tokens(effectiveQuery);
                var matches = new List<LlmMemory>();
                foreach (LlmMemory memory in _memories)
                {
                    if (!IsOwnActiveMemory(memory)) continue;
                    if (InvestigationText.MatchesAll(BuildHaystack(memory), tokens)) matches.Add(memory);
                }
                matches.Sort(CompareMemories);

                result.Query = effectiveQuery;
                int count = Math.Min(take, matches.Count);
                var hits = new List<MemoryHit>(count);
                for (int i = 0; i < count; i++) hits.Add(ToHit(matches[i], _options));

                result.Memories = hits.AsReadOnly();
                result.Truncated = matches.Count > hits.Count;
                result.Succeed(hits.Count);
                return result;
            }
            finally
            {
                result.ApplyResponseBound(_options);
            }
        }

        private bool IsOwnActiveMemory(LlmMemory memory)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.Content)) return false;
            if (!string.Equals(memory.GameId, _expectedGameId, StringComparison.Ordinal)) return false;
            if (!string.Equals(memory.SeatId, _expectedSeatId, StringComparison.Ordinal)) return false;
            if (!string.Equals(memory.Scope, LlmMemoryScope.Game, StringComparison.Ordinal)) return false;
            if (!string.Equals(memory.Visibility, LlmMemoryVisibility.Seat, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(memory.SupersededBy)) return false;
            if (memory.SchemaVersion != LlmMemory.CurrentSchemaVersion) return false;
            return memory.Type == LlmMemoryType.Fact
                || memory.Type == LlmMemoryType.Lesson
                || memory.Type == LlmMemoryType.Decision;
        }

        private static int CompareMemories(LlmMemory left, LlmMemory right)
        {
            int bySeason = right.Season.CompareTo(left.Season);
            if (bySeason != 0) return bySeason;
            int byImportance = right.Importance.CompareTo(left.Importance);
            if (byImportance != 0) return byImportance;
            int byConfidence = right.Confidence.CompareTo(left.Confidence);
            if (byConfidence != 0) return byConfidence;
            return string.CompareOrdinal(left.Id ?? "", right.Id ?? "");
        }

        private static string BuildHaystack(LlmMemory memory)
        {
            var sb = new StringBuilder(128);
            InvestigationText.AppendTerm(sb, memory.Type);
            InvestigationText.AppendTerm(sb, memory.Source);
            InvestigationText.AppendTerm(sb, memory.Content);
            InvestigationText.AppendTerm(sb, InvestigationText.Number(memory.Season));
            return InvestigationText.Normalize(sb.ToString());
        }

        private static MemoryHit ToHit(LlmMemory memory, InvestigationOptions options)
        {
            int max = options.EffectiveTextChars;
            return new MemoryHit
            {
                MemoryId = InvestigationText.Clip(memory.Id, max),
                Type = InvestigationText.Clip(memory.Type, max),
                Source = InvestigationText.Clip(memory.Source, max),
                Season = memory.Season,
                Content = InvestigationText.Clip(memory.Content, max),
                Importance = memory.Importance,
                Confidence = memory.Confidence,
            };
        }
    }
}
