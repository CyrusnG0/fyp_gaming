using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    // search_history: bounded deterministic search over the seat's already filtered EventView list.
    // The canonical EventLog and GameState are never referenced or reachable from this file.
    public sealed partial class InvestigationService
    {
        /// <summary>
        /// Search the observation's own filtered history. Empty query returns the newest events.
        /// Results are newest-first; <see cref="HistorySearchResult.NextOffset"/> is -1 when no
        /// further page exists. The query is matched as inert text, never interpreted as an action.
        /// </summary>
        public HistorySearchResult SearchHistory(string query, int limit, int offset = 0)
        {
            var result = new HistorySearchResult();
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
                take = Math.Min(_options.DefaultHistoryLimit, _options.EffectiveHistoryLimit);
                take = Math.Max(1, take);
            }
            else if (limit > _options.EffectiveHistoryLimit)
            {
                result.Fail("limit_too_large", "The result limit exceeds the configured bound.");
                return result;
            }
            else
            {
                take = limit;
            }

            if (offset < 0)
            {
                result.Fail("invalid_offset", "The pagination offset cannot be negative.");
                return result;
            }
            if (offset > _options.MaxPaginationOffset)
            {
                result.Fail("offset_too_large", "The pagination offset exceeds the configured bound.");
                return result;
            }

            List<string> tokens = InvestigationText.Tokens(effectiveQuery);
            var matches = new List<EventView>();
            foreach (EventView view in _observation.Events)
            {
                if (view == null) continue;
                if (InvestigationText.MatchesAll(BuildHaystack(view), tokens)) matches.Add(view);
            }
            matches.Sort(CompareNewestFirst);

            result.Query = effectiveQuery;
            result.Offset = offset;

            int start = Math.Min(offset, matches.Count);
            int count = Math.Min(take, matches.Count - start);
            var hits = new List<HistoryHit>(count);
            for (int i = start; i < start + count; i++) hits.Add(ToHit(matches[i], _options));

            result.Hits = hits.AsReadOnly();
            UpdateHistoryPagination(result, matches.Count);
            result.Succeed(hits.Count);

            // Enforce the serialized-output bound by dropping whole trailing hits, never by cutting
            // JSON text. The envelope therefore always remains parseable.
            while (hits.Count > 0 && result.ToJson().Length > _options.EffectiveResponseChars)
            {
                hits.RemoveAt(hits.Count - 1);
                UpdateHistoryPagination(result, matches.Count);
                result.Succeed(hits.Count);
            }
            return result;
        }

        private void UpdateHistoryPagination(HistorySearchResult result, int totalMatches)
        {
            int consumed = result.Offset + result.Hits.Count;
            result.NextOffset = consumed < totalMatches ? consumed : -1;
            result.Truncated = result.NextOffset >= 0;
        }

        private static int CompareNewestFirst(EventView left, EventView right)
        {
            int bySeq = right.Seq.CompareTo(left.Seq);
            if (bySeq != 0) return bySeq;
            int byType = string.CompareOrdinal(left.EventType, right.EventType);
            if (byType != 0) return byType;
            return string.CompareOrdinal(left.SourceActionId, right.SourceActionId);
        }

        private static string BuildHaystack(EventView view)
        {
            var sb = new StringBuilder(128);
            InvestigationText.AppendTerm(sb, view.EventType);
            InvestigationText.AppendTerm(sb, view.Phase);
            InvestigationText.AppendTerm(sb, view.ActorSeatId);
            InvestigationText.AppendTerm(sb, view.ControllerType);
            InvestigationText.AppendTerm(sb, view.Visibility);
            InvestigationText.AppendTerm(sb, view.SourceActionId);
            InvestigationText.AppendTerm(sb, view.Text);
            InvestigationText.AppendTerm(sb, InvestigationText.Number(view.Season));
            if (view.Payload != null)
            {
                foreach (KeyValuePair<string, string> pair in view.Payload)
                {
                    InvestigationText.AppendTerm(sb, pair.Key);
                    InvestigationText.AppendTerm(sb, pair.Value);
                }
            }
            if (view.Args != null)
            {
                foreach (KeyValuePair<string, string> pair in view.Args)
                {
                    InvestigationText.AppendTerm(sb, pair.Key);
                    InvestigationText.AppendTerm(sb, pair.Value);
                }
            }
            return InvestigationText.Normalize(sb.ToString());
        }

        private static HistoryHit ToHit(EventView view, InvestigationOptions options)
        {
            int max = options.EffectiveTextChars;
            var hit = new HistoryHit
            {
                Seq = view.Seq,
                Season = view.Season,
                Phase = InvestigationText.Clip(view.Phase, max),
                ActorSeatId = InvestigationText.Clip(view.ActorSeatId, max),
                ControllerType = InvestigationText.Clip(view.ControllerType, max),
                EventType = InvestigationText.Clip(view.EventType, max),
                Visibility = InvestigationText.Clip(view.Visibility, max),
                SourceActionId = InvestigationText.Clip(view.SourceActionId, max),
                Text = InvestigationText.Clip(view.Text, max),
                Args = CopyPayload(view.Args, options),
            };
            IReadOnlyDictionary<string, string> payload = CopyPayload(view.Payload, options);
            if (payload != null) hit.Payload = payload;
            return hit;
        }
    }
}
