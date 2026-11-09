using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LuanShi.Engine;

internal static partial class DiagnosticsTests
{
    // ---- explicit, single-invocation live probe -----------------------------
    //
    // This is never part of the normal test run. It performs at most two provider
    // requests in one process (hard cap), and only when the caller invokes it:
    //   dotnet run (project) Tests/ResponseDiagnostics/ResponseDiagnostics.csproj -c Release \
    //     -- --live-probe <configPath> <runDirectory>
    //
    // Request 1 uses the configured output cap. A second request with a deliberately tiny
    // cap is made only when request 1 shows no reasoning field at all, to test whether the
    // provider accounts reasoning against max_tokens. No output ever prints the API key,
    // authorization header or model text.

    private sealed class ProbeResult
    {
        public string Tag;
        public int MaxOutputTokens;
        public int Requests;
        public string State;
        public string EndReason;
        public string FailureReason;
        public bool Ok;
        public string FinishReason;
        public int? InputTokens;
        public int? OutputTokens;
        public int? ReasoningTokens;
        public int? TotalTokens;
        public int? ReasoningContentChars;
        public int ContentChars;
        public bool? ContentTrimmedEmpty;
        public string ResultError;
        public string RunRecordPath;
        public string ExchangePath;
        public bool ReasoningObserved =>
            (ReasoningTokens.HasValue && ReasoningTokens.Value > 0)
            || (ReasoningContentChars.HasValue && ReasoningContentChars.Value > 0);
        public bool ContentEmpty => ContentChars == 0 || ContentTrimmedEmpty == true;
    }

    private static int RunLiveProbe(string[] args)
    {
        string configPath = args[1];
        string runDir = args.Length >= 3
            ? args[2]
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "runs"));

        LlmConfig config;
        try { config = LlmConfig.LoadFromFile(configPath); }
        catch (Exception) { Console.WriteLine("PROBE error: config could not be loaded"); return 2; }
        if (!config.HasApiKey)
        {
            Console.WriteLine("PROBE error: API key is not configured");
            Console.WriteLine("PROBE config: " + config.Redacted());
            return 2;
        }

        try { Directory.CreateDirectory(runDir); }
        catch (Exception) { Console.WriteLine("PROBE error: run directory could not be created"); return 2; }

        int cap = config.MaxOutputTokens > 0 ? config.MaxOutputTokens : 3000;
        ProbeResult first = Probe(configPath, runDir, cap, "configured-cap");
        Print(first);

        // Only one controlled follow-up, and only if the first response exposes no reasoning at
        // all. This is not a retry of a failed answer: it tests the provider's budget accounting.
        if (first.Ok && !first.ReasoningObserved && !first.ContentEmpty)
        {
            ProbeResult second = Probe(configPath, runDir, 32, "tiny-cap");
            Print(second);
        }

        Console.WriteLine("PROBE done");
        return 0;
    }

    private static ProbeResult Probe(string configPath, string runDir, int maxOutputTokens, string tag)
    {
        LlmConfig config = LlmConfig.LoadFromFile(configPath);
        config.MaxOutputTokens = maxOutputTokens;
        // Exactly one provider request per runner, regardless of the response shape.
        config.MaxRequestsPerTurn = 1;
        config.MaxActionsPerTurn = 1;
        config.MaxInvalidPerTurn = 1;
        config.MaxTotalRequests = 1;
        config.MaxTurnSeconds = Math.Max(config.MaxTurnSeconds, 120);
        config.RequestTimeoutMs = Math.Max(config.RequestTimeoutMs, 60000);

        string dir = Path.Combine(runDir,
            "empty-reply-probe-" + tag + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(dir);
        var recorder = new LlmRunRecorder(dir);
        var transport = new OpenAiChatTransport(config);
        var runner = new LlmTurnRunner(transport, config, recorder);
        var host = new TestHost(FixtureObservation());

        runner.Begin(host, 1, "seat-a", 20261109);
        DateTime deadline = DateTime.UtcNow.AddSeconds(180);
        while ((runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
               && DateTime.UtcNow < deadline)
        {
            runner.Tick();
            if (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
                Thread.Sleep(20);
        }
        if (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
            runner.Cancel("probe timed out");

        var result = new ProbeResult
        {
            Tag = tag,
            MaxOutputTokens = maxOutputTokens,
            Requests = runner.TotalRequests,
            State = runner.State.ToString(),
            EndReason = runner.EndReason.ToString(),
            FailureReason = runner.FailureReason,
            RunRecordPath = recorder.FilePath,
            ExchangePath = recorder.ExchangeFilePathFor(runner.RunId),
        };

        // The run record is the primary telemetry surface; the sidecar keeps the full bodies.
        JsonObject record = LastRecord(result.RunRecordPath);
        if (record == null)
        {
            result.Ok = false;
            result.ResultError = "no run record";
            return result;
        }
        result.Ok = true;
        result.FinishReason = S(record, "finish_reason");
        result.InputTokens = I(record, "input_tokens");
        result.OutputTokens = I(record, "output_tokens");
        result.ReasoningTokens = I(record, "reasoning_tokens");
        result.TotalTokens = I(record, "total_tokens");
        result.ReasoningContentChars = I(record, "reasoning_content_chars");
        result.ContentChars = I(record, "content_chars") ?? 0;
        result.ContentTrimmedEmpty = B(record, "content_trimmed_empty");
        result.ResultError = S(record, "result_error");
        return result;
    }

    private static void Print(ProbeResult result)
    {
        Console.WriteLine("PROBE tag=" + result.Tag
            + " max_output_tokens=" + result.MaxOutputTokens
            + " requests=" + result.Requests
            + " state=" + result.State
            + " end_reason=" + result.EndReason);
        Console.WriteLine("PROBE   finish_reason=" + (result.FinishReason ?? "unknown")
            + " input_tokens=" + Value(result.InputTokens)
            + " output_tokens=" + Value(result.OutputTokens)
            + " reasoning_tokens=" + Value(result.ReasoningTokens)
            + " total_tokens=" + Value(result.TotalTokens)
            + " reasoning_content_chars=" + Value(result.ReasoningContentChars)
            + " content_chars=" + result.ContentChars
            + " content_trimmed_empty=" + (result.ContentTrimmedEmpty.HasValue
                ? result.ContentTrimmedEmpty.Value.ToString().ToLowerInvariant() : "unknown"));
        if (!string.IsNullOrEmpty(result.FailureReason))
            Console.WriteLine("PROBE   stop_reason=" + Safe(result.FailureReason));
        if (!string.IsNullOrEmpty(result.ResultError))
            Console.WriteLine("PROBE   result_error=" + Safe(result.ResultError));
        Console.WriteLine("PROBE   run_record=" + result.RunRecordPath);
        Console.WriteLine("PROBE   exchanges=" + result.ExchangePath);
    }

    private static string Value(int? value)
        => value.HasValue ? value.Value.ToString() : "unknown";

    private static string Safe(string text)
        // The runner already redacted its own key; this removes generic token shapes again.
        => LlmResponseDiagnostics.RedactForCapture(text, null);

    private static JsonObject LastRecord(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string[] lines = File.ReadAllLines(path);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                if (StrictJson.TryParse(lines[i], out JsonNode node, out _) && node is JsonObject obj)
                    return obj;
            }
        }
        catch (Exception) { }
        return null;
    }
}
