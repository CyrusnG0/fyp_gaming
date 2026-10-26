using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LuanShi.Engine;

internal static partial class Harness
{
    private const string LlmFakeKey = "sk-test-DO-NOT-LEAK-B2-7f31c9";

    private static void RunLlmRunner()
    {
        RunLlmConfig();
        RunLlmHappyTurn();
        RunLlmRepairAndLimits();
        RunLlmHostBinding();
        RunLlmProviderErrors();
        RunLlmCancelAndLateResponse();
        RunLlmHistoryBound();
        RunLlmRecorder();
        RunLlmRunnerHttp();
        RunLlmPromptSafety();
        RunLlmProviderLenient();
        RunLlmLiveSmokeOffline();
        RunLlmHardeningB4();
        RunLlmHardeningB5();
        RunSpectatorSession();
    }

    private static void RunLlmConfig()
    {
        Section("LLM config: file, env override, secret hygiene [DEMO-B2]");

        string dir = LlmTempDir("config");
        try
        {
            using (new LlmEnvScope(null, null, null))
            {
                string missing = Path.Combine(dir, "missing.json");
                var absent = LlmConfig.LoadFromFile(missing);
                Check(!absent.HasApiKey && absent.Provider == "deepseek"
                      && absent.Model == "deepseek-flash"
                      && absent.BaseUrl == "https://api.deepseek.com",
                    "missing config file => defaults, no key");
                Check(absent.MaxRequestsPerTurn == 6 && absent.MaxActionsPerTurn == 5
                      && absent.MaxInvalidPerTurn == 2 && absent.MaxTotalRequests == 60
                      && absent.MaxOutputTokens == 400,
                    "budget defaults are the demo-slice values");

                string path = Path.Combine(dir, "llm.local.json");
                File.WriteAllText(path,
                    "{\"provider\":\"deepseek\",\"base_url\":\"https://unit.test\","
                    + "\"model\":\"unit-model\",\"api_key\":\"" + LlmFakeKey + "\","
                    + "\"api_key_env\":\"LUANSHI_LLM_API_KEY\",\"json_mode\":false,"
                    + "\"max_requests_per_turn\":3,\"max_actions_per_turn\":2,"
                    + "\"max_invalid_per_turn\":1,\"max_turn_seconds\":9,"
                    + "\"request_timeout_ms\":1234,\"max_output_tokens\":99,"
                    + "\"max_total_requests\":7,\"usd_per_mtok_in\":1,"
                    + "\"usd_per_mtok_out\":\"2.5\",\"max_usd\":10}");
                var file = LlmConfig.LoadFromFile(path);
                Check(file.HasApiKey && file.Provider == "deepseek" && file.BaseUrl == "https://unit.test"
                      && file.Model == "unit-model" && !file.JsonMode,
                    "config file parses snake_case provider/url/model/api_key");
                Check(file.MaxRequestsPerTurn == 3 && file.MaxActionsPerTurn == 2
                      && file.MaxInvalidPerTurn == 1 && file.MaxTurnSeconds == 9
                      && file.RequestTimeoutMs == 1234 && file.MaxOutputTokens == 99
                      && file.MaxTotalRequests == 7,
                    "config file parses every bound");
                Check(file.UsdPerMTokIn == 1 && file.UsdPerMTokOut == 2.5 && file.MaxUsd == 10,
                    "integer and decimal-string prices parse without a second JSON parser");
                Check(!file.Redacted().Contains(LlmFakeKey)
                      && !file.ToString().Contains(LlmFakeKey)
                      && (file.LoadWarning == null || !file.LoadWarning.Contains(LlmFakeKey)),
                    "Redacted/ToString/warning never expose the API key");
                Check(file.Redacted().Contains("api_key=<set>"), "Redacted reports key presence only");

                File.WriteAllText(path, "{\"api_key\":\"" + LlmFakeKey);
                var broken = LlmConfig.LoadFromFile(path);
                Check(!broken.HasApiKey && !broken.Redacted().Contains(LlmFakeKey),
                    "malformed config degrades safely and never echoes its contents");

                Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", "env-key-sentinel");
                Environment.SetEnvironmentVariable("LUANSHI_LLM_MODEL", "env-model");
                Environment.SetEnvironmentVariable("LUANSHI_LLM_BASE_URL", "https://env.test");
                File.WriteAllText(path,
                    "{\"api_key\":\"" + LlmFakeKey + "\",\"model\":\"file-model\","
                    + "\"base_url\":\"https://file.test\"}");
                var overridden = LlmConfig.LoadFromFile(path);
                Check(overridden.HasApiKey && overridden.Model == "env-model"
                      && overridden.BaseUrl == "https://env.test"
                      && !overridden.Redacted().Contains("env-key-sentinel"),
                    "LUANSHI_LLM_* environment overrides file values and stays redacted");
            }
        }
        finally { LlmDelete(dir); }

        try
        {
            var cfg = new LlmConfig();
            var thrown = false;
            try { new LlmTurnRunner(null, cfg, null); }
            catch (ArgumentNullException ex)
            {
                thrown = true;
                Check(!ex.ToString().Contains(LlmFakeKey), "runner construction exception never contains the key");
            }
            Check(thrown, "null transport is rejected at construction");
        }
        catch (Exception ex)
        {
            Check(false, "driver/config exception path failed: " + ex.GetType().Name);
        }
    }

