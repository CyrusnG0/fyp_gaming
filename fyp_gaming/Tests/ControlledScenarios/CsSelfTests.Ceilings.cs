using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;
namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        // CS-13..CS-21: whole-run ceilings and the transport-independent recording wrapper. They
        // drive the production runner/budget/wrapper with local fakes and assert plumbing only.
        private static void CeilingsAndTransportRegression(TestLog log)
        { Guard(log, RunRequestCap); Guard(log, RemainingAllowance); Guard(log, ScenarioCapName);
          Guard(log, InFlightRunWallStop); Guard(log, RecordingTransportCompleted); Guard(log, CancellationAndSeal);
          Guard(log, RefusalDoesNotDispatch); Guard(log, CredentialPrivacy); Guard(log, NeverReturnedEvidence); }
        private static void Guard(TestLog log, Action<TestLog> test)
        { try { test(log); } catch (Exception e)
          { log.Failed++; Console.WriteLine("  FAIL (exception) " + e.GetType().Name + ": " + e.Message); } }
        // CS-13: the shared request budget bounds ACTUAL inner calls, not just the runner's sum.
        private static void RunRequestCap(TestLog log)
        {
            string outRoot = NewRunDir("cs13-cap");
            try
            {
                CsManifest manifest = Manifest(AllScenarioIds(), 4300);
                manifest.RunCeilings.MaxRunRequests = 2;
                CsRunResult run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot());
                int requests = 0;
                foreach (CsScenarioResult s in run.Scenarios) requests += s.Requests;
                List<JsonElement> lines = ReadJsonLines(Path.Combine(run.RunDirectory, "dispatches.ndjson"));
                int dispatched = 0;
                foreach (JsonElement line in lines)
                    if (CsEvidenceRecorder.Text(line, "dispatch_state") != "refused_before_dispatch") dispatched++;
                log.Check(requests <= 2 && dispatched <= 2,
                    "[CS-13] the run request cap bounds actual dispatches in results and written evidence",
                    "requests=" + requests + " lines=" + lines.Count + " dispatched_lines=" + dispatched);
                log.Check(dispatched == requests, "[CS-13] each written non-refusal line maps to one actual inner call",
                    "requests=" + requests + " dispatched_lines=" + dispatched);
                log.Check(run.Status == CsStatus.Stopped && !run.Scenarios.Exists(s => s.Status == CsStatus.Completed),
                    "[CS-13] the capped run is Stopped and no scenario is reported Completed", run.Status + " " + run.Detail);
                log.Check(run.Scenarios.Count == manifest.Scenarios.Count
                        && run.Scenarios.Exists(s => s.Status == CsStatus.Skipped),
                    "[CS-13] scenarios the run never reached are retained as Skipped",
                    string.Join(",", run.Scenarios.ConvertAll(s => s.Status).ToArray()));
            }
            finally { Cleanup(outRoot); }
        }
        // CS-14: less whole-run allowance than the next scenario's own limit, and a 1-left scenario.
        private static void RemainingAllowance(TestLog log)
        {
            string outRoot = NewRunDir("cs14-manifest");
            try
            {
                CsManifest manifest = Manifest(AllScenarioIds(), 4310);
                manifest.RunCeilings.MaxRunRequests = 3;
                CsRunResult run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot());
                int requests = 0;
                foreach (CsScenarioResult s in run.Scenarios) requests += s.Requests;
                CsScenarioResult first = run.Scenarios.Count > 0 ? run.Scenarios[0] : null;
                log.Check(requests <= 3,
                    "[CS-14] a run with less allowance than the first scenario's limit never exceeds it",
                    "requests=" + requests);
                log.Check(first != null && first.Status != CsStatus.Completed && first.Limit == "max_run_requests",
                    "[CS-14] the first scenario is stopped by max_run_requests, not max_scenario_requests",
                    first == null ? "missing" : first.Status + " limit=" + first.Limit);
            }
            finally { Cleanup(outRoot); }
            string shared = NewTempDir("cs14-shared");
            try
            {
                var budget = new CsRunBudget(3, 120000);
                CsScenarioSpec firstSpec = Builtin("cs-visible-contact-v1", 4311);
                firstSpec.Scripts["seat-0"] = new List<CsScriptStep>
                    { CsScenarioFactory.Step(CsScenarioFactory.EndTurn("bounded first scenario")) };
                SetCeilings(firstSpec);
                CsScenarioResult first = CsRunner.RunScenario(firstSpec, shared, FakeModel, TestGit(), "cs14-a", budget);
                int left = budget.RemainingRequests;
                CsScenarioSpec secondSpec = Builtin("cs-research-purchase-v1", 4312);
                SetCeilings(secondSpec);
                CsScenarioResult second = CsRunner.RunScenario(secondSpec, shared, FakeModel, TestGit(), "cs14-b", budget);
                log.Check(first.Requests == 2 && left == 1,
                    "[CS-14] the first scenario consumes exactly two of three shared request slots",
                    "first=" + first.Requests + " left=" + left);
                log.Check(second.Requests <= 1 && budget.Dispatched <= 3 && second.Status != CsStatus.Completed,
                    "[CS-14] a later scenario given one remaining request never exceeds it",
                    "second=" + second.Requests + " total=" + budget.Dispatched
                    + " status=" + second.Status + " limit=" + second.Limit);
            }
            finally { Cleanup(shared); }
        }
        // CS-15: a scenario cap below the run's remaining allowance is named exactly.
        private static void ScenarioCapName(TestLog log)
        {
            string outRoot = NewRunDir("cs15");
            try
            {
                CsManifest manifest = Manifest(new[] { "cs-visible-contact-v1" }, 4320);
                manifest.RunCeilings.MaxRunRequests = 20;
                manifest.RunCeilings.MaxScenarioRequests = 1;
                CsRunResult run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot());
                CsScenarioResult s = run.Scenarios.Count > 0 ? run.Scenarios[0] : null;
                log.Check(s != null && s.Status == CsStatus.Stopped && s.Limit == "max_scenario_requests",
                    "[CS-15] a scenario cap below the run remaining is named max_scenario_requests",
                    s == null ? "missing" : s.Status + " limit=" + s.Limit);
                log.Check(s != null && s.Requests == 1, "[CS-15] the scenario cap of one bounds actual dispatches",
                    s == null ? "missing" : "requests=" + s.Requests);
            }
            finally { Cleanup(outRoot); }
        }
        // CS-16: a token-ignoring inner task is abandoned at the run deadline; the run is Stopped.
        private static void InFlightRunWallStop(TestLog log)
        {
            var budget = new CsRunBudget(100, 300);
            CsWorldSnapshot before = Snap("seat-0", "before");
            var transport = Wrap(new IgnoringDelayInner(5000), budget, before: before);
            var clock = Stopwatch.StartNew();
            LlmResponse response = Send(transport, "SYS-WALL", "USER-WALL");
            clock.Stop();
            CsDispatchCapture capture = FirstCapture(transport);
            log.Check(response != null && !response.Ok && response.Error == LlmErrorKind.Timeout
                    && clock.ElapsedMilliseconds < 1000,
                "[CS-16] an in-flight run wall deadline returns a bounded Timeout within one second",
                "ok=" + (response == null ? "null" : response.Ok.ToString()) + " error="
                + (response == null ? "null" : response.Error.ToString()) + " elapsed_ms=" + clock.ElapsedMilliseconds);
            log.Check(capture != null && capture.State == "deadline_abandoned"
                    && capture.System == "SYS-WALL" && capture.User == "USER-WALL"
                    && capture.Before != null && capture.Before.ObservationJson == before.ObservationJson
                    && capture.ReplyText == null && capture.Ok == null && capture.InputTokens == null
                    && capture.OutputTokens == null && capture.FinishReason == null,
                "[CS-16] the abandoned dispatch keeps prompt and before-state with no fabricated reply",
                capture == null ? "no capture" : "state=" + capture.State + " reply=" + (capture.ReplyText ?? "null"));
            string root = NewTempDir("cs16-run");
            try
            {
                CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4325);
                spec.Scripts["seat-0"] = new List<CsScriptStep>
                    { CsScenarioFactory.Step(CsScenarioFactory.EndTurn("wall fixture"), fault: "request_timeout", delayMs: 5000) };
                spec.MaxRequests = 40; spec.MaxWallSeconds = 30; spec.RequestTimeoutMs = 60000;
                CsScenarioResult result = CsRunner.RunScenario(spec, root, FakeModel, TestGit(),
                    "cs16-run", new CsRunBudget(100, 300));
                log.Check(result.Status == CsStatus.Stopped && result.Limit == "max_run_wall_seconds",
                    "[CS-16] the scenario reports Stopped with limit max_run_wall_seconds",
                    result.Status + " limit=" + result.Limit + " detail=" + result.Detail);
                List<JsonElement> dispatches = ReadJsonLines(Path.Combine(result.Directory, "dispatches.ndjson"));
                JsonElement first = dispatches.Count > 0 ? dispatches[0] : default;
                string state = CsEvidenceRecorder.Text(first, "dispatch_state");
                bool terminal = state == "deadline_abandoned" || state == "no_response_before_seal"
                    || state == "cancelled" || state == "timed_out";
                log.Check(terminal && IsNull(Obj(first, "reply"), "raw_text"),
                    "[CS-16] the written dispatch is terminal with no fabricated reply",
                    "state=" + state + " reply=" + NodeText(Obj(first, "reply")));
            }
            finally { Cleanup(root); }
        }
        // CS-17: ANY ILlmTransport is recorded at the dispatch boundary; unknown usage stays null.
        private static void RecordingTransportCompleted(TestLog log)
        {
            CsWorldSnapshot before = Snap("seat-0", "before");
            CsWorldSnapshot after = Snap("seat-0", "after");
            var transport = Wrap(new FixedInner(Reply("{\"type\":\"end_turn\"}", 11, 22, "stop")),
                CsRunBudget.Unbounded(), before: before, after: after);
            LlmResponse response = Send(transport, "SYSTEM-BYTES", "USER-BYTES");
            CsDispatchCapture c = FirstCapture(transport);
            log.Check(c != null && c.Seat == "seat-0" && c.System == "SYSTEM-BYTES" && c.User == "USER-BYTES"
                    && c.Before != null && c.Before.ObservationJson == before.ObservationJson
                    && c.State == "completed" && c.Ok == true && c.ReplyText == "{\"type\":\"end_turn\"}"
                    && c.InputTokens == 11 && c.OutputTokens == 22 && c.FinishReason == "stop"
                    && c.HttpStatus == 200 && c.After != null && c.After.ObservationJson == after.ObservationJson,
                "[CS-17] a plain transport is recorded with seat, prompt, before/after state, reply and usage",
                c == null ? "no capture" : "state=" + c.State + " seat=" + c.Seat + " in=" + c.InputTokens
                + " out=" + c.OutputTokens + " finish=" + c.FinishReason);
            log.Check(response != null && response.Ok, "[CS-17] the wrapper returns the inner response unchanged",
                response == null ? "null" : response.Error.ToString());
            var unknown = Wrap(new FixedInner(Reply("{\"type\":\"end_turn\"}", null, null, "stop")),
                CsRunBudget.Unbounded());
            Send(unknown, "s", "u");
            CsDispatchCapture uc = FirstCapture(unknown);
            log.Check(uc != null && uc.InputTokens == null && uc.OutputTokens == null
                    && !uc.InputTokens.HasValue && !uc.OutputTokens.HasValue,
                "[CS-17] unreported usage stays null, never zero",
                uc == null ? "no capture" : "in=" + uc.InputTokens + " out=" + uc.OutputTokens);
        }
        // CS-18: cancellation is terminal; late replies never mutate sealed evidence.
        private static void CancellationAndSeal(TestLog log)
        {
            var cancelled = Wrap(new ObservingCancelInner(), CsRunBudget.Unbounded());
            var cts = new CancellationTokenSource();
            Task<LlmResponse> pending = cancelled.SendAsync(new LlmRequest("SYS", "USER", 16, 60000), cts.Token);
            cts.Cancel();
            LlmResponse response = pending.GetAwaiter().GetResult();
            CsDispatchCapture c = FirstCapture(cancelled);
            log.Check(response != null && !response.Ok && response.Text == null
                    && c != null && c.State == "cancelled" && c.ReplyText == null,
                "[CS-18] an observed cancellation leaves a terminal capture with no fabricated reply",
                c == null ? "no capture" : "state=" + c.State + " reply=" + (c.ReplyText ?? "null"));
            var lateInner = new LateAfterCancelInner();
            var late = Wrap(lateInner, CsRunBudget.Unbounded());
            var lateCts = new CancellationTokenSource();
            Task<LlmResponse> latePending = late.SendAsync(new LlmRequest("SYS", "USER", 16, 60000), lateCts.Token);
            lateCts.Cancel();
            latePending.GetAwaiter().GetResult();
            late.Seal();
            string sealedCopy = SerializeCaptures(late);
            lateInner.Release.TrySetResult(true);
            int waited = 0;
            while (late.LateResponsesDiscarded == 0 && waited < 2000) { Thread.Sleep(10); waited += 10; }
            string afterLate = SerializeCaptures(late);
            CsDispatchCapture lc = FirstCapture(late);
            log.Check(lc != null && (lc.State == "cancelled" || lc.State == "timed_out")
                    && lc.ReplyText == null && late.LateResponsesDiscarded >= 1,
                "[CS-18] a late reply after cancellation is discarded and counted, not linked",
                "state=" + (lc == null ? "none" : lc.State) + " late=" + late.LateResponsesDiscarded);
            log.Check(sealedCopy == afterLate, "[CS-18] sealed captures are byte-identical after the late reply arrives",
                "before_chars=" + sealedCopy.Length + " after_chars=" + afterLate.Length);
            var never = Wrap(new NeverInner(), CsRunBudget.Unbounded());
            Task<LlmResponse> inFlight = never.SendAsync(new LlmRequest("SYS", "USER", 16, 60000), CancellationToken.None);
            never.Seal();
            CsDispatchCapture sc = FirstCapture(never);
            log.Check(!inFlight.IsCompleted && sc != null && sc.State == "no_response_before_seal"
                    && sc.ReplyText == null && sc.Ok == null && sc.InputTokens == null && sc.FinishReason == null,
                "[CS-18] a request in flight at Seal becomes no_response_before_seal with no reply",
                sc == null ? "no capture" : "state=" + sc.State + " completed=" + inFlight.IsCompleted);
        }
        // CS-19: a budget refusal happens before the inner call and is not counted as dispatched.
        private static void RefusalDoesNotDispatch(TestLog log)
        {
            var inner = new FixedInner(Reply("{\"type\":\"end_turn\"}", 1, 1, "stop"));
            var budget = new CsRunBudget(1, 600000);
            var transport = Wrap(inner, budget);
            LlmResponse first = Send(transport, "s1", "u1");
            LlmResponse second = Send(transport, "s2", "u2");
            List<CsDispatchCapture> captures = transport.Captures();
            CsDispatchCapture refused = captures.Count > 1 ? captures[1] : null;
            log.Check(first != null && first.Ok && inner.Calls == 1 && transport.DispatchedCount == 1
                    && budget.RemainingRequests == 0,
                "[CS-19] the inner transport is called only for the one reserved run request",
                "calls=" + inner.Calls + " dispatched=" + transport.DispatchedCount);
            log.Check(refused != null && refused.State == "refused_before_dispatch"
                    && refused.RefusalReason == "max_run_requests" && refused.ReplyText == null
                    && second != null && !second.Ok && second.Error == LlmErrorKind.BudgetExhausted,
                "[CS-19] an exhausted run budget refuses before dispatch with a response-free capture",
                refused == null ? "no capture" : "state=" + refused.State + " reason=" + refused.RefusalReason
                + " reply=" + (refused.ReplyText ?? "null"));
            var scenarioInner = new FixedInner(Reply("{\"type\":\"end_turn\"}", 1, 1, "stop"));
            var scenarioTransport = Wrap(scenarioInner, CsRunBudget.Unbounded(), maxScenarioRequests: 1);
            Send(scenarioTransport, "s1", "u1");
            Send(scenarioTransport, "s2", "u2");
            List<CsDispatchCapture> scenarioCaptures = scenarioTransport.Captures();
            CsDispatchCapture sr = scenarioCaptures.Count > 1 ? scenarioCaptures[1] : null;
            log.Check(sr != null && sr.RefusalReason == "max_scenario_requests"
                    && scenarioInner.Calls == 1 && scenarioTransport.DispatchedCount == 1,
                "[CS-19] a scenario cap refusal is not counted as dispatched",
                sr == null ? "no capture" : "reason=" + sr.RefusalReason + " calls=" + scenarioInner.Calls
                + " dispatched=" + scenarioTransport.DispatchedCount);
        }
        // CS-20: a real-shaped API key never leaks into capture serialization or written evidence.
        private static void CredentialPrivacy(TestLog log)
        {
            const string key = "sk-CS13CANARYCANARYCANARY12345";
            string previous = Environment.GetEnvironmentVariable("LUANSHI_LLM_API_KEY");
            Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", key);
            string configDir = NewTempDir("cs20-config");
            string runRoot = null;
            try
            {
                string configPath = Path.Combine(configDir, "llm.json");
                File.WriteAllText(configPath,
                    "{\"provider\":\"fake\",\"model\":\"fake-model\",\"api_key\":\"" + key + "\"}");
                LlmConfig secret = LlmConfig.LoadFromFile(configPath);
                log.Check(secret.HasApiKey && !secret.Redacted().Contains(key),
                    "[CS-20] the config holds the key but its redacted form never prints it",
                    "has_key=" + secret.HasApiKey);
                var transport = Wrap(new FixedInner(Reply("{\"type\":\"end_turn\"}", 1, 1, "stop")),
                    CsRunBudget.Unbounded(), before: Snap("seat-0", "before"));
                Send(transport, "system " + key, "user " + key);
                var context = new CsDispatchContext
                {
                    RunId = "cs20", ScenarioId = "cs20", Commit = "test", BaseCommit = "test",
                    StudySeat = "seat-0", ProviderKind = "fake", ProviderModel = FakeModel,
                    UsageSource = "fake_deterministic", Seed = 1, ScenarioMaxRequests = 10,
                    ScenarioWallSeconds = 30, RequestTimeoutMs = 5000, PromptLimit = 40000,
                    SettingsHash = "settings", ConfigRedacted = secret.Redacted(),
                    Captures = transport.Captures(), RunFileName = "run.jsonl", ExchangeFileName = "exchanges.ndjson",
                };
                string record = JsonSerializer.Serialize(CsEvidence.Build(context, 0));
                log.Check(!record.Contains(key), "[CS-20] a serialized dispatch record never contains the API key",
                    "chars=" + record.Length);
                CsScenarioResult result = RunSpec(Builtin("cs-visible-contact-v1", 4330), "cs20-run", out runRoot);
                bool leaked = false; int scanned = 0;
                foreach (string path in Directory.GetFiles(result.Directory))
                { scanned++; if (File.ReadAllText(path).Contains(key)) leaked = true; }
                log.Check(scanned > 0 && !leaked, "[CS-20] no written evidence of one scenario run contains the API key",
                    "files=" + scanned + " leaked=" + leaked + " status=" + result.Status);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", previous);
                Cleanup(configDir); Cleanup(runRoot);
            }
        }
        // CS-21: a request that never returns is still written with a terminal state and null reply.
        private static void NeverReturnedEvidence(TestLog log)
        {
            CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4340);
            spec.Scripts["seat-0"] = new List<CsScriptStep>
                { CsScenarioFactory.Step(CsScenarioFactory.EndTurn("never returns"), fault: "request_timeout", delayMs: 600000) };
            spec.MaxRequests = 10; spec.MaxWallSeconds = 1; spec.RequestTimeoutMs = 600000;
            CsScenarioResult result = RunSpec(spec, "cs21", out string root);
            try
            {
                List<JsonElement> dispatches = ReadJsonLines(Path.Combine(result.Directory, "dispatches.ndjson"));
                JsonElement first = dispatches.Count > 0 ? dispatches[0] : default;
                string state = CsEvidenceRecorder.Text(first, "dispatch_state");
                JsonElement reply = Obj(first, "reply");
                log.Check(dispatches.Count >= 1 && !string.IsNullOrEmpty(state) && state != "in_flight",
                    "[CS-21] a never-returned request still has a written dispatch line with its state",
                    "lines=" + dispatches.Count + " state=" + state);
                log.Check(IsNull(reply, "raw_text") && IsNull(reply, "finish_reason")
                        && IsNull(reply, "input_tokens") && IsNull(reply, "output_tokens") && IsNull(reply, "total_tokens"),
                    "[CS-21] the line's reply fields are null, never fabricated or zero", NodeText(reply));
                log.Check(Names(first, "unknown_fields", "reply.response_text")
                        && Names(first, "unknown_fields", "reply.input_tokens"),
                    "[CS-21] the absent reply fields are named in unknown_fields", NodeText(Obj(first, "unknown_fields")));
                log.Check(result.Status == CsStatus.Stopped,
                    "[CS-21] a scenario whose request never returned is not Completed",
                    result.Status + " limit=" + result.Limit + " detail=" + result.Detail);
            }
            finally { Cleanup(root); }
        }
        private sealed class FixedInner : ILlmTransport
        {
            private readonly LlmResponse _response; public int Calls;
            public FixedInner(LlmResponse response) { _response = response; }
            public string Provider => "fake"; public string Model => "fake-model";
            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            { Interlocked.Increment(ref Calls); return Task.FromResult(_response); }
        }
        private sealed class IgnoringDelayInner : ILlmTransport
        {
            private readonly int _delayMs;
            public IgnoringDelayInner(int delayMs) { _delayMs = delayMs; }
            public string Provider => "fake"; public string Model => "fake-model";
            public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            {
                await Task.Delay(_delayMs).ConfigureAwait(false); // deliberately ignores ct
                return Reply("late reply that must be discarded", 1, 1, "stop");
            }
        }
        private sealed class ObservingCancelInner : ILlmTransport
        {
            public string Provider => "fake"; public string Model => "fake-model";
            public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); return Reply("never", 1, 1, "stop"); }
        }
        private sealed class LateAfterCancelInner : ILlmTransport
        {
            public readonly TaskCompletionSource<bool> Release =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public string Provider => "fake"; public string Model => "fake-model";
            public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            { await Release.Task.ConfigureAwait(false); return Reply("late reply", 2, 3, "stop"); }
        }
        private sealed class NeverInner : ILlmTransport
        {
            public readonly TaskCompletionSource<LlmResponse> Pending =
                new TaskCompletionSource<LlmResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Calls;
            public string Provider => "fake"; public string Model => "fake-model";
            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            { Interlocked.Increment(ref Calls); return Pending.Task; }
        }
        private static LlmResponse Reply(string text, int? inputTokens, int? outputTokens, string finishReason)
        { var response = LlmResponse.Success(text, 5, inputTokens, outputTokens, "fixed-request", finishReason);
          response.HttpStatus = 200; return response; }
        private static CsRecordingTransport Wrap(ILlmTransport inner, CsRunBudget budget, string seat = "seat-0",
            int maxScenarioRequests = 10, long scenarioDeadlineMs = long.MaxValue,
            CsWorldSnapshot before = null, CsWorldSnapshot after = null)
            => new CsRecordingTransport(inner, () => seat, budget, maxScenarioRequests, scenarioDeadlineMs,
                _ => before ?? Snap(seat, "before"), _ => after ?? Snap(seat, "after"));
        private static LlmResponse Send(CsRecordingTransport transport, string system, string user)
            => transport.SendAsync(new LlmRequest(system, user, 16, 60000), CancellationToken.None).GetAwaiter().GetResult();
        private static CsDispatchCapture FirstCapture(CsRecordingTransport transport)
        { List<CsDispatchCapture> captures = transport.Captures(); return captures.Count > 0 ? captures[0] : null; }
        private static readonly JsonSerializerOptions CaptureJson = new JsonSerializerOptions { IncludeFields = true };
        private static string SerializeCaptures(CsRecordingTransport transport)
            => JsonSerializer.Serialize(transport.Captures(), CaptureJson);
        private static CsWorldSnapshot Snap(string seat, string marker)
            => new CsWorldSnapshot { SeatId = seat, ObservationJson = "{\"marker\":\"" + marker + "\"}",
                ObservationSha256 = CsWorld.Sha256(marker), WorldDigest = marker };
        private static string[] AllScenarioIds()
        { string[] ids = new string[CsScenarios.Keys.Count];
          for (int i = 0; i < ids.Length; i++) ids[i] = CsScenarios.Keys[i]; return ids; }
        private static void SetCeilings(CsScenarioSpec spec)
        { spec.MaxRequests = 40; spec.MaxWallSeconds = 30; spec.RequestTimeoutMs = 5000; }
    }
}
