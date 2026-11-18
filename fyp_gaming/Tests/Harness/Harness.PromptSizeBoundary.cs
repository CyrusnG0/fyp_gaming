using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LuanShi.Engine;

// [new] Deterministic probe for the rich + spatial prompt-size boundary at the 40,000-char
// provider limit. It builds RichContextInputs without clocks or network, sweeps the inputs that
// reach the ceiling, and records the result table in Tests/PromptSizeProbe.out.txt.
//
// The level-4 fix is asserted directly (the old failing boundaries now fit). Remaining boundary
// characterisations describe where the fixed assembler still cannot fit; they are not known bugs.
internal static partial class Harness
{
    private const int PromptSizeProbeLimit = 40000;
    private const int PromptSizeProbeSystemChars = 13968;
    private const string PromptSizeProbeMovementSectionName = "march destinations across your armies";

    // Captured from the current code BEFORE the level-4/reservation changes. Any request that
    // already fit at trim level <=3 must remain byte-identical: these are its prompt and report.
    private const int PromptSizeProbePreFitChars = 39882;
    private const int PromptSizeProbePreFitUserLen = 25914;
    private const string PromptSizeProbePreFitUserHash =
        "cd04ce41bc0b37aad9ac1036126a1ecf17f6e51d8dd260cb003d226cce534efa";
    private const string PromptSizeProbePreFitReportHash =
        "8ae6e5ce97cb727a548c745ffdba8e250cf6c1c32530b23efc35a010b3c05d0d";
    private const string PromptSizeProbePreFitObservationHash =
        "c482117c3c1fa45cf2ba2b8724e1e5b56d4a905acfbe3917684db3a21939ef36";
    private const string PromptSizeProbePreFitSpatialHash =
        "acc0e62be3b26c94e142c161d4c946fe5e82fd978cbb2eb059d6c414e3d549a3";
    private const int PromptSizeProbePreCompactChars = 36241;
    private const int PromptSizeProbePreCompactUserLen = 22273;
    private const string PromptSizeProbePreCompactUserHash =
        "93a5262ab8152150a7dbbcf67a0f2a28891ab506c06c408e01dbf32c09de23b6";
    private const string PromptSizeProbePreCompactReportHash =
        "b925b1e155dfc3c383f1b09ac50b5ebc300da3b98a350286cd82dddc22bec004";
    private const string PromptSizeProbePreCompactObservationHash =
        "70209247072e1dd9c585dbaf645bfe7687646e6f61dc49b2d046f34ab9958edd";
    private const string PromptSizeProbePreCompactSpatialHash =
        "06e6059ffb487a2ddf88e9725e3f9132d58d249b621a1c41421d956e42be3be4";