    private static void RunLlmHappyTurn()
    {
        Section("LLM runner: happy turn changes real engine state [DEMO-B2]");

        var engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var config = new LlmConfig { MaxRequestsPerTurn = 8, MaxActionsPerTurn = 5, MaxTurnSeconds = 30 };
        var runner = new LlmTurnRunner(fake, config, null);
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\",\"summary\":\"grow food\"}",
            inputTokens: 100, outputTokens: 20);
        fake.EnqueueSuccess("{\"type\":\"train\",\"target_id\":\"city-a\",\"summary\":\"drill\"}",
            inputTokens: 120, outputTokens: 25);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 80, outputTokens: 10);

        runner.Begin(host, 42, "seat-a", 99);
        Check(runner.State == RunnerState.Requesting && runner.TotalRequests == 1
              && fake.CallCount == 1,
            "Begin dispatches exactly one request without blocking");

        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && host.EndTurnCalls == 1
              && runner.FailureReason == null,
            "reclaim -> train -> end_turn finishes and calls EndTurn exactly once");
        Check(runner.RequestsThisTurn == 3 && runner.ActionsThisTurn == 2
              && runner.InvalidThisTurn == 0 && runner.TotalRequests == 3,
            "request/action counters reflect the bounded loop");
        Check(engine.State.Cities[0].ReclaimPct == BalanceConfig.ReclaimPctPerUse
              && engine.State.Cities[0].Training == 1,
            "the model's accepted orders really mutated engine state");
        Check(host.SubmitCalls == 2 && host.ObserveCalls == 3
              && host.UnboundActors.All(a => a == null),
            "all observations come from host.Observe and Submit receives unbound commands");
        Check(fake.Requests.Count == 3
              && fake.Requests[1].User.Contains("\"reclaim_pct\":15")
              && fake.Requests[1].User.Contains("reclaim city-a -> ok"),
            "the next prompt carries a fresh observation and bounded action feedback");
        Check(runner.LastSummary == "drill" && runner.LastResult == "end_turn",
            "last decision summary and result are exposed");
        Check(runner.TotalInputTokens == 300 && runner.TotalOutputTokens == 55
              && runner.CostUsd == null,
            "token totals are tracked and cost is unknown without prices");
        Check(host.Commands.Count == 2 && host.Commands[0].ActorSeatId == "seat-a"
              && host.Commands[0].ControllerType == ControllerType.Llm,
            "host-bound actor/controller appear only after Submit");
    }

    private static void RunLlmHostBinding()
    {
        Section("LLM runner: parser rejects spoofed host fields before Submit [DEMO-B2]");

        var engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig { MaxInvalidPerTurn = 3 }, null);
        fake.EnqueueSuccess("{\"type\":\"end_turn\",\"actor_seat_id\":\"seat-b\"}");
        fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 7, "seat-a", 3);
        LlmDrain(runner);
        Check(host.SubmitCalls == 0 && runner.InvalidThisTurn == 2
              && runner.TotalRequests == 3 && runner.State == RunnerState.Finished,
            "spoofed actor and unsupported action are rejected before host.Submit");
    }

    private static void RunLlmHistoryBound()
    {
        Section("LLM runner: recent history is bounded [DEMO-B2]");

        var engine = LlmEngine();
        var host = new LlmPermissiveHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 20, MaxActionsPerTurn = 20, MaxInvalidPerTurn = 5 },
            null);
        for (int i = 0; i < 14; i++)
            fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 2);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.RecentHistory.Count == 12
              && runner.ActionsThisTurn == 14 && host.EndTurnCalls == 1,
            "history keeps only the last 12 action/result lines");
    }

    private static void RunLlmRecorder()
    {
        Section("LLM recorder: JSONL, nullable usage, secret absence [DEMO-B2]");

        string dir = LlmTempDir("records");
        try
        {
            using (new LlmEnvScope(LlmFakeKey, null, null))
            {
                var config = LlmConfig.LoadFromFile(null);
                config.MaxRequestsPerTurn = 5;
                config.MaxActionsPerTurn = 3;
                config.UsdPerMTokIn = 1;
                config.UsdPerMTokOut = 2;
                var engine = LlmEngine();
                var host = new LlmHarnessHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                var recorder = new LlmRunRecorder(dir);
                var runner = new LlmTurnRunner(fake, config, recorder);
                fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}",
                    0, 1000000, 500000, "req-1");
                fake.EnqueueSuccess("{\"type\":\"end_turn\",\"reason\":\"" + LlmFakeKey + "\"}",
                    0, null, null, "req-2");
                runner.Begin(host, 77, "seat-a", 11);
                LlmDrain(runner);

                string[] files = Directory.GetFiles(dir, "*.jsonl");
                Check(files.Length == 1, "one JSONL file per run id");
                string[] lines = File.ReadAllLines(files[0]);
                Check(lines.Length == 2, "one recorded line per provider request");
                bool allValid = true;
                var objects = new List<JsonObject>();
                foreach (string line in lines)
                {
                    allValid &= StrictJson.TryParse(line, out JsonNode node, out _);
                    if (node is JsonObject obj) objects.Add(obj);
                }
                Check(allValid && objects.Count == 2, "every recorded line is valid StrictJson");
                JsonObject first = objects[0];
                JsonObject second = objects[1];
                Check(LlmStr(first, "provider") == "fake" && LlmStr(first, "model") == "fake-model"
                      && LlmStr(first, "prompt_version") == PromptBuilder.PromptVersion
                      && LlmStr(first, "schema_hash") == PromptBuilder.SchemaHash()
                      && LlmInt(first, "game_seed") == 11 && LlmInt(first, "seed") == 11
                      && LlmInt(first, "turn_token") == 77
                      && LlmStr(first, "run_id") == runner.RunId,
                    "every line carries run/provider/model/prompt/schema/seed/token");
                Check(LlmStr(first, "observation_json") != null
                      && LlmStr(first, "observation_json").Contains("\"seat_id\":\"seat-a\"")
                      && LlmStr(first, "observation_hash") != null
                      && LlmNode(first, "parse_ok") is JsonBool parseOk && parseOk.Value
                      && LlmInt(first, "requests_this_turn") >= 1,
                    "line carries exact observation JSON, hash and parse state");
                Check(LlmInt(first, "input_tokens") == 1000000
                      && LlmInt(first, "output_tokens") == 500000
                      && LlmDouble(first, "cost_usd") == 2,
                    "known usage and computed cost are recorded");
                Check(LlmNode(second, "input_tokens") is JsonNull
                      && LlmNode(second, "output_tokens") is JsonNull
                      && LlmNode(second, "cost_usd") is JsonNull,
                    "unknown usage/cost is null, never zero");
                Check(runner.TotalInputTokens == null && runner.TotalOutputTokens == null
                      && runner.CostUsd == null,
                    "mixed/unknown usage keeps totals null rather than undercounting");
                string allText = string.Join("\n", lines);
                Check(!allText.Contains(LlmFakeKey) && !runner.RunId.Contains(LlmFakeKey),
                    "run records and run id never contain the API key");
                Check(LlmStr(second, "state_after") == RunnerState.Finished.ToString(),
                    "state_after records the terminal state");
            }
        }
        finally { LlmDelete(dir); }
    }

    // ---- shared fixtures ----------------------------------------------------

    private static LlmConfig LlmKeyedConfig()
    {
        using (new LlmEnvScope(LlmFakeKey, null, null))
            return LlmConfig.LoadFromFile(null);
    }

    private static GameEngine LlmEngine()
    {
        var engine = Build(17, 3, morale: 100, food: 5000);
        engine.State.FindFaction("seat-a").Controller = ControllerType.Llm;
        return engine;
    }

    private static void LlmDrain(LlmTurnRunner runner)
    {
        for (int i = 0; i < 100 && (runner.State == RunnerState.Requesting
                                 || runner.State == RunnerState.Executing); i++)
            runner.Tick();
    }

    private static string LlmTempDir(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "luanshi-b2-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void LlmDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception) { }
    }

    private sealed class LlmEnvScope : IDisposable
    {
        private readonly string _key, _model, _base;

        public LlmEnvScope(string key, string model, string baseUrl)
        {
            _key = Environment.GetEnvironmentVariable("LUANSHI_LLM_API_KEY");
            _model = Environment.GetEnvironmentVariable("LUANSHI_LLM_MODEL");
            _base = Environment.GetEnvironmentVariable("LUANSHI_LLM_BASE_URL");
            Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", key);
            Environment.SetEnvironmentVariable("LUANSHI_LLM_MODEL", model);
            Environment.SetEnvironmentVariable("LUANSHI_LLM_BASE_URL", baseUrl);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", _key);
            Environment.SetEnvironmentVariable("LUANSHI_LLM_MODEL", _model);
            Environment.SetEnvironmentVariable("LUANSHI_LLM_BASE_URL", _base);
        }
    }

    /// <summary>
    /// Gate-aware harness host. It owns a real engine seat turn: BeginSeatTurn at construction,
    /// SubmitAs with that captured token, and EndSeatTurn when the runner finishes. Commands reach
    /// SubmitAs without an actor/controller so the engine's host binding is what is under test.
    /// </summary>
    private sealed class LlmHarnessHost : ITurnHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;

        public int ObserveCalls;
        public int SubmitCalls;
        public int EndTurnCalls;
        public readonly int Token;
        public readonly List<ActionCommand> Commands = new List<ActionCommand>();
        public readonly List<string> UnboundActors = new List<string>();
        public readonly List<ValidationResult> Results = new List<ValidationResult>();

        public LlmHarnessHost(GameEngine engine, string seat)
        {
            _engine = engine;
            _seat = seat;
            Token = engine.BeginSeatTurn(seat);
        }

        public Observation Observe()
        {
            ObserveCalls++;
            return _engine.Observe(_seat);
        }

        public ValidationResult Submit(ActionCommand cmd)
        {
            SubmitCalls++;
            UnboundActors.Add(cmd == null ? null : cmd.ActorSeatId);
            if (cmd == null) return ValidationResult.Fail("null command");
            Commands.Add(cmd);
            ValidationResult result = _engine.SubmitAs(_seat, Token, cmd);
            Results.Add(result);
            return result;
        }

        public void EndTurn()
        {
            EndTurnCalls++;
            _engine.EndSeatTurn(Token);
        }
    }

    private sealed class LlmPermissiveHost : ITurnHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;

        public int EndTurnCalls;

        public LlmPermissiveHost(GameEngine engine, string seat)
        {
            _engine = engine;
            _seat = seat;
        }

        public Observation Observe() => _engine.Observe(_seat);
        public ValidationResult Submit(ActionCommand cmd) => ValidationResult.Success;
        public void EndTurn() { EndTurnCalls++; }
    }

    private static JsonNode LlmNode(JsonObject obj, string key)
        => obj.TryGet(key, out JsonNode value) ? value : null;

    private static string LlmStr(JsonObject obj, string key)
        => LlmNode(obj, key) is JsonString text ? text.Value : null;

    private static int LlmInt(JsonObject obj, string key)
        => LlmNode(obj, key) is JsonInt number ? (int)number.Value : int.MinValue;

    private static double LlmDouble(JsonObject obj, string key)
    {
        JsonNode node = LlmNode(obj, key);
        if (node is JsonInt number) return number.Value;
        if (node is JsonString text && double.TryParse(text.Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value))
            return value;
        return double.NaN;
    }
}
