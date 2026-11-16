using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // One scenario inside the whole run: staged setup, fake scripts, bounded spectator loop, evidence.
    public static partial class CsRunner
    {
        /// <summary>
        /// Compatibility overload for existing callers and tests. It applies the same selection
        /// gate, so asking for a live kind here is refused rather than silently made offline.
        /// </summary>
        public static CsScenarioResult RunScenario(CsScenarioSpec spec, string runDirectory,
            string providerModel, CsGitInfo git, string runId, CsRunBudget budget = null,
            string providerKind = "fake", CsRunCeilings runCeilings = null)
        {
            IScenarioProvider provider = CsProviderFactory.Create(providerKind, providerModel);
            return RunScenario(spec, runDirectory, provider, git, runId, budget, runCeilings,
                runCeilings?.MaxOutputTokens);
        }

        /// <summary>
        /// Internal provider-injection overload (tests and <see cref="Run"/> use it). It is NOT a
        /// public entry point: an external caller must use the string-kind overload above, which
        /// goes through <see cref="CsProviderFactory"/> and refuses live without --allow-live.
        /// </summary>
        internal static CsScenarioResult RunScenario(CsScenarioSpec spec, string runDirectory,
            IScenarioProvider provider, CsGitInfo git, string runId, CsRunBudget budget = null,
            CsRunCeilings runCeilings = null, int? maxOutputTokens = null, bool allowLive = false)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            int? requestedOutputTokens = maxOutputTokens ?? runCeilings?.MaxOutputTokens;
            // Every live path is authorized, bounded and token-limited BEFORE any directory is
            // created or the provider is asked for a transport. The fake path keeps its defaults.
            ValidateLiveDispatch(provider, allowLive, budget, requestedOutputTokens);
            int outputTokens = provider.IsLive ? requestedOutputTokens.Value : requestedOutputTokens ?? 400;
            string scenarioDir = Path.Combine(runDirectory, spec.ScenarioId);
            Directory.CreateDirectory(scenarioDir);
            var result = new CsScenarioResult
            {
                ScenarioId = spec.ScenarioId, Seed = spec.Seed, StudySeat = spec.StudySeat,
                Directory = scenarioDir, Status = CsStatus.Failed, SeasonsPlanned = spec.Seasons,
                ProviderKind = provider.Kind, ProviderModel = provider.Model,
                ProviderLive = provider.IsLive, MaxOutputTokens = outputTokens,
                RequestTimeoutMs = spec.RequestTimeoutMs,
            };
            var clock = Stopwatch.StartNew();
            budget ??= CsRunBudget.Unbounded();
            // Result.json is rewritten after a failed sweep, so the captures that shape it must
            // survive out of the try block even when the body throws.
            List<CsDispatchCapture> capturesForResult = new List<CsDispatchCapture>();
            try
            {
                // Stable game id: replaying the same staged setup must produce the same prompts.
                GameEngine engine = CsWorld.Create(spec,
                    "controlled-" + spec.ScenarioId + "-" + spec.Seed.ToString());
                result.StartWorldDigest = CsWorld.CanonicalDigest(engine.State);

                CsScenarioFile file = CsScenarioFile.FromSpec(spec);
                WriteJson(scenarioDir, "scenario.json", file);
                // Hash the exact bytes written, so the audit can re-derive it from script.json.
                string scriptJson = StagedScriptJson(spec);
                File.WriteAllText(Path.Combine(scenarioDir, "script.json"),
                    CsEvidence.Redact(scriptJson), new UTF8Encoding(false));
                result.SetupHash = CsWorld.Sha256(JsonSerializer.Serialize(file.Setup, CsJson.Compact));
                result.ScriptSha256 = CsWorld.Sha256(scriptJson);

                // Effective ceilings clamp this scenario to what the whole run still allows. They
                // are computed before the config so settings.json and the production runner carry
                // the effective values; the recording wrapper enforces the same values as backstop.
                long specWallMs = Math.Max(1, spec.MaxWallSeconds) * 1000L;
                long scenarioStartMs = budget.ElapsedMs;
                long runRemainingMs = budget.RemainingMs;
                long effectiveWallMs = Math.Min(specWallMs, runRemainingMs);
                if (effectiveWallMs < 1) effectiveWallMs = 1;
                // Round UP to whole seconds: the loop and wrapper enforce the exact millisecond
                // deadline, so a floor here would make the production config stop earlier than
                // the computed effective wall.
                int effectiveWallSeconds = (int)Math.Max(1, (effectiveWallMs + 999L) / 1000L);
                int effectiveRequests = Math.Min(spec.MaxRequests, budget.RemainingRequests);
                bool runRequestConstrained = budget.RemainingRequests < spec.MaxRequests;
                bool runWallConstrained = runRemainingMs < specWallMs;
                long scenarioDeadlineMs = scenarioStartMs + effectiveWallMs;

                LlmConfig config = provider.IsLive
                    ? CsSettings.BuildLive(spec, provider.Model, effectiveRequests,
                        effectiveWallSeconds, outputTokens)
                    : CsSettings.Build(spec, provider.Model, effectiveRequests,
                        effectiveWallSeconds, outputTokens);
                string baseUrlDisplay = provider.IsLive
                    ? CsLiveEnvironment.DisplayBaseUrl(config.BaseUrl) : null;
                result.SettingsHash = provider.IsLive
                    ? CsSettings.HashLive(spec, config, baseUrlDisplay)
                    : CsSettings.Hash(spec, config);
                result.EffectiveMaxRequests = effectiveRequests;
                result.EffectiveWallSeconds = effectiveWallSeconds;
                var providerBlock = new Dictionary<string, object>
                {
                    { "kind", provider.Kind }, { "model", provider.Model },
                    { "live", provider.IsLive }, { "base_url_display", baseUrlDisplay },
                    { "env_var_names", provider.IsLive ? CsLiveEnvironment.EnvVarNames : Array.Empty<string>() },
                    { "max_output_tokens", outputTokens },
                    { "request_timeout_ms", spec.RequestTimeoutMs },
                    { "max_requests", effectiveRequests },
                    { "max_wall_seconds", effectiveWallSeconds },
                    { "effective_max_requests", effectiveRequests },
                    { "effective_max_wall_seconds", effectiveWallSeconds },
                    { "commit", git.HeadCommit ?? CsGitInfo.BaseCommit },
                    { "dirty", git.DirtyFiles.Count > 0 }, { "seed", spec.Seed },
                    { "scenario_id", spec.ScenarioId },
                    { "settings_hash", result.SettingsHash },
                    { "limits_note", CsLimits.LimitsNote },
                };
                WriteJson(scenarioDir, "settings.json", new Dictionary<string, object>
                {
                    { "scenario_id", spec.ScenarioId }, { "seed", spec.Seed },
                    { "settings_hash", result.SettingsHash },
                    { "provider", providerBlock },
                    { "config_redacted", config.Redacted() },
                    { "flags", new Dictionary<string, object>
                        {
                            { "attack", spec.EnableAttack }, { "research", spec.EnableResearch },
                            { "manual_lookup", spec.EnableManualLookup },
                            { "deliberation", spec.EnableDeliberation },
                        } },
                    { "ceilings", new Dictionary<string, object>
                        {
                            { "max_requests", effectiveRequests },
                            { "max_wall_seconds", effectiveWallSeconds },
                            { "request_timeout_ms", spec.RequestTimeoutMs },
                            { "max_output_tokens", outputTokens },
                            { "declared_max_requests", spec.MaxRequests },
                            { "declared_max_wall_seconds", spec.MaxWallSeconds },
                        } },
                });

                // The production stores persist model-authored text. Wrap them so the configured
                // key is redacted before it reaches disk (the directory sweep below is the final
                // backstop for anything else either store writes).
                ILlmMemoryStore memory = spec.EnableDeliberation
                    ? new CsRedactingMemoryStore(
                        new LocalLlmMemoryStore(Path.Combine(scenarioDir, "memory"))) : null;
                ILlmStrategicNoteStore notes = spec.EnableDeliberation
                    ? new CsRedactingNoteStore(
                        new LocalLlmStrategicNoteStore(Path.Combine(scenarioDir, "notes.jsonl"))) : null;

                SpectatorSession session = null;
                var context = new CsProviderContext
                {
                    RunDirectory = scenarioDir, ScenarioId = spec.ScenarioId, Seed = spec.Seed,
                    Config = config, ActiveSeatId = () => session == null ? null : session.ActiveSeatId,
                    Script = spec.ScriptFor(spec.StudySeat),
                    Scripts = new Dictionary<string, IReadOnlyList<CsScriptStep>>(StringComparer.Ordinal)
                    {
                        { spec.StudySeat, spec.ScriptFor(spec.StudySeat) },
                        { spec.OpponentSeat, spec.ScriptFor(spec.OpponentSeat) },
                    },
                };
                ILlmTransport inner = provider.CreateTransport(context);
                var transport = new CsRecordingTransport(inner, context.ActiveSeatId, budget,
                    effectiveRequests, scenarioDeadlineMs,
                    seat => CsWorld.Capture(engine, seat, config, true),
                    seat => CsWorld.Capture(engine, seat, config, false),
                    provider.IsLive);

                var recorder = new CsEvidenceRecorder(scenarioDir)
                {
                    AfterSnapshot = seat => CsWorld.Capture(engine, seat, config, false),
                };
                var runner = new LlmTurnRunner(transport, config, recorder, memory, notes);
                recorder.BindRunId(runner.RunId);
                session = new SpectatorSession(engine, runner, spec.Seasons, spec.Seed, recorder);

                string wallLimit = null;
                session.Start();
                while (session.State == SpectatorState.Running)
                {
                    if (budget.DeadlineReached)
                    { wallLimit = "max_run_wall_seconds"; session.Stop(); break; }
                    if (budget.ElapsedMs - scenarioStartMs >= effectiveWallMs)
                    {
                        wallLimit = runWallConstrained ? "max_run_wall_seconds" : "max_scenario_wall_seconds";
                        session.Stop();
                        break;
                    }
                    session.Tick();
                    Thread.Yield();
                }

                // Freeze the capture list before reading it, so no late response can mutate evidence.
                transport.Seal();
                result.Requests = transport.DispatchedCount;
                result.Dispatches = transport.Captures().Count;
                result.LateResponsesDiscarded = transport.LateResponsesDiscarded;
                result.SeasonsPlayed = session.SeasonsPlayed;
                result.RunId = runner.RunId;
                result.FinalWorldDigest = CsWorld.CanonicalDigest(engine.State);
                List<CsDispatchCapture> captures = transport.Captures();
                capturesForResult = captures;
                List<CsRunLine> runLines = recorder.RunLines();

                // The recording transport has the same wall deadline as this loop. If its timer
                // wins the race by a fraction of a millisecond, the runner sees a timeout failure
                // before the loop can call Stop(). Attribute that abandonment to the same named
                // ceiling so a wall boundary is never misreported as a provider failure.
                bool wallCutByTransport = WallCutByTransport(captures, runWallConstrained,
                    ref wallLimit);

                var contextData = new CsDispatchContext
                {
                    RunId = runId, ScenarioId = spec.ScenarioId, Commit = git.HeadCommit ?? CsGitInfo.BaseCommit,
                    BaseCommit = CsGitInfo.BaseCommit, StudySeat = spec.StudySeat,
                    ProviderKind = provider.Kind, ProviderModel = provider.Model,
                    ProviderLive = provider.IsLive,
                    UsageSource = provider.IsLive ? "provider_reported" : "fake_deterministic",
                    ProviderBaseUrlDisplay = baseUrlDisplay,
                    ProviderEnvVarNames = provider.IsLive
                        ? new List<string>(CsLiveEnvironment.EnvVarNames) : new List<string>(),
                    ProviderMaxOutputTokens = outputTokens,
                    Dirty = git.DirtyFiles.Count > 0, Seed = spec.Seed,
                    ScenarioMaxRequests = effectiveRequests, ScenarioWallSeconds = effectiveWallSeconds,
                    RequestTimeoutMs = spec.RequestTimeoutMs, PromptLimit = CsSettings.PromptLimit(config),
                    LateResponsesDiscarded = result.LateResponsesDiscarded, RunCeilings = runCeilings,
                    SettingsHash = result.SettingsHash, ConfigRedacted = config.Redacted(),
                    Flags = new CsFeatureBlock
                    {
                        Attack = spec.EnableAttack, Research = spec.EnableResearch,
                        ManualLookup = spec.EnableManualLookup, Deliberation = spec.EnableDeliberation,
                    },
                    Captures = captures, RunLines = runLines, Exchanges = recorder.ExchangeLines(),
                    RunFileName = Path.GetFileName(recorder.RunPath ?? runner.RunId + ".jsonl"),
                    ExchangeFileName = Path.GetFileName(recorder.ExchangePath ?? runner.RunId + "-exchanges.ndjson"),
                };
                CsEvidence.WritePrompts(Path.Combine(scenarioDir, "prompts.ndjson"), contextData);
                CsEvidence.WriteDispatches(Path.Combine(scenarioDir, "dispatches.ndjson"), contextData);
                result.Files.Add("scenario.json");
                result.Files.Add("script.json");
                result.Files.Add("settings.json");
                result.Files.Add("prompts.ndjson");
                result.Files.Add("dispatches.ndjson");
                result.Files.Add(contextData.RunFileName);
                result.Files.Add(contextData.ExchangeFileName);

                try
                {
                    string summaryPath = SpectatorSummary.Write(scenarioDir, session, config,
                        transport, runner);
                    // The production summary redacts the config key itself; this second pass runs
                    // the whole file through the same central redaction as every other artefact.
                    if (!string.IsNullOrEmpty(summaryPath) && File.Exists(summaryPath))
                        File.WriteAllText(summaryPath, CsEvidence.Redact(File.ReadAllText(summaryPath)),
                            new UTF8Encoding(false));
                    foreach (string path in Directory.GetFiles(scenarioDir, "pilot-*-summary.json"))
                        result.Files.Add(Path.GetFileName(path));
                }
                catch (Exception) { }

                Classify(result, session, spec, captures, runLines, wallLimit,
                    runRequestConstrained ? "max_run_requests" : "max_scenario_requests",
                    wallCutByTransport);
                result.PrivacyLeaks = PrivacyLeaks(spec, captures);
                WriteJson(scenarioDir, "result.json", ResultObject(result, captures));
                File.WriteAllText(Path.Combine(scenarioDir, spec.ScenarioId + "-report.txt"),
                    CsEvidence.Redact(CsReport.ProviderBanner(
                        provider.Kind, provider.Model, provider.IsLive)
                        + CsReport.ScenarioText(result)), new UTF8Encoding(false));
                result.Files.Add(spec.ScenarioId + "-report.txt");
                result.Files.Add("result.json");
            }
            catch (Exception e)
            {
                result.Status = CsStatus.Failed;
                // Exception text is provider-influenced; redact before it can reach result.json
                // or a report.
                result.Detail = CsEvidence.Redact(e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                // The note/memory stores are written during the session and the production summary
                // pass writes last; sweep every scenario artifact after both, including on failure.
                CsRedactionSweepResult sweep = CsEvidence.RedactDirectory(scenarioDir);
                if (sweep.Failed)
                {
                    CsEvidence.ApplySweepFailure(result, sweep);
                    // The sweep outcome is known only now, so result.json is rewritten here (via
                    // the central redaction) to carry the failed status and bounded detail. This is
                    // the post-sweep write the success path cannot provide.
                    try
                    {
                        WriteJson(scenarioDir, "result.json",
                            ResultObject(result, capturesForResult));
                    }
                    catch (Exception) { }
                }
            }
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }

        /// <summary>
        /// Explicit live authorization and bounded limits. Rejection happens before a scenario
        /// directory exists and before provider.CreateTransport, with the refusal/limit exception
        /// required by the public boundary. The fake path is deliberately unchanged.
        /// </summary>
        internal static void ValidateLiveDispatch(IScenarioProvider provider, bool allowLive,
            CsRunBudget budget, int? maxOutputTokens, Func<string, string> getenv = null)
        {
            if (provider == null || !provider.IsLive) return;
            if (!allowLive) throw new CsLiveRefusedException(CsProviderFactory.LiveRefusal);
            // A live provider override builds its config without the runner's earlier read, so the
            // COMPLETE environment is validated here before any directory or dispatch. Messages
            // are exactly CsLiveEnvironment's variable-name errors; no value is ever included.
            CsLiveEnvironmentResult environment = CsLiveEnvironment.Read(getenv);
            if (!environment.Ok)
                throw new CsLiveConfigException(string.Join("; ", environment.Errors));
            if (budget == null || !budget.IsBounded)
                throw new CsLiveConfigException("live runs require a bounded whole-run budget with "
                    + "a finite positive max_run_requests and max_run_wall_seconds; an unbounded "
                    + "budget is refused");
            if (!maxOutputTokens.HasValue || maxOutputTokens.Value <= 0)
                throw new CsLiveConfigException(
                    "live runs require run_ceilings.max_output_tokens > 0 (there is no 400 default)");
            if (maxOutputTokens.Value > CsLimits.MaxOutputTokensCeiling)
                throw new CsLiveConfigException("live runs require run_ceilings.max_output_tokens <= "
                    + CsLimits.MaxOutputTokensCeiling);
        }
    }
}