    private static void RunPromptSizeBoundaryProbe()
    {
        Section("[new] prompt-size boundary probe (rich deliberation + spatial, 40,000-char provider limit)");

        var outRows = new StringBuilder(8192);
        outRows.AppendLine("variable,value,trim_level,prompt_chars,limit,fits,spatial_move_options_dropped");
        string outPath = PromptSizeProbeOutputPath();

        RichContextResult baseline = ProbeAssemble(new PromptSizeProbeCase());
        ProbeWriteRow(outRows, "baseline", 0, baseline);
        Check(baseline.Report.MaxPromptChars == PromptSizeProbeLimit,
            $"[new] probe: effective prompt limit is {baseline.Report.MaxPromptChars} (min of 100000 config and 40000 provider)");
        Check(baseline.Report.TrimLevel == 3,
            $"[new] probe: the late-game baseline only fits at trim level {baseline.Report.TrimLevel}");
        Check(baseline.Report.Fits && baseline.Report.PromptChars <= PromptSizeProbeLimit,
            $"[new] probe: the baseline fits at level 3 ({baseline.Report.PromptChars}/{PromptSizeProbeLimit} chars)");

        PromptSizeProbePreChangeParity(baseline);

        // ---- H-a: level 4 absorbs the old 2-pass/note overflow ---------------------------
        int historyLineFail = PromptSizeProbeSweepHistoryLines(outRows);
        RichContextResult oldBoundary = ProbeAssemble(new PromptSizeProbeCase { HistoryLineChars = 109 });
        Check(oldBoundary.Report.Fits && oldBoundary.Report.TrimLevel == 4,
            $"[new] probe H-a fixed: the pre-fix boundary (109 history chars, was 40001/40000 at level 3) "
            + $"now fits at level {oldBoundary.Report.TrimLevel} ({oldBoundary.Report.PromptChars}/{PromptSizeProbeLimit})");
        if (historyLineFail > 0)
        {
            RichContextResult fail = ProbeAssemble(new PromptSizeProbeCase { HistoryLineChars = historyLineFail });
            Check(!fail.Report.Fits && fail.Report.TrimLevel >= 4
                  && fail.Report.UnfitReason == "mandatory_context_exceeds_limit",
                $"[new] probe H-a: level 5 still overflows at history line {historyLineFail} chars "
                + $"({fail.Report.PromptChars}/{PromptSizeProbeLimit}, reason={fail.Report.UnfitReason})");
            Check(ProbeAssemble(new PromptSizeProbeCase { HistoryLineChars = historyLineFail - 1 }).Report.Fits,
                "[new] probe H-a: one character shorter history fits, so this is the new boundary");
        }
        else
        {
            Check(false, "[new] probe H-a: a 40-line history up to 2000 chars/line never overflowed at level 4");
        }

        // ---- H-b: the assembler no longer passes budget 0 --------------------------------
        MovementView probeMovement = ProbeMovement(new PromptSizeProbeCase());
        var probeOpts = new SpatialContextOptions
        {
            Level = SpatialDetailLevel.Rich,
            ProviderCharLimit = PromptSizeProbeLimit,
        };
        string fullSection = PromptBuilder.MovementOptionsText(probeMovement, int.MaxValue, int.MaxValue,
            probeOpts, out _);
        string zeroBudgetSection = PromptBuilder.RenderSpatialMovementSectionForBudget(probeMovement,
            probeOpts, 0, out SpatialTruncationReport zeroReport);
        string oneBudgetSection = PromptBuilder.RenderSpatialMovementSectionForBudget(probeMovement,
            probeOpts, 1, out SpatialTruncationReport oneReport);
        ProbeWriteRawRow(outRows, "render_section_budget", 0, -1, zeroBudgetSection.Length,
            PromptSizeProbeLimit, false, zeroReport?.MoveOptionsDropped ?? -1);
        ProbeWriteRawRow(outRows, "render_section_budget", 1, -1, oneBudgetSection.Length,
            PromptSizeProbeLimit, oneBudgetSection.Length <= 1, oneReport?.MoveOptionsDropped ?? -1);
        Check(zeroBudgetSection.Length == fullSection.Length && zeroReport != null
              && zeroReport.MoveOptionsDropped == 0,
            $"[new] probe H-b: the shared renderer's budget=0 semantics are unchanged for other callers "
            + $"(full {zeroBudgetSection.Length} chars, dropped={zeroReport?.MoveOptionsDropped})");
        Check(oneBudgetSection.Length < zeroBudgetSection.Length && oneReport != null
              && oneReport.MoveOptionsDropped > 0,
            $"[new] probe H-b: budget=1 selects the smallest section "
            + $"({oneBudgetSection.Length} chars, dropped={oneReport?.MoveOptionsDropped})");

        int hbCityFail = PromptSizeProbeSweepObservationCities(outRows,
            out RichContextResult hbFirstFail, out int hbZeroCities, out RichContextResult hbZeroResult);
        Check(hbFirstFail != null && !hbFirstFail.Report.Fits,
            hbFirstFail == null
                ? "[new] probe H-b: adding up to 200 observation cities never overflowed at 40000"
                : $"[new] probe H-b: adding {hbCityFail} observation cities overflows at level 4 "
                    + $"({hbFirstFail.Report.PromptChars}/{PromptSizeProbeLimit})");
        if (hbZeroResult != null)
        {
            string hbZeroSection = PromptSizeProbeMovementSection(hbZeroResult.User);
            Check(PromptSizeProbeNonMovementReachesLimit(hbZeroResult)
                  && hbZeroSection.Length <= RichContextAssembler.MovementReserveChars
                  && hbZeroSection.Length < fullSection.Length
                  && hbZeroResult.Report.SpatialTruncation != null
                  && hbZeroResult.Report.SpatialTruncation.MoveOptionsDropped > 0,
                $"[new] probe H-b fixed: at {hbZeroCities} extra cities the non-movement text alone reaches "
                + $"the ceiling, and the assembler now returns the {hbZeroSection.Length}-char reserve (not the full section) with "
                + $"dropped={hbZeroResult.Report.SpatialTruncation?.MoveOptionsDropped} instead of the full "
                + $"{fullSection.Length} chars");
            Check(!hbZeroResult.Report.Fits,
                "[new] probe H-b: the prompt still fails because the non-movement text alone already exceeds the ceiling");
        }
        else
        {
            Check(false, "[new] probe H-b: adding up to 200 observation cities never reached the budget<=0 branch");
        }

        // ---- H-c / level 5: the inquiry is bounded, never silently dropped -----------------
        RichContextResult oldInquiry = ProbeAssemble(new PromptSizeProbeCase { InquiryChars = 674 });
        Check(oldInquiry.Report.Fits && oldInquiry.Report.TrimLevel == 4
              && oldInquiry.User.Contains(ProbeInquiry(674), StringComparison.Ordinal),
            $"[new] probe H-c fixed within the level-4 budget: the pre-fix 674-char inquiry now fits at "
            + $"level {oldInquiry.Report.TrimLevel} ({oldInquiry.Report.PromptChars}/{PromptSizeProbeLimit}) "
            + "and is still present verbatim");
        int inquiryFail = PromptSizeProbeSweepInquiry(outRows);
        Check(inquiryFail < 0,
            "[new] probe level 5: the 0..40000 inquiry sweep fits at every size "
            + "(the mandatory text stays bounded and level 5 bounds the result)");
        Check(ProbeAssemble(new PromptSizeProbeCase { InquiryChars = 40000 }).Report.Fits,
            "[new] probe level 5: a 40,000-char inquiry result still fits the 40,000-char prompt");

        PromptSizeBoundaryLevel4Report();
        PromptSizeBoundaryInquiryBuckets();
        PromptSizeBoundaryMandatoryExceeds();
        PromptSizeBoundaryFloorUnfit();
        PromptSizeBoundaryInquiryBudgetAccounting();
        PromptSizeBoundaryMandatorySectionsPreserved();
        PromptSizeBoundaryDiplomacyRunner();
        PromptSizeBoundaryReservationFailure();
        PromptSizeBoundaryDiagnosticsCountsOnly();
        PromptSizeBoundaryPilotSummary();
        PromptSizeBoundaryOriginalCommitParity();
        PromptSizeBoundaryRunnerDiagnostics();
        PromptSizeBoundaryReserve();
        PromptSizeBoundaryLongTurn();

        if (PromptSizeProbeWrite(outPath, outRows))
            Console.WriteLine("    probe sweep written: " + outPath);
    }

