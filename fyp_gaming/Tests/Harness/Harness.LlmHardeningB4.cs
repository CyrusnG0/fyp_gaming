using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmHardeningB4()
    {
        RunB4TargetOracle();
        RunB4ObservationSeatCheck();
        RunB4EndTurnTarget();
        RunB4StrictJsonControls();
        RunB4HistoryReset();
        RunB4RecorderSnapshot();
        RunB4RecorderSharing();
        RunB4FaultedTask();
        RunB4StaleToken();
        RunB4LateAfterCancel();
        RunB4CostGuard();
        RunB4TransportHardening();
    }

    // ---- 1: hidden-target oracle -------------------------------------------------

    private sealed class B4Feedback
    {
        public string LastResult;
        public string RecordedError;
    }

    private static void RunB4TargetOracle()
    {
        Section("LLM runner: hidden target feedback is indistinguishable [DEMO-B4]");

        var hiddenEngine = B4HiddenCityEngine();
        Check(!hiddenEngine.Observe("seat-a").ToJson().Contains("city-hidden"),
            "[DEMO-B4] fixture: hidden city is absent from the seat observation");

        B4Feedback hidden = B4FeedbackFor(hiddenEngine, "city-hidden", ActionType.Reclaim);
        B4Feedback missing = B4FeedbackFor(B4HiddenCityEngine(), "city-missing", ActionType.Reclaim);
        Check(hidden.LastResult == "invalid target" && missing.LastResult == "invalid target",
            "[DEMO-B4] hidden existing and nonexistent targets both return 'invalid target'");
        Check(string.Equals(hidden.RecordedError, missing.RecordedError, StringComparison.Ordinal)
              && hidden.RecordedError == "invalid target",
            "[DEMO-B4] recorded model feedback is byte-identical for hidden vs nonexistent ids");
        Check(!hidden.LastResult.Contains("city-hidden")
              && !missing.LastResult.Contains("city-missing"),
            "[DEMO-B4] the model-visible result never echoes the rejected id");

        var poor = LlmEngine();
        poor.State.FindCity("city-a").Gold = 0;
        B4Feedback gold = B4FeedbackFor(poor, "city-a", ActionType.LightenLabor);
        Check(gold.LastResult != null && gold.LastResult.Contains("not enough gold")
              && gold.RecordedError == gold.LastResult && !gold.LastResult.Contains("city-a"),
            "[DEMO-B4] non-target validation errors pass through without revealing ids");
    }

    private static B4Feedback B4FeedbackFor(GameEngine engine, string target, string type)
    {
        string dir = LlmTempDir("b4-feedback");
        try
        {
            var recorder = new LlmRunRecorder(dir);
            var fake = new FakeLlmTransport();
            var host = new LlmHarnessHost(engine, "seat-a");
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxInvalidPerTurn = 1, MaxRequestsPerTurn = 5, MaxActionsPerTurn = 5 },
                recorder);
            fake.EnqueueSuccess("{\"type\":\"" + type + "\",\"target_id\":\"" + target + "\"}");
            runner.Begin(host, 1, "seat-a", 42);
            LlmDrain(runner);

            string recorded = null;
            string[] files = Directory.GetFiles(dir, "*.jsonl");
            if (files.Length == 1)
            {
                string[] lines = File.ReadAllLines(files[0]);
                if (lines.Length == 1
                    && StrictJson.TryParse(lines[0], out JsonNode node, out _)
                    && node is JsonObject obj)
                    recorded = LlmStr(obj, "result_error");
            }
            return new B4Feedback { LastResult = runner.LastResult, RecordedError = recorded };
        }
        finally { LlmDelete(dir); }
    }

    private static GameEngine B4HiddenCityEngine()
    {
        var engine = Build(17, 3, morale: 100, food: 5000, sight: false);
        engine.State.FindFaction("seat-a").Controller = ControllerType.Llm;
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Human,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-hidden", Name = "Hidden Enemy", X = 7, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 500, Gold = 500, Garrison = 0,
        });
        return engine;
    }

    // ---- 2: observation seat check ------------------------------------------------

    private sealed class B4WrongSeatHost : ITurnHost
    {
        private readonly GameEngine _engine;
        private readonly string _submitSeat;
        private readonly string _observeSeat;
        private readonly int _token;

        public int EndTurnCalls;

        public B4WrongSeatHost(GameEngine engine, string submitSeat, string observeSeat)
        {
            _engine = engine;
            _submitSeat = submitSeat;
            _observeSeat = observeSeat;
            _token = engine.BeginSeatTurn(submitSeat);
        }

        public Observation Observe() => _engine.Observe(_observeSeat);
        public ValidationResult Submit(ActionCommand cmd) => _engine.SubmitAs(_submitSeat, _token, cmd);
        public void EndTurn() { EndTurnCalls++; _engine.EndSeatTurn(_token); }
    }

    private static void RunB4ObservationSeatCheck()
    {
        Section("LLM runner: foreign observation is rejected before prompt/record [DEMO-B4]");

        string dir = LlmTempDir("b4-seat-check");
        try
        {
            var engine = B4TwoSeatEngine();
            var host = new B4WrongSeatHost(engine, "seat-a", "seat-b");
            var fake = new FakeLlmTransport();
            var recorder = new LlmRunRecorder(dir);
            var runner = new LlmTurnRunner(fake, new LlmConfig(), recorder);

            runner.Begin(host, 1, "seat-a", 9);
            Check(runner.State == RunnerState.Failed
                  && runner.FailureReason == "observation seat mismatch"
                  && fake.CallCount == 0 && host.EndTurnCalls == 1,
                "[DEMO-B4] seat mismatch fails before any provider request");
            Check(Directory.GetFiles(dir, "*.jsonl").Length == 0,
                "[DEMO-B4] the foreign observation is never written to the run record");
        }
        finally { LlmDelete(dir); }
    }

    // ---- 3: end_turn target -------------------------------------------------------

    private static void RunB4EndTurnTarget()
    {
        Section("LLM parser: end_turn target null/omitted/rejected [DEMO-B4]");

        var omitted = LlmActionParser.Parse("{\"type\":\"end_turn\"}");
        var jsonNull = LlmActionParser.Parse("{\"type\":\"end_turn\",\"target_id\":null}");
        var empty = LlmActionParser.Parse("{\"type\":\"end_turn\",\"target_id\":\"\"}");
        Check(omitted.Ok && omitted.Action.TargetId == null
              && jsonNull.Ok && jsonNull.Action.TargetId == null
              && empty.Ok && empty.Action.TargetId == null,
            "[DEMO-B4] end_turn accepts omitted, null and empty target_id");
        var bad = LlmActionParser.Parse("{\"type\":\"end_turn\",\"target_id\":\"city-a\"}");
        Check(!bad.Ok && bad.Error == "invalid field",
            "[DEMO-B4] end_turn with a non-empty target_id is rejected as 'invalid field'");
        var nonEndNull = LlmActionParser.Parse("{\"type\":\"levy_tax\",\"target_id\":null}");
        Check(!nonEndNull.Ok && nonEndNull.Error == "target is required",
            "[DEMO-B4] a null target is still invalid for a domestic order");
    }

    // ---- 4: decoded JSON control characters --------------------------------------

    private static void RunB4StrictJsonControls()
    {
        Section("StrictJson: raw controls rejected, escaped controls decoded [DEMO-B4/B5]");

        CheckB4ControlAccepted("escaped NUL", "{\"x\":\"a\\u0000b\"}", "a\0b");
        CheckB4ControlAccepted("escaped U+001F", "{\"x\":\"a\\u001Fb\"}",
            "a" + (char)0x1F + "b");
        CheckB4ControlAccepted("escaped newline", "{\"x\":\"a\\nb\"}", "a\nb");
        CheckB4ControlAccepted("escaped tab", "{\"x\":\"a\\tb\"}", "a\tb");
        CheckB4ControlRejected("raw NUL", "{\"x\":\"a" + (char)0 + "b\"}");
        CheckB4ControlRejected("raw U+001F", "{\"x\":\"a" + (char)0x1F + "b\"}");
        bool literalBackslash = StrictJson.TryParse("{\"x\":\"a\\\\nb\"}", out JsonNode node, out _)
                                && node is JsonObject obj
                                && LlmNode(obj, "x") is JsonString text
                                && text.Value == "a\\nb";
        Check(literalBackslash, "[DEMO-B4] an escaped backslash followed by n is still valid text");
    }

    private static void CheckB4ControlAccepted(string label, string json, string expected)
    {
        bool ok = StrictJson.TryParse(json, out JsonNode node, out _)
                  && node is JsonObject obj
                  && LlmNode(obj, "x") is JsonString text
                  && text.Value == expected;
        Check(ok, "[DEMO-B5] " + label + " is valid JSON and decodes to the control character");
    }

    private static void CheckB4ControlRejected(string label, string json)
    {
        bool ok = StrictJson.TryParse(json, out _, out string error);
        Check(!ok && error == "control character",
            "[DEMO-B4] " + label + " is rejected as a control character");
    }

    // ---- 5: history reset ---------------------------------------------------------

    private static void RunB4HistoryReset()
    {
        Section("LLM runner: history reset on seat/seed and ResetSession [DEMO-B4]");

        var engine = B4TwoSeatEngine();
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 6, MaxActionsPerTurn = 5, MaxInvalidPerTurn = 3 },
            null);

        var hostA = new LlmHarnessHost(engine, "seat-a");
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(hostA, 1, "seat-a", 5);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.RecentHistory.Count == 1,
            "[DEMO-B4] fixture: one accepted action is remembered");

        var hostSame = new LlmHarnessHost(engine, "seat-a");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(hostSame, 2, "seat-a", 5);
        Check(runner.RecentHistory.Count == 1,
            "[DEMO-B4] same seat + seed keeps the session history across turns");
        LlmDrain(runner);

        var hostNewSeed = new LlmHarnessHost(engine, "seat-a");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(hostNewSeed, 3, "seat-a", 6);
        Check(runner.RecentHistory.Count == 0,
            "[DEMO-B4] a different seed clears the history");
        LlmDrain(runner);

        var hostSeatB = new LlmHarnessHost(engine, "seat-b");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(hostSeatB, 4, "seat-b", 6);
        Check(runner.RecentHistory.Count == 0,
            "[DEMO-B4] a different seat clears the history");
        LlmDrain(runner);

        int totalBefore = runner.TotalRequests;
        runner.ResetSession();
        Check(runner.State == RunnerState.Idle && runner.RecentHistory.Count == 0
              && runner.RequestsThisTurn == 0 && runner.ActionsThisTurn == 0
              && runner.InvalidThisTurn == 0 && runner.TotalRequests == totalBefore,
            "[DEMO-B4] ResetSession clears history/turn counters but preserves TotalRequests");
    }

    // ---- 6: recorder snapshots and explicit run ids -------------------------------

    private static void RunB4RecorderSnapshot()
    {
        Section("LLM recorder: response-time snapshots [DEMO-B4]");

        string dir = LlmTempDir("b4-snapshot");
        try
        {
            var engine = LlmEngine();
            var host = new LlmHarnessHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            var recorder = new LlmRunRecorder(dir);
            var runner = new LlmTurnRunner(fake, new LlmConfig(), recorder);
            fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}",
                0, 10, 2, "r1");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1, "r2");
            runner.Begin(host, 1, "seat-a", 8);
            LlmDrain(runner);

            string[] files = Directory.GetFiles(dir, "*.jsonl");
            Check(files.Length == 1, "[DEMO-B4] snapshot fixture writes one run file");
            string[] lines = files.Length == 1 ? File.ReadAllLines(files[0]) : new string[0];
            JsonObject first = B4RecordObject(lines, 0);
            JsonObject second = B4RecordObject(lines, 1);
            Check(first != null && LlmInt(first, "requests_this_turn") == 1
                  && LlmStr(first, "state_after") == RunnerState.Executing.ToString(),
                "[DEMO-B4] first line snapshots requests=1 before the next request dispatches");
            Check(second != null && LlmInt(second, "requests_this_turn") == 2
                  && LlmStr(second, "state_after") == RunnerState.Finished.ToString(),
                "[DEMO-B4] terminal line snapshots the second request and Finished state");
        }
        finally { LlmDelete(dir); }
    }

    private static JsonObject B4RecordObject(string[] lines, int index)
    {
        if (index < 0 || index >= lines.Length) return null;
        return StrictJson.TryParse(lines[index], out JsonNode node, out _) ? node as JsonObject : null;
    }

    private static void RunB4RecorderSharing()
    {
        Section("LLM recorder: two runners sharing one recorder never mix files [DEMO-B4]");

        string dir = LlmTempDir("b4-sharing");
        try
        {
            var shared = new LlmRunRecorder(dir);
            var fake1 = new FakeLlmTransport();
            var runner1 = new LlmTurnRunner(fake1, new LlmConfig(), shared);
            fake1.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner1.Begin(new LlmHarnessHost(LlmEngine(), "seat-a"), 1, "seat-a", 11);
            LlmDrain(runner1);

            var fake2 = new FakeLlmTransport();
            var runner2 = new LlmTurnRunner(fake2, new LlmConfig(), shared);
            fake2.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner2.Begin(new LlmHarnessHost(LlmEngine(), "seat-a"), 2, "seat-a", 12);
            LlmDrain(runner2);

            string path1 = Path.Combine(dir, runner1.RunId + ".jsonl");
            string path2 = Path.Combine(dir, runner2.RunId + ".jsonl");
            Check(runner1.RunId != runner2.RunId && File.Exists(path1) && File.Exists(path2),
                "[DEMO-B4] each runner writes its own run-id file");
            string[] lines1 = File.Exists(path1) ? File.ReadAllLines(path1) : new string[0];
            string[] lines2 = File.Exists(path2) ? File.ReadAllLines(path2) : new string[0];
            JsonObject first = B4RecordObject(lines1, 0);
            JsonObject second = B4RecordObject(lines2, 0);
            Check(lines1.Length == 1 && lines2.Length == 1
                  && first != null && LlmStr(first, "run_id") == runner1.RunId
                  && second != null && LlmStr(second, "run_id") == runner2.RunId,
                "[DEMO-B4] sharing a recorder does not route one runner's line into the other's file");
        }
        finally { LlmDelete(dir); }
    }

    // ---- 7: faulted task is observed ---------------------------------------------

    private sealed class B4FaultedTransport : ILlmTransport
    {
        public Task<LlmResponse> Returned { get; private set; }
        public string Provider => "faulted";
        public string Model => "faulted";

        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            Returned = Task.FromException<LlmResponse>(new InvalidOperationException("faulted"));
            return Returned;
        }

        public void Forget() { Returned = null; }
    }

    private static void RunB4FaultedTask()
    {
        Section("LLM runner: an already-faulted in-flight task is observed [DEMO-B4]");

        int unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (sender, args) =>
        {
            args.SetObserved();
            Interlocked.Increment(ref unobserved);
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            var transport = new B4FaultedTransport();
            var runner = new LlmTurnRunner(transport, new LlmConfig(), null);
            runner.Begin(new LlmHarnessHost(LlmEngine(), "seat-a"), 1, "seat-a", 4);
            runner.Cancel("faulted-task test");
            transport.Forget();

            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Check(runner.State == RunnerState.Cancelled && unobserved == 0,
                "[DEMO-B4] a faulted task dropped by Cancel does not surface as unobserved");
        }
        finally { TaskScheduler.UnobservedTaskException -= handler; }
    }

    // ---- 8: gated host, stale token and stubborn late response -------------------

    private static void RunB4StaleToken()
    {
        Section("LLM runner: gated host rejects a stale token mid-turn [DEMO-B4]");

        var engine = B4TwoSeatEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 13);

        // The host moves on to seat-b while the model response is still pending. The old token must
        // never execute the model's reclaim.
        engine.BeginSeatTurn("seat-b");
        LlmDrain(runner);

        Check(host.SubmitCalls == 1 && host.Results.Count == 1
              && !host.Results[0].Ok && host.Results[0].Error == "stale turn token",
            "[DEMO-B4] the host-bound SubmitAs rejects the stale token");
        Check(engine.State.FindCity("city-a").ReclaimPct == 0
              && runner.InvalidThisTurn == 1
              && runner.RecentHistory.Count > 0
              && runner.RecentHistory[runner.RecentHistory.Count - 1].Contains("rejected"),
            "[DEMO-B4] the stale action is not executed and the rejection is model feedback");
    }

    private sealed class B4StubbornTransport : ILlmTransport
    {
        private readonly TaskCompletionSource<LlmResponse> _first =
            new TaskCompletionSource<LlmResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _secondText;
        private int _calls;

        public B4StubbornTransport(string secondText) { _secondText = secondText; }
        public string Provider => "stubborn";
        public string Model => "stubborn-model";
        public int Calls => Volatile.Read(ref _calls);

        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 1) return _first.Task; // deliberately ignores cancellation
            return Task.FromResult(LlmResponse.Success(_secondText, 0, 1, 1, "second"));
        }

        public void CompleteFirst(string text)
            => _first.TrySetResult(LlmResponse.Success(text, 0, 1, 1, "late"));
    }

    private static void RunB4LateAfterCancel()
    {
        Section("LLM runner: a cancellation-ignoring late response stays inert [DEMO-B4]");

        var engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var transport = new B4StubbornTransport("{\"type\":\"end_turn\"}");
        var runner = new LlmTurnRunner(transport,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxTurnSeconds = 30 }, null);

        runner.Begin(host, 1, "seat-a", 14);
        Check(transport.Calls == 1, "[DEMO-B4] fixture: the stubborn request is in flight");
        runner.Cancel("handover");
        transport.CompleteFirst("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        runner.Tick();

        Check(runner.State == RunnerState.Cancelled && host.SubmitCalls == 0
              && engine.State.FindCity("city-a").ReclaimPct == 0,
            "[DEMO-B4] a response that ignores cancellation and completes after Cancel never executes");

        runner.Begin(host, 2, "seat-a", 14);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && host.EndTurnCalls == 1
              && transport.Calls == 2,
            "[DEMO-B4] the next gated turn still finishes normally after the late response");
    }

    // ---- 9: cost guard fail-closed ------------------------------------------------

    private static void RunB4CostGuard()
    {
        Section("LLM runner: MaxUsd fails closed on unknown usage [DEMO-B4]");

        Check(new LlmConfig().MaxTotalRequests == 60,
            "[DEMO-B4] MaxTotalRequests default remains the approved demo cap of 60");

        var noPrices = new LlmConfig { MaxUsd = 2 };
        var fakeNoPrices = new FakeLlmTransport();
        var hostNoPrices = new LlmHarnessHost(LlmEngine(), "seat-a");
        var runnerNoPrices = new LlmTurnRunner(fakeNoPrices, noPrices, null);
        runnerNoPrices.Begin(hostNoPrices, 1, "seat-a", 15);
        Check(runnerNoPrices.State == RunnerState.Failed
              && runnerNoPrices.FailureReason == "usd budget unknown"
              && fakeNoPrices.CallCount == 0 && hostNoPrices.EndTurnCalls == 1,
            "[DEMO-B4] MaxUsd without prices fails before sending any request");

        var priced = new LlmConfig { MaxUsd = 2, UsdPerMTokIn = 1, UsdPerMTokOut = 1 };
        var fakeUnknown = new FakeLlmTransport();
        var engineUnknown = LlmEngine();
        var hostUnknown = new LlmHarnessHost(engineUnknown, "seat-a");
        var runnerUnknown = new LlmTurnRunner(fakeUnknown, priced, null);
        fakeUnknown.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}"); // no usage
        runnerUnknown.Begin(hostUnknown, 1, "seat-a", 16);
        LlmDrain(runnerUnknown);
        Check(runnerUnknown.State == RunnerState.Failed
              && runnerUnknown.FailureReason == "usd budget unknown"
              && fakeUnknown.CallCount == 1
              && hostUnknown.SubmitCalls == 0
              && engineUnknown.State.FindCity("city-a").ReclaimPct == 0,
            "[DEMO-B4] a successful response with uncounted usage stops without executing");

        var fakeKnown = new FakeLlmTransport();
        var hostKnown = new LlmHarnessHost(LlmEngine(), "seat-a");
        var runnerKnown = new LlmTurnRunner(fakeKnown, priced, null);
        fakeKnown.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 10, 10);
        runnerKnown.Begin(hostKnown, 1, "seat-a", 17);
        LlmDrain(runnerKnown);
        Check(runnerKnown.State == RunnerState.Finished && runnerKnown.FailureReason == null
              && runnerKnown.CostUsd.HasValue,
            "[DEMO-B4] priced usage below MaxUsd still finishes normally");
    }

    // ---- 10: transport hardening --------------------------------------------------

    private static void RunB4TransportHardening()
    {
        Section("LLM HTTP transport: https, no redirects, bounded body [DEMO-B4]");

        var insecure = LlmKeyedConfig();
        insecure.BaseUrl = "http://example.com";
        var insecureTransport = new OpenAiChatTransport(insecure);
        LlmResponse insecureResponse = insecureTransport.SendAsync(
            new LlmRequest("s", "u", 8, 1000), CancellationToken.None).GetAwaiter().GetResult();
        Check(!insecureResponse.Ok && insecureResponse.Error == LlmErrorKind.Network
              && insecureResponse.ErrorDetail.Contains("https"),
            "[DEMO-B4] non-loopback plain HTTP is rejected before any request");

        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        const string success =
            "{\"id\":\"chatcmpl-b4\",\"choices\":[{\"message\":{\"role\":\"assistant\","
            + "\"content\":\"{\\\"type\\\":\\\"end_turn\\\"}\"}}],"
            + "\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}";

        LlmHttpStub decoy = null;
        LlmHttpStub redirect = null;
        try
        {
            decoy = new LlmHttpStub(200, success, 0);
            LlmResponse redirected = LlmHttpOnce(302, "", 0, 5000, out redirect, decoy.BaseUrl);
            Check(!redirected.Ok && redirected.Error == LlmErrorKind.Network
                  && decoy.RequestCount == 0,
                "[DEMO-B4] a 3xx is a failure and the redirect target is never contacted");
        }
        finally
        {
            if (redirect != null) redirect.Dispose();
            if (decoy != null) decoy.Dispose();
        }

        LlmHttpStub tooLarge = null;
        try
        {
            string huge = "{\"padding\":\"" + new string('a', OpenAiChatTransport.MaxResponseBytes + 64) + "\"}";
            LlmResponse oversized = LlmHttpOnce(200, huge, 0, 5000, out tooLarge);
            Check(!oversized.Ok && oversized.Error == LlmErrorKind.Malformed
                  && oversized.ErrorDetail == "response too large",
                "[DEMO-B4] a response above 1 MiB is rejected as 'response too large'");
        }
        finally
        {
            if (tooLarge != null) tooLarge.Dispose();
        }
    }

    private static GameEngine B4TwoSeatEngine()
    {
        var engine = LlmEngine();
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Human,
        });
        return engine;
    }
}
