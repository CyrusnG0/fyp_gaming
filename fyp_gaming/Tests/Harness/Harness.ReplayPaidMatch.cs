using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class PaidShapeTransport : ILlmTransport
    {
        private readonly Queue<string> _replies = new Queue<string>();
        private int _calls;
        private readonly int? _failOnCall;
        public string Provider => "fake";
        public string Model => "fake-paid-shape";
        public PaidShapeTransport(IEnumerable<string> replies, int? failOnCall = null)
        {
            foreach (var r in replies) _replies.Enqueue(r);
            _failOnCall = failOnCall;
        }
        public System.Threading.Tasks.Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            _calls++;
            if (_failOnCall.HasValue && _calls == _failOnCall.Value)
                return System.Threading.Tasks.Task.FromResult(LlmResponse.Failure(LlmErrorKind.Network, "transport failure", 5));
            string reply = _replies.Count > 0 ? _replies.Dequeue() : "{\"type\":\"end_turn\",\"summary\":\"done\"}";
            int inTok = ((req.System?.Length ?? 0) + (req.User?.Length ?? 0)) / 4 + 10;
            int outTok = reply.Length / 4 + 5;
            return System.Threading.Tasks.Task.FromResult(
                LlmResponse.Success(reply, 5, inTok, outTok, "fake-" + _calls, "stop", null, inTok + outTok, null, null, 200));
        }
    }

    private static GameEngine BuildPaidShapeEngine(int seed, string gameId)
    {
        var engine = new GameEngine(seed, gameId, BalanceProfile.Standard);
        engine.State.Map = ReplaySession.BuildDefaultStaggeredHexMap(26, 17);
        engine.State.Factions.Add(new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm, Prestige = 50, ResearchPoints = 25 });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm, Prestige = 50, ResearchPoints = 25 });
        var c0 = new CityState { Id = "city-wei", Name = "Wei Capital", X = 3, Z = 8, OwnerSeatId = "seat-0", Population = 10000, Food = 500, Gold = 300, Garrison = 500 };
        var c1 = new CityState { Id = "city-shu", Name = "Chengdu", X = 22, Z = 8, OwnerSeatId = "seat-1", Population = 10000, Food = 500, Gold = 300, Garrison = 500 };
        engine.State.Cities.Add(c0);
        engine.State.Cities.Add(c1);
        engine.State.Cities.Add(new CityState { Id = "city-south", Name = "Southern City", X = 12, Z = 2, OwnerSeatId = null, Population = 3000, Food = 200, Gold = 100 });
        engine.State.Armies.Add(new ArmyState { Id = "army-wei-1", Name = "Wei Army", OwnerSeatId = "seat-0", Troops = 2000, X = 2, Z = 6 });
        engine.State.Armies.Add(new ArmyState { Id = "army-shu-1", Name = "Shu Army", OwnerSeatId = "seat-1", Troops = 2000, X = 23, Z = 6 });
        engine.BeginSeason();
        return engine;
    }

    private static void RunReplayPaidMatch()
    {
        Section("[REPLAY-PAID] bootstrap verification, fail-closed checks, and paid run-20261122140759713-c8b0fcfc");
        string root = Path.Combine(Path.GetTempPath(), "luanshi-replay-paid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // 1. Self-contained synthetic run matching paid-run schema (bootstrap + rich + inquiry + research
            //    + diplomacy + garrison + invalid parse + network failure with content_text:null + incomplete season).
            string cfgPath = Path.Combine(root, "cfg.json");
            File.WriteAllText(cfgPath,
                "{\"provider\":\"fake\",\"enable_deliberation\":true,\"experiment_profile\":\"rich-deliberation-v1\","
                + "\"enable_attack\":true,\"enable_research\":true,\"enable_diplomacy\":true,\"enable_garrison_conversion\":true,"
                + "\"visible_march_routes\":true,\"spatial_context\":\"rich\","
                + "\"max_requests_per_turn\":6,\"max_total_requests\":40,\"max_actions_per_turn\":4,\"max_accepted_actions_per_session\":30,"
                + "\"max_inquiries_per_turn\":3,\"max_inquiries_per_session\":10,\"max_turn_seconds\":120,\"max_session_seconds\":600,"
                + "\"request_timeout_ms\":30000,\"max_output_tokens\":400,\"max_session_input_tokens\":1000000,\"max_session_output_tokens\":100000,"
                + "\"max_prompt_chars\":80000}");
            LlmConfig cfg = LlmConfig.LoadFromFile(cfgPath);
            var rec = new LlmRunRecorder(root);
            var eng = BuildPaidShapeEngine(12345, "paid-shape-game");
            // Intentionally send an invalid JSON reply (call 4) that the live parser rejects, plus network failure (call 7).
            var replies = new[]
            {
                "{\"tool\":\"manual_lookup\",\"topic\":\"research\"}",
                "{\"type\":\"research\",\"args\":{\"line\":\"agri\"}}",
                "{\"type\":\"convert_garrison\",\"target_id\":\"city-wei\",\"param_a\":100}",
                "{\"type\":\"propose_treaty\",\"target_id\":\"seat-1\",\"args\":{\"treaty_type\":\"nap\",\"duration\":\"6\"}}",
                "{\"type\":\"end_turn\",\"summary\":\"s1 wei done\"}",
                "{\"type\":\"reclaim\",\"target_id\":\"city-shu\"}",
                "{\"type\":\"end_turn\",\"summary\":\"s1 shu done\"}",
                "{\"type\":\"fortify\",\"target_id\":\"city-wei\"}",
                "{\"type\":\"end_turn\",\"summary\":\"s2 wei network fail\"}", // replaced by network failure on call 9
            };
            var transport = new PaidShapeTransport(replies, failOnCall: 9);
            var runner = new LlmTurnRunner(transport, cfg, rec, null, new MemoryLlmStrategicNoteStore());
            var session = new SpectatorSession(eng, runner, 3, 12345, rec);
            string bootPath = rec.WriteReplayBootstrap(eng, 12345, cfg, "test", "spectator", 3);
            string runId = runner.RunId;
            session.Start();
            while (session.State == SpectatorState.Running)
            {
                session.Tick();
                Thread.Sleep(1);
            }
            string sumPath = SpectatorSummary.Write(root, session, cfg, transport, runner);

            Check(ReplayLoader.TryResolveRunFiles(runId, root, out string rs, out string rj, out string re, out string rb, out string rErr)
                && rb == bootPath,
                $"[REPLAY-PAID] resolved arbitrary safe runId '{runId}' with bootstrap (err={rErr ?? "none"})");

            ReplayBundle synBundle = ReplayLoader.Load(rs, rj, re, runId, rb);
            Check(synBundle.Bootstrap != null && synBundle.Requests.Count == 9 && synBundle.SetupSource == "bootstrap",
                $"[REPLAY-PAID] synthetic paid-shape bundle loaded 9 requests incl. network failure exchange (count={synBundle.Requests.Count})");

            var synReplay = new ReplaySession(synBundle);
            synReplay.RunToCompletion();
            bool feedHasProviderFailure = synReplay.EventFeed.Count == 9
                && synReplay.EventFeed[8].ResultText == "provider failure"
                && synReplay.EventFeed[8].ActionSummary.Contains("Provider failure (Network)");
            Check(synReplay.IsCompleted && !synReplay.HasDivergence && synReplay.ObservationHashesMatched == 9
                && synReplay.AcceptedActionsReplayed == 5 && synReplay.RejectedActionsReplayed == 1
                && synReplay.ProviderFailuresReplayed == 1 && synReplay.InvalidResponsesReplayed == 0
                && synReplay.EngineRejectionsReplayed == 0
                && feedHasProviderFailure
                && ReplaySession.DescribeRequestAction(synBundle.Requests[8]).Contains("Provider failure (Network)")
                && synReplay.ReplayedAssaultTroopRatio == false
                && synReplay.AssaultTroopRatioProvenance == AssaultTroopRatioRuleProvenance.Established
                && synReplay.VoluntaryEndTurnsReplayed == 2 && synReplay.ActionOutcomeMismatches == 0
                && synReplay.IsIncompleteRun && synReplay.FinalReport.AllSeasonsScoreboardExactMatch && synReplay.FinalReport.RawExactMatch,
                $"[REPLAY-PAID] synthetic run exact parity: obs={synReplay.ObservationHashesMatched}/9 acc={synReplay.AcceptedActionsReplayed}/5 provErr={synReplay.ProviderFailuresReplayed}/1 mis={synReplay.ActionOutcomeMismatches} inc={synReplay.IsIncompleteRun} sbExact={synReplay.FinalReport?.AllSeasonsScoreboardExactMatch}");

            // Fail-closed security / integrity checks on the synthetic bundle:
            bool missingBootThrew = false;
            File.Move(bootPath, bootPath + ".bak");
            try { ReplayLoader.Load(rs, rj, re, runId); }
            catch (FileNotFoundException) { missingBootThrew = true; }
            finally { File.Move(bootPath + ".bak", bootPath); }
            Check(missingBootThrew, "[REPLAY-PAID] non-legacy run without bootstrap.json fails closed");

            string tamperedBoot = Path.Combine(root, runId + "-tampered-boot.json");
            File.WriteAllText(tamperedBoot, File.ReadAllText(bootPath).Replace("\"name\":\"Wei Capital\"", "\"name\":\"Tampered\""));
            bool hashMismatchThrew = false;
            try { ReplayLoader.Load(rs, rj, re, runId, tamperedBoot); }
            catch (InvalidDataException ex) { hashMismatchThrew = ex.Message.Contains("sha256 mismatch"); }
            Check(hashMismatchThrew, "[REPLAY-PAID] tampered initial_world hash is rejected");

            string badExch = Path.Combine(root, runId + "-bad-ex.ndjson");
            var exLines = new List<string>(File.ReadAllLines(re));
            int lastResultIdx = exLines.FindLastIndex(l => l.Contains("\"exchange_event\":\"result\"", StringComparison.Ordinal));
            if (lastResultIdx >= 0) exLines.RemoveAt(lastResultIdx);
            File.WriteAllLines(badExch, exLines);
            bool incompleteExThrew = false;
            try { ReplayLoader.Load(rs, rj, badExch, runId, bootPath); }
            catch (InvalidDataException) { incompleteExThrew = true; }
            Check(incompleteExThrew, "[REPLAY-PAID] missing exchange result event for failed request fails closed");

            // 2. Actual paid run run-20261122140759713-c8b0fcfc
            string paidRunId = "run-20261122140759713-c8b0fcfc";
            if (ReplayLoader.TryResolveRunFiles(paidRunId, null, out string ps, out string pj, out string pe, out string pb, out _))
            {
                ReplayBundle paidBundle = ReplayLoader.Load(ps, pj, pe, paidRunId, pb);
                Check(paidBundle.Bootstrap != null && paidBundle.Requests.Count == 37
                    && paidBundle.SetupSource == "bootstrap" && paidBundle.MapSource == "bootstrap",
                    $"[REPLAY-PAID] real paid run loaded: 37 reqs, setup={paidBundle.SetupSource}, map={paidBundle.MapSource}");

                // Run diagnostic replay first (PauseOnDivergence=false) to inspect exact hashes & actions end-to-end.
                var diagPaid = new ReplaySession(paidBundle) { PauseOnDivergence = false };
                diagPaid.RunToCompletion();
                var rpt = diagPaid.FinalReport;
                Console.WriteLine($"  [PAID-RUN diag] obs={rpt.ObservationHashesMatched}/{rpt.TotalRequests} mismatch={rpt.ObservationHashMismatches} "
                    + $"acc={rpt.AcceptedActionsReplayed}/{rpt.ExpectedAcceptedActions} rej={rpt.RejectedActionsReplayed}/{rpt.ExpectedRejectedActions} "
                    + $"endTurns={rpt.VoluntaryEndTurnsReplayed}/{rpt.ExpectedVoluntaryEndTurns} actMismatch={rpt.ActionOutcomeMismatches} "
                    + $"incomplete={rpt.IsIncompleteRun} stop='{rpt.StopReason}' prov={diagPaid.VisibleMarchRuleProvenance} "
                    + $"engineVis={diagPaid.ReplayedVisibleMarchRoutes} llmVis={diagPaid.ReplayedLlmVisibleMarchRoutes} "
                    + $"sbExact={rpt.AllSeasonsScoreboardExactMatch} rawExact={rpt.RawExactMatch}");
                if (rpt.ObservationHashDiscrepancies != null && rpt.ObservationHashDiscrepancies.Count > 0)
                    foreach (var d in rpt.ObservationHashDiscrepancies) Console.WriteLine("    [PAID-RUN obs-diff] " + d);
                if (rpt.RawScoreboardMismatches != null && rpt.RawScoreboardMismatches.Count > 0)
                    foreach (var m in rpt.RawScoreboardMismatches) Console.WriteLine("    [PAID-RUN sb-diff] " + m);
                if (rpt.ActionDiscrepancies != null && rpt.ActionDiscrepancies.Count > 0)
                    foreach (var ad in rpt.ActionDiscrepancies) Console.WriteLine("    [PAID-RUN act-diff] " + ad);

                Check(diagPaid.VisibleMarchRuleProvenance == VisibleMarchRuleProvenance.LlmHostOnlyHistorical
                    && !diagPaid.ReplayedVisibleMarchRoutes
                    && !diagPaid.Engine.State.RequireVisibleMarchRoutes
                    && diagPaid.ReplayedLlmVisibleMarchRoutes
                    && diagPaid.EstablishedSharedVisibleMarchRoutes == null,
                    "[REPLAY-PAID] preserves LlmHostOnlyHistorical movement provenance without asserting shared-engine rule");
                Check(diagPaid.ReplayedAssaultTroopRatio
                    && diagPaid.Engine.State.RequireAssaultTroopRatio
                    && diagPaid.AssaultTroopRatioProvenance == AssaultTroopRatioRuleProvenance.HistoricalOldRulePreserved
                    && diagPaid.ProviderFailuresReplayed == 1 && diagPaid.ExpectedProviderFailures == 1
                    && diagPaid.InvalidResponsesReplayed == 3 && diagPaid.ExpectedInvalidResponses == 3
                    && diagPaid.EngineRejectionsReplayed == 0 && diagPaid.ExpectedEngineRejections == 0,
                    $"[REPLAY-PAID] preserves historical 3:1 provenance and splits invalid ({diagPaid.InvalidResponsesReplayed}/3) vs provider error ({diagPaid.ProviderFailuresReplayed}/1)");
                Check(diagPaid.IsIncompleteRun && diagPaid.CurrentSeason == 3
                    && diagPaid.StopReason != null && diagPaid.StopReason.Contains("usage unknown"),
                    "[REPLAY-PAID] paused Season 3 does not fabricate rollover and records stop reason");
                Check(diagPaid.AcceptedActionsReplayed == 26 && diagPaid.ExpectedAcceptedActions == 26
                    && diagPaid.RejectedActionsReplayed == 4 && diagPaid.ExpectedRejectedActions == 4
                    && diagPaid.VoluntaryEndTurnsReplayed == 3 && diagPaid.ExpectedVoluntaryEndTurns == 3
                    && diagPaid.ActionOutcomeMismatches == 0,
                    $"[REPLAY-PAID] action/parse/end-turn parity: acc={diagPaid.AcceptedActionsReplayed}/26 rej={diagPaid.RejectedActionsReplayed}/4 end={diagPaid.VoluntaryEndTurnsReplayed}/3 mis={diagPaid.ActionOutcomeMismatches}");

                // Also verify fail-closed default PauseOnDivergence=true behavior matches diagnostic divergence count honestly:
                var strictPaid = new ReplaySession(paidBundle);
                Check(strictPaid.PauseOnDivergence, "[REPLAY-PAID] bootstrap session defaults to PauseOnDivergence=true");
                strictPaid.RunToCompletion();
                if (rpt.ObservationHashMismatches == 0)
                    Check(strictPaid.IsCompleted && !strictPaid.HasDivergence && strictPaid.ObservationHashesMatched == 37,
                        "[REPLAY-PAID] strict fail-closed replay completed 37/37 without divergence");
                else
                    Check(strictPaid.HasDivergence && strictPaid.State == ReplayState.Paused && !strictPaid.IsCompleted,
                        $"[REPLAY-PAID] strict fail-closed paused at first CoreCLR/runtime divergence (reqIdx={strictPaid.CurrentRequestIndex}, matched={strictPaid.ObservationHashesMatched})");
            }
            else
            {
                Console.WriteLine("  [REPLAY-PAID] skipped external run-20261122140759713-c8b0fcfc (not found from working directory)");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
