using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public sealed class CsRunResult
    {
        public string RunId { get; set; }
        public string RunDirectory { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public string Branch { get; set; }
        public string HeadCommit { get; set; }
        public string ProviderKind { get; set; }
        public string ProviderModel { get; set; }
        public bool ProviderLive { get; set; }
        public string ProviderBaseUrlDisplay { get; set; }
        public int ProviderMaxOutputTokens { get; set; }
        public List<string> ProviderEnvVarNames { get; } = new List<string>();
        public int Requests { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public int UnknownUsageRequests { get; set; }
        public int LateResponsesDiscarded { get; set; }
        public long ElapsedMs { get; set; }
        public List<CsScenarioResult> Scenarios { get; } = new List<CsScenarioResult>();
        public List<string> Limitations { get; } = new List<string>();
        public List<string> DirtyFiles { get; } = new List<string>();
        public List<string> UntrackedBuildInputs { get; } = new List<string>();
    }

    public sealed class CsReplayResult
    {
        public bool Matched { get; set; }
        public string NewRunDirectory { get; set; }
        public List<string> Details { get; } = new List<string>();
    }

    /// <summary>
    /// Runs a bounded manifest through the production spectator session. Every scenario has an
    /// explicit request and wall ceiling, the whole run has both too, and a scenario stopped by a
    /// ceiling or a provider failure is reported as stopped/failed, never as a pass.
    /// </summary>
    public static partial class CsRunner
    {
        public static CsRunResult Run(CsManifest manifest, string baseOut, string repoRoot,
            CsRunOptions options = null)
        {
            options ??= new CsRunOptions();
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            bool liveManifest = manifest.Provider != null
                && string.Equals(manifest.Provider.Kind, "openai-compatible", StringComparison.Ordinal);
            bool liveOverride = options.ProviderOverride != null && options.ProviderOverride.IsLive;

            // Decision 2: the gate runs before any directory, provider construction, env read,
            // transport or dispatch. The same gate is enforced for library callers.
            if ((liveManifest || liveOverride) && !options.AllowLive)
                throw new CsLiveRefusedException(CsProviderFactory.LiveRefusal);

            // Decision 3: the environment is validated once, before the first scenario. Only
            // variable names ever leave Read(); the key travels via LlmConfig.Load inside settings.
            bool needsEnvironment = liveOverride || (liveManifest && options.ProviderOverride == null);
            CsLiveEnvironmentResult environment = null;
            if (needsEnvironment)
            {
                environment = CsLiveEnvironment.Read();
                if (!environment.Ok)
                    throw new CsLiveConfigException(string.Join("; ", environment.Errors));
            }

            IScenarioProvider provider;
            if (options.ProviderOverride != null)
                provider = options.ProviderOverride;
            else if (liveManifest)
            {
                string resolved = CsLiveEnvironment.ResolveModel(
                    manifest.Provider.Model, environment.Model);
                provider = CsProviderFactory.Create(manifest.Provider.Kind, resolved, true);
            }
            else provider = CsProviderFactory.Create(manifest.Provider.Kind, manifest.Provider.Model);

            // A live path never gets the fake 400 default: the same validated token limit, bounded
            // budget and explicit authorization are checked here before the run directory exists.
            int maxOutputTokens = manifest.RunCeilings?.MaxOutputTokens
                ?? (provider.IsLive ? 0 : 400);
            int maxScenarios = manifest.RunCeilings.MaxScenarios.Value;
            int maxRunRequests = manifest.RunCeilings.MaxRunRequests.Value;
            long maxRunMs = manifest.RunCeilings.MaxRunWallSeconds.Value * 1000L;
            // One budget for the whole run: every dispatch reserves from it, so the actual total
            // can never exceed max_run_requests even when a scenario's own cap is larger.
            var budget = new CsRunBudget(maxRunRequests, maxRunMs);
            ValidateLiveDispatch(provider, options.AllowLive, budget, maxOutputTokens);
            string baseUrlDisplay = provider.IsLive && environment != null
                ? CsLiveEnvironment.DisplayBaseUrl(environment.BaseUrl) : null;

            string outRoot = CsPaths.ResolveOut(baseOut, repoRoot);
            string runId = CsPaths.NewRunId();
            string runDir = Path.Combine(outRoot, runId);
            Directory.CreateDirectory(runDir);
            CsGitInfo git = CsGit.Collect(repoRoot);
            var result = new CsRunResult
            {
                RunId = runId, RunDirectory = runDir, Branch = git.Branch, HeadCommit = git.HeadCommit,
                ProviderKind = provider.Kind, ProviderModel = provider.Model,
                ProviderLive = provider.IsLive, ProviderBaseUrlDisplay = baseUrlDisplay,
                ProviderMaxOutputTokens = maxOutputTokens,
            };
            if (provider.IsLive)
                result.ProviderEnvVarNames.AddRange(CsLiveEnvironment.EnvVarNames);
            result.DirtyFiles.AddRange(git.DirtyFiles);
            result.UntrackedBuildInputs.AddRange(git.UntrackedBuildInputs);

            var clock = Stopwatch.StartNew();
            string stopLimit = null;

            for (int i = 0; i < manifest.Scenarios.Count; i++)
            {
                CsManifestScenario entry = manifest.Scenarios[i];
                if (i >= maxScenarios) { stopLimit = "max_scenarios"; result.Scenarios.Add(Skipped(entry, "max_scenarios", manifest)); continue; }
                if (budget.RemainingRequests <= 0) { stopLimit = "max_run_requests"; result.Scenarios.Add(Skipped(entry, "max_run_requests", manifest)); continue; }
                if (budget.DeadlineReached) { stopLimit = "max_run_wall_seconds"; result.Scenarios.Add(Skipped(entry, "max_run_wall_seconds", manifest)); continue; }

                if (!CsScenarios.TryGet(entry.ScenarioId, entry.Seed, out CsScenarioSpec spec))
                {
                    var missing = Skipped(entry, "unknown_scenario", manifest);
                    missing.Status = CsStatus.Failed;
                    result.Scenarios.Add(missing);
                    continue;
                }
                spec.MaxRequests = manifest.ScenarioRequests(entry);
                spec.MaxWallSeconds = manifest.ScenarioWallSeconds(entry);
                spec.RequestTimeoutMs = manifest.ScenarioRequestTimeoutMs(entry);
                CsScenarioResult scenario = RunScenario(spec, runDir, provider, git, runId,
                    budget, manifest.RunCeilings, maxOutputTokens, options.AllowLive);
                result.Scenarios.Add(scenario);
                bool more = i + 1 < manifest.Scenarios.Count;
                if (scenario.Status == CsStatus.Failed) { stopLimit = "scenario_failed"; break; }
                if (budget.DeadlineReached) { if (more) stopLimit = "max_run_wall_seconds"; break; }
                if (budget.RemainingRequests <= 0) { if (more) stopLimit = "max_run_requests"; break; }
            }

            // Any manifest scenario not reached is retained as skipped, so the run cannot look complete.
            for (int i = result.Scenarios.Count; i < manifest.Scenarios.Count; i++)
                result.Scenarios.Add(Skipped(manifest.Scenarios[i], stopLimit ?? "not_reached",
                    manifest));

            int actualRequests = 0;
            result.ElapsedMs = clock.ElapsedMilliseconds;
            result.UnknownUsageRequests = 0;
            bool anyUnknown = false;
            long inputSum = 0, outputSum = 0;
            foreach (CsScenarioResult scenario in result.Scenarios)
            {
                actualRequests += scenario.Requests;
                result.LateResponsesDiscarded += scenario.LateResponsesDiscarded;
                // A skipped / not-reached scenario dispatched nothing, so it has no usage to
                // aggregate and must not turn known totals into null.
                if (scenario.Dispatches == 0) continue;
                result.UnknownUsageRequests += scenario.UnknownUsageRequests;
                if (scenario.InputTokens.HasValue && scenario.OutputTokens.HasValue)
                {
                    inputSum += scenario.InputTokens.Value;
                    outputSum += scenario.OutputTokens.Value;
                }
                else anyUnknown = true;
            }
            // The sum of ACTUAL dispatches, never session.TotalRequests (which also counts a
            // refused-backstop request that never reached the transport).
            result.Requests = actualRequests;
            if (result.Scenarios.Count > 0)
            {
                result.InputTokens = anyUnknown ? (long?)null : inputSum;
                result.OutputTokens = anyUnknown ? (long?)null : outputSum;
            }

            bool anyFailed = result.Scenarios.Exists(s => s.Status == CsStatus.Failed);
            bool anyStopped = result.Scenarios.Exists(s => s.Status == CsStatus.Stopped
                || s.Status == CsStatus.Skipped || s.Status == CsStatus.CompletedWithStops);
            bool allCompleted = result.Scenarios.Count == manifest.Scenarios.Count
                && result.Scenarios.TrueForAll(s => s.Status == CsStatus.Completed);
            result.Status = anyFailed ? CsStatus.Failed
                : anyStopped || !allCompleted ? CsStatus.Stopped : CsStatus.Completed;
            result.Detail = stopLimit == null ? null : "run ceiling or stop: " + stopLimit;
            if (result.ProviderLive)
            {
                result.Limitations.Add("live replies are model-written (source=provider), NOT fixtures");
                result.Limitations.Add("usage is provider_reported; missing usage stays unknown");
                result.Limitations.Add(CsLimits.LimitsNote);
            }
            else
            {
                result.Limitations.Add("fake replies are fixtures (source=fake_scripted), not model behaviour");
                result.Limitations.Add("no USD cap and no live/provider validation: usage is fake_deterministic");
            }
            result.Limitations.Add("unknown usage is reported as null, never as zero");
            if (result.UnknownUsageRequests > 0)
                result.Detail = (result.Detail == null ? "" : result.Detail + "; ")
                    + result.UnknownUsageRequests + " request(s) without usage";

            WriteAggregateDispatches(runDir, result);
            WriteRunManifest(runDir, manifest, result, git);
            return result;
        }

        public static CsReplayResult Replay(string runDirectory, string baseOut, string repoRoot)
        {
            var replay = new CsReplayResult();
            string manifestPath = Path.Combine(runDirectory, "run-manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("run-manifest.json not found", manifestPath);
            string providerModel = "fake-model", providerKind = "fake";
            bool recordedLive = false;
            int? maxOutputTokens = null;
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("provider", out JsonElement p))
                {
                    if (p.TryGetProperty("model", out JsonElement m)
                        && m.ValueKind == JsonValueKind.String) providerModel = m.GetString();
                    if (p.TryGetProperty("kind", out JsonElement k)
                        && k.ValueKind == JsonValueKind.String) providerKind = k.GetString();
                    if (p.TryGetProperty("live", out JsonElement l)
                        && l.ValueKind == JsonValueKind.True) recordedLive = true;
                }
                if (root.TryGetProperty("run_ceilings", out JsonElement rc)
                    && rc.ValueKind == JsonValueKind.Object
                    && rc.TryGetProperty("max_output_tokens", out JsonElement tokens)
                    && tokens.ValueKind == JsonValueKind.Number
                    && tokens.TryGetInt32(out int tokenValue)) maxOutputTokens = tokenValue;
            }
            // Replay is fake-only. A recorded live run is never re-dispatched: that would call a
            // provider and could spend money, and its replies were not fixtures.
            if (recordedLive || string.Equals(providerKind, "openai-compatible", StringComparison.Ordinal))
                throw new CsLiveRefusedException(
                    "replay is fake-only: the recorded run used the live provider "
                    + "'openai-compatible'. A live run cannot be replayed and no provider is called.");

            string outRoot = CsPaths.ResolveOut(baseOut, repoRoot);
            string runId = CsPaths.NewRunId();
            string newDir = Path.Combine(outRoot, runId);
            Directory.CreateDirectory(newDir);
            CsGitInfo git = CsGit.Collect(repoRoot);
            replay.NewRunDirectory = newDir;
            replay.Matched = true;
            // Replay must not inherit the recorded run's ceiling; it reproduces the scenario's own
            // recorded caps, so an unbounded run budget is used for the whole replay.
            CsRunBudget replayBudget = CsRunBudget.Unbounded();
            IScenarioProvider provider = CsProviderFactory.Create(providerKind, providerModel);

            foreach (string dir in Directory.GetDirectories(runDirectory))
            {
                string scenarioPath = Path.Combine(dir, "scenario.json");
                if (!File.Exists(scenarioPath)) continue;
                CsScenarioFile file = CsJson.Parse<CsScenarioFile>(File.ReadAllText(scenarioPath), out string error);
                if (file == null) { replay.Matched = false; replay.Details.Add(dir + ": " + error); continue; }
                CsScenarioSpec spec = file.ToSpec();
                if (spec.MaxRequests <= 0 || spec.MaxWallSeconds <= 0 || spec.RequestTimeoutMs <= 0)
                {
                    replay.Matched = false;
                    replay.Details.Add(file.ScenarioId + ": recorded ceilings are incomplete");
                    continue;
                }
                CsScenarioResult rerun = RunScenario(spec, newDir, provider, git, runId,
                    replayBudget, null, maxOutputTokens);
                string oldPath = Path.Combine(dir, "result.json");
                string newPath = Path.Combine(newDir, spec.ScenarioId, "result.json");
                if (!File.Exists(oldPath)) { replay.Matched = false; replay.Details.Add(file.ScenarioId + ": no original result.json"); continue; }
                using (JsonDocument oldDoc = JsonDocument.Parse(File.ReadAllText(oldPath)))
                using (JsonDocument newDoc = JsonDocument.Parse(File.ReadAllText(newPath)))
                {
                    JsonElement oldRoot = oldDoc.RootElement, newRoot = newDoc.RootElement;
                    string oldSettings = Str(oldRoot, "settings_hash"), newSettings = Str(newRoot, "settings_hash");
                    string oldDigest = Str(oldRoot, "final_world_digest"), newDigest = Str(newRoot, "final_world_digest");
                    string oldPrompts = JsonSerializer.Serialize(oldRoot.GetProperty("prompt_hashes"));
                    string newPrompts = JsonSerializer.Serialize(newRoot.GetProperty("prompt_hashes"));
                    bool ok = oldSettings == newSettings && oldDigest == newDigest && oldPrompts == newPrompts;
                    replay.Matched &= ok;
                    replay.Details.Add(file.ScenarioId + ": " + (ok ? "reproduced" : "MISMATCH")
                        + " settings=" + (oldSettings == newSettings) + " prompts=" + (oldPrompts == newPrompts)
                        + " final_digest=" + (oldDigest == newDigest));
                }
            }
            return replay;
        }
    }
}
