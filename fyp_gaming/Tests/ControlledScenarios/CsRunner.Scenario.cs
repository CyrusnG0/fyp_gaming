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
        public static CsScenarioResult RunScenario(CsScenarioSpec spec, string runDirectory,
            string providerModel, CsGitInfo git, string runId, CsRunBudget budget = null,
            string providerKind = "fake", CsRunCeilings runCeilings = null)
        {
            string scenarioDir = Path.Combine(runDirectory, spec.ScenarioId);
            Directory.CreateDirectory(scenarioDir);
            var result = new CsScenarioResult
            {
                ScenarioId = spec.ScenarioId, Seed = spec.Seed, StudySeat = spec.StudySeat,
                Directory = scenarioDir, Status = CsStatus.Failed, SeasonsPlanned = spec.Seasons,
            };
            var clock = Stopwatch.StartNew();
            budget ??= CsRunBudget.Unbounded();
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
                File.WriteAllText(Path.Combine(scenarioDir, "script.json"), scriptJson, new UTF8Encoding(false));
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

                IScenarioProvider provider = CsProviderFactory.Create(providerKind, providerModel);
                LlmConfig config = CsSettings.Build(spec, provider.Model, effectiveRequests, effectiveWallSeconds);
                result.SettingsHash = CsSettings.Hash(spec, config);
                WriteJson(scenarioDir, "settings.json", new Dictionary<string, object>
                {
                    { "scenario_id", spec.ScenarioId }, { "seed", spec.Seed },
                    { "settings_hash", result.SettingsHash },
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
                            { "declared_max_requests", spec.MaxRequests },
                            { "declared_max_wall_seconds", spec.MaxWallSeconds },
                        } },
                });

                ILlmMemoryStore memory = spec.EnableDeliberation
                    ? new LocalLlmMemoryStore(Path.Combine(scenarioDir, "memory")) : null;
                ILlmStrategicNoteStore notes = spec.EnableDeliberation
                    ? new LocalLlmStrategicNoteStore(Path.Combine(scenarioDir, "notes.jsonl")) : null;

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
                    seat => CsWorld.Capture(engine, seat, config, false));

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
                List<CsRunLine> runLines = recorder.RunLines();

                // The recording transport has the same wall deadline as this loop. If its timer
                // wins the race by a fraction of a millisecond, the runner sees a timeout failure
                // before the loop can call Stop(). Attribute that abandonment to the same named
                // ceiling so a wall boundary is never misreported as a provider failure.
                bool wallCutByTransport = false;
                if (wallLimit == null)
                {
                    foreach (CsDispatchCapture cap in captures)
                    {
                        if (cap.State == "deadline_abandoned")
                            wallLimit = runWallConstrained
                                ? "max_run_wall_seconds" : "max_scenario_wall_seconds";
                        else if (cap.RefusalReason == "max_run_wall_seconds"
                            || cap.RefusalReason == "max_scenario_wall_seconds")
                            wallLimit = cap.RefusalReason;
                        else continue;
                        wallCutByTransport = true;
                        break;
                    }
                }

                var contextData = new CsDispatchContext
                {
                    RunId = runId, ScenarioId = spec.ScenarioId, Commit = git.HeadCommit ?? CsGitInfo.BaseCommit,
                    BaseCommit = CsGitInfo.BaseCommit, StudySeat = spec.StudySeat,
                    ProviderKind = provider.Kind, ProviderModel = provider.Model,
                    ProviderLive = provider.IsLive,
                    UsageSource = provider.IsLive ? "provider_reported" : "fake_deterministic",
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
                    SpectatorSummary.Write(scenarioDir, session, config, transport, runner);
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
                    CsReport.ScenarioText(result), new UTF8Encoding(false));
                result.Files.Add(spec.ScenarioId + "-report.txt");
                result.Files.Add("result.json");
            }
            catch (Exception e)
            {
                result.Status = CsStatus.Failed;
                result.Detail = e.GetType().Name + ": " + e.Message;
            }
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }
    }
}
