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
    // CS-27..CS-30: the live transport boundary through the full runner. A local fake transport
    // (never a real provider) is injected; the tests assert config plumbing, the shared request
    // ceiling, the in-flight deadline/late-reply seal, and the post-action state join.
    public static partial class CsSelfTests
    {
        private static void LiveTransportConfigAndBoundedRun(TestLog log)
        {
            Guard(log, LiveTransportConfig);
            Guard(log, LiveOutputTokenLimit);
            Guard(log, LiveSharedRequestCap);
            Guard(log, LiveDeadlineAndLateReply);
            Guard(log, ActionAfterJoin);
        }

        /// <summary>CS-25: live manifests need a bounded output-token limit; the wrapper refuses unset requests.</summary>
        private static void LiveOutputTokenLimit(TestLog log)
        {
            foreach (int? tokens in new int?[] { null, 0, -1, CsLimits.MaxOutputTokensCeiling + 1 })
            {
                CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4420);
                manifest.RunCeilings.MaxOutputTokens = tokens;
                List<string> errors = CsManifest.Validate(manifest);
                log.Check(Has(errors, "max_output_tokens"),
                    "[CS-25] live max_output_tokens="
                    + (tokens.HasValue ? tokens.Value.ToString() : "missing")
                    + " is refused by manifest validation", string.Join("; ", errors));
            }
            CsManifest fake = Manifest(new[] { "cs-visible-contact-v1" }, 4421);
            fake.RunCeilings.MaxOutputTokens = null;
            log.Check(CsManifest.Validate(fake).Count == 0,
                "[CS-25] the fake path still accepts an absent output-token ceiling");

            var inner = new CountingTransport(_ => EndTurnReply());
            var budget = new CsRunBudget(10, 600000);
            var transport = new CsRecordingTransport(inner, () => "seat-0", budget, 10, long.MaxValue,
                _ => Snap("seat-0", "before"), _ => Snap("seat-0", "after"),
                requireOutputTokenLimit: true);
            foreach (int tokens in new[] { 0, -1 })
            {
                LlmResponse response = transport.SendAsync(new LlmRequest("s", "u", tokens, 60000),
                    CancellationToken.None).GetAwaiter().GetResult();
                List<CsDispatchCapture> captures = transport.Captures();
                CsDispatchCapture capture = captures[captures.Count - 1];
                log.Check(response != null && !response.Ok
                        && capture.State == "refused_before_dispatch"
                        && capture.RefusalReason == "no_output_token_limit" && capture.ReplyText == null,
                    "[CS-25] a request with MaxOutputTokens=" + tokens
                    + " is refused before dispatch with no reply",
                    "state=" + capture.State + " reason=" + capture.RefusalReason);
            }
            log.Check(inner.Calls == 0 && transport.DispatchedCount == 0 && budget.Dispatched == 0,
                "[CS-25] the output-token refusal never calls the inner transport and is not counted",
                "calls=" + inner.Calls + " dispatched=" + transport.DispatchedCount
                + " budget=" + budget.Dispatched);
            LlmResponse allowed = transport.SendAsync(new LlmRequest("s", "u", 16, 60000),
                CancellationToken.None).GetAwaiter().GetResult();
            log.Check(allowed != null && allowed.Ok && inner.Calls == 1 && transport.DispatchedCount == 1,
                "[CS-25] a positive output-token limit still dispatches normally");
        }

        /// <summary>Transport whose reply task ignores its CancellationToken until the test releases it.</summary>
        private sealed class BlockingLiveTransport : ILlmTransport
        {
            private readonly TaskCompletionSource<LlmResponse> _release;
            private readonly object _gate = new object();
            private readonly List<LlmRequest> _requests = new List<LlmRequest>();
            public int Calls;
            public int Completed;

            public BlockingLiveTransport(TaskCompletionSource<LlmResponse> release) { _release = release; }

            public string Provider => "openai-compatible";
            public string Model => LiveEnvModel;

            public LlmRequest FirstRequest
            { get { lock (_gate) return _requests.Count > 0 ? _requests[0] : null; } }

            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                lock (_gate) _requests.Add(req);
                return AwaitRelease();
            }

            private async Task<LlmResponse> AwaitRelease()
            {
                LlmResponse response = await _release.Task.ConfigureAwait(false);
                Interlocked.Increment(ref Completed);
                return response;
            }
        }

        /// <summary>CS-27: the injected transport sees the effective config and request bounds.</summary>
        private static void LiveTransportConfig(TestLog log)
        {
            string outRoot = NewRunDir("cs27");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv();
                    CountingTransport inner = null;
                    var provider = new TestLiveProvider(
                        ctx => inner = new CountingTransport(_ => EndTurnReply()));
                    CsRunResult run = CsRunner.Run(
                        LiveManifest(new[] { "cs-visible-contact-v1" }, 4500),
                        outRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    LlmConfig config = provider.Config;
                    CsScenarioResult scenario = run.Scenarios[0];
                    JsonElement settings = ReadJsonFile(Path.Combine(scenario.Directory, "settings.json"));
                    JsonElement providerBlock = Obj(settings, "provider");
                    JsonElement ceilings = Obj(settings, "ceilings");
                    log.Check(config != null && inner != null && inner.FirstRequest != null,
                        "[CS-27] the injected provider received a config and the transport a request");
                    if (config == null || inner == null || inner.FirstRequest == null) return;

                    LlmRequest request = inner.FirstRequest;
                    log.Check(config.Provider == "openai-compatible" && config.Model == LiveEnvModel
                            && config.MaxOutputTokens == CsEvidenceRecorder.Int(providerBlock, "max_output_tokens")
                            && config.RequestTimeoutMs == CsEvidenceRecorder.Int(providerBlock, "request_timeout_ms")
                            && config.MaxTotalRequests == CsEvidenceRecorder.Int(ceilings, "max_requests")
                            && config.MaxTurnSeconds == CsEvidenceRecorder.Int(ceilings, "max_wall_seconds"),
                        "[CS-27] the transport config equals the effective settings.json values",
                        "provider=" + config.Provider + " model=" + config.Model
                        + " out=" + config.MaxOutputTokens + " timeout=" + config.RequestTimeoutMs
                        + " total=" + config.MaxTotalRequests + " turn=" + config.MaxTurnSeconds);
                    log.Check(request.MaxOutputTokens > 0
                            && request.MaxOutputTokens == config.MaxOutputTokens
                            && request.TimeoutMs == config.RequestTimeoutMs,
                        "[CS-27] the sent LlmRequest carries the effective output-token limit and timeout",
                        "out=" + request.MaxOutputTokens + " timeout=" + request.TimeoutMs);

                    var context = new CsProviderContext
                    {
                        RunDirectory = null, ScenarioId = "cs-visible-contact-v1", Seed = 1,
                        Config = new LlmConfig(), ActiveSeatId = () => "seat-0",
                        Script = new List<CsScriptStep>(),
                    };
                    ILlmTransport live = new CsLiveScenarioProvider(LiveEnvModel).CreateTransport(context);
                    log.Check(live is OpenAiChatTransport,
                        "[CS-27] CsLiveScenarioProvider.CreateTransport returns OpenAiChatTransport");
                }
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>CS-28: the shared request cap bounds every scenario through the full runner.</summary>
        private static void LiveSharedRequestCap(TestLog log)
        {
            string outRoot = NewRunDir("cs28");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv();
                    int calls = 0;
                    var provider = new TestLiveProvider(ctx => new CountingTransport(_ =>
                    {
                        Interlocked.Increment(ref calls);
                        return EndTurnReply();
                    }));
                    CsManifest manifest = LiveManifest(AllScenarioIds(), 4510,
                        ceilings => ceilings.MaxRunRequests = 3);
                    CsRunResult run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot(), LiveRun(provider));

                    int requests = 0;
                    foreach (CsScenarioResult scenario in run.Scenarios) requests += scenario.Requests;
                    int dispatchedLines = 0;
                    foreach (CsScenarioResult scenario in run.Scenarios)
                    {
                        if (string.IsNullOrEmpty(scenario.Directory)) continue;
                        foreach (JsonElement line in ReadJsonLines(
                            Path.Combine(scenario.Directory, "dispatches.ndjson")))
                            if (CsEvidenceRecorder.Text(line, "dispatch_state") != "refused_before_dispatch")
                                dispatchedLines++;
                    }
                    log.Check(requests <= 3 && dispatchedLines <= 3 && calls <= 3,
                        "[CS-28] max_run_requests=3 bounds wrapper counts, evidence lines and inner calls",
                        "requests=" + requests + " lines=" + dispatchedLines + " calls=" + calls);
                    log.Check(calls == requests,
                        "[CS-28] each counted request maps to exactly one actual inner call",
                        "calls=" + calls + " requests=" + requests);
                    log.Check(run.Status == CsStatus.Stopped
                            && run.Status != CsStatus.Completed
                            && run.Scenarios.Exists(s => s.Status == CsStatus.Skipped),
                        "[CS-28] the capped live run is Stopped (never Completed) and keeps skipped scenarios",
                        run.Status + " " + run.Detail);
                }
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>CS-29: in-flight deadline evidence, then a late reply leaves sealed bytes unchanged.</summary>
        private static void LiveDeadlineAndLateReply(TestLog log)
        {
            string outRoot = NewRunDir("cs29");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv();
                    var release = new TaskCompletionSource<LlmResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var blocking = new BlockingLiveTransport(release);
                    var provider = new TestLiveProvider(ctx => blocking);
                    // A 900 ms runner request timeout lands comfortably inside the 1 s wall
                    // ceilings the manifest validator allows, so the transport's terminal state is
                    // recorded before the scenario loop seals at the wall. The transport still
                    // ignores its CancellationToken.
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4520, ceilings =>
                    {
                        ceilings.MaxRunWallSeconds = 1;
                        ceilings.MaxScenarioWallSeconds = 1;
                        ceilings.RequestTimeoutMs = 900;
                    });
                    var clock = Stopwatch.StartNew();
                    CsRunResult run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    clock.Stop();
                    CsScenarioResult scenario = run.Scenarios[0];
                    log.Check(clock.ElapsedMilliseconds < 8000 && scenario.Status != CsStatus.Completed,
                        "[CS-29] an ignoring in-flight transport returns in bounded time and is not Completed",
                        "elapsed_ms=" + clock.ElapsedMilliseconds + " status=" + scenario.Status);
                    log.Check(scenario.Limit == "max_run_wall_seconds"
                            || scenario.Limit == "max_scenario_wall_seconds",
                        "[CS-29] the bounded stop names the wall ceiling", scenario.Limit ?? "null");

                    List<JsonElement> dispatches = ReadJsonLines(
                        Path.Combine(scenario.Directory, "dispatches.ndjson"));
                    JsonElement first = dispatches.Count > 0 ? dispatches[0] : default;
                    string state = CsEvidenceRecorder.Text(first, "dispatch_state");
                    JsonElement reply = Obj(first, "reply");
                    JsonElement before = Obj(Obj(first, "state"), "before");
                    LlmRequest request = blocking.FirstRequest;
                    log.Check((state == "timed_out" || state == "cancelled"
                                || state == "deadline_abandoned")
                            && request != null
                            && CsEvidenceRecorder.Text(Obj(first, "prompt"), "system") == request.System
                            && CsEvidenceRecorder.Text(Obj(first, "prompt"), "user") == request.User
                            && CsEvidenceRecorder.Text(before, "canonical_world_digest") != null
                            && CsEvidenceRecorder.Text(before, "observation_sha256")
                                == CsWorld.Sha256(CsEvidenceRecorder.Text(before, "observation_json")),
                        "[CS-29] the abandoned dispatch keeps the exact prompt and before-state in a terminal state",
                        "state=" + state);
                    log.Check(IsNull(reply, "raw_text") && IsNull(reply, "finish_reason")
                            && IsNull(reply, "input_tokens") && IsNull(reply, "output_tokens")
                            && Names(first, "unknown_fields", "reply.response_text"),
                        "[CS-29] the abandoned dispatch has no fabricated reply fields and names them unknown",
                        NodeText(reply));

                    Dictionary<string, string> beforeHashes = HashTree(run.RunDirectory);
                    release.TrySetResult(EndTurnReply("late reply that must be discarded"));
                    int waited = 0;
                    while (blocking.Completed == 0 && waited < 3000) { Thread.Sleep(10); waited += 10; }
                    Dictionary<string, string> afterHashes = HashTree(run.RunDirectory);
                    log.Check(blocking.Completed >= 1,
                        "[CS-29] the late reply was actually produced after sealing",
                        "completed=" + blocking.Completed);
                    log.Check(SameTree(beforeHashes, afterHashes),
                        "[CS-29] sealed dispatch/result bytes are unchanged and no new files appear",
                        "files=" + afterHashes.Count);

                    // The full run's recording wrapper is internal, so the equivalent late counter is
                    // checked on a directly constructed wrapper (CS-18 proves the same discard path).
                    var harnessRelease = new TaskCompletionSource<LlmResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var harnessInner = new BlockingLiveTransport(harnessRelease);
                    var harnessBudget = new CsRunBudget(10, 600000);
                    var harness = new CsRecordingTransport(harnessInner, () => "seat-0", harnessBudget,
                        10, harnessBudget.ElapsedMs + 300,
                        _ => Snap("seat-0", "before"), _ => Snap("seat-0", "after"));
                    LlmResponse abandoned = harness.SendAsync(
                        new LlmRequest("SYS-LATE", "USER-LATE", 16, 60000),
                        CancellationToken.None).GetAwaiter().GetResult();
                    harness.Seal();
                    string sealedCopy = SerializeCaptures(harness);
                    harnessRelease.TrySetResult(EndTurnReply("late harness reply"));
                    waited = 0;
                    while (harness.LateResponsesDiscarded == 0 && waited < 3000)
                    { Thread.Sleep(10); waited += 10; }
                    CsDispatchCapture harnessCapture = FirstCapture(harness);
                    log.Check(abandoned != null && !abandoned.Ok
                            && harnessCapture != null && harnessCapture.State == "deadline_abandoned"
                            && harness.LateResponsesDiscarded >= 1
                            && SerializeCaptures(harness) == sealedCopy,
                        "[CS-29] a late reply after the wrapper deadline is counted and sealed evidence is unchanged",
                        "late=" + harness.LateResponsesDiscarded);
                }
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>CS-30: a dispatch's after-state is the post-action joined run line, not the return snapshot.</summary>
        private static void ActionAfterJoin(TestLog log)
        {
            CsScenarioResult result = RunSpec(Builtin("cs-visible-contact-v1", 4242), "cs30", out string root);
            try
            {
                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(result.Directory, "dispatches.ndjson"));
                List<JsonElement> runs = ReadJsonLines(RunFile(result.Directory));
                int accepted = IndexOf(dispatches, d =>
                    CsEvidenceRecorder.Bool(Obj(d, "action"), "accepted") == true
                    && CsEvidenceRecorder.Text(Obj(d, "action"), "command_json") != null);
                log.Check(accepted >= 0 && dispatches.Count == runs.Count && dispatches.Count > 1,
                    "[CS-30] the scripted run has an accepted action and one run line per dispatch",
                    "accepted=" + accepted + " dispatches=" + dispatches.Count + " runs=" + runs.Count);
                if (accepted < 0 || dispatches.Count != runs.Count) return;

                JsonElement acc = dispatches[accepted];
                JsonElement runLine = runs[accepted];
                string afterDigest = Digest(acc, "after");
                string beforeDigest = Digest(acc, "before");
                string runLink = CsEvidenceRecorder.Text(Obj(acc, "links"), "run_record");
                bool linked = runLink != null
                    && runLink.EndsWith(":" + (accepted + 1), StringComparison.Ordinal)
                    && CsEvidenceRecorder.Text(runLine, "seat_id")
                        == CsEvidenceRecorder.Text(acc, "seat_id")
                    && CsEvidenceRecorder.Int(runLine, "turn_token")
                        == CsEvidenceRecorder.Int(acc, "turn_token")
                    && CsEvidenceRecorder.Int(runLine, "request_index")
                        == CsEvidenceRecorder.Int(acc, "request_index");
                log.Check(linked,
                    "[CS-30] the accepted dispatch joins to the run line at its index with matching identity",
                    "link=" + runLink);
                log.Check(afterDigest != null && beforeDigest != null && afterDigest != beforeDigest,
                    "[CS-30] the accepted action's after digest differs from its before digest",
                    "before=" + beforeDigest + " after=" + afterDigest);
                log.Check(CsEvidenceRecorder.Bool(runLine, "result_ok") == true
                        && CsEvidenceRecorder.Text(runLine, "command_json") != null,
                    "[CS-30] the joined run line confirms the action was accepted before the after snapshot");
                log.Check(accepted + 1 >= dispatches.Count
                        || Digest(dispatches[accepted + 1], "before") == afterDigest,
                    "[CS-30] the next dispatch begins from the accepted action's joined after-state");

                // Test hook: the wrapper snapshots the engine when the transport RETURNS, before the
                // runner has applied the reply, so that snapshot is the pre-action state. The rebuilt
                // engine (same staged setup/seed) proves the digest; evidence uses the run line.
                CsScenarioSpec rebuiltSpec = Builtin("cs-visible-contact-v1", 4242);
                GameEngine engine = CsWorld.Create(rebuiltSpec,
                    "controlled-" + rebuiltSpec.ScenarioId + "-" + rebuiltSpec.Seed.ToString());
                string seat = CsEvidenceRecorder.Text(acc, "seat_id") ?? "seat-0";
                CsWorldSnapshot wrapperView = CsWorld.Capture(engine, seat, null, false);
                var inner = new FixedInner(Reply("{\"type\":\"end_turn\"}", 1, 1, "stop"));
                var wrapper = Wrap(inner, CsRunBudget.Unbounded(), before: wrapperView, after: wrapperView);
                Send(wrapper, "hook-system", "hook-user");
                CsDispatchCapture hook = FirstCapture(wrapper);
                log.Check(hook != null && hook.After != null
                        && hook.After.WorldDigest == wrapperView.WorldDigest
                        && hook.After.WorldDigest == beforeDigest
                        && hook.After.WorldDigest != afterDigest,
                    "[CS-30] the wrapper's response-time After is the pre-action state, not the final joined state",
                    "wrapper_after=" + (hook == null || hook.After == null ? "null" : hook.After.WorldDigest));

                int noAction = IndexOf(dispatches,
                    d => CsEvidenceRecorder.Bool(Obj(d, "action"), "accepted") == false);
                string noAfter = noAction < 0 ? null : Digest(dispatches[noAction], "after");
                string noBefore = noAction < 0 ? null : Digest(dispatches[noAction], "before");
                log.Check(noAction >= 0 && noAfter != null && noAfter == noBefore && noAfter != afterDigest,
                    "[CS-30] a dispatch with no accepted action keeps after equal to its own before, "
                    + "never another dispatch's state",
                    "index=" + noAction + " after=" + (noAfter ?? "null"));
            }
            finally { Cleanup(root); }
        }

        private static Dictionary<string, string> HashTree(string directory)
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(directory.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                hashes[relative] = CsWorld.Sha256(File.ReadAllText(path));
            }
            return hashes;
        }

        private static bool SameTree(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            if (before.Count != after.Count) return false;
            foreach (KeyValuePair<string, string> pair in before)
            {
                if (!after.TryGetValue(pair.Key, out string hash) || hash != pair.Value) return false;
            }
            return true;
        }
    }
}
