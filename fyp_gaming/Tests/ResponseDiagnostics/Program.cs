using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LuanShi.Engine;

internal static partial class DiagnosticsTests
{
    // A fake key with a recognisable sk- shape. It must never reach a run record or sidecar.
    private const string FakeKey = "sk-test-DO-NOT-LEAK-RESPONSE-9f3a";
    private const string BearerSecret = "abc.def.ghi";

    private static int passed, failed;

    private static void Check(bool ok, string name)
    {
        if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
        else { failed++; Console.WriteLine("  FAIL  " + name); }
    }

    private static void Section(string title) => Console.WriteLine("\n== " + title + " ==");

    private static int Main(string[] args)
    {
        if (args != null && args.Length >= 2 && args[0] == "--live-probe")
            return RunLiveProbe(args);

        RunNormalTelemetry();
        RunEmptyReasoningBudget();
        RunWhitespaceOnlyBudget();
        RunTruncatedNonEmpty();
        RunMissingUsage();
        RunHttpFailureCapture();
        RunDispatchFailureCapture();
        RunTimeoutAndCancelRecords();
        RunSecretRedaction();

        Console.WriteLine("\n" + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // ---- shared fixtures ----------------------------------------------------

    private sealed class TestHost : ITurnHost
    {
        private readonly Observation _observation;
        public int EndTurnCalls;
        public int SubmitCalls;

        public TestHost(Observation observation) { _observation = observation; }
        public Observation Observe() => _observation;
        public ValidationResult Submit(ActionCommand cmd)
        {
            SubmitCalls++;
            return ValidationResult.Success;
        }
        public void EndTurn() => EndTurnCalls++;
    }

    private sealed class RunResult
    {
        public LlmTurnRunner Runner;
        public LlmRunRecorder Recorder;
        public TestHost Host;
        public string Dir;
    }

    private static Observation FixtureObservation(string cityNameSuffix = null)
    {
        var engine = new GameEngine(7);
        engine.State.Map = Corridor(4);
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Llm,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A" + (cityNameSuffix ?? ""), X = 0, Z = 0,
            OwnerSeatId = "seat-a", Population = 10000, Food = 5000, Gold = 1000,
            Garrison = 0, Morale = 100,
        });
        engine.BeginSeason();
        engine.State.Season = 1;
        engine.State.Phase = "player_orders";
        return engine.Observe("seat-a");
    }

    private static MapData Corridor(int width)
    {
        var map = new MapData
        {
            Width = width, Height = 1, Walkable = new bool[width],
            Cost = new int[width], Neighbors = new int[width][],
        };
        for (int x = 0; x < width; x++)
        {
            map.Walkable[x] = true;
            map.Cost[x] = 1;
            var list = new List<int>();
            if (x > 0) list.Add(x - 1);
            if (x < width - 1) list.Add(x + 1);
            map.Neighbors[x] = list.ToArray();
        }
        return map;
    }

    private static LlmConfig Config(string baseUrl, int maxOutputTokens)
    {
        string dir = TempDir("cfg");
        string path = Path.Combine(dir, "llm.local.json");
        File.WriteAllText(path,
            "{\"provider\":\"stub\",\"base_url\":" + MiniJson.Str(baseUrl) + ","
            + "\"model\":\"stub-model\",\"api_key\":" + MiniJson.Str(FakeKey) + ","
            + "\"json_mode\":true,\"max_output_tokens\":" + maxOutputTokens + "}");
        var config = LlmConfig.LoadFromFile(path);
        config.MaxOutputTokens = maxOutputTokens;
        config.MaxRequestsPerTurn = 5;
        config.MaxActionsPerTurn = 5;
        config.MaxInvalidPerTurn = 5;
        config.MaxTurnSeconds = 30;
        return config;
    }

    private static RunResult RunTurn(ILlmTransport transport, LlmConfig config, string tag,
        string cityNameSuffix = null)
    {
        string dir = TempDir(tag);
        var recorder = new LlmRunRecorder(dir);
        var runner = new LlmTurnRunner(transport, config, recorder);
        var host = new TestHost(FixtureObservation(cityNameSuffix));
        runner.Begin(host, 1, "seat-a", 7);
        for (int i = 0; i < 400
             && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
        {
            runner.Tick();
            if (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
                Thread.Sleep(5);
        }
        return new RunResult { Runner = runner, Recorder = recorder, Host = host, Dir = dir };
    }

    private static LlmResponse SendOnce(OpenAiChatTransport transport, int maxOutputTokens = 3000)
    {
        var request = new LlmRequest("SYSTEM", "USER", maxOutputTokens, 10000);
        return transport.SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Envelope(string content, string finishReason, bool includeUsage,
        int inputTokens = 0, int outputTokens = 0, int reasoningTokens = 0,
        int? totalTokens = null, string reasoningContent = null)
    {
        var sb = new System.Text.StringBuilder(512);
        sb.Append("{\"id\":\"chatcmpl-diag\",\"object\":\"chat.completion\",\"choices\":[{");
        sb.Append("\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":")
          .Append(MiniJson.Str(content));
        if (reasoningContent != null)
            sb.Append(",\"reasoning_content\":").Append(MiniJson.Str(reasoningContent));
        sb.Append("},\"logprobs\":null,\"finish_reason\":").Append(MiniJson.Str(finishReason));
        sb.Append("}]");
        if (includeUsage)
        {
            sb.Append(",\"usage\":{\"prompt_tokens\":").Append(inputTokens)
              .Append(",\"completion_tokens\":").Append(outputTokens)
              .Append(",\"total_tokens\":").Append(totalTokens ?? inputTokens + outputTokens)
              .Append(",\"completion_tokens_details\":{\"reasoning_tokens\":")
              .Append(reasoningTokens).Append("}}");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static List<JsonObject> RunRecords(string dir) => Records(dir, "*.jsonl");
    private static List<JsonObject> ExchangeRecords(string dir) => Records(dir, "*.ndjson");

    private static List<JsonObject> Records(string dir, string pattern)
    {
        var result = new List<JsonObject>();
        foreach (string file in Directory.GetFiles(dir, pattern))
        {
            foreach (string line in File.ReadAllLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (StrictJson.TryParse(line, out JsonNode node, out _) && node is JsonObject obj)
                    result.Add(obj);
            }
        }
        return result;
    }

    private static string ExchangeText(string dir)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string file in Directory.GetFiles(dir, "*.ndjson"))
            sb.Append(File.ReadAllText(file));
        return sb.ToString();
    }

    private static string S(JsonObject obj, string key)
    {
        if (obj != null && obj.TryGet(key, out JsonNode node) && node is JsonString text)
            return text.Value;
        return null;
    }

    private static int? I(JsonObject obj, string key)
    {
        if (obj != null && obj.TryGet(key, out JsonNode node) && node is JsonInt number)
            return (int)number.Value;
        return null;
    }

    private static bool? B(JsonObject obj, string key)
    {
        if (obj != null && obj.TryGet(key, out JsonNode node) && node is JsonBool value)
            return value.Value;
        return null;
    }

    private static bool IsNull(JsonObject obj, string key)
        => obj != null && obj.TryGet(key, out JsonNode node) && node is JsonNull;

    private static JsonObject Child(JsonObject obj, string key)
    {
        if (obj != null && obj.TryGet(key, out JsonNode node) && node is JsonObject child)
            return child;
        return null;
    }

    private static JsonArray Array(JsonObject obj, string key)
    {
        if (obj != null && obj.TryGet(key, out JsonNode node) && node is JsonArray array)
            return array;
        return null;
    }

    private static string TempDir(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "luanshi-diag-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception) { }
    }
}