    /// <summary>Level-4 reduction reporting plus the never-truncated inputs.</summary>
    private static void PromptSizeBoundaryLevel4Report()
    {
        var historyCase = new PromptSizeProbeCase { HistoryLineChars = 109 };
        RichContextInputs historyInput = ProbeInputs(historyCase);
        RichContextResult historyResult = ProbeAssembleWithInputs(historyInput);
        string eventName = "observation events (newest kept)";
        string historyName = "own action feedback lines (newest kept)";
        string memoryName = "long-term memories (ranked, character-limited)";
        RichContextReport.Section events = historyResult.Report.Sections.Find(s => s != null && s.Name == eventName);
        RichContextReport.Section history = historyResult.Report.Sections.Find(s => s != null && s.Name == historyName);
        RichContextReport.Section memories = historyResult.Report.Sections.Find(s => s != null && s.Name == memoryName);
        Check(historyResult.Report.TrimLevel == 4 && historyResult.Report.Fits,
            $"[new] probe level 4: history case fits at trim level {historyResult.Report.TrimLevel} "
            + $"({historyResult.Report.PromptChars}/{PromptSizeProbeLimit})");
        Check(events != null && events.Included == 10 && events.Available == 120
              && events.Note != null && events.Note.Contains("older events: use search_history", StringComparison.Ordinal)
              && events.Note.Contains("further reduced for the prompt-size ceiling", StringComparison.Ordinal),
            "[new] probe level 4 report states the event reduction (10 of 120, use search_history)");
        Check(history != null && history.Included == 6 && history.Available == 40
              && history.Note != null
              && history.Note.Contains("oldest feedback lines omitted", StringComparison.Ordinal),
            "[new] probe level 4 report states the feedback-history reduction (6 of 40)");
        Check(memories != null && memories.Included > 0 && memories.Included < memories.Available
              && memories.Note != null
              && memories.Note.Contains("the rest: use recall_memory", StringComparison.Ordinal)
              && memories.Note.Contains("memory budget halved", StringComparison.Ordinal),
            "[new] probe level 4 report states the memory reduction (use recall_memory, budget halved)");

        Check(historyResult.User.Contains(ActionCatalogue.ToPromptText(historyInput.Options),
                StringComparison.Ordinal)
              && historyResult.User.Contains("INFORMATION ACCESS AND LIMITS", StringComparison.Ordinal)
              && historyResult.User.Contains("Allowances before this reply", StringComparison.Ordinal)
              && historyResult.User.Contains("Provider ceilings:", StringComparison.Ordinal),
            "[new] probe level 4 never drops the candidate orders or the allowance/budget text");

        var inquiryCase = new PromptSizeProbeCase { InquiryChars = 674 };
        RichContextInputs inquiryInput = ProbeInputs(inquiryCase);
        RichContextResult inquiryResult = ProbeAssembleWithInputs(inquiryInput);
        Check(inquiryResult.Report.TrimLevel == 4
              && inquiryResult.User.Contains(ProbeInquiry(674), StringComparison.Ordinal)
              && inquiryResult.User.Contains(ActionCatalogue.ToPromptText(inquiryInput.Options),
                    StringComparison.Ordinal),
            "[new] probe level 4 preserves the inquiry result and candidate orders byte-for-byte");
    }

