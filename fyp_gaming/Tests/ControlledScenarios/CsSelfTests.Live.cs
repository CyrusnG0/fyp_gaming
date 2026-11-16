using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // CS-22..CS-26: live-provider plumbing. Every test injects a local fake ILlmTransport through
    // CsRunOptions.ProviderOverride; no network call is made and no real credential is used. The
    // tests assert plumbing only (gating, validation, recording, labelling), never a "good" move.
    public static partial class CsSelfTests
    {
        private const string LiveSentinelKey = "sk-TESTSENTINEL0123456789abcdef";
        private const string LiveBaseUrl = "https://example.invalid/v1";
        private const string LiveEnvModel = "live-env-model-sentinel";
        private const string LiveOtherModel = "live-other-model-sentinel";

        private static void LiveProviderPlumbing(TestLog log)
        {
            Guard(log, LiveRefusedWithoutGate);
            Guard(log, LiveEnvironmentValidation);
            Guard(log, LiveModelResolution);
            Guard(log, LiveCredentialsAndLabelling);
        }

        // Sets the three live variables for the duration of a test and restores them.
        private sealed class LiveEnvLease : IDisposable
        {
            private readonly Dictionary<string, string> _saved = new Dictionary<string, string>(StringComparer.Ordinal);
            public LiveEnvLease()
            {
                foreach (string name in CsLiveEnvironment.EnvVarNames)
                    _saved[name] = Environment.GetEnvironmentVariable(name);
            }
            public void Dispose()
            {
                foreach (KeyValuePair<string, string> pair in _saved)
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        private static void SetLiveEnv(string model = LiveEnvModel, string baseUrl = LiveBaseUrl,
            string key = LiveSentinelKey)
        {
            Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", key);
            Environment.SetEnvironmentVariable("LUANSHI_LLM_BASE_URL", baseUrl);
            Environment.SetEnvironmentVariable("LUANSHI_LLM_MODEL", model);
        }

        private static void ClearLiveEnv()
        {
            foreach (string name in CsLiveEnvironment.EnvVarNames) Environment.SetEnvironmentVariable(name, null);
        }

        private static CsRunOptions LiveRun(IScenarioProvider provider)
            => new CsRunOptions { AllowLive = true, ProviderOverride = provider };

        // Live-labelled provider whose transport is supplied entirely by the test.
        private sealed class TestLiveProvider : IScenarioProvider
        {
            private readonly Func<CsProviderContext, ILlmTransport> _create;
            public int Created; public LlmConfig Config;
            public TestLiveProvider(Func<CsProviderContext, ILlmTransport> create) { _create = create; }
            public string Kind => "openai-compatible";
            public string Model { get; set; } = LiveEnvModel;
            public bool IsLive => true;
            public ILlmTransport CreateTransport(CsProviderContext context)
            {
                Interlocked.Increment(ref Created);
                Config = context.Config;
                return _create(context);
            }
        }

        /// <summary>Local transport that answers immediately and records what the runner sent.</summary>
        private sealed class CountingTransport : ILlmTransport
        {
            private readonly Func<LlmRequest, LlmResponse> _reply;
            public int Calls; public LlmRequest FirstRequest; public LlmRequest LastRequest;
            public CountingTransport(Func<LlmRequest, LlmResponse> reply) { _reply = reply; }
            public string Provider => "openai-compatible";
            public string Model => LiveEnvModel;
            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                if (FirstRequest == null) FirstRequest = req;
                LastRequest = req;
                return Task.FromResult(_reply(req));
            }
        }

        private static LlmResponse EndTurnReply(string summary = "live-deterministic fixture")
            => LlmResponse.Success("{\"type\":\"end_turn\",\"summary\":\"" + summary + "\"}",
                1, 12, 6, "live-test-request", "stop", null, 18, null, null, 200);

        /// <summary>Valid live manifest (max_output_tokens defaults to 128); throws on invalid input.</summary>
        private static CsManifest LiveManifest(string[] ids, int firstSeed,
            Action<CsRunCeilings> tweak = null, string model = "from-env")
        {
            var manifest = new CsManifest
            {
                Schema = CsManifest.SchemaId,
                Provider = new CsProviderManifest { Kind = "openai-compatible", Model = model },
                RunCeilings = new CsRunCeilings
                { MaxScenarios = ids.Length, MaxRunRequests = 200, MaxRunWallSeconds = 120,
                  MaxScenarioRequests = 40, MaxScenarioWallSeconds = 30, RequestTimeoutMs = 5000,
                  MaxOutputTokens = 128, UnknownUsagePolicy = "report" },
            };
            for (int i = 0; i < ids.Length; i++)
                manifest.Scenarios.Add(new CsManifestScenario { ScenarioId = ids[i], Seed = firstSeed + i });
            tweak?.Invoke(manifest.RunCeilings);
            List<string> errors = CsManifest.Validate(manifest);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            return manifest;
        }

        /// <summary>CS-22: the library gate refuses a live manifest before any directory or transport.</summary>
        private static void LiveRefusedWithoutGate(TestLog log)
        {
            string outRoot = NewRunDir("cs22");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv();
                    var transport = new CountingTransport(_ => EndTurnReply());
                    var provider = new TestLiveProvider(ctx => transport);
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4400);
                    string message = null;
                    try
                    {
                        CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot(),
                            new CsRunOptions { ProviderOverride = provider });
                    }
                    catch (CsLiveRefusedException e) { message = e.Message; }
                    log.Check(message != null
                            && message.IndexOf("--allow-live", StringComparison.Ordinal) >= 0
                            && !message.Contains(LiveSentinelKey),
                        "[CS-22] a live manifest without AllowLive is refused by the library gate",
                        message ?? "no exception");
                    log.Check(provider.Created == 0 && transport.Calls == 0
                            && Directory.GetDirectories(outRoot).Length == 0,
                        "[CS-22] the refusal precedes provider construction, dispatch and the run directory",
                        "created=" + provider.Created + " calls=" + transport.Calls);
                    string factoryMessage = null;
                    try { CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot()); }
                    catch (CsLiveRefusedException e) { factoryMessage = e.Message; }
                    log.Check(factoryMessage != null,
                        "[CS-22] the provider factory path is refused by the same gate");
                }
            }
            finally { Cleanup(outRoot); }
        }

        private static string ConfigError(CsManifest manifest, string outRoot, out int calls, out int created)
        {
            var transport = new CountingTransport(_ => EndTurnReply());
            var provider = new TestLiveProvider(ctx => transport);
            try
            {
                CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot(), LiveRun(provider));
                calls = transport.Calls; created = provider.Created;
                return null;
            }
            catch (CsLiveConfigException e)
            {
                calls = transport.Calls; created = provider.Created;
                return e.Message;
            }
        }

        /// <summary>CS-23: missing/invalid environment is named (never echoed) before dispatch.</summary>
        private static void LiveEnvironmentValidation(TestLog log)
        {
            string outRoot = NewRunDir("cs23");
            try
            {
                foreach (string missing in CsLiveEnvironment.EnvVarNames)
                {
                    using (new LiveEnvLease())
                    {
                        SetLiveEnv();
                        Environment.SetEnvironmentVariable(missing, null);
                        string message = ConfigError(LiveManifest(new[] { "cs-visible-contact-v1" }, 4405),
                            outRoot, out int calls, out int created);
                        log.Check(message != null && message.Contains(missing)
                                && !message.Contains(LiveSentinelKey) && !message.Contains(LiveEnvModel)
                                && calls == 0 && created == 0,
                            "[CS-23] a missing " + missing + " is named before dispatch and its value is not printed",
                            message ?? "no exception");
                    }
                }
                using (new LiveEnvLease())
                {
                    ClearLiveEnv();
                    string message = ConfigError(LiveManifest(new[] { "cs-visible-contact-v1" }, 4406),
                        outRoot, out int calls, out int created);
                    bool named = message != null;
                    foreach (string name in CsLiveEnvironment.EnvVarNames)
                        named &= message != null && message.Contains(name);
                    log.Check(named && calls == 0 && created == 0,
                        "[CS-23] with all three variables missing every name is listed and no value is printed",
                        message ?? "no exception");
                }
                var rejectedUrls = new[]
                { "http://example.invalid/v1", "https://secretuser:secretpass@example.invalid/v1",
                  "https://example.invalid/v1?api_key=QUERYSECRET" };
                foreach (string bad in rejectedUrls)
                {
                    using (new LiveEnvLease())
                    {
                        SetLiveEnv(baseUrl: bad);
                        string message = ConfigError(LiveManifest(new[] { "cs-visible-contact-v1" }, 4407),
                            outRoot, out int calls, out int created);
                        log.Check(message != null && message.Contains("LUANSHI_LLM_BASE_URL")
                                && !message.Contains("example.invalid") && !message.Contains("secretuser")
                                && !message.Contains("secretpass") && !message.Contains("QUERYSECRET")
                                && calls == 0 && created == 0,
                            "[CS-23] a non-https / userinfo / query base URL is refused without echoing it",
                            message ?? "no exception");
                    }
                }
                log.Check(Directory.GetDirectories(outRoot).Length == 0,
                    "[CS-23] environment failures happen before any run directory or dispatch");
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>CS-24: model mismatch is refused; from-env resolves and is recorded.</summary>
        private static void LiveModelResolution(TestLog log)
        {
            string outRoot = NewRunDir("cs24");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv(model: LiveEnvModel);
                    string message = null;
                    try
                    {
                        CsRunner.Run(LiveManifest(new[] { "cs-visible-contact-v1" }, 4410,
                            model: LiveOtherModel), outRoot, CsPaths.RepoRoot(),
                            new CsRunOptions { AllowLive = true });
                    }
                    catch (CsLiveConfigException e) { message = e.Message; }
                    log.Check(message != null && !message.Contains(LiveEnvModel)
                            && !message.Contains(LiveOtherModel),
                        "[CS-24] a provider.model that differs from the environment is refused without printing models",
                        message ?? "no exception");
                    log.Check(Directory.GetDirectories(outRoot).Length == 0,
                        "[CS-24] a model mismatch is refused before a run directory exists");

                    // An unregistered scenario exercises factory model resolution without any transport.
                    var resolved = new CsManifest
                    {
                        Schema = CsManifest.SchemaId,
                        Provider = new CsProviderManifest { Kind = "openai-compatible", Model = "from-env" },
                        RunCeilings = new CsRunCeilings
                        { MaxScenarios = 1, MaxRunRequests = 10, MaxRunWallSeconds = 30,
                          MaxScenarioRequests = 10, MaxScenarioWallSeconds = 10,
                          RequestTimeoutMs = 5000, MaxOutputTokens = 128, UnknownUsagePolicy = "report" },
                    };
                    resolved.Scenarios.Add(new CsManifestScenario
                    { ScenarioId = "cs-live-not-registered-v1", Seed = 4411 });
                    CsRunResult run = CsRunner.Run(resolved, outRoot, CsPaths.RepoRoot(),
                        new CsRunOptions { AllowLive = true });
                    JsonElement manifestJson = ReadJsonFile(Path.Combine(run.RunDirectory, "run-manifest.json"));
                    log.Check(run.ProviderModel == LiveEnvModel && run.ProviderLive
                            && CsEvidenceRecorder.Text(Obj(manifestJson, "provider"), "model") == LiveEnvModel,
                        "[CS-24] provider.model=from-env resolves to LUANSHI_LLM_MODEL and is recorded",
                        run.ProviderModel);
                }
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>CS-26: the key never reaches evidence; labels, limits and banners stay honest.</summary>
        private static void LiveCredentialsAndLabelling(TestLog log)
        {
            string outRoot = NewRunDir("cs26");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv();
                    var provider = new TestLiveProvider(ctx => new CountingTransport(_ => EndTurnReply()));
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4430);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsRunResult run;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }

                    log.Check(run.ProviderLive && run.ProviderKind == "openai-compatible"
                            && run.ProviderModel == LiveEnvModel,
                        "[CS-26] the live run is labelled with the live provider and resolved model");
                    string[] files = Directory.GetFiles(run.RunDirectory, "*", SearchOption.AllDirectories);
                    bool leaked = captured.ToString().Contains(LiveSentinelKey);
                    foreach (string path in files)
                        if (File.ReadAllText(path).Contains(LiveSentinelKey)) leaked = true;
                    log.Check(files.Length > 0 && !leaked,
                        "[CS-26] the sentinel key appears in no run file and not in captured console output",
                        "files=" + files.Length + " leaked=" + leaked);

                    CsScenarioResult scenario = run.Scenarios[0];
                    JsonElement settings = ReadJsonFile(Path.Combine(scenario.Directory, "settings.json"));
                    string redacted = CsEvidenceRecorder.Text(settings, "config_redacted") ?? "";
                    JsonElement providerBlock = Obj(settings, "provider");
                    string baseDisplay = CsEvidenceRecorder.Text(providerBlock, "base_url_display") ?? "";
                    log.Check(redacted.Contains("api_key=<set>") && !redacted.Contains(LiveSentinelKey),
                        "[CS-26] settings.json records config_redacted with api_key=<set>");
                    log.Check(CsEvidenceRecorder.Text(providerBlock, "kind") == "openai-compatible"
                            && CsEvidenceRecorder.Text(providerBlock, "model") == LiveEnvModel
                            && CsEvidenceRecorder.Bool(providerBlock, "live") == true
                            && baseDisplay == LiveBaseUrl
                            && CsEvidenceRecorder.Int(providerBlock, "max_output_tokens") == 128
                            && Names(providerBlock, "env_var_names", "LUANSHI_LLM_API_KEY")
                            && Names(providerBlock, "env_var_names", "LUANSHI_LLM_BASE_URL")
                            && Names(providerBlock, "env_var_names", "LUANSHI_LLM_MODEL")
                            && !baseDisplay.Contains("?") && !baseDisplay.Contains("@"),
                        "[CS-26] the provider block carries kind/model/live/safe base URL/env names/token limit",
                        NodeText(providerBlock));

                    bool numericUsd = false;
                    foreach (string path in files) numericUsd |= HasNumericUsd(File.ReadAllText(path));
                    string runManifestText = File.ReadAllText(Path.Combine(run.RunDirectory, "run-manifest.json"));
                    log.Check(!numericUsd && redacted.Contains("max_usd=unknown")
                            && runManifestText.Contains(CsLimits.LimitsNote)
                            && settings.GetRawText().Contains(CsLimits.LimitsNote),
                        "[CS-26] no USD value is computed and the fixed LIMITS_NOTE is present",
                        "numeric_usd=" + numericUsd);

                    List<JsonElement> dispatches = ReadJsonLines(
                        Path.Combine(scenario.Directory, "dispatches.ndjson"));
                    bool providerSource = dispatches.Count > 0;
                    foreach (JsonElement dispatch in dispatches)
                    {
                        providerSource &= CsEvidenceRecorder.Text(Obj(dispatch, "reply"), "source") == "provider";
                        providerSource &= !dispatch.GetRawText().Contains("fake_scripted");
                    }
                    JsonElement runManifest = ReadJsonFile(Path.Combine(run.RunDirectory, "run-manifest.json"));
                    string report = File.ReadAllText(Path.Combine(scenario.Directory,
                        scenario.ScenarioId + "-report.txt"));
                    log.Check(providerSource
                            && CsEvidenceRecorder.Text(Obj(runManifest, "usage"), "source") == "provider_reported"
                            && report.StartsWith("PROVIDER: LIVE openai-compatible " + LiveEnvModel,
                                StringComparison.Ordinal),
                        "[CS-26] live evidence is provider/provider_reported with the live report banner");
                }

                string fakeRoot = NewRunDir("cs26-fake");
                try
                {
                    CsRunResult fakeRun = CsRunner.Run(
                        Manifest(new[] { "cs-visible-contact-v1" }, 4431), fakeRoot, CsPaths.RepoRoot());
                    CsScenarioResult fakeScenario = fakeRun.Scenarios[0];
                    List<JsonElement> dispatches = ReadJsonLines(
                        Path.Combine(fakeScenario.Directory, "dispatches.ndjson"));
                    bool fakeSource = dispatches.Count > 0;
                    foreach (JsonElement dispatch in dispatches)
                        fakeSource &= CsEvidenceRecorder.Text(Obj(dispatch, "reply"), "source") == "fake_scripted";
                    string report = File.ReadAllText(Path.Combine(fakeScenario.Directory,
                        fakeScenario.ScenarioId + "-report.txt"));
                    log.Check(fakeSource && report.StartsWith("PROVIDER: FAKE", StringComparison.Ordinal)
                            && !report.Contains("PROVIDER: LIVE"),
                        "[CS-26] a fake run stays labelled fake_scripted and has no live banner");
                }
                finally { Cleanup(fakeRoot); }
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>True when any cost/USD field carries a number rather than unknown/null.</summary>
        private static bool HasNumericUsd(string text)
        {
            string[] markers = { "cost_usd\":", "usd_per_mtok_in=", "usd_per_mtok_out=", "max_usd=" };
            foreach (string marker in markers)
            {
                int at = text.IndexOf(marker, StringComparison.Ordinal);
                while (at >= 0)
                {
                    int i = at + marker.Length;
                    while (i < text.Length && (text[i] == ' ' || text[i] == '"' || text[i] == '\\')) i++;
                    if (i < text.Length && (char.IsDigit(text[i]) || text[i] == '-')) return true;
                    at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal);
                }
            }
            return false;
        }
    }
}
