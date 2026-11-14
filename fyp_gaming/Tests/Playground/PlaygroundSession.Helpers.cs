using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Memory, budget-policy and assertion helpers for the session loop.</summary>
    internal sealed partial class PlaygroundSession
    {
        private void LoadSeatMemories(string seatId, int season)
        {
            if (_memoryStore == null) { _memories.Clear(); return; }
            _memoryStore.Consolidate(_config.GameId, seatId, season,
                LlmMemoryLifecycle.DefaultRetentionSeasons, LlmMemoryLifecycle.DefaultMaxItems);
            _memories.Clear();
            foreach (LlmMemory memory in _memoryStore.Load(_config.GameId, seatId))
                _memories.Add(memory);
        }

        private void StoreMemory(string seatId, LlmMemory memory)
        {
            if (memory == null || _memoryStore == null) return;
            foreach (LlmMemory existing in _memories)
                if (existing.Id == memory.Id) return;
            if (_memoryStore.Append(memory)) _memories.Add(memory);
        }

        private void SeedFixtureMemories()
        {
            if (!_config.SeedFixtureMemories || _memoryStore == null) return;
            foreach (string seatId in PlaygroundScenario.SeatOrder(_engine))
            {
                LlmMemory memory = LlmMemoryExtractor.FromSummary(_config.GameId, seatId, 0,
                    "Fixture memory for " + seatId + ": hold the river line.");
                if (memory != null) _memoryStore.Append(memory);
            }
        }

        private void AddAutomaticAssertions()
        {
            foreach (PlaygroundInquiryRecord inquiry in _record.Inquiries)
            {
                _record.Check(inquiry.CpBefore == inquiry.CpAfter,
                    "inquiry spends no command point",
                    inquiry.SeatId + " request " + inquiry.RequestIndex);
                _record.Check(inquiry.ActionAdmissionsBefore == inquiry.ActionAdmissionsAfter,
                    "inquiry spends no action admission",
                    inquiry.SeatId + " request " + inquiry.RequestIndex);
            }
            foreach (PlaygroundActionRecord action in _record.Checks)
            {
                _record.Check(string.Equals(action.LiveStateHashBeforeCheck,
                    action.LiveStateHashAfterCheck, StringComparison.Ordinal),
                    "Check leaves authoritative state unchanged",
                    action.SeatId + " request " + action.RequestIndex);
            }
        }

        private static DeliberationBudgetPolicy BuildPolicy(PlaygroundConfig config)
        {
            var limits = new DeliberationBudgetLimits
            {
                MaxRequestsPerTurn = config.MaxRequestsPerTurn,
                MaxRequestsPerSession = config.MaxRequestsPerSession,
                MaxActionsPerTurn = config.MaxActionsPerTurn,
                MaxActionsPerSession = config.MaxActionsPerSession,
                MaxInquiryPerTurn = config.MaxInquiryPerTurn,
                MaxInquiryPerSession = config.MaxInquiryPerSession,
                MaxWallMillisPerTurn = config.MaxWallMillisPerTurn,
                MaxWallMillisPerSession = config.MaxWallMillisPerSession,
                MaxInputTokensPerTurn = DeliberationBudgetLimits.Unenforced,
                MaxInputTokensPerSession = DeliberationBudgetLimits.Unenforced,
                MaxOutputTokensPerTurn = DeliberationBudgetLimits.Unenforced,
                MaxOutputTokensPerSession = DeliberationBudgetLimits.Unenforced,
            };
            return DeliberationBudgetPolicies.Research("playground-request-limited",
                DeliberationBudgetMode.RequestLimited, limits);
        }

        private static string Bound(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            if (value.Length <= max) return value;
            return value.Substring(0, max) + "...[truncated]";
        }
    }
}
