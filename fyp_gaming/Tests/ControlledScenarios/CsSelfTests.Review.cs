using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // CS-31..CS-33: review fixes for the live-provider boundary. No network call and no real
    // credential: a test-only provider/transport is injected and the sentinel key exists only in
    // the process environment for the duration of a lease.
    public static partial class CsSelfTests
    {
        private const string ReviewSentinelKey = "SENTINELKEY-ab12cd34ef56gh78";

        private static void ReviewLiveSafety(TestLog log)
        {
            Guard(log, LiveRunScenarioRequiresAuthorizationAndBounds);
            Guard(log, ExactKeyRedaction);
            Guard(log, BaseUrlCredentialPaths);
        }

        // ---- CS-31: every live dispatch path needs authorization, a bounded budget and tokens ----

        private static void LiveRunScenarioRequiresAuthorizationAndBounds(TestLog log)
        {
            using (new LiveEnvLease())
            {
                SetLiveEnv();
                string root = NewTempDir("cs31");
                try
                {
                    CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4600);
                    spec.MaxRequests = 10; spec.MaxWallSeconds = 10; spec.RequestTimeoutMs = 5000;
                    var provider = new TestLiveProvider(
                        ctx => new CountingTransport(_ => EndTurnReply()));

                    Exception refused = ScenarioException(spec, root, provider,
                        new CsRunBudget(10, 60000), 64, allowLive: false);
                    log.Check(refused is CsLiveRefusedException
                            && Directory.GetFileSystemEntries(root).Length == 0
                            && provider.Created == 0,
                        "[CS-31] the public RunScenario overload refuses a live provider without allowLive "
                        + "before any directory or transport",
                        refused == null ? "no exception" : refused.GetType().Name
                            + " created=" + provider.Created);

                    var badBudgets = new Func<CsRunBudget>[]
                    {
                        () => null,
                        CsRunBudget.Unbounded,
                        () => new CsRunBudget(0, 60000),
                        () => new CsRunBudget(10, 0),
                    };
                    for (int i = 0; i < badBudgets.Length; i++)
                    {
                        Exception rejected = ScenarioException(spec, root, provider,
                            badBudgets[i](), 64, allowLive: true);
                        log.Check(rejected is CsLiveConfigException
                                && Directory.GetFileSystemEntries(root).Length == 0
                                && provider.Created == 0,
                            "[CS-31] live budget case " + i
                            + " (null/unbounded/non-positive) is refused before any directory or transport",
                            rejected == null ? "no exception" : rejected.GetType().Name);
                    }

                    foreach (int? tokens in new int?[] { null, 0, -5, CsLimits.MaxOutputTokensCeiling + 1 })
                    {
                        Exception rejected = ScenarioException(spec, root, provider,
                            new CsRunBudget(10, 60000), tokens, allowLive: true);
                        log.Check(rejected is CsLiveConfigException
                                && Directory.GetFileSystemEntries(root).Length == 0
                                && provider.Created == 0,
                            "[CS-31] live max_output_tokens="
                            + (tokens.HasValue ? tokens.Value.ToString() : "missing")
                            + " is refused (no 400 default, no over-ceiling value)",
                            rejected == null ? "no exception" : rejected.GetType().Name);
                    }

                    CsScenarioResult accepted = CsRunner.RunScenario(spec, root, provider,
                        TestGit(), "review-cs31-accepted", new CsRunBudget(10, 60000), null, 64, true);
                    log.Check(accepted != null && accepted.ProviderLive && provider.Created == 1
                            && Directory.Exists(Path.Combine(root, spec.ScenarioId)),
                        "[CS-31] an authorized, bounded, token-limited live call still runs",
                        accepted == null ? "null result" : accepted.Status);
                }
                finally { Cleanup(root); }

                string fakeRoot = NewTempDir("cs31-fake");
                try
                {
                    CsScenarioResult fake = CsRunner.RunScenario(Builtin("cs-visible-contact-v1", 4601),
                        fakeRoot, FakeModel, TestGit(), "review-cs31-fake");
                    log.Check(fake != null && !fake.ProviderLive && fake.Status != null,
                        "[CS-31] the fake path keeps its null-budget / 400-token defaults",
                        fake == null ? "null result" : fake.Status);
                }
                finally { Cleanup(fakeRoot); }
            }
        }

        private static Exception ScenarioException(CsScenarioSpec spec, string root,
            IScenarioProvider provider, CsRunBudget budget, int? tokens, bool allowLive)
        {
            try
            {
                CsRunner.RunScenario(spec, root, provider, TestGit(),
                    "review-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    budget, null, tokens, allowLive);
                return null;
            }
            catch (Exception e) { return e; }
        }

        // ---- CS-32: the exact non-sk- sentinel is scrubbed from every derived field ----

        private static void ExactKeyRedaction(TestLog log)
        {
            using (new LiveEnvLease())
            {
                SetLiveEnv(key: ReviewSentinelKey);
                string echoRoot = NewRunDir("cs32-echo");
                try
                {
                    var provider = new TestLiveProvider(ctx => new CountingTransport(
                        _ => EndTurnReply("reply echoes " + ReviewSentinelKey)));
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4620);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsRunResult run;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        run = CsRunner.Run(manifest, echoRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }

                    string[] files = Directory.GetFiles(run.RunDirectory, "*",
                        SearchOption.AllDirectories);
                    bool leaked = captured.ToString().Contains(ReviewSentinelKey);
                    bool marker = false;
                    foreach (string path in files)
                    {
                        string text = File.ReadAllText(path);
                        if (text.Contains(ReviewSentinelKey)) leaked = true;
                        if (text.Contains(CsEvidence.RedactedKeyMarker)) marker = true;
                    }
                    log.Check(files.Length > 0 && !leaked,
                        "[CS-32] an echoed non-sk- key is absent from every run file and console capture",
                        "files=" + files.Length + " leaked=" + leaked);
                    log.Check(marker,
                        "[CS-32] the exact-value redaction marker is present in the saved evidence");
                }
                finally { Cleanup(echoRoot); }

                string throwRoot = NewRunDir("cs32-throw");
                try
                {
                    var transport = new ReviewThrowingTransport(
                        "provider failure containing " + ReviewSentinelKey);
                    var provider = new TestLiveProvider(ctx => transport);
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4625);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        CsRunner.Run(manifest, throwRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }
                    bool leaked = captured.ToString().Contains(ReviewSentinelKey);
                    foreach (string path in Directory.GetFiles(throwRoot, "*",
                        SearchOption.AllDirectories))
                        if (File.ReadAllText(path).Contains(ReviewSentinelKey)) leaked = true;
                    log.Check(transport.Calls > 0 && !leaked,
                        "[CS-32] an exception message containing the key never reaches files or console",
                        "calls=" + transport.Calls + " leaked=" + leaked);
                }
                finally { Cleanup(throwRoot); }

                string detailRoot = NewTempDir("cs32-detail");
                try
                {
                    var provider = new TestLiveProvider(
                        ctx => throw new InvalidOperationException(
                            "transport creation failed: " + ReviewSentinelKey));
                    CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4630);
                    spec.MaxRequests = 5; spec.MaxWallSeconds = 10; spec.RequestTimeoutMs = 5000;
                    CsScenarioResult result = CsRunner.RunScenario(spec, detailRoot, provider,
                        TestGit(), "review-cs32-detail", new CsRunBudget(10, 60000), null, 64, true);
                    log.Check(result.Status == CsStatus.Failed
                            && result.Detail != null
                            && result.Detail.Contains(CsEvidence.RedactedKeyMarker)
                            && !result.Detail.Contains(ReviewSentinelKey),
                        "[CS-32] a thrown exception message is redacted before it becomes result.Detail",
                        result.Detail ?? "null");
                }
                finally { Cleanup(detailRoot); }
            }
        }

        private sealed class ReviewThrowingTransport : ILlmTransport
        {
            private readonly string _message;
            public int Calls;
            public ReviewThrowingTransport(string message) { _message = message; }
            public string Provider => "openai-compatible";
            public string Model => LiveEnvModel;
            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                throw new InvalidOperationException(_message);
            }
        }

        // ---- CS-33: credential-bearing URL paths are rejected and never displayed ----

        private static void BaseUrlCredentialPaths(TestLog log)
        {
            string malicious = "https://example.invalid/v1/" + ReviewSentinelKey + "/";
            string display = CsLiveEnvironment.DisplayBaseUrl(malicious);
            log.Check(display == "https://example.invalid/..." && !display.Contains(ReviewSentinelKey),
                "[CS-33] a secret-like path segment is replaced by /... in the display form",
                display ?? "null");
            log.Check(CsLiveEnvironment.DisplayBaseUrl("https://example.invalid/v1")
                    == "https://example.invalid/v1",
                "[CS-33] a short plain path is still displayed in full");

            using (new LiveEnvLease())
            {
                SetLiveEnv(key: ReviewSentinelKey, baseUrl: malicious);
                CsLiveEnvironmentResult read = CsLiveEnvironment.Read();
                log.Check(!read.Ok && read.Errors.Count > 0
                        && read.Errors[0].Contains("LUANSHI_LLM_BASE_URL")
                        && !read.Errors[0].Contains(ReviewSentinelKey)
                        && !read.Errors[0].Contains("example.invalid"),
                    "[CS-33] Read rejects a key in the path and names only the variable",
                    read.Errors.Count == 0 ? "accepted" : read.Errors[0]);

                string manifestRoot = NewRunDir("cs33-manifest");
                try
                {
                    string message = null;
                    try
                    {
                        CsRunner.Run(LiveManifest(new[] { "cs-visible-contact-v1" }, 4640),
                            manifestRoot, CsPaths.RepoRoot(),
                            new CsRunOptions { AllowLive = true });
                    }
                    catch (CsLiveConfigException e) { message = e.Message; }
                    log.Check(message != null && message.Contains("LUANSHI_LLM_BASE_URL")
                            && !message.Contains(ReviewSentinelKey)
                            && !message.Contains("example.invalid")
                            && Directory.GetDirectories(manifestRoot).Length == 0,
                        "[CS-33] the runner rejects before a run directory and never prints the URL",
                        message ?? "no exception");
                }
                finally { Cleanup(manifestRoot); }

                string runRoot = NewTempDir("cs33-run");
                try
                {
                    // A direct live dispatch now validates the complete environment before it
                    // creates anything, so this part uses the valid synthetic URL; the malicious
                    // URL is still covered by Read and the manifest path above.
                    SetLiveEnv(key: ReviewSentinelKey, baseUrl: LiveBaseUrl);
                    var provider = new TestLiveProvider(
                        ctx => new CountingTransport(_ => EndTurnReply()));
                    CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4641);
                    spec.MaxRequests = 10; spec.MaxWallSeconds = 10; spec.RequestTimeoutMs = 5000;
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsScenarioResult result;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        result = CsRunner.RunScenario(spec, runRoot, provider, TestGit(),
                            "review-cs33", new CsRunBudget(10, 60000), null, 64, true);
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }

                    bool leaked = captured.ToString().Contains(ReviewSentinelKey);
                    bool displaySafe = false;
                    foreach (string path in Directory.GetFiles(runRoot, "*",
                        SearchOption.AllDirectories))
                    {
                        string text = File.ReadAllText(path);
                        if (text.Contains(ReviewSentinelKey)) leaked = true;
                        if (Path.GetFileName(path) == "settings.json")
                        {
                            JsonElement settings = ReadJsonFile(path);
                            displaySafe = CsEvidenceRecorder.Text(
                                Obj(settings, "provider"), "base_url_display") == LiveBaseUrl;
                        }
                    }
                    log.Check(result != null && !leaked && displaySafe,
                        "[CS-33] the display path is safe in every saved file and console capture",
                        "leaked=" + leaked + " display_safe=" + displaySafe);
                }
                finally { Cleanup(runRoot); }
            }
        }
    }
}
