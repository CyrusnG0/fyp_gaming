using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal static partial class PlaygroundSelfTests
    {
        private static void ReplayRuleConsistencyAndProvenanceTests(TestLog log)
        {
            LlmConfig spatialConfig = ProductionConfig();
            spatialConfig.EnableGarrisonConversion = true;
            spatialConfig.SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Rich };

            // 1. Fail closed if bootstrap is built before engine.State.RequireVisibleMarchRoutes is synced.
            var unsyncedEngine = BuildCorridorRouteWorld(ProdSeed + 5, unseenBridgeWalkable: true, sharedVisibleRule: false);
            bool rejectedUnsynced = false;
            try
            {
                ReplayBootstrap.BuildJson(unsyncedEngine, ProdSeed + 5, "run-unsynced", spatialConfig, "rev", "single_llm", 1);
            }
            catch (InvalidOperationException ex)
            {
                rejectedUnsynced = ex.Message.Contains("diverges from engine state");
            }
            log.Check(rejectedUnsynced,
                "fairness-bootstrap: ReplayBootstrap fails closed when visible_march_routes diverges from engine state rule");

            string tempRoot = Path.Combine(Path.GetTempPath(), "luanshi-replay-rule-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                // 2. Non-Unity single-agent and spectator hosts set the engine rule before bootstrap capture,
                //    preserving tactical garrison_conversion flags alongside visible_march_routes.
                var singleEngine = BuildCorridorRouteWorld(ProdSeed + 6, unseenBridgeWalkable: true, sharedVisibleRule: false);
                var singleHost = new SingleLlmTurnHost(singleEngine, spatialConfig, PlaygroundScenario.SeatA,
                    () => singleEngine.Observe(PlaygroundScenario.SeatA), cmd => ValidationResult.Fail("unused"), () => { });
                var singleRec = new LlmRunRecorder(tempRoot);
                singleRec.UseRunId("run-single-sync");
                string singleJson = File.ReadAllText(singleRec.WriteReplayBootstrap(singleEngine, ProdSeed + 6, spatialConfig, "rev", "single_llm", 0));

                var specEngine = BuildCorridorRouteWorld(ProdSeed + 7, unseenBridgeWalkable: true, sharedVisibleRule: false);
                specEngine.State.FindFaction(PlaygroundScenario.SeatA).Controller = ControllerType.Llm;
                var fake = new FakeLlmTransport();
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var specRec = new LlmRunRecorder(tempRoot);
                var runner = new LlmTurnRunner(fake, spatialConfig, specRec);
                var session = new SpectatorSession(specEngine, runner, 1, ProdSeed + 7, specRec);
                string specJson = File.ReadAllText(specRec.WriteReplayBootstrap(specEngine, ProdSeed + 7, spatialConfig, "rev", "spectator", 1));
                session.Start();
                while (session.State == SpectatorState.Running) session.Tick();
                string summaryJson = File.ReadAllText(SpectatorSummary.Write(tempRoot, session, spatialConfig, fake, runner));

                log.Check(singleEngine.State.RequireVisibleMarchRoutes
                          && singleJson.Contains("\"garrison_conversion\":true")
                          && singleJson.Contains("\"visible_march_routes\":true")
                          && singleJson.Contains("\"require_visible_march_routes\":true")
                          && specEngine.State.RequireVisibleMarchRoutes
                          && specJson.Contains("\"garrison_conversion\":true")
                          && specJson.Contains("\"visible_march_routes\":true")
                          && specJson.Contains("\"require_visible_march_routes\":true")
                          && summaryJson.Contains("\"visible_march_routes_enforced\":true"),
                    "fairness-bootstrap: single-agent and spectator hosts sync rule before bootstrap and preserve tactical flags");

                // 3. Old pre-spatial LLM recording: shared engine rule=false, LLM host rule=false.
                ReplayBundle legacyBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: null,
                    bootstrapVisibleMarchRoutes: null,
                    bootstrapStateRequireVisible: null,
                    spatialLevel: null,
                    promptVersion: PromptBuilder.PromptVersion);
                var legacyReplay = new ReplaySession(legacyBundle);
                legacyReplay.RunToCompletion();
                log.Check(!legacyReplay.ReplayedVisibleMarchRoutes
                          && !legacyReplay.ReplayedLlmVisibleMarchRoutes
                          && legacyReplay.EstablishedSharedVisibleMarchRoutes == false
                          && legacyReplay.VisibleMarchRuleProvenance == VisibleMarchRuleProvenance.LegacyUnenforced
                          && legacyReplay.VisibleMarchRuleEstablished,
                    "fairness-replay-legacy: pre-spatial run preserves RequireVisibleMarchRoutes=false");

                // 4. Historical spatial LLM-only run (matches pre-91b2399 c8b0fcfc shape: +spatial,
                //    visible_march_routes_enforced=true, features.visible_march_routes=true, NO initial_world rule):
                //    enforces LLM-host visible routes without claiming shared engine RequireVisibleMarchRoutes=true.
                string histSpatialVersion = PromptBuilder.CombinedVersionFor(true, false, null, false, false, true, true, true, true)
                    + PromptBuilder.GarrisonConversionVersionSuffix;
                ReplayBundle histLlmBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: true,
                    bootstrapVisibleMarchRoutes: true,
                    bootstrapStateRequireVisible: null,
                    spatialLevel: "rich",
                    promptVersion: histSpatialVersion,
                    requests: new List<ReplayRequestRecord>
                    {
                        new ReplayRequestRecord
                        {
                            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                            ControllerType = ControllerType.Llm, Season = 1, TurnToken = 1, RequestIndex = 1, TotalRequests = 2,
                            PromptVersion = histSpatialVersion, ParseOk = true, ResultOk = false, ResultRecorded = true,
                            ResultError = FeedbackPolicy.DestinationNotReachable,
                            RawModelText = "{\"type\":\"march\",\"target_id\":\"cav-replay\",\"param_a\":5,\"param_b\":0}",
                        },
                        EndTurnRecord(histSpatialVersion, ControllerType.Llm, 2, 2),
                    });
                var histLlmReplay = new ReplaySession(histLlmBundle, BuildCorridor(11));
                SetupReplayCorridorUnits(histLlmReplay.Engine);
                histLlmReplay.RunToCompletion();

                log.Check(!histLlmReplay.ReplayedVisibleMarchRoutes
                          && !histLlmReplay.Engine.State.RequireVisibleMarchRoutes
                          && histLlmReplay.ReplayedLlmVisibleMarchRoutes
                          && histLlmReplay.EstablishedSharedVisibleMarchRoutes == null
                          && histLlmReplay.VisibleMarchRuleProvenance == VisibleMarchRuleProvenance.LlmHostOnlyHistorical
                          && histLlmReplay.RejectedActionsReplayed == 1
                          && histLlmReplay.ActionOutcomeMismatches == 0,
                    "fairness-replay-historical-spatial: LLM-only historical spatial run enforces LLM host route without claiming shared engine rule");

                // 5. Historical spatial run with a HUMAN action and no initial_world.require_visible_march_routes:
                //    cannot assume +spatial proved the human engine rule; does not falsely reject human shortcut,
                //    and refuses to claim RawExactMatch / ExactVerificationPassed.
                var histHumanBundle = BuildExactMatchableBundle(
                    tempRoot,
                    includeInitialWorldRule: false,
                    sharedEngineRule: false,
                    spatialLevel: "standard",
                    promptVersion: PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false, true, false),
                    humanShortcutExpectedOk: true);
                var histHumanReplay = new ReplaySession(histHumanBundle, BuildCorridor(11));
                SetupReplayCorridorUnits(histHumanReplay.Engine);
                histHumanReplay.RunToCompletion();

                log.Check(!histHumanReplay.ReplayedVisibleMarchRoutes
                          && histHumanReplay.VisibleMarchRuleProvenance == VisibleMarchRuleProvenance.Unestablished
                          && !histHumanReplay.VisibleMarchRuleEstablished
                          && histHumanReplay.AcceptedActionsReplayed == 2
                          && histHumanReplay.ActionOutcomeMismatches == 0
                          && !histHumanReplay.FinalReport.RawExactMatch
                          && !histHumanReplay.FinalReport.ExactVerificationPassed
                          && histHumanReplay.FinalReport.FormatReport().Contains("Visible March Rule:"),
                    "fairness-replay-historical-human: spatial prompt without initial_world rule does not force human rule or invent verified PASS",
                    $"rev={histHumanReplay.ReplayedVisibleMarchRoutes} prov={histHumanReplay.VisibleMarchRuleProvenance} est={histHumanReplay.VisibleMarchRuleEstablished} acc={histHumanReplay.AcceptedActionsReplayed} mis={histHumanReplay.ActionOutcomeMismatches} raw={histHumanReplay.FinalReport.RawExactMatch} obsMis={histHumanReplay.ObservationHashMismatches} sbExact={histHumanReplay.FinalReport.AllSeasonsScoreboardExactMatch}");

                // 6. New recording loaded from files WITH bootstrap initial_world.require_visible_march_routes=true
                //    and BOTH human (rejected shortcut) and LLM (accepted visible move) actions verifies cleanly.
                var newHumanBundle = BuildExactMatchableBundle(
                    tempRoot,
                    includeInitialWorldRule: true,
                    sharedEngineRule: true,
                    spatialLevel: "rich",
                    promptVersion: PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false, true, false),
                    humanShortcutExpectedOk: false);
                var newHumanReplay = new ReplaySession(newHumanBundle, BuildCorridor(11));
                SetupReplayCorridorUnits(newHumanReplay.Engine);
                newHumanReplay.RunToCompletion();

                log.Check(newHumanReplay.ReplayedVisibleMarchRoutes
                          && newHumanReplay.Engine.State.RequireVisibleMarchRoutes
                          && newHumanReplay.EstablishedSharedVisibleMarchRoutes == true
                          && newHumanReplay.VisibleMarchRuleProvenance == VisibleMarchRuleProvenance.SharedEngineEstablished
                          && newHumanReplay.VisibleMarchRuleEstablished
                          && newHumanReplay.RejectedActionsReplayed == 1
                          && newHumanReplay.AcceptedActionsReplayed == 1
                          && newHumanReplay.ActionOutcomeMismatches == 0
                          && newHumanReplay.FinalReport.RawExactMatch
                          && newHumanReplay.FinalReport.ExactVerificationPassed,
                    "fairness-replay-new-human: bootstrap initial_world rule establishes shared rule across human and LLM actions",
                    $"rev={newHumanReplay.ReplayedVisibleMarchRoutes} prov={newHumanReplay.VisibleMarchRuleProvenance} est={newHumanReplay.VisibleMarchRuleEstablished} rej={newHumanReplay.RejectedActionsReplayed} acc={newHumanReplay.AcceptedActionsReplayed} mis={newHumanReplay.ActionOutcomeMismatches} raw={newHumanReplay.FinalReport.RawExactMatch} obsMis={newHumanReplay.ObservationHashMismatches} sbExact={newHumanReplay.FinalReport.AllSeasonsScoreboardExactMatch}");

                // 7. Divergence detection: summary vs bootstrap disagreement fails loader; accepted unseen march
                //    under initial_world.require_visible_march_routes=true fails replay parity.
                bool loaderCaughtDivergence = false;
                string divDir = Path.Combine(tempRoot, "div");
                Directory.CreateDirectory(divDir);
                string sumDivPath = Path.Combine(divDir, $"pilot-{ReplayLoader.VerifiedDemoRunId}-summary.json");
                string jnlDivPath = Path.Combine(divDir, $"{ReplayLoader.VerifiedDemoRunId}.jsonl");
                string excDivPath = Path.Combine(divDir, $"{ReplayLoader.VerifiedDemoRunId}-exchanges.ndjson");
                string bootDivPath = Path.Combine(divDir, $"{ReplayLoader.VerifiedDemoRunId}-bootstrap.json");

                File.WriteAllText(sumDivPath, "{\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\",\"seed\":12345,\"balance_profile\":\"standard\",\"seasons_played\":1,\"requests\":1,\"visible_march_routes_enforced\":false}");
                File.WriteAllText(jnlDivPath, "{\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\",\"seed\":12345,\"prompt_version\":\"demo-slice-5-persona\",\"seat_id\":\"seat-0\",\"turn_token\":1,\"season\":1,\"request_index\":1,\"total_requests\":1,\"raw_model_text\":\"{\\\"type\\\":\\\"end_turn\\\"}\",\"parse_ok\":true,\"result_ok\":true}\n");
                File.WriteAllText(excDivPath, "{\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":1,\"exchange_id\":\"ex-1\",\"exchange_event\":\"request\",\"request\":{\"user\":\"u\"}}\n{\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":1,\"exchange_id\":\"ex-1\",\"exchange_event\":\"result\",\"response\":{\"content_text\":\"{\\\"type\\\":\\\"end_turn\\\"}\"}}\n");
                File.WriteAllText(bootDivPath, "{\"schema\":\"luanshi-replay-bootstrap/1\",\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\",\"features\":{\"visible_march_routes\":true},\"initial_world\":{\"require_visible_march_routes\":true}}");
                try
                {
                    ReplayLoader.Load(sumDivPath, jnlDivPath, excDivPath);
                }
                catch (InvalidDataException ex)
                {
                    loaderCaughtDivergence = ex.Message.Contains("divergence");
                }

                string spVer = PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false, true, false);
                ReplayBundle marchDivBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: true,
                    bootstrapVisibleMarchRoutes: true,
                    bootstrapStateRequireVisible: true,
                    spatialLevel: "rich",
                    promptVersion: spVer,
                    requests: new List<ReplayRequestRecord>
                    {
                        new ReplayRequestRecord
                        {
                            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                            ControllerType = ControllerType.Llm, Season = 1, TurnToken = 1, RequestIndex = 1, TotalRequests = 2,
                            PromptVersion = spVer, ParseOk = true, ResultOk = true, ResultRecorded = true,
                            RawModelText = "{\"type\":\"march\",\"target_id\":\"cav-replay\",\"param_a\":5,\"param_b\":0}",
                        },
                        EndTurnRecord(spVer, ControllerType.Llm, 2, 2),
                    });
                var divReplay = new ReplaySession(marchDivBundle, BuildCorridor(11));
                SetupReplayCorridorUnits(divReplay.Engine);
                divReplay.RunToCompletion();

                log.Check(loaderCaughtDivergence
                          && divReplay.UnexpectedRejections == 1
                          && divReplay.ActionOutcomeMismatches == 1,
                    "fairness-replay-divergence: loader and ReplaySession catch rule/action divergence");

                // 8. Historical 3:1 city assault replay provenance vs post-removal replay:
                //    - Historical run WITHOUT initial_world.require_assault_troop_ratio preserves 3:1 rule,
                //      so a recorded <3:1 assault rejection still fails with AssaultRatioTooLow (0 mismatches).
                string oldAtkVer = PromptBuilder.LegacyAttackPromptVersion;
                ReplayBundle histAssaultBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: null,
                    bootstrapVisibleMarchRoutes: null,
                    bootstrapStateRequireVisible: null,
                    spatialLevel: null,
                    promptVersion: oldAtkVer,
                    requests: new List<ReplayRequestRecord>
                    {
                        new ReplayRequestRecord
                        {
                            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                            ControllerType = ControllerType.Llm, Season = 1, TurnToken = 1, RequestIndex = 1, TotalRequests = 2,
                            PromptVersion = oldAtkVer, ParseOk = true, ResultOk = false, ResultRecorded = true,
                            ResultError = FeedbackPolicy.AssaultRatioTooLow,
                            RawModelText = "{\"type\":\"attack_city\",\"target_id\":\"inf-replay\",\"param_a\":2,\"param_b\":0}",
                        },
                        EndTurnRecord(oldAtkVer, ControllerType.Llm, 2, 2),
                    });
                var histAssaultReplay = new ReplaySession(histAssaultBundle, BuildCorridor(11));
                SetupReplayAssaultUnits(histAssaultReplay.Engine);
                histAssaultReplay.RunToCompletion();
                log.Check(histAssaultReplay.ReplayedAssaultTroopRatio
                          && histAssaultReplay.Engine.State.RequireAssaultTroopRatio
                          && histAssaultReplay.AssaultTroopRatioProvenance == AssaultTroopRatioRuleProvenance.HistoricalOldRulePreserved
                          && histAssaultReplay.EngineRejectionsReplayed == 1
                          && histAssaultReplay.ActionOutcomeMismatches == 0,
                    "assault-replay-historical: old run without require_assault_troop_ratio preserves 3:1 rejection",
                    $"ratio={histAssaultReplay.ReplayedAssaultTroopRatio} prov={histAssaultReplay.AssaultTroopRatioProvenance} rej={histAssaultReplay.EngineRejectionsReplayed} mis={histAssaultReplay.ActionOutcomeMismatches}");

                //    - New post-removal bundle WITH require_assault_troop_ratio=false and -2 prompt accepts <3:1 assault.
                string newAtkVer = PromptBuilder.AttackPromptVersion;
                ReplayBundle newAssaultBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: null,
                    bootstrapVisibleMarchRoutes: null,
                    bootstrapStateRequireVisible: null,
                    spatialLevel: null,
                    promptVersion: newAtkVer,
                    requests: new List<ReplayRequestRecord>
                    {
                        new ReplayRequestRecord
                        {
                            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                            ControllerType = ControllerType.Llm, Season = 1, TurnToken = 1, RequestIndex = 1, TotalRequests = 2,
                            PromptVersion = newAtkVer, ParseOk = true, ResultOk = true, ResultRecorded = true,
                            RawModelText = "{\"type\":\"attack_city\",\"target_id\":\"inf-replay\",\"param_a\":2,\"param_b\":0}",
                        },
                        EndTurnRecord(newAtkVer, ControllerType.Llm, 2, 2),
                    });
                newAssaultBundle.BootstrapAssaultTroopRatio = false;
                newAssaultBundle.BootstrapStateRequireAssaultTroopRatio = false;
                newAssaultBundle.Summary.AssaultTroopRatioEnforced = false;
                var newAssaultReplay = new ReplaySession(newAssaultBundle, BuildCorridor(11));
                SetupReplayAssaultUnits(newAssaultReplay.Engine);
                newAssaultReplay.RunToCompletion();
                log.Check(!newAssaultReplay.ReplayedAssaultTroopRatio
                          && !newAssaultReplay.Engine.State.RequireAssaultTroopRatio
                          && newAssaultReplay.AssaultTroopRatioProvenance == AssaultTroopRatioRuleProvenance.Established
                          && newAssaultReplay.AcceptedActionsReplayed == 1
                          && newAssaultReplay.ActionOutcomeMismatches == 0,
                    "assault-replay-new: post-removal bootstrap replays below-3:1 assault as accepted",
                    $"ratio={newAssaultReplay.ReplayedAssaultTroopRatio} prov={newAssaultReplay.AssaultTroopRatioProvenance} acc={newAssaultReplay.AcceptedActionsReplayed} mis={newAssaultReplay.ActionOutcomeMismatches}");

                //    - Rule divergence fail-closed: require_assault_troop_ratio=false paired with legacy -1 prompt throws.
                bool caughtRatioDivergence = false;
                ReplayBundle badRatioBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: null, bootstrapVisibleMarchRoutes: null, bootstrapStateRequireVisible: null,
                    spatialLevel: null, promptVersion: oldAtkVer,
                    requests: new List<ReplayRequestRecord> { EndTurnRecord(oldAtkVer, ControllerType.Llm, 1, 1) });
                badRatioBundle.BootstrapStateRequireAssaultTroopRatio = false;
                try { new ReplaySession(badRatioBundle); }
                catch (InvalidOperationException ex) { caughtRatioDivergence = ex.Message.Contains("diverges"); }
                log.Check(caughtRatioDivergence,
                    "assault-replay-divergence: rule/prompt mismatch fails closed");

                // 9. Action-reference identities (+actions-1, optionally before +garrison-conversion-2) must
                //    resolve the same assault rule, features and season summary as the bare version.
                string refTail = PromptBuilder.ActionReferenceVersionSuffix + PromptBuilder.GarrisonConversionVersionSuffix;
                string oldRefVer = oldAtkVer + refTail;
                ReplayBundle histRefBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: null, bootstrapVisibleMarchRoutes: null, bootstrapStateRequireVisible: null,
                    spatialLevel: null, promptVersion: oldRefVer,
                    requests: new List<ReplayRequestRecord>
                    {
                        new ReplayRequestRecord
                        {
                            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                            ControllerType = ControllerType.Llm, Season = 1, TurnToken = 1, RequestIndex = 1, TotalRequests = 2,
                            PromptVersion = oldRefVer, ParseOk = true, ResultOk = false, ResultRecorded = true,
                            ResultError = FeedbackPolicy.AssaultRatioTooLow,
                            RawModelText = "{\"type\":\"attack_city\",\"target_id\":\"inf-replay\",\"param_a\":2,\"param_b\":0}",
                        },
                        EndTurnRecord(oldRefVer, ControllerType.Llm, 2, 2),
                    });
                var histRefReplay = new ReplaySession(histRefBundle, BuildCorridor(11));
                SetupReplayAssaultUnits(histRefReplay.Engine);
                histRefReplay.RunToCompletion();
                log.Check(histRefReplay.ReplayedAssaultTroopRatio
                          && histRefReplay.AssaultTroopRatioProvenance == AssaultTroopRatioRuleProvenance.HistoricalOldRulePreserved
                          && histRefReplay.EngineRejectionsReplayed == 1
                          && histRefReplay.ActionOutcomeMismatches == 0,
                    "assault-replay-actions-historical: a legacy identity with +actions-1 still replays the 3:1 rejection",
                    $"ratio={histRefReplay.ReplayedAssaultTroopRatio} prov={histRefReplay.AssaultTroopRatioProvenance} rej={histRefReplay.EngineRejectionsReplayed} mis={histRefReplay.ActionOutcomeMismatches}");

                string newRefVer = newAtkVer + refTail;
                ReplayBundle newRefBundle = BuildRuleReplayBundle(
                    visibleMarchRoutesEnforced: null, bootstrapVisibleMarchRoutes: null, bootstrapStateRequireVisible: null,
                    spatialLevel: null, promptVersion: newRefVer,
                    requests: new List<ReplayRequestRecord>
                    {
                        new ReplayRequestRecord
                        {
                            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                            ControllerType = ControllerType.Llm, Season = 1, TurnToken = 1, RequestIndex = 1, TotalRequests = 2,
                            PromptVersion = newRefVer, ParseOk = true, ResultOk = true, ResultRecorded = true,
                            RawModelText = "{\"type\":\"attack_city\",\"target_id\":\"inf-replay\",\"param_a\":2,\"param_b\":0}",
                        },
                        EndTurnRecord(newRefVer, ControllerType.Llm, 2, 2),
                    });
                var newRefReplay = new ReplaySession(newRefBundle, BuildCorridor(11));
                SetupReplayAssaultUnits(newRefReplay.Engine);
                newRefReplay.RunToCompletion();
                log.Check(!newRefReplay.ReplayedAssaultTroopRatio
                          && newRefReplay.AssaultTroopRatioProvenance != AssaultTroopRatioRuleProvenance.HistoricalOldRulePreserved
                          && newRefReplay.AcceptedActionsReplayed == 1
                          && newRefReplay.ActionOutcomeMismatches == 0,
                    "assault-replay-actions-new: a post-removal identity with +actions-1 replays the below-3:1 assault as accepted",
                    $"ratio={newRefReplay.ReplayedAssaultTroopRatio} prov={newRefReplay.AssaultTroopRatioProvenance} acc={newRefReplay.AcceptedActionsReplayed} mis={newRefReplay.ActionOutcomeMismatches}");

                // Season summary: the attack order is parsed as an attack (not counted invalid) purely from the identity.
                var refSummary = ReplaySeasonSummaryBuilder.Build(newRefBundle);
                ReplaySideSeasonSummary refSide = refSummary.Count > 0 ? (refSummary[0].Wei ?? refSummary[0].Shu) : null;
                log.Check(refSide != null && refSide.InvalidRequests == 0 && refSide.AcceptedActions == 1,
                    "assault-replay-actions-summary: +actions-1 identity does not turn a recorded attack into an invalid request",
                    refSide == null ? "no summary" : $"accepted={refSide.AcceptedActions} invalid={refSide.InvalidRequests}");

                //    - Genuinely unknown provenance (no assault wording, no recorded rule) is not silently assumed.
                string bareRefVer = PromptBuilder.PromptVersion + PromptBuilder.ActionReferenceVersionSuffix;
                log.Check(!PromptBuilder.HasLegacyAssaultRatioPromptVersion(bareRefVer)
                          && !PromptBuilder.HasNoAssaultRatioPromptVersion(bareRefVer),
                    "assault-replay-actions-unknown: an identity that names no assault rule claims neither rule");
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static ReplayBundle BuildExactMatchableBundle(string rootDir, bool includeInitialWorldRule,
            bool sharedEngineRule, string spatialLevel, string promptVersion, bool humanShortcutExpectedOk)
        {
            string sub = Path.Combine(rootDir, includeInitialWorldRule ? "new_human" : "hist_human");
            Directory.CreateDirectory(sub);

            // Precompute exact observation hashes and season-1 scoreboard from the actual deterministic world.
            var simBundle = BuildRuleReplayBundle(true, true, includeInitialWorldRule ? (bool?)sharedEngineRule : null,
                spatialLevel, promptVersion, new List<ReplayRequestRecord>());
            var sim = new ReplaySession(simBundle, BuildCorridor(11));
            SetupReplayCorridorUnits(sim.Engine);
            sim.Engine.State.RequireVisibleMarchRoutes = sharedEngineRule;
            int tok = sim.Engine.BeginSeatTurn("seat-0");

            string h1 = ComputeObsHash(sim.Engine.Observe("seat-0"));
            sim.Engine.State.FindFaction("seat-0").Controller = ControllerType.Human;
            var cmdHuman = new ActionCommand
            {
                Type = ActionType.March,
                ActorSeatId = "seat-0",
                ControllerType = ControllerType.Human,
                TargetId = "cav-replay",
                ParamA = 5,
                ParamB = 0,
            };
            ValidationResult r1 = sim.Engine.SubmitAs("seat-0", tok, cmdHuman);
            if (r1.Ok != humanShortcutExpectedOk)
                throw new InvalidOperationException("Unexpected human shortcut outcome in test fixture.");

            string h2 = ComputeObsHash(sim.Engine.Observe("seat-0"));
            sim.Engine.State.FindFaction("seat-0").Controller = ControllerType.Llm;
            var cmdLlm = new ActionCommand
            {
                Type = ActionType.March,
                ActorSeatId = "seat-0",
                ControllerType = ControllerType.Llm,
                TargetId = "cav-llm",
                ParamA = 2,
                ParamB = 0,
            };
            ValidationResult r2 = sim.Engine.SubmitAs("seat-0", tok, cmdLlm);
            if (!r2.Ok)
                throw new InvalidOperationException("Unexpected LLM visible move failure: " + r2.Error);

            string h3 = ComputeObsHash(sim.Engine.Observe("seat-0"));
            sim.Engine.EndSeatTurn(tok);
            sim.Engine.EndSeason();
            var finalScores = sim.CurrentScores();

            string sumPath = Path.Combine(sub, $"pilot-{ReplayLoader.VerifiedDemoRunId}-summary.json");
            string jnlPath = Path.Combine(sub, $"{ReplayLoader.VerifiedDemoRunId}.jsonl");
            string excPath = Path.Combine(sub, $"{ReplayLoader.VerifiedDemoRunId}-exchanges.ndjson");
            string bootPath = Path.Combine(sub, $"{ReplayLoader.VerifiedDemoRunId}-bootstrap.json");

            ReplaySeatScore wei = finalScores[0], shu = finalScores[1];
            File.WriteAllText(sumPath,
                "{\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\",\"seed\":12345,\"balance_profile\":\"standard\""
                + ",\"seasons_played\":1,\"requests\":3,\"visible_march_routes_enforced\":true"
                + ",\"spatial_context_level\":" + MiniJson.Str(spatialLevel)
                + ",\"seasons\":[{\"season\":1,\"scoreboard\":["
                + ScoreJson(wei) + "," + ScoreJson(shu) + "]}]}");

            File.WriteAllText(bootPath,
                "{\"schema\":\"luanshi-replay-bootstrap/1\",\"run_id\":\"" + ReplayLoader.VerifiedDemoRunId + "\""
                + ",\"features\":{\"visible_march_routes\":true}"
                + (includeInitialWorldRule
                    ? ",\"initial_world\":{\"require_visible_march_routes\":" + (sharedEngineRule ? "true" : "false") + ",\"require_assault_troop_ratio\":false}"
                    : ",\"initial_world\":{\"season\":1}")
                + "}");

            string humanCmdJson = "{\"type\":\"march\",\"target_id\":\"cav-replay\",\"param_a\":5,\"param_b\":0,\"controller_type\":\"human\"}";
            string llmRaw = "{\"type\":\"march\",\"target_id\":\"cav-llm\",\"param_a\":2,\"param_b\":0}";
            string endRaw = "{\"type\":\"end_turn\"}";

            var jnl = new StringBuilder();
            jnl.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seed\":12345")
               .Append(",\"prompt_version\":").Append(MiniJson.Str(promptVersion))
               .Append(",\"seat_id\":\"seat-0\",\"controller_type\":\"human\",\"turn_token\":1,\"season\":1,\"request_index\":1,\"total_requests\":3")
               .Append(",\"observation_hash\":").Append(MiniJson.Str(h1))
               .Append(",\"command_json\":").Append(MiniJson.Str(humanCmdJson))
               .Append(",\"parse_ok\":true,\"result_ok\":").Append(humanShortcutExpectedOk ? "true" : "false")
               .Append(",\"result_error\":").Append(MiniJson.Str(r1.Error)).Append("}\n");
            jnl.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seed\":12345")
               .Append(",\"prompt_version\":").Append(MiniJson.Str(promptVersion))
               .Append(",\"seat_id\":\"seat-0\",\"controller_type\":\"llm\",\"turn_token\":1,\"season\":1,\"request_index\":2,\"total_requests\":3")
               .Append(",\"observation_hash\":").Append(MiniJson.Str(h2))
               .Append(",\"raw_model_text\":").Append(MiniJson.Str(llmRaw))
               .Append(",\"parse_ok\":true,\"result_ok\":true}\n");
            jnl.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seed\":12345")
               .Append(",\"prompt_version\":").Append(MiniJson.Str(promptVersion))
               .Append(",\"seat_id\":\"seat-0\",\"controller_type\":\"llm\",\"turn_token\":1,\"season\":1,\"request_index\":3,\"total_requests\":3")
               .Append(",\"observation_hash\":").Append(MiniJson.Str(h3))
               .Append(",\"raw_model_text\":").Append(MiniJson.Str(endRaw))
               .Append(",\"parse_ok\":true,\"result_ok\":true}\n");
            File.WriteAllText(jnlPath, jnl.ToString());

            var exc = new StringBuilder();
            exc.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":2,\"exchange_id\":\"ex-2\",\"exchange_event\":\"request\",\"request\":{\"user\":\"u\"}}\n");
            exc.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":2,\"exchange_id\":\"ex-2\",\"exchange_event\":\"result\",\"response\":{\"content_text\":").Append(MiniJson.Str(llmRaw)).Append("}}\n");
            exc.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":3,\"exchange_id\":\"ex-3\",\"exchange_event\":\"request\",\"request\":{\"user\":\"u\"}}\n");
            exc.Append("{\"run_id\":\"").Append(ReplayLoader.VerifiedDemoRunId).Append("\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":3,\"exchange_id\":\"ex-3\",\"exchange_event\":\"result\",\"response\":{\"content_text\":").Append(MiniJson.Str(endRaw)).Append("}}\n");
            File.WriteAllText(excPath, exc.ToString());

            return ReplayLoader.Load(sumPath, jnlPath, excPath);
        }

        private static string ScoreJson(ReplaySeatScore s)
            => "{\"seat_id\":" + MiniJson.Str(s.SeatId)
               + ",\"faction\":" + MiniJson.Str(s.Faction)
               + ",\"gold\":" + s.Gold
               + ",\"food\":" + s.Food
               + ",\"population\":" + s.Population
               + ",\"garrison\":" + s.Garrison
               + ",\"cities\":" + s.Cities + "}";

        private static string ComputeObsHash(Observation obs)
        {
            string json = PromptBuilder.BoundedObservationJson(obs);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json ?? ""));
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void SetupReplayCorridorUnits(GameEngine engine)
        {
            engine.State.Cities.Clear();
            engine.State.WatchTowers.Clear();
            engine.State.Armies.Clear();
            engine.State.Cities.Add(new CityState { Id = "cw", Name = "W", X = 0, Z = 0, OwnerSeatId = "seat-0", Population = 3000, Food = 300, Gold = 300 });
            engine.State.Cities.Add(new CityState { Id = "ce", Name = "E", X = 8, Z = 0, OwnerSeatId = "seat-0", Population = 3000, Food = 300, Gold = 300 });
            engine.State.Armies.Add(new ArmyState { Id = "cav-replay", Name = "C", OwnerSeatId = "seat-0", Troops = 1000, UnitType = UnitTypes.Cavalry, X = 1, Z = 0 });
            engine.State.Armies.Add(new ArmyState { Id = "cav-llm", Name = "L", OwnerSeatId = "seat-0", Troops = 1000, UnitType = UnitTypes.Cavalry, X = 1, Z = 0 });
        }

        private static void SetupReplayAssaultUnits(GameEngine engine)
        {
            engine.State.Cities.Clear();
            engine.State.WatchTowers.Clear();
            engine.State.Armies.Clear();
            engine.State.Cities.Add(new CityState { Id = "cw", Name = "W", X = 0, Z = 0, OwnerSeatId = "seat-0", Population = 3000, Food = 300, Gold = 300 });
            engine.State.Cities.Add(new CityState { Id = "ce", Name = "E", X = 2, Z = 0, OwnerSeatId = "seat-1", Population = 3000, Food = 300, Gold = 300, Garrison = 1000 });
            engine.State.Armies.Add(new ArmyState { Id = "inf-replay", Name = "I", OwnerSeatId = "seat-0", Troops = 1500, UnitType = UnitTypes.Infantry, X = 1, Z = 0 });
        }

        private static ReplayRequestRecord EndTurnRecord(string promptVersion, string controllerType, int reqIdx, int total)
            => new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId,
                Seed = 12345,
                SeatId = "seat-0",
                ControllerType = controllerType,
                Season = 1,
                TurnToken = 1,
                RequestIndex = reqIdx,
                TotalRequests = total,
                PromptVersion = promptVersion,
                ParseOk = true,
                ResultOk = true,
                ResultRecorded = true,
                RawModelText = "{\"type\":\"end_turn\"}",
            };

        private static ReplayBundle BuildRuleReplayBundle(bool? visibleMarchRoutesEnforced,
            bool? bootstrapVisibleMarchRoutes, bool? bootstrapStateRequireVisible,
            string spatialLevel, string promptVersion, IReadOnlyList<ReplayRequestRecord> requests = null)
        {
            return new ReplayBundle
            {
                Summary = new ReplaySummaryData
                {
                    RunId = ReplayLoader.VerifiedDemoRunId,
                    Seed = 12345,
                    BalanceProfileKey = "standard",
                    SeasonsPlayed = 1,
                    TotalRequests = requests != null ? requests.Count : 1,
                    VisibleMarchRoutesEnforced = visibleMarchRoutesEnforced,
                    SpatialContextLevel = spatialLevel,
                },
                BootstrapVisibleMarchRoutes = bootstrapVisibleMarchRoutes,
                BootstrapStateRequireVisibleMarchRoutes = bootstrapStateRequireVisible,
                Requests = requests ?? new List<ReplayRequestRecord> { EndTurnRecord(promptVersion, ControllerType.Llm, 1, 1) },
            };
        }
    }
}
