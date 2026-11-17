using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class ScriptedReplayTransport : ILlmTransport
    {
        private readonly Queue<string> _replies = new Queue<string>();
        public string Provider => "fake";
        public string Model => "fake-replay-test";
        public ScriptedReplayTransport(IEnumerable<string> replies) { foreach (var r in replies) _replies.Enqueue(r); }
        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            string reply = _replies.Count > 0 ? _replies.Dequeue() : "{\"type\":\"end_turn\",\"summary\":\"done\"}";
            return Task.FromResult(LlmResponse.Success(reply, 1, 10, 5, "fake"));
        }
    }

    private static GameEngine BuildReplayTestEngine(int seed, string gameId)
    {
        var engine = new GameEngine(seed, gameId, BalanceProfile.FoodEasy);
        // 5x3 grid with walkable cells and 4-neighbor links
        int w = 5, h = 3, n = w * h;
        var walk = new bool[n];
        var cost = new int[n];
        var neigh = new int[n][];
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = z * w + x;
                walk[idx] = true;
                cost[idx] = 1;
                var list = new List<int>();
                if (x > 0) list.Add(z * w + (x - 1));
                if (x + 1 < w) list.Add(z * w + (x + 1));
                if (z > 0) list.Add((z - 1) * w + x);
                if (z + 1 < h) list.Add((z + 1) * w + x);
                neigh[idx] = list.ToArray();
            }
        }
        engine.State.Map = new MapData { Width = w, Height = h, Walkable = walk, Cost = cost, Neighbors = neigh };
        engine.State.ScenarioId = "compact-contact-v1";
        engine.State.Factions.Add(new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm, Prestige = 50 });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm, Prestige = 50 });

        var c0 = new CityState { Id = "city-wei", Name = "Luoyang", X = 0, Z = 1, OwnerSeatId = "seat-0", Population = 10000, Food = 3000, Gold = 900, Garrison = 500, Morale = 60, Defense = 1, Training = 1 };
        var c1 = new CityState { Id = "city-shu", Name = "Chengdu", X = 4, Z = 1, OwnerSeatId = "seat-1", Population = 10000, Food = 3000, Gold = 900, Garrison = 500, Morale = 60, Defense = 1, Training = 1 };
        engine.State.Cities.Add(c0);
        engine.State.Cities.Add(c1);
        engine.State.Armies.Add(new ArmyState { Id = "army-wei-1", Name = "Wei Vanguard", OwnerSeatId = "seat-0", Troops = 2000, X = 1, Z = 1, Morale = 60 });
        engine.State.Armies.Add(new ArmyState { Id = "army-shu-1", Name = "Shu Vanguard", OwnerSeatId = "seat-1", Troops = 2000, X = 3, Z = 1, Morale = 60 });
        engine.BeginSeason();
        return engine;
    }

    private static string RecordSyntheticMatch(string root, int seasonsPlanned, string[] replies, out string runId, bool pauseAfterOneSeason = false)
    {
        string cfgPath = Path.Combine(root, "cfg.json");
        File.WriteAllText(cfgPath, "{\"provider\":\"fake\",\"enable_attack\":true,\"enable_research\":true,\"enable_diplomacy\":true,\"visible_march_routes\":true,\"max_requests_per_turn\":5}");
        var config = LlmConfig.LoadFromFile(cfgPath);
        var recorder = new LlmRunRecorder(root);
        var engine = BuildReplayTestEngine(777, "replay-test-game");
        var transport = new ScriptedReplayTransport(replies);
        var runner = new LlmTurnRunner(transport, config, recorder);
        var session = new SpectatorSession(engine, runner, seasonsPlanned, 777, recorder);
        recorder.WriteReplayBootstrap(engine, 777, config, "test", "spectator", seasonsPlanned);
        runId = recorder.RunId;

        while (session.State != SpectatorState.Completed && session.State != SpectatorState.Paused && session.State != SpectatorState.Failed)
        {
            session.Tick();
            if (pauseAfterOneSeason && engine.State.Season == 2 && session.State == SpectatorState.Running
                && engine.State.Cities.Find(c => c.Id == "city-wei").ReclaimPct > 0)
            {
                session.Stop();
                break;
            }
            Thread.Sleep(1);
        }

        SpectatorSummary.Write(root, session, config, transport);
        return root;
    }

    private static void RunReplayPlayback()
    {
        Section("[REPLAY-PLAYBACK] bootstrap reconstruction, complete/paused runs, fail-closed checks, and real run");

        string root = Path.Combine(Path.GetTempPath(), "luanshi-replay-playback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // 1. Complete synthetic match with research, movement, combat, end_turn
            string dirComplete = Path.Combine(root, "complete");
            Directory.CreateDirectory(dirComplete);
            var replies = new[]
            {
                // S1 seat-0: march to (2,1) adjacent to Shu at (3,1), then end_turn
                "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":2,\"param_b\":1,\"summary\":\"advance to (2,1)\"}",
                "{\"type\":\"end_turn\",\"summary\":\"end S1 wei\"}",
                // S1 seat-1: attack army-wei-1 at (2,1), then end_turn
                "{\"type\":\"attack_army\",\"target_id\":\"army-shu-1\",\"param_a\":2,\"param_b\":1,\"summary\":\"strike Wei vanguard\"}",
                "{\"type\":\"end_turn\",\"summary\":\"end S1 shu\"}",
                // S2 seat-0: research agri (now 30 points after S2 rollover), then end_turn
                "{\"type\":\"research\",\"args\":{\"line\":\"agri\"},\"summary\":\"research agriculture\"}",
                "{\"type\":\"end_turn\",\"summary\":\"end S2 wei\"}",
                // S2 seat-1: end_turn
                "{\"type\":\"end_turn\",\"summary\":\"end S2 shu\"}"
            };
            RecordSyntheticMatch(dirComplete, 2, replies, out string completeRunId);

            ReplayBundle completeBundle = ReplayLoader.LoadByRunId(completeRunId, dirComplete);
            var completeSession = new ReplaySession(completeBundle);
            completeSession.RunToCompletion();
            Check(completeSession.IsCompleted && !completeSession.IsIncompleteRun, "[REPLAY-PLAYBACK] complete match finishes as complete");
            Check(!completeSession.HasDivergence, $"[REPLAY-PLAYBACK] complete match has 0 divergence (got '{completeSession.DivergenceSummary}')");
            Check(completeSession.ObservationHashesMatched == completeBundle.Requests.Count,
                $"[REPLAY-PLAYBACK] complete match observation hashes {completeSession.ObservationHashesMatched}/{completeBundle.Requests.Count}");
            Check(completeSession.ActionOutcomeMismatches == 0, "[REPLAY-PLAYBACK] complete match has 0 action outcome mismatches");
            Check(completeSession.AcceptedActionsReplayed == 3, $"[REPLAY-PLAYBACK] complete match replayed all 3 accepted actions (march, attack_army, research; got {completeSession.AcceptedActionsReplayed})");
            Check(completeSession.FinalReport != null && completeSession.FinalReport.AllSeasonsScoreboardExactMatch,
                "[REPLAY-PLAYBACK] complete match all-season scoreboards match");
            Check(completeSession.Engine.State.FindFaction("seat-0").ResearchLevel(ResearchLine.Agri) == 1,
                "[REPLAY-PLAYBACK] research command replayed to agri level 1");
            Check(completeSession.EventFeed.Count == completeBundle.Requests.Count && !string.IsNullOrEmpty(completeSession.EventFeed[0].ExchangeId),
                "[REPLAY-PLAYBACK] feed events carry ExchangeId linkage");

            // 2. Incomplete / paused match
            string dirPaused = Path.Combine(root, "paused");
            Directory.CreateDirectory(dirPaused);
            RecordSyntheticMatch(dirPaused, 3, new[]
            {
                "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":2,\"param_b\":1,\"summary\":\"advance\"}",
                "{\"type\":\"end_turn\",\"summary\":\"end S1 wei\"}",
                "{\"type\":\"end_turn\",\"summary\":\"end S1 shu\"}",
                "{\"type\":\"reclaim\",\"target_id\":\"city-wei\",\"summary\":\"reclaim in S2\"}"
            }, out string pausedRunId, pauseAfterOneSeason: true);

            ReplayBundle pausedBundle = ReplayLoader.LoadByRunId(pausedRunId, dirPaused);
            var pausedSession = new ReplaySession(pausedBundle);
            pausedSession.RunToCompletion();
            Check(pausedSession.IsIncompleteRun, "[REPLAY-PLAYBACK] paused match is marked IsIncompleteRun");
            Check(pausedSession.Engine.State.Season == 2, $"[REPLAY-PLAYBACK] paused match stops in Season 2 without fabricating S3 (season={pausedSession.Engine.State.Season})");
            Check(pausedSession.ReplayedScores.Count == 1, $"[REPLAY-PLAYBACK] paused match records only completed S1 scoreboard (count={pausedSession.ReplayedScores.Count})");
            Check(pausedSession.FinalReport != null && pausedSession.FinalReport.RawExactMatch,
                "[REPLAY-PLAYBACK] paused match S1 scoreboard + observations match without fabricating S2");

            // 3. Missing bootstrap fails closed
            string dirMissing = Path.Combine(root, "missing_boot");
            Directory.CreateDirectory(dirMissing);
            foreach (string f in Directory.GetFiles(dirComplete))
            {
                if (!f.EndsWith("-bootstrap.json", StringComparison.Ordinal))
                    File.Copy(f, Path.Combine(dirMissing, Path.GetFileName(f)));
            }
            bool resolvedMissing = ReplayLoader.TryResolveRunFiles(completeRunId, dirMissing, out _, out _, out _, out _, out string errMissing);
            Check(!resolvedMissing && errMissing != null && errMissing.Contains("missing bootstrap"),
                "[REPLAY-PLAYBACK] missing bootstrap fails closed in TryResolveRunFiles");

            // 4. Tampered bootstrap fails closed + hash divergence pauses playback
            string dirTamper = Path.Combine(root, "tamper");
            Directory.CreateDirectory(dirTamper);
            foreach (string f in Directory.GetFiles(dirComplete))
                File.Copy(f, Path.Combine(dirTamper, Path.GetFileName(f)));
            string bootPath = Path.Combine(dirTamper, $"{completeRunId}-bootstrap.json");
            File.WriteAllText(bootPath, File.ReadAllText(bootPath).Replace("\"seed\":777", "\"seed\":778"));
            bool bootThrew = false;
            try { ReplayLoader.LoadByRunId(completeRunId, dirTamper); }
            catch (InvalidDataException) { bootThrew = true; }
            Check(bootThrew, "[REPLAY-PLAYBACK] tampered bootstrap seed fails closed");

            // Restore bootstrap, tamper observation_hash in jsonl -> playback pauses on divergence
            File.Copy(Path.Combine(dirComplete, $"{completeRunId}-bootstrap.json"), bootPath, true);
            string jsonlPath = Path.Combine(dirTamper, $"{completeRunId}.jsonl");
            string[] lines = File.ReadAllLines(jsonlPath);
            int hIdx = lines[0].IndexOf("\"observation_hash\":\"", StringComparison.Ordinal);
            if (hIdx >= 0)
            {
                int vStart = hIdx + "\"observation_hash\":\"".Length;
                lines[0] = lines[0].Substring(0, vStart) + "00000000" + lines[0].Substring(vStart + 8);
                File.WriteAllLines(jsonlPath, lines);
            }
            ReplayBundle divBundle = ReplayLoader.LoadByRunId(completeRunId, dirTamper);
            var divSession = new ReplaySession(divBundle);
            divSession.Play();
            divSession.Tick(1.0f);
            Check(divSession.HasDivergence && divSession.State == ReplayState.Paused,
                "[REPLAY-PLAYBACK] observation hash divergence pauses playback and sets HasDivergence");

            // 5. Real saved run (run-20261117050620701-0270f853) if present in diplomacy-scenario-prep/runs
            string realRunId = "run-20261117050620701-0270f853";
            if (ReplayLoader.TryResolveRunFiles(realRunId, null, out string rs, out string rj, out string re, out string rb, out _))
            {
                ReplayBundle realBundle = ReplayLoader.Load(rs, rj, re, realRunId, rb);
                var realSession = new ReplaySession(realBundle) { PauseOnDivergence = false };
                realSession.RunToCompletion();
                var rpt = realSession.FinalReport;
                Console.WriteLine($"  [REAL-RUN] obs={rpt.ObservationHashesMatched}/{rpt.TotalRequests} mismatch={rpt.ObservationHashMismatches} acc={rpt.AcceptedActionsReplayed}/{rpt.ExpectedAcceptedActions} actMismatch={rpt.ActionOutcomeMismatches} incomplete={rpt.IsIncompleteRun} stop='{rpt.StopReason}' allSeasonsExact={rpt.AllSeasonsScoreboardExactMatch}");
                if (rpt.RawScoreboardMismatches != null)
                    foreach (var m in rpt.RawScoreboardMismatches) Console.WriteLine("    scoreboard diff: " + m);
                if (rpt.ObservationHashDiscrepancies != null)
                    foreach (var d in rpt.ObservationHashDiscrepancies) Console.WriteLine("    obs diff: " + d);
                Check(realBundle.Requests.Count == 63, $"[REAL-RUN] loaded 63 requests (got {realBundle.Requests.Count})");
                Check(realSession.IsIncompleteRun, "[REAL-RUN] flagged as incomplete/paused run");
                Check(rpt.ActionOutcomeMismatches == 0, $"[REAL-RUN] 0 action outcome mismatches (got {rpt.ActionOutcomeMismatches})");
            }
            else
            {
                Console.WriteLine("  [REAL-RUN] skipped (real run files not present in this checkout)");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }
}
