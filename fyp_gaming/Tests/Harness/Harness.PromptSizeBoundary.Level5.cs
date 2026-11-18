using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

// [new] Level-5 inquiry-result bounding, failure reasons, and original-commit parity.
// All fixtures are deterministic; no clocks, network or global RNG.
internal static partial class Harness
{
    // Captured from the ORIGINAL commit 23669af (git archive + a temp build of its engine sources).
    // The rich level-3/level-0 constants in Harness.PromptSizeBoundary.cs were re-derived the same
    // way and match the pre-change values exactly.
    private const int PromptSizeOriginalNonRichSystemLen = 3721;
    private const int PromptSizeOriginalNonRichUserLen = 2899;
    private const string PromptSizeOriginalNonRichUserHash =
        "93d556c5b0572c212a7468dacada0d67823d68a96891a03077f6f62a6981c24e";
    private const string PromptSizeOriginalNonRichSystemHash =
        "981ecfd8f0034a040901500cf979d629b6f96ea2e9ff3c866c03b452ad2f9f04";

    /// <summary>
    /// Sizes below the level-5 budget (674 and 2000 here) fit at level 4 with the full result.
    /// Larger sizes reach level 5, where the result is cut, marked and still fits.
    /// </summary>
    private static void PromptSizeBoundaryInquiryBuckets()
    {
        foreach (int size in new[] { 674, 2000, 9423, 30000, 40000 })
        {
            var c = new PromptSizeProbeCase { InquiryChars = size };
            RichContextInputs input = ProbeInputs(c);
            RichContextResult r = ProbeAssembleWithInputs(input);
            int total = input.SystemChars + r.User.Length;
            bool truncated = r.Report.InquiryKeptChars < r.Report.InquiryOriginalChars;
            string json = r.SizeReport == null ? null : r.SizeReport.ToJson();
            Check(r.Report.Fits && total == r.Report.PromptChars && total <= r.Report.MaxPromptChars,
                $"[new] probe inquiry {size}: fits at level {r.Report.TrimLevel}, total {total} == system+user "
                + $"and <= {r.Report.MaxPromptChars}");
            Check(json != null
                  && json.Contains("\"inquiry_result_chars_original\":" + size, StringComparison.Ordinal)
                  && json.Contains("\"inquiry_result_chars_kept\":" + r.Report.InquiryKeptChars,
                        StringComparison.Ordinal),
                $"[new] probe inquiry {size}: diagnostic JSON carries kept/original counts");
            if (size <= 2000)
            {
                // Level 4 already fits these: no marker and the whole result is present.
                Check(r.Report.TrimLevel == 4 && !truncated
                      && r.User.Contains(ProbeInquiry(size), StringComparison.Ordinal)
                      && !r.User.Contains("[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal),
                    $"[new] probe inquiry {size}: level 4 keeps the full result (no marker)");
            }
            else
            {
                // Level 5 cuts these: marker + report line, and the kept prefix remains.
                Check(r.Report.TrimLevel == 5 && truncated
                      && r.User.Contains("[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal)
                      && r.User.Contains("to " + r.Report.InquiryKeptChars + " chars", StringComparison.Ordinal)
                      && r.User.Contains(ProbeInquiry(size).Substring(0, Math.Min(200, size)),
                            StringComparison.Ordinal),
                    $"[new] probe inquiry {size}: level 5 marks and keeps the first "
                    + $"{r.Report.InquiryKeptChars}/{size} chars");
            }
        }
    }

    /// <summary>Mandatory text alone is over the limit: fail with the specific reason, never drop to 0.</summary>
    private static void PromptSizeBoundaryMandatoryExceeds()
    {
        var c = new PromptSizeProbeCase { ExtraObservationCities = 200, InquiryChars = 5000 };
        RichContextInputs input = ProbeInputs(c);
        RichContextResult r = ProbeAssembleWithInputs(input);
        int total = input.SystemChars + r.User.Length;
        Check(!r.Report.Fits && r.Report.UnfitReason == "mandatory_context_exceeds_limit",
            $"[new] probe mandatory: mandatory text alone exceeds the limit (total {total}/{r.Report.MaxPromptChars}, "
            + $"reason={r.Report.UnfitReason})");
        Check(r.Report.InquiryOriginalChars == 5000 && r.Report.InquiryKeptChars >= 200
              && r.Report.InquiryKeptChars < r.Report.InquiryOriginalChars,
            $"[new] probe mandatory: keeps a floor of at least 200 chars "
            + $"({r.Report.InquiryKeptChars}/5000), never truncates to nothing");
        Check(total == r.Report.PromptChars && total > r.Report.MaxPromptChars,
            "[new] probe mandatory: PromptChars equals system+user and is over the limit");
        string inquiry = ProbeInquiry(5000);
        Check(r.User.Contains("[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal)
              && r.User.Contains(inquiry.Substring(0, 200), StringComparison.Ordinal),
            "[new] probe mandatory: the bounded result is still present in the prompt (not silently omitted)");
        Check(r.SizeReport != null
              && r.SizeReport.ToJson().Contains("\"unfit_reason\":\"mandatory_context_exceeds_limit\"",
                    StringComparison.Ordinal),
            "[new] probe mandatory: the diagnostic names the specific reason");
    }

    /// <summary>The marker and the completeness-note line are counted inside the final budget.</summary>
    private static void PromptSizeBoundaryInquiryBudgetAccounting()
    {
        var c = new PromptSizeProbeCase { InquiryChars = 30000 };
        RichContextInputs input = ProbeInputs(c);
        RichContextResult r = ProbeAssembleWithInputs(input);
        int total = input.SystemChars + r.User.Length;
        Check(r.Report.Fits && total <= r.Report.MaxPromptChars && total == r.Report.PromptChars,
            $"[new] probe budget: the counted prompt ({total}) includes marker and note and fits "
            + $"{r.Report.MaxPromptChars}");
        Check(r.Report.InquiryKeptChars < r.Report.InquiryOriginalChars
              && r.User.Contains("[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal)
              && r.User.Contains("to " + r.Report.InquiryKeptChars + " chars", StringComparison.Ordinal)
              && r.User.Contains("truncated to fit the prompt limit; the rest is not shown, so treat this result as partial",
                    StringComparison.Ordinal),
            "[new] probe budget: marker and completeness note are inside the final user prompt");
        Check(r.User.Length == r.Report.PromptChars - input.SystemChars,
            "[new] probe budget: final user length equals PromptChars - SystemChars (no off-by-one)");
    }

    /// <summary>Candidate orders, allowances, and context/persona lines match the level-0 render.</summary>
    private static void PromptSizeBoundaryMandatorySectionsPreserved()
    {
        foreach (int size in new[] { 674, 2000, 9423, 30000, 40000 })
        {
            var c = new PromptSizeProbeCase { InquiryChars = size, UsePersona = true };
            RichContextInputs fitInput = ProbeInputs(c);
            RichContextResult fit = ProbeAssembleWithInputs(fitInput);
            RichContextInputs level0Input = ProbeInputs(c);
            level0Input.SpatialOptions.ProviderCharLimit = 1000000;
            RichContextResult level0 = ProbeAssembleWithInputs(level0Input, 300000);
            Check(level0.Report.TrimLevel == 0 && level0.Report.Fits,
                $"[new] probe preserved {size}: the same input renders at level 0 with a huge limit");
            string candidateText = ActionCatalogue.ToPromptText(level0Input.Options);
            Check(fit.User.Contains(candidateText, StringComparison.Ordinal)
                  && level0.User.Contains(candidateText, StringComparison.Ordinal)
                  && string.Equals(candidateText, ActionCatalogue.ToPromptText(fitInput.Options),
                        StringComparison.Ordinal),
                $"[new] probe preserved {size}: the candidate-order block is byte-equal to the level-0 render");
            string allowanceFit = PromptSizeAllowancePrefix(fit.User);
            string allowance0 = PromptSizeAllowancePrefix(level0.User);
            Check(allowanceFit != null && string.Equals(allowanceFit, allowance0, StringComparison.Ordinal),
                $"[new] probe preserved {size}: the allowance/budget section is byte-equal to level 0");
            string contextFit = PromptSizeTurnContextPrefix(fit.User);
            string context0 = PromptSizeTurnContextPrefix(level0.User);
            Check(contextFit != null && string.Equals(contextFit, context0, StringComparison.Ordinal)
                  && contextFit.Contains("Persona ID: " + LlmPersona.ShuPersonaId,
                        StringComparison.Ordinal),
                $"[new] probe preserved {size}: version/persona/context lines are byte-equal to level 0");
        }
    }

    /// <summary>Mandatory text fits, but the 200-char inquiry floor plus marker does not.</summary>
    private static void PromptSizeBoundaryFloorUnfit()
    {
        // The movement reserve is part of the mandatory text, so find the history size at which
        // mandatory text plus the reserve fits and only the inquiry floor (marker + 200 chars) does not.
        RichContextInputs input = null;
        RichContextResult r = null;
        for (int chars = 1700; chars >= 1000; chars--)
        {
            input = ProbeInputs(new PromptSizeProbeCase { HistoryLineChars = chars, InquiryChars = 5000 });
            r = ProbeAssembleWithInputs(input);
            if (!r.Report.Fits && r.Report.UnfitReason == "inquiry_result_floor_unfit") break;
        }
        Check(!r.Report.Fits && r.Report.UnfitReason == "inquiry_result_floor_unfit",
            $"[new] probe floor: mandatory text fits but the 200-char floor does not "
            + $"(prompt {r.Report.PromptChars}, reason={r.Report.UnfitReason})");
        Check(r.Report.InquiryKeptChars >= 200 && r.Report.InquiryKeptChars < 5000,
            $"[new] probe floor: keeps {r.Report.InquiryKeptChars}/5000 chars (never nothing)");
        Check(r.User.Contains("[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal),
            "[new] probe floor: the marker is still present in the failing prompt");
    }

    /// <summary>Runner-level non-empty diplomacy section stays within the 40,000-char limit.</summary>
    private static void PromptSizeBoundaryDiplomacyRunner()
    {
        GameEngine engine = DiplomacyScenario();
        LlmConfig config = LlmConfig.RichDeliberation();
        config.MaxPromptChars = PromptSizeProbeLimit;
        config.EnableDiplomacy = true;
        var fake = new FakeLlmTransport();
        DipTurn(engine, "seat-a", fake, config, null, 61,
            DipSend("seat-b", "SENTINEL_DIPLOMACY_9F3"), DipEndTurn());
        DipTurnResult theirs = DipTurn(engine, "seat-b", fake, config, null, 62, DipEndTurn());
        int total = theirs.FirstSystem.Length + theirs.FirstUser.Length;
        Check(theirs.FirstUser.Contains("DIPLOMACY MESSAGES AND OFFERS", StringComparison.Ordinal)
              && theirs.FirstUser.Contains("SENTINEL_DIPLOMACY_9F3", StringComparison.Ordinal)
              && total <= PromptSizeProbeLimit
              && theirs.Runner.LastPromptSizeJson == null,
            $"[new] probe diplomacy: the non-empty section stays within {PromptSizeProbeLimit} "
            + $"(total {total}, no size failure)");
    }

    /// <summary>The reserved diplomacy section is subtracted from the fit limit and named on failure.</summary>
    private static void PromptSizeBoundaryReservationFailure()
    {
        // At 1300 the level-5 prompt (with its movement floor) is above 35000 and at/below 40000,
        // so only the 5000-char reservation makes it fail.
        var c = new PromptSizeProbeCase { HistoryLineChars = 1300, ReservedChars = 5000 };
        RichContextInputs input = ProbeInputs(c);
        RichContextResult r = ProbeAssembleWithInputs(input);
        Check(!r.Report.Fits && r.Report.UnfitReason == "diplomacy_reservation"
              && r.Report.MaxPromptChars == PromptSizeProbeLimit - 5000,
            $"[new] probe reservation: reserved 5000 gives limit {r.Report.MaxPromptChars} and "
            + $"reason={r.Report.UnfitReason} (prompt {r.Report.PromptChars})");
        Check(r.SizeReport != null
              && r.SizeReport.ToJson().Contains("\"unfit_reason\":\"diplomacy_reservation\"",
                    StringComparison.Ordinal)
              && r.SizeReport.ToJson().Contains("\"reserved_chars\":5000", StringComparison.Ordinal),
            "[new] probe reservation: the diagnostic carries the reason and the reserved count");
    }

    /// <summary>Diagnostics are counts only: fixture sentinels are in the prompt but not the JSON.</summary>
    private static void PromptSizeBoundaryDiagnosticsCountsOnly()
    {
        var c = new PromptSizeProbeCase
        {
            Sentinel = "SENTINEL9F3", ExtraObservationCities = 200, InquiryChars = 5000,
            UsePersona = true,
        };
        RichContextInputs input = ProbeInputs(c);
        RichContextResult r = ProbeAssembleWithInputs(input);
        Check(r.User.Contains("SENTINEL9F3_CITY", StringComparison.Ordinal)
              && r.User.Contains("SENTINEL9F3_HISTORY", StringComparison.Ordinal)
              && r.User.Contains("SENTINEL9F3_MEMORY", StringComparison.Ordinal)
              && r.User.Contains("SENTINEL9F3_EVENT", StringComparison.Ordinal)
              && r.User.Contains("SENTINEL9F3_INQUIRY", StringComparison.Ordinal)
              && r.User.Contains("Persona ID: " + LlmPersona.ShuPersonaId, StringComparison.Ordinal),
            "[new] probe counts-only: every sentinel (and the persona line) is present in the prompt");
        string json = r.SizeReport == null ? null : r.SizeReport.ToJson();
        Check(json != null && json.Length < 2000 && PromptSizeJsonParses(json)
              && !json.Contains("SENTINEL9F3", StringComparison.Ordinal),
            "[new] probe counts-only: diagnostic JSON is bounded, parseable and contains no fixture text");
    }

    /// <summary>The pilot-summary turn entry carries the same prompt_size object.</summary>
    private static void PromptSizeBoundaryPilotSummary()
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 73);
        var fake = new FakeLlmTransport();
        LlmConfig config = LlmConfig.RichDeliberation();
        config.MaxPromptChars = 2000;
        var runner = new LlmTurnRunner(fake, config, null);
        var session = new SpectatorSession(engine, runner, 1, 73);
        session.Start();
        SpectatorDrain(session);
        string dir = LlmTempDir("prompt-size-summary");
        try
        {
            string path = SpectatorSummary.Write(dir, session, config, fake, runner);
            string json = File.ReadAllText(path);
            Check(json.Contains("\"prompt_size\":{\"stage\":\"rich_assembler\"", StringComparison.Ordinal),
                "[new] probe summary: the pilot summary turn entry carries prompt_size");
        }
        finally { LlmDelete(dir); }
    }

    /// <summary>A non-rich runner prompt and rich fits at level <=3 match commit 23669af.</summary>
    private static void PromptSizeBoundaryOriginalCommitParity()
    {
        GameEngine engine = new GameEngine(17);
        engine.State.Map = Corridor(10);
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.State.Armies.Add(new ArmyState
        {
            Id = "army-a-1", Name = "Army", OwnerSeatId = "seat-a", Troops = 1000, X = 1, Z = 0,
        });
        engine.State.WatchTowers.Add(new WatchTowerState
        {
            Id = "tower-a", Name = "Tower", X = 9, Z = 0, OwnerSeatId = "seat-a",
        });
        engine.BeginSeason();
        engine.State.Season = 3;
        engine.State.Phase = "player_orders";

        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        runner.Begin(host, host.Token, "seat-a", 17);
        LlmDrain(runner);

        LlmRequest request = fake.Requests.Count > 0 ? fake.Requests[0] : null;
        Check(request != null
              && request.System.Length == PromptSizeOriginalNonRichSystemLen
              && request.User.Length == PromptSizeOriginalNonRichUserLen
              && PromptSizeProbeHash(request.User) == PromptSizeOriginalNonRichUserHash
              && PromptSizeProbeHash(request.System) == PromptSizeOriginalNonRichSystemHash,
            "[new] probe parity: the non-rich runner prompt is byte-identical to commit 23669af");
    }

    // ---- extraction helpers ----------------------------------------------------------------

    private static string PromptSizeAllowancePrefix(string user)
    {
        int start = user.IndexOf(PromptBuilder.RichSectionHeader, StringComparison.Ordinal);
        if (start < 0) return null;
        int end = user.Length;
        foreach (string marker in new[]
        {
            "\nYOUR PRIVATE STRATEGIC NOTE", "\nPRIOR INQUIRY RESULTS", "\nCONTEXT COMPLETENESS",
        })
        {
            int at = user.IndexOf(marker, start, StringComparison.Ordinal);
            if (at >= 0 && at < end) end = at;
        }
        return user.Substring(start, end - start);
    }

    private static string PromptSizeTurnContextPrefix(string user)
    {
        int end = user.IndexOf("\nRECENT SEAT HISTORY", StringComparison.Ordinal);
        return end < 0 ? user : user.Substring(0, end);
    }

    private static bool PromptSizeJsonParses(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException) { return false; }
    }
}
