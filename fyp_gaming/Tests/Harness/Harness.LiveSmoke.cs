using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    // ---- live smoke (run only with --live <config>) ---------------------------

    /// <summary>
    /// One provider turn against the configured LLM. This is never part of the normal suite: Program
    /// dispatches to it only when the command line carries <c>--live &lt;configPath&gt;</c>. The
    /// secret itself is never printed; the configuration line comes from <see cref="LlmConfig.Redacted"/>.
    /// </summary>
    private static int RunLiveSmoke(string configPath)
        => RunLiveSmoke(configPath, Console.Out);

    private static int RunLiveSmoke(string configPath, TextWriter output)
    {
        if (output == null) output = Console.Out;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            output.WriteLine("LIVE-SMOKE error: missing config path");
            return 2;
        }

        LlmConfig config;
        try { config = LlmConfig.LoadFromFile(configPath); }
        catch (Exception)
        {
            output.WriteLine("LIVE-SMOKE error: config could not be loaded");
            return 2;
        }

        if (!config.HasApiKey)
        {
            output.WriteLine("LIVE-SMOKE error: API key is not configured");
            output.WriteLine("LIVE-SMOKE config: " + config.Redacted());
            return 2;
        }

        // Spend guard for a single smoke request sequence, even if the local file is generous.
        config.MaxTotalRequests = LiveCap(config.MaxTotalRequests, 6);
        config.MaxRequestsPerTurn = LiveCap(config.MaxRequestsPerTurn, 4);
        config.MaxActionsPerTurn = LiveCap(config.MaxActionsPerTurn, 3);

        var engine = LiveSmokeEngine();
        // Same host-bound turn path as the demo: BeginSeatTurn here, then SubmitAs under that token.
        var host = new LlmHarnessHost(engine, "seat-a");
        string recorderDir = Path.Combine(Path.GetTempPath(),
            "luanshi-live-smoke-" + Guid.NewGuid().ToString("N"));
        var recorder = new LlmRunRecorder(recorderDir);
        var transport = new OpenAiChatTransport(config);
        var runner = new LlmTurnRunner(transport, config, recorder);

        output.WriteLine("LIVE-SMOKE config: " + config.Redacted());
        output.WriteLine("LIVE-SMOKE provider=" + transport.Provider);
        output.WriteLine("LIVE-SMOKE model=" + transport.Model);

        string before = LiveSmokeState(engine);
        runner.Begin(host, 0, "seat-a", 20261027);

        DateTime deadline = DateTime.UtcNow.AddSeconds(90);
        while ((runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
               && DateTime.UtcNow < deadline)
        {
            runner.Tick();
            if (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
                Thread.Sleep(50);
        }

        if (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
        {
            runner.Cancel("live smoke timed out after 90s");
            output.WriteLine("LIVE-SMOKE error: turn did not finish within 90s");
        }

        string after = LiveSmokeState(engine);
        string inputTokens = runner.TotalInputTokens.HasValue
            ? runner.TotalInputTokens.Value.ToString(CultureInfo.InvariantCulture) : "unknown";
        string outputTokens = runner.TotalOutputTokens.HasValue
            ? runner.TotalOutputTokens.Value.ToString(CultureInfo.InvariantCulture) : "unknown";

        output.WriteLine("LIVE-SMOKE state=" + runner.State);
        output.WriteLine("LIVE-SMOKE status=" + SafeLine(runner.Status));
        output.WriteLine("LIVE-SMOKE requests=" + runner.RequestsThisTurn
                         + " total_requests=" + runner.TotalRequests);
        output.WriteLine("LIVE-SMOKE actions=" + runner.ActionsThisTurn);
        output.WriteLine("LIVE-SMOKE last_command_json="
                         + (string.IsNullOrEmpty(runner.LastCommandJson)
                             ? "(none)" : SafeLine(runner.LastCommandJson)));
        output.WriteLine("LIVE-SMOKE last_result="
                         + (string.IsNullOrEmpty(runner.LastResult)
                             ? "(none)" : SafeLine(runner.LastResult)));
        output.WriteLine("LIVE-SMOKE tokens=input=" + inputTokens + " output=" + outputTokens
                         + (inputTokens == "unknown" && outputTokens == "unknown" ? " (unknown)" : ""));
        output.WriteLine("LIVE-SMOKE recorder=" + recorder.FilePath);
        output.WriteLine("LIVE-SMOKE engine_before=" + before);
        output.WriteLine("LIVE-SMOKE engine_after=" + after);
        bool changed = before != after;
        output.WriteLine("LIVE-SMOKE state-changing action: " + (changed ? "yes" : "no"));

        bool clean = runner.State == RunnerState.Finished || runner.State == RunnerState.Failed;
        return clean ? 0 : 1;
    }

    private static int LiveCap(int configured, int cap)
    {
        if (configured <= 0) return cap;
        return Math.Min(configured, cap);
    }

    private static GameEngine LiveSmokeEngine()
    {
        // Reuse the same deterministic fixture the runner harness uses: seat-a owns city-a with
        // 1,000 gold / 5,000 food and 5 command points, so reclaim/train/levy_tax are legal.
        var engine = Build(17, 3, morale: 100, food: 5000);
        engine.State.FindFaction("seat-a").Controller = ControllerType.Llm;
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Human,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 2, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 1000, Gold = 1000, Garrison = 0,
        });
        return engine;
    }

    private static string LiveSmokeState(GameEngine engine)
    {
        var faction = engine.State.FindFaction("seat-a");
        var city = engine.State.FindCity("city-a");
        return string.Format(CultureInfo.InvariantCulture,
            "season={0};command_points={1};gold={2};food={3};garrison={4};reclaim_pct={5};training={6};morale={7}",
            engine.State.Season,
            faction == null ? -1 : faction.CommandPoints,
            city == null ? -1 : city.Gold,
            city == null ? -1 : city.Food,
            city == null ? -1 : city.Garrison,
            city == null ? -1 : city.ReclaimPct,
            city == null ? -1 : city.Training,
            city == null ? -1 : city.Morale);
    }

    private static string SafeLine(string value)
    {
        if (value == null) return "(none)";
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    // ---- [DEMO-B3] prompt size safety ----------------------------------------

    private static void RunLlmPromptSafety()
    {
        Section("LLM prompt size safety: bounded event history [DEMO-B3]");

        var engine = LiveSmokeEngine();
        var observation = engine.Observe("seat-a");
        int expectedCount = observation.Events.Count + 130;
        for (int i = 0; i < 130; i++)
            observation.Events.Add(LlmSyntheticEvent(i, 5, "marker-" + i.ToString("000", CultureInfo.InvariantCulture)));
        EventView first = observation.Events[0];
        EventView last = observation.Events[observation.Events.Count - 1];

        var options = ActionCatalogue.Build(observation);
        var history = new List<string> { "reclaim city-a -> ok" };
        string boundedJson = PromptBuilder.BoundedObservationJson(observation);
        string prompt = PromptBuilder.UserPrompt(observation, options, history, 4, 3);

        Check(observation.Events.Count == expectedCount
              && ReferenceEquals(first, observation.Events[0])
              && ReferenceEquals(last, observation.Events[expectedCount - 1]),
            "the original observation keeps all 130 added events (not mutated)");
        Check(observation.ToJson().Contains("marker-000"),
            "the original observation still serializes its oldest event");

        bool bounded = StrictJson.TryParse(boundedJson, out JsonNode root, out _)
                       && root is JsonObject obj
                       && LlmNode(obj, "events") is JsonArray events
                       && events.Count == PromptBuilder.MaxPromptObservationEvents;
        Check(bounded, "bounded observation JSON carries exactly the last 30 events");
        Check(boundedJson.Contains("marker-100") && boundedJson.Contains("marker-129")
              && !boundedJson.Contains("marker-099") && !boundedJson.Contains("marker-000"),
            "bounded event window is the newest 30, not the oldest");
        Check(prompt.Contains("marker-100") && prompt.Contains("marker-129")
              && !prompt.Contains("marker-099") && !prompt.Contains("marker-000"),
            "user prompt carries only the newest 30 events");
        Check(prompt == PromptBuilder.UserPrompt(observation, options, history, 4, 3),
            "bounded user prompt is deterministic for the same observation");

        // The runner must record exactly the JSON it sent (the bounded view), not the full history.
        string dir = LlmTempDir("prompt-record");
        try
        {
            var fake = new FakeLlmTransport();
            var recorder = new LlmRunRecorder(dir);
            var host = new LlmFixedObservationHost(observation);
            var runner = new LlmTurnRunner(fake, new LlmConfig(), recorder);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(host, 1, "seat-a", 3);
            LlmDrain(runner);

            string[] lines = File.ReadAllLines(recorder.FilePath);
            JsonObject record = lines.Length == 1
                && StrictJson.TryParse(lines[0], out JsonNode recordNode, out _)
                && recordNode is JsonObject recordObject ? recordObject : null;
            string recordedObservation = record == null ? null : LlmStr(record, "observation_json");
            Check(recordedObservation == boundedJson
                  && fake.Requests.Count == 1
                  && fake.Requests[0].User.Contains(boundedJson),
                "run record observation_json is exactly the bounded JSON sent in the prompt");
        }
        finally { LlmDelete(dir); }

        // Five synthetic seasons with more than 100 events; the bounded prompt must stay under the cap.
        var fiveSeason = LiveSmokeEngine();
        var longObservation = fiveSeason.Observe("seat-a");
        for (int i = 0; i < 200; i++)
        {
            int season = (i / 40) + 1; // 5 seasons x 40 events
            longObservation.Events.Add(LlmSyntheticEvent(i, season,
                "season-" + season.ToString(CultureInfo.InvariantCulture) + "-marker-" + i, 200));
        }
        longObservation.Season = 5;
        string system = PromptBuilder.SystemPrompt();
        string longPrompt = PromptBuilder.UserPrompt(longObservation,
            ActionCatalogue.Build(longObservation), new List<string>(), 4, 3);
        Check(LlmTurnRunner.MaxPromptChars == 40000,
            "runner prompt cap is 40,000 characters");
        Check(system.Length + longPrompt.Length <= LlmTurnRunner.MaxPromptChars,
            "a 5-season synthetic game (200 events) stays under the prompt cap");
        Check(PromptBuilder.BoundedObservationJson(longObservation).Length
              < longObservation.ToJson().Length,
            "the 5-season prompt observation is smaller than the full history");
    }

    private static EventView LlmSyntheticEvent(int seq, int season, string marker, int padding = 0)
    {
        var payload = new Dictionary<string, string>
        {
            { "marker", marker },
            { "note", padding > 0 ? new string('n', padding) : "synthetic note" },
        };
        var record = new EventRecord
        {
            Seq = seq,
            GameId = "g-b3",
            Season = season,
            Phase = "player_orders",
            ActorSeatId = "seat-a",
            ControllerType = ControllerType.Llm,
            EventType = "synthetic",
            Visibility = "public",
            Payload = payload,
            SourceActionId = null,
        };
        return new EventView(record, new Dictionary<string, string>(payload), null, null);
    }

    private sealed class LlmFixedObservationHost : ITurnHost
    {
        private readonly Observation _observation;

        public LlmFixedObservationHost(Observation observation) { _observation = observation; }
        public Observation Observe() => _observation;
        public ValidationResult Submit(ActionCommand cmd) => ValidationResult.Success;
        public void EndTurn() { }
    }

    // ---- [DEMO-B3] provider response leniency --------------------------------

    private static void RunLlmProviderLenient()
    {
        Section("LLM provider envelope: lenient numbers, nested usage, null logprobs [DEMO-B3]");

        string content = "{\"type\":\"end_turn\"}";
        string body = "{\"id\":\"chatcmpl-lenient\",\"object\":\"chat.completion\","
            + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":"
            + MiniJson.Str(content) + "},\"logprobs\":null,\"finish_reason\":\"stop\"}],"
            + "\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7,\"total_tokens\":18,"
            + "\"completion_tokens_details\":{\"reasoning_tokens\":3,\"cost\":0.00042,\"rate\":1e-5},"
            + "\"prompt_tokens_details\":{\"cached_tokens\":0}},"
            + "\"system_fingerprint\":\"fp_b3\"}";

        bool lenient = StrictJson.TryParseLenientNumbers(body, out JsonNode root, out _);
        var obj = root as JsonObject;
        Check(lenient && obj != null && !StrictJson.TryParse(body, out _, out _),
            "lenient provider parse accepts floats/exponents while the model parser stays strict");

        bool shape = false;
        if (obj != null)
        {
            var choices = LlmNode(obj, "choices") as JsonArray;
            var choice = choices != null && choices.Count > 0 ? choices.Items[0] as JsonObject : null;
            var message = choice == null ? null : LlmNode(choice, "message") as JsonObject;
            var text = message == null ? null : LlmNode(message, "content") as JsonString;
            var usage = LlmNode(obj, "usage") as JsonObject;
            var details = usage == null ? null : LlmNode(usage, "completion_tokens_details") as JsonObject;
            shape = text != null && text.Value == content
                    && choice != null && LlmNode(choice, "logprobs") is JsonNull
                    && usage != null && LlmInt(usage, "prompt_tokens") == 11
                    && LlmInt(usage, "completion_tokens") == 7
                    && details != null && LlmNode(details, "cost") is JsonNull;
        }
        Check(shape, "lenient parse yields message content, integer usage and inert float details");

        string longBody = "{\"padding\":\"" + new string('a', 17000) + "\"}";
        Check(StrictJson.TryParseLenientNumbers(longBody, out _, out _)
              && !StrictJson.TryParse(longBody, out _, out _),
            "lenient provider bodies may exceed the 16,384-character strict cap");
        string oversize = "{\"padding\":\"" + new string('a', StrictJson.MaxLenientInputLength) + "\"}";
        Check(!StrictJson.TryParseLenientNumbers(oversize, out _, out _),
            "lenient provider bodies still have a 262,144-character cap");

        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        LlmResponse response = LlmHttpOnce(200, body, 0, 5000, out LlmHttpStub stub);
        Check(response != null && response.Ok && response.Text == content
              && response.InputTokens == 11 && response.OutputTokens == 7
              && response.ProviderRequestId == "chatcmpl-lenient",
            "HTTP provider body with float usage details and null logprobs parses end to end");
        stub.Dispose();
    }

    // ---- [DEMO-B3] offline proof of the live-smoke path -----------------------

    private static void RunLlmLiveSmokeOffline()
    {
        Section("LLM live smoke: offline loopback proof [DEMO-B3]");
        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        string dir = LlmTempDir("live-smoke");
        LlmLiveHttpStub stub = null;
        try
        {
            stub = new LlmLiveHttpStub(
                LlmLiveBody("live-1", "{\"type\":\"reclaim\",\"target_id\":\"city-a\",\"summary\":\"grow food\"}", 10, 5),
                LlmLiveBody("live-2", "{\"type\":\"end_turn\"}", 12, 3));
            string configPath = Path.Combine(dir, "llm.local.json");
            File.WriteAllText(configPath,
                "{\"provider\":\"stub-provider\",\"base_url\":\"" + stub.BaseUrl + "\","
                + "\"model\":\"stub-model\",\"api_key\":\"" + LlmFakeKey + "\","
                + "\"json_mode\":true,\"max_total_requests\":99,"
                + "\"max_requests_per_turn\":99,\"max_actions_per_turn\":99}");

            int code;
            string text;
            using (new LlmEnvScope(null, null, null))
            {
                var writer = new StringWriter();
                code = RunLiveSmoke(configPath, writer);
                text = writer.ToString();
                stub.WaitServed(5000);
            }

            Check(code == 0 && stub.RequestCount == 2
                  && text.Contains("LIVE-SMOKE provider=stub-provider")
                  && text.Contains("LIVE-SMOKE model=stub-model")
                  && text.Contains("LIVE-SMOKE state=Finished")
                  && text.Contains("LIVE-SMOKE requests=2")
                  && text.Contains("LIVE-SMOKE actions=1")
                  && text.Contains("LIVE-SMOKE state-changing action: yes"),
                "offline live smoke runs reclaim -> end_turn and exits 0");
            Check(text.Contains("LIVE-SMOKE config: ") && text.Contains("api_key=<set>")
                  && !text.Contains(LlmFakeKey) && !text.Contains("Bearer "),
                "live smoke prints only redacted config (no key, no headers)");
            Check(text.Contains("LIVE-SMOKE tokens=input=22 output=8"),
                "live smoke prints token totals");

            string recorderPath = LlmLiveField(text, "LIVE-SMOKE recorder=");
            Check(!string.IsNullOrEmpty(recorderPath) && File.Exists(recorderPath)
                  && File.ReadAllLines(recorderPath).Length == 2,
                "live smoke writes one recorder line per provider request");
        }
        finally
        {
            if (stub != null) stub.Dispose();
            LlmDelete(dir);
        }
    }

    private static string LlmLiveBody(string id, string content, int inputTokens, int outputTokens)
    {
        return "{\"id\":" + MiniJson.Str(id) + ",\"object\":\"chat.completion\",\"choices\":[{"
            + "\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + MiniJson.Str(content) + "},"
            + "\"logprobs\":null,\"finish_reason\":\"stop\"}],"
            + "\"usage\":{\"prompt_tokens\":" + inputTokens.ToString(CultureInfo.InvariantCulture)
            + ",\"completion_tokens\":" + outputTokens.ToString(CultureInfo.InvariantCulture)
            + ",\"total_tokens\":" + (inputTokens + outputTokens).ToString(CultureInfo.InvariantCulture)
            + ",\"completion_tokens_details\":{\"reasoning_tokens\":2,\"cost\":0.00042}},"
            + "\"system_fingerprint\":\"fp_b3\"}";
    }

    private static string LlmLiveField(string text, string prefix)
    {
        if (text == null || prefix == null) return null;
        int start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return null;
        start += prefix.Length;
        int end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        return text.Substring(start, end - start).Trim();
    }

    private sealed class LlmLiveHttpStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string[] _bodies;
        private readonly Task _serve;
        private int _served;

        public string BaseUrl { get; }
        public int RequestCount => Volatile.Read(ref _served);
        public string Error { get; private set; }

        public LlmLiveHttpStub(params string[] bodies)
        {
            _bodies = bodies ?? new string[0];
            int port = LlmFreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _serve = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            try
            {
                for (int i = 0; i < _bodies.Length; i++)
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    using (var reader = new StreamReader(context.Request.InputStream,
                        context.Request.ContentEncoding ?? Encoding.UTF8))
                        await reader.ReadToEndAsync();

                    byte[] bytes = Encoding.UTF8.GetBytes(_bodies[i] ?? "");
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    context.Response.Close();
                    Interlocked.Increment(ref _served);
                }
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name;
            }
        }

        public void WaitServed(int milliseconds)
        {
            try { _serve.Wait(milliseconds); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch (Exception) { }
            try { _listener.Close(); } catch (Exception) { }
            WaitServed(1000);
        }
    }
}