    /// <summary>The pre-fix levels 0-3 must stay byte-identical when the input already fit.</summary>
    private static void PromptSizeProbePreChangeParity(RichContextResult baseline)
    {
        Check(baseline.Report.PromptChars == PromptSizeProbePreFitChars
              && baseline.User.Length == PromptSizeProbePreFitUserLen
              && PromptSizeProbeHash(baseline.User) == PromptSizeProbePreFitUserHash
              && PromptSizeProbeHash(baseline.Report.ToJson()) == PromptSizeProbePreFitReportHash
              && PromptSizeProbeHash(baseline.ObservationJson) == PromptSizeProbePreFitObservationHash
              && PromptSizeProbeHash(baseline.SpatialTruncation.ToJson()) == PromptSizeProbePreFitSpatialHash,
            "[new] probe parity: a level-3 fit is byte-identical to the pre-level-4 code "
            + "(user, report, observation JSON and spatial truncation)");

        var compactCase = new PromptSizeProbeCase
        {
            HistoryLineChars = 0, EventCount = 10, MemoryCount = 5, PriorResultCount = 0,
            OptionsPerArmy = 5, ArmyCount = 2, VisibleRowCount = 3,
        };
        RichContextResult compact = ProbeAssemble(compactCase);
        Check(compact.Report.TrimLevel == 0 && compact.Report.PromptChars == PromptSizeProbePreCompactChars
              && compact.User.Length == PromptSizeProbePreCompactUserLen
              && PromptSizeProbeHash(compact.User) == PromptSizeProbePreCompactUserHash
              && PromptSizeProbeHash(compact.Report.ToJson()) == PromptSizeProbePreCompactReportHash
              && PromptSizeProbeHash(compact.ObservationJson) == PromptSizeProbePreCompactObservationHash
              && PromptSizeProbeHash(compact.SpatialTruncation.ToJson()) == PromptSizeProbePreCompactSpatialHash,
            "[new] probe parity: a level-0 fit is byte-identical to the pre-level-4 code");
    }
}
