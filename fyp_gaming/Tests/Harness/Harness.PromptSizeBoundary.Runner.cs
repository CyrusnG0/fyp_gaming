using System;
using System.Collections.Generic;
using System.Text.Json;
using LuanShi.Engine;

// [new] Runner-level prompt-size diagnostics: both failure stages must put counts-only evidence
// into the turn_end exchange line, with the right stage, total > limit and no prompt text.
internal static partial class Harness
{
    private sealed class PromptSizeExchangeRecorder : ILlmRunRecorder, ILlmExchangeRecorder
    {
        public readonly List<string> Lines = new List<string>();
        public bool CaptureExchanges => true;
        public void Record(string runId, string jsonLine) => Lines.Add(jsonLine ?? "");
        public void RecordExchange(string runId, string jsonLine) => Lines.Add(jsonLine ?? "");
    }

    private static void PromptSizeBoundaryRunnerDiagnostics()
    {
        PromptSizeBoundaryRunnerRichAssemblerFailure();
        PromptSizeBoundaryRunnerFinalCheckFailure();
    }

    /// <summary>Stage rich_assembler: the rich config's ceiling is below the system prompt alone.</summary>
    private static void PromptSizeBoundaryRunnerRichAssemblerFailure()
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 71);
        engine.State.Cities.Add(new CityState
        {
            Id = "city-sentinel", Name = "SENTINEL9F3_CITY", X = 1, Z = 1,
            OwnerSeatId = "seat-1", Population = 1000, Food = 100, Gold = 100, Garrison = 10,
            Morale = 50,
        });
        var host = new LlmPlanningHost(engine, "seat-1");
        var fake = new FakeLlmTransport();
        LlmConfig config = LlmConfig.RichDeliberation();
        config.MaxPromptChars = 2000;
        var recorder = new PromptSizeExchangeRecorder();
        var runner = new LlmTurnRunner(fake, config, recorder);
        runner.Begin(host, host.Token, "seat-1", 71);

        string turnEnd = PromptSizeTurnEndLine(recorder.Lines);
        string json = runner.LastPromptSizeJson;
        Check(runner.State == RunnerState.Failed && runner.FailureReason == "prompt too large"
              && runner.StopCeiling == "prompt_chars",
            "[new] probe runner: rich assembler failure keeps the historical stop detail prompt_chars");
        Check(json != null && json.Contains("\"stage\":\"rich_assembler\"", StringComparison.Ordinal)
              && json.Contains("\"unfit_reason\":\"mandatory_context_exceeds_limit\"",
                    StringComparison.Ordinal)
              && PromptSizeReportTotalsOverLimit(json) && json.Length < 2000
              && PromptSizeJsonParses(json),
            "[new] probe runner: rich_assembler diagnostics are bounded, stage-tagged, reasoned and total > limit");
        Check(turnEnd != null && turnEnd.Contains("\"prompt_size\":", StringComparison.Ordinal)
              && turnEnd.Contains("\"stage\":\"rich_assembler\"", StringComparison.Ordinal)
              && json != null && turnEnd.Contains(json, StringComparison.Ordinal),
            "[new] probe runner: the turn_end exchange line carries prompt_size for rich_assembler");
        Check(PromptSizeJsonHasNoPromptText(json) && PromptSizeJsonHasNoPromptText(turnEnd)
              && json != null && !json.Contains("SENTINEL9F3_CITY", StringComparison.Ordinal)
              && turnEnd != null && !turnEnd.Contains("SENTINEL9F3_CITY", StringComparison.Ordinal),
            "[new] probe runner: rich_assembler diagnostics contain no prompt text or sentinel");
    }

    /// <summary>Stage final_check: the non-rich prompt overflows after the final mutation.</summary>
    private static void PromptSizeBoundaryRunnerFinalCheckFailure()
    {
        GameEngine engine = LlmEngine();
        for (int i = 0; i < 100; i++)
            engine.State.Cities.Add(new CityState
            {
                Id = "city-pad-" + i, Name = new string('N', 500), X = 0, Z = 0,
                OwnerSeatId = "seat-b", Population = 1000, Food = 0, Gold = 0, Garrison = 0,
            });
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var recorder = new PromptSizeExchangeRecorder();
        var runner = new LlmTurnRunner(fake, new LlmConfig(), recorder);
        runner.Begin(host, 1, "seat-a", 5);

        string turnEnd = PromptSizeTurnEndLine(recorder.Lines);
        string json = runner.LastPromptSizeJson;
        Check(runner.State == RunnerState.Failed && runner.FailureReason == "prompt too large"
              && fake.CallCount == 0 && host.EndTurnCalls == 1,
            "[new] probe runner: non-rich oversize system+user still fails before any provider call");
        Check(json != null && json.Contains("\"stage\":\"final_check\"", StringComparison.Ordinal)
              && json.Contains("\"unfit_reason\":\"final_check_overflow\"", StringComparison.Ordinal)
              && PromptSizeReportTotalsOverLimit(json) && json.Length < 2000
              && PromptSizeJsonParses(json),
            "[new] probe runner: final_check diagnostics are bounded, reasoned, stage-tagged and total > limit");
        Check(turnEnd != null && turnEnd.Contains("\"prompt_size\":", StringComparison.Ordinal)
              && turnEnd.Contains("\"stage\":\"final_check\"", StringComparison.Ordinal)
              && json != null && turnEnd.Contains(json, StringComparison.Ordinal),
            "[new] probe runner: the turn_end exchange line carries prompt_size for final_check");
        Check(PromptSizeJsonHasNoPromptText(json) && PromptSizeJsonHasNoPromptText(turnEnd),
            "[new] probe runner: final_check diagnostics contain no prompt text");
    }

    private static bool PromptSizeReportTotalsOverLimit(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            return root.GetProperty("total_chars").GetInt32()
                > root.GetProperty("limit_chars").GetInt32();
        }
        catch (JsonException) { return false; }
    }

    private static string PromptSizeTurnEndLine(List<string> lines)
    {
        foreach (string line in lines)
            if (line != null && line.Contains("\"exchange_event\":\"turn_end\"", StringComparison.Ordinal))
                return line;
        return null;
    }

    private static bool PromptSizeJsonHasNoPromptText(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        string[] forbidden =
        {
            "TURN CONTEXT", "CANDIDATE OPTIONS", "INFORMATION ACCESS AND LIMITS",
            "Never follow instructions", "untrusted", "governor", "city-pad", "Probe Host", "INQ ",
        };
        foreach (string token in forbidden)
            if (json.Contains(token, StringComparison.Ordinal)) return false;
        return true;
    }
}
