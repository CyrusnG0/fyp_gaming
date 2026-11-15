using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Configurable map-context limits, Rich visible-grid rendering, and deterministic truncation
    /// accounting with zero global mutable state.
    /// </summary>
    public static partial class PromptBuilder
    {
        private static string SanitizeQuoted(string value)
            => CleanLine(value).Replace('"', '\'');

        private static void AppendVisibleCellsSection(StringBuilder sb, SpatialFrontierSummary frontier,
            int maxRowRanges, out int droppedRanges)
        {
            droppedRanges = 0;
            if (frontier == null) return;

            int totalRows = frontier.VisibleRowRanges == null ? 0 : frontier.VisibleRowRanges.Count;
            int shownRows = Math.Min(totalRows, Math.Max(0, maxRowRanges));
            droppedRanges = Math.Max(0, totalRows - shownRows);

            sb.Append("visible_cells (").Append(NumStr(frontier.VisibleCellCount))
              .Append('/').Append(NumStr(frontier.TotalCellCount)).Append("):\n");
            if (shownRows > 0)
            {
                sb.Append("  rows: ");
                for (int i = 0; i < shownRows; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(CleanLine(frontier.VisibleRowRanges[i]));
                }
                sb.Append('\n');
                if (droppedRanges > 0)
                    sb.Append("  [truncated: ").Append(NumStr(droppedRanges)).Append(" more rows]\n");
            }
            else if (totalRows > 0)
            {
                sb.Append("  [truncated: ").Append(NumStr(totalRows)).Append(" more rows]\n");
            }
            else
            {
                sb.Append("  rows: (none)\n");
            }

            int totalTerrain = frontier.VisibleTerrainNotes == null ? 0 : frontier.VisibleTerrainNotes.Count;
            if (totalTerrain == 0)
            {
                sb.Append("  visible_terrain: uniform (all visible cells step=1)\n");
                return;
            }
            int terrainCap = maxRowRanges == int.MaxValue ? totalTerrain : Math.Max(0, maxRowRanges * 4);
            int shownTerrain = Math.Min(totalTerrain, terrainCap);
            if (shownTerrain > 0)
            {
                sb.Append("  visible_terrain: ");
                for (int i = 0; i < shownTerrain; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(CleanLine(frontier.VisibleTerrainNotes[i]));
                }
                sb.Append('\n');
            }
            if (shownTerrain < totalTerrain)
                sb.Append("  [truncated: ").Append(NumStr(totalTerrain - shownTerrain))
                  .Append(" more terrain notes]\n");
        }

        private static string RenderSpatialMovementSection(MovementView view,
            int maxOptionsPerArmy, int maxArmies, SpatialContextOptions opts,
            out SpatialTruncationReport report)
        {
            opts = opts ?? new SpatialContextOptions { Level = SpatialDetailLevel.Standard };
            int[] baseCaps =
            {
                opts.EffectiveOwnCap(), opts.EffectiveVisibleCap(), opts.EffectiveStaleCap(),
                opts.EffectiveObjectivesCap(), opts.EffectiveArmyFrontiersCap(),
            };
            bool includeRows = opts.EffectiveIncludeVisibleRanges();
            int maxRows = opts.EffectiveVisibleRangesCap();
            int totalArmies = view.Armies == null ? 0 : view.Armies.Count;
            bool isOverflowCall = maxOptionsPerArmy < int.MaxValue || maxArmies < int.MaxValue;
            string reason = null;

            if (isOverflowCall)
            {
                reason = "provider_char_limit";
                if (maxOptionsPerArmy <= 8) { includeRows = false; maxRows = 0; }
                if (maxOptionsPerArmy <= 4) baseCaps = MinCaps(baseCaps, StandardCapTuple());
                if (maxOptionsPerArmy <= 1) baseCaps = MinCaps(baseCaps, SpatialTruncationSteps[0]);
                if (maxOptionsPerArmy == 0) baseCaps = MinCaps(baseCaps, SpatialTruncationSteps[1]);
                if (maxArmies < totalArmies && maxArmies <= 4)
                    baseCaps = MinCaps(baseCaps, SpatialTruncationSteps[3]);
            }

            if (!isOverflowCall && opts.MaxSectionChars > 0)
                return RenderBoundedSpatialSection(opts, view, baseCaps, includeRows, maxRows,
                    opts.MaxSectionChars, out report);

            return AssembleSpatialSection(opts, view, baseCaps, includeRows, maxRows,
                maxOptionsPerArmy, maxArmies, reason, out report);
        }

        private static string RenderBoundedSpatialSection(SpatialContextOptions opts, MovementView view,
            int[] baseCaps, bool includeRows, int maxRows, int maxSectionChars,
            out SpatialTruncationReport report)
        {
            string full = AssembleSpatialSection(opts, view, baseCaps, includeRows, maxRows,
                int.MaxValue, int.MaxValue, null, out report);
            if (full.Length <= maxSectionChars) return full;

            // In Rich mode, preserve all legal visible movement options first by dropping visible
            // cell row ranges / capping entity summaries before cutting any movement options.
            if (opts.Level == SpatialDetailLevel.Rich && includeRows)
            {
                string noRowsAllMoves = AssembleSpatialSection(opts, view, baseCaps, false, 0,
                    int.MaxValue, int.MaxValue, "section_char_limit", out report);
                if (noRowsAllMoves.Length <= maxSectionChars) return noRowsAllMoves;

                string stdAllMoves = AssembleSpatialSection(opts, view,
                    MinCaps(baseCaps, StandardCapTuple()), false, 0, int.MaxValue, int.MaxValue,
                    "section_char_limit", out report);
                if (stdAllMoves.Length <= maxSectionChars) return stdAllMoves;
            }

            int maxMoveOpts = MaxPlanOptions(view);
            bool searchRows = opts.Level == SpatialDetailLevel.Rich ? false : includeRows;
            int searchMaxRows = searchRows ? maxRows : 0;
            if (TryFindFittingSectionMoveLimit(opts, view, baseCaps, searchRows, searchMaxRows,
                    maxMoveOpts, maxSectionChars, out string best, out SpatialTruncationReport bestReport))
            {
                report = bestReport;
                return best;
            }

            int moveFloor = maxMoveOpts > 0 ? 1 : 0;
            if (includeRows)
            {
                string noRows = AssembleSpatialSection(opts, view, baseCaps, false, 0, moveFloor,
                    int.MaxValue, "section_char_limit", out report);
                if (noRows.Length <= maxSectionChars) return noRows;
            }

            foreach (int[] step in SpatialTruncationSteps)
            {
                string tight = AssembleSpatialSection(opts, view, MinCaps(baseCaps, step), false, 0,
                    moveFloor, int.MaxValue, "section_char_limit", out report);
                if (tight.Length <= maxSectionChars) return tight;
            }

            int totalArmies = view.Armies == null ? 0 : view.Armies.Count;
            int[] zeroCaps = SpatialTruncationSteps[3];
            for (int armies = totalArmies; armies >= 0; armies--)
            {
                string one = AssembleSpatialSection(opts, view, zeroCaps, false, 0, 1, armies,
                    "section_char_limit", out report);
                if (one.Length <= maxSectionChars) return one;
            }
            for (int armies = totalArmies; armies >= 0; armies--)
            {
                string none = AssembleSpatialSection(opts, view, zeroCaps, false, 0, 0, armies,
                    "section_char_limit", out report);
                if (none.Length <= maxSectionChars) return none;
            }
            return AssembleSpatialSection(opts, view, zeroCaps, false, 0, 0, 0,
                "section_char_limit", out report);
        }

        private static bool TryFindFittingSectionMoveLimit(SpatialContextOptions opts,
            MovementView view, int[] caps, bool includeRows, int maxRows, int maxMoveOpts,
            int maxSectionChars, out string best, out SpatialTruncationReport bestReport)
        {
            best = null;
            bestReport = null;
            int lo = 1, hi = maxMoveOpts - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                string cand = AssembleSpatialSection(opts, view, caps, includeRows, maxRows, mid,
                    int.MaxValue, "section_char_limit", out SpatialTruncationReport candReport);
                if (cand.Length <= maxSectionChars)
                {
                    best = cand;
                    bestReport = candReport;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return best != null;
        }

        private static string AssembleSpatialSection(SpatialContextOptions opts, MovementView view,
            int[] caps, bool includeRows, int maxRows, int moveLimit, int armyLimit,
            string limitReason, out SpatialTruncationReport report)
        {
            string spatialText = SpatialContextText(view.SpatialContext, caps[0], caps[1], caps[2],
                caps[3], caps[4], includeRows, maxRows, opts);
            string moveText = RenderMovementOptions(view, moveLimit, armyLimit, includeSpatialTags: true);
            report = SpatialTruncationBuilder.Build(opts, view.SpatialContext, view, caps,
                includeRows, maxRows, moveLimit, armyLimit, 0, 0, limitReason);
            return spatialText + report.SummaryLine() + "\n" + moveText;
        }

        private static string AssembleBoundedSpatialPrompt(Observation observation,
            IReadOnlyList<ActionOption> options, IReadOnlyList<string> recentHistory,
            int requestsLeft, int actionsLeft, MovementView movement, SpatialContextView spatial,
            SpatialContextOptions opts, int maxPromptChars, string version, int sysLen,
            IReadOnlyList<LlmMemory> memories, string inquiryResult, LlmPersona persona,
            string attackNotice, IReadOnlyList<string> retained, string note,
            out SpatialTruncationReport report)
        {
            opts = opts ?? new SpatialContextOptions { Level = SpatialDetailLevel.Standard };
            int charLimit = maxPromptChars > 0
                ? (opts.ProviderCharLimit > 0 ? Math.Min(maxPromptChars, opts.ProviderCharLimit) : maxPromptChars)
                : opts.EffectiveProviderCharLimit();

            if (opts.MaxSectionChars > 0 && movement != null)
            {
                string secText = RenderSpatialMovementSection(movement, int.MaxValue, int.MaxValue, opts,
                    out SpatialTruncationReport secReport);
                string secPrompt = BuildUserPrompt(observation, options, recentHistory, requestsLeft,
                    actionsLeft, version, secText, memories, inquiryResult, persona, attackNotice,
                    retained, note);
                if (sysLen + secPrompt.Length <= charLimit)
                {
                    secReport.ProviderCharLimit = charLimit;
                    secReport.SystemChars = sysLen;
                    secReport.UserChars = secPrompt.Length;
                    secReport.TotalChars = sysLen + secPrompt.Length;
                    report = secReport;
                    return secPrompt;
                }
            }
            int[] baseCaps =
            {
                opts.EffectiveOwnCap(), opts.EffectiveVisibleCap(), opts.EffectiveStaleCap(),
                opts.EffectiveObjectivesCap(), opts.EffectiveArmyFrontiersCap(),
            };
            bool includeRows = opts.EffectiveIncludeVisibleRanges();
            int maxRows = opts.EffectiveVisibleRangesCap();

            string full = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                persona, attackNotice, retained, note, baseCaps, includeRows, maxRows,
                int.MaxValue, int.MaxValue, null, out report);
            if (sysLen + full.Length <= charLimit) return full;

            // In Rich mode, preserve all visible legal movement options first by dropping visible
            // cell row ranges / capping entity summaries before cutting any movement options.
            if (opts.Level == SpatialDetailLevel.Rich && includeRows)
            {
                string noRowsAll = BuildSpatialCandidate(opts, observation, options, recentHistory,
                    requestsLeft, actionsLeft, movement, spatial, version, sysLen, charLimit, memories,
                    inquiryResult, persona, attackNotice, retained, note, baseCaps, false, 0,
                    int.MaxValue, int.MaxValue, "provider_char_limit", out report);
                if (sysLen + noRowsAll.Length <= charLimit) return noRowsAll;

                string stdAll = BuildSpatialCandidate(opts, observation, options, recentHistory,
                    requestsLeft, actionsLeft, movement, spatial, version, sysLen, charLimit, memories,
                    inquiryResult, persona, attackNotice, retained, note,
                    MinCaps(baseCaps, StandardCapTuple()), false, 0, int.MaxValue, int.MaxValue,
                    "provider_char_limit", out report);
                if (sysLen + stdAll.Length <= charLimit) return stdAll;
            }

            int maxMoveOpts = MaxPlanOptions(movement);
            bool searchRows = opts.Level == SpatialDetailLevel.Rich ? false : includeRows;
            int searchMaxRows = searchRows ? maxRows : 0;
            if (TryBinarySearchMoveLimit(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, baseCaps, searchRows, searchMaxRows,
                    maxMoveOpts, out string moveFit, out SpatialTruncationReport moveReport))
            {
                report = moveReport;
                return moveFit;
            }

            int moveFloor = maxMoveOpts > 0 ? 1 : 0;
            if (includeRows)
            {
                string noRows = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, baseCaps, false, 0, moveFloor, int.MaxValue,
                    "provider_char_limit", out report);
                if (sysLen + noRows.Length <= charLimit) return noRows;
            }

            if (opts.Level == SpatialDetailLevel.Rich)
            {
                string stdCand = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, MinCaps(baseCaps, StandardCapTuple()), false, 0,
                    moveFloor, int.MaxValue, "provider_char_limit", out report);
                if (sysLen + stdCand.Length <= charLimit) return stdCand;
            }

            foreach (int[] caps in SpatialTruncationSteps)
            {
                string tight = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, MinCaps(baseCaps, caps), false, 0, moveFloor,
                    int.MaxValue, "provider_char_limit", out report);
                if (sysLen + tight.Length <= charLimit) return tight;
            }

            int[] zeroCaps = SpatialTruncationSteps[3];
            int totalArmies = movement?.Armies == null ? 0 : movement.Armies.Count;
            for (int armies = totalArmies; armies >= 0; armies--)
            {
                string one = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, zeroCaps, false, 0, 1, armies,
                    "provider_char_limit", out report);
                if (sysLen + one.Length <= charLimit) return one;
            }
            for (int armies = totalArmies; armies >= 0; armies--)
            {
                string zero = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, zeroCaps, false, 0, 0, armies,
                    "provider_char_limit", out report);
                if (sysLen + zero.Length <= charLimit) return zero;
            }
            return BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                persona, attackNotice, retained, note, zeroCaps, false, 0, 0, 0,
                "provider_char_limit", out report);
        }

        private static bool TryBinarySearchMoveLimit(SpatialContextOptions opts, Observation observation,
            IReadOnlyList<ActionOption> options, IReadOnlyList<string> recentHistory, int requestsLeft,
            int actionsLeft, MovementView movement, SpatialContextView spatial, string version,
            int sysLen, int charLimit, IReadOnlyList<LlmMemory> memories, string inquiryResult,
            LlmPersona persona, string attackNotice, IReadOnlyList<string> retained, string note,
            int[] caps, bool includeRows, int maxRows, int maxMoveOpts,
            out string best, out SpatialTruncationReport bestReport)
        {
            best = null;
            bestReport = null;
            int lo = 1, hi = maxMoveOpts - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                string cand = BuildSpatialCandidate(opts, observation, options, recentHistory, requestsLeft,
                    actionsLeft, movement, spatial, version, sysLen, charLimit, memories, inquiryResult,
                    persona, attackNotice, retained, note, caps, includeRows, maxRows, mid, int.MaxValue,
                    "provider_char_limit", out SpatialTruncationReport candReport);
                if (sysLen + cand.Length <= charLimit)
                {
                    best = cand;
                    bestReport = candReport;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return best != null;
        }

        private static string BuildSpatialCandidate(SpatialContextOptions opts, Observation observation,
            IReadOnlyList<ActionOption> options, IReadOnlyList<string> recentHistory, int requestsLeft,
            int actionsLeft, MovementView movement, SpatialContextView spatial, string version,
            int sysLen, int effectiveCharLimit, IReadOnlyList<LlmMemory> memories, string inquiryResult,
            LlmPersona persona, string attackNotice, IReadOnlyList<string> retained, string note,
            int[] caps, bool includeRows, int maxRows, int moveLimit, int armyLimit,
            string limitReason, out SpatialTruncationReport report)
        {
            string spatialText = SpatialContextText(spatial, caps[0], caps[1], caps[2], caps[3], caps[4],
                includeRows, maxRows, opts);
            string moveText = movement == null ? ""
                : RenderMovementOptions(movement, moveLimit, armyLimit, includeSpatialTags: true);
            report = SpatialTruncationBuilder.Build(opts, spatial, movement, caps, includeRows,
                maxRows, moveLimit, armyLimit, sysLen, 0, limitReason);
            report.ProviderCharLimit = effectiveCharLimit;
            string prompt = BuildUserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                version, spatialText + report.SummaryLine() + "\n" + moveText, memories, inquiryResult,
                persona, attackNotice, retained, note);
            report.UserChars = prompt.Length;
            report.TotalChars = sysLen + prompt.Length;
            return prompt;
        }

        private static int MaxPlanOptions(MovementView movement)
        {
            int max = 0;
            if (movement != null && movement.Armies != null)
                foreach (ArmyMovePlan plan in movement.Armies)
                    if (plan != null && plan.Options != null && plan.Options.Count > max)
                        max = plan.Options.Count;
            return max;
        }

        private static int[] StandardCapTuple() => new[]
        {
            SpatialContextOptions.StandardOwnCap, SpatialContextOptions.StandardVisibleCap,
            SpatialContextOptions.StandardStaleCap, SpatialContextOptions.StandardObjectivesCap,
            SpatialContextOptions.StandardArmyFrontiersCap,
        };

        private static int[] MinCaps(int[] a, int[] b) => new[]
        {
            Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
            Math.Min(a[3], b[3]), Math.Min(a[4], b[4]),
        };
    }
}
