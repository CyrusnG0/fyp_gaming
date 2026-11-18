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

    private static string RecordSyntheticMatch(string root, int seasonsPlanned, string[] replies, out string runId, bool pauseAfterOneSeason = false, LlmConfig overrideConfig = null)
    {
        LlmConfig config;
        if (overrideConfig != null)
        {
            config = overrideConfig;
        }
        else
        {
            string cfgPath = Path.Combine(root, "cfg.json");
            File.WriteAllText(cfgPath, "{\"provider\":\"fake\",\"enable_attack\":true,\"enable_research\":true,\"enable_diplomacy\":true,\"visible_march_routes\":true,\"max_requests_per_turn\":5}");
            config = LlmConfig.LoadFromFile(cfgPath);
        }
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
            var weiArmyBefore = divSession.Engine.State.FindArmy("army-wei-1");
            int weiXBefore = weiArmyBefore.X, weiZBefore = weiArmyBefore.Z, weiTroopsBefore = weiArmyBefore.Troops;
            int logCountBefore = divSession.Engine.Log.Records.Count;
            divSession.Play();
            divSession.Tick(1.0f);
            var weiArmyAfter = divSession.Engine.State.FindArmy("army-wei-1");
            Check(divSession.HasDivergence && divSession.State == ReplayState.Paused,
                "[REPLAY-PLAYBACK] observation hash divergence pauses playback and sets HasDivergence");
            Check(divSession.CurrentRequestIndex == 0 && !divSession.IsCompleted
                && divSession.AcceptedActionsReplayed == 0 && divSession.EventFeed.Count == 0
                && weiArmyAfter.X == weiXBefore && weiArmyAfter.Z == weiZBefore
                && divSession.Engine.Log.Records.Count == logCountBefore,
                "[REPLAY-PLAYBACK] observation hash mismatch stops fail-closed before submitting march, advancing index, or mutating canonical state");

            // 4a-mid: Mid-match mismatch (request 2: attack_army) via RunToCompletion also stops fail-closed before submitting
            string dirTamperMid = Path.Combine(root, "tamper_mid");
            Directory.CreateDirectory(dirTamperMid);
            foreach (string f in Directory.GetFiles(dirComplete))
                File.Copy(f, Path.Combine(dirTamperMid, Path.GetFileName(f)));
            string midJsonlPath = Path.Combine(dirTamperMid, $"{completeRunId}.jsonl");
            string[] midLines = File.ReadAllLines(midJsonlPath);
            int midHIdx = midLines[2].IndexOf("\"observation_hash\":\"", StringComparison.Ordinal);
            if (midHIdx >= 0)
            {
                int vStart = midHIdx + "\"observation_hash\":\"".Length;
                midLines[2] = midLines[2].Substring(0, vStart) + "00000000" + midLines[2].Substring(vStart + 8);
                File.WriteAllLines(midJsonlPath, midLines);
            }
            ReplayBundle midDivBundle = ReplayLoader.LoadByRunId(completeRunId, dirTamperMid);
            var midDivSession = new ReplaySession(midDivBundle);
            midDivSession.RunToCompletion();
            int weiHpBeforeAttack = midDivSession.Engine.State.FindArmy("army-wei-1").Troops;
            Check(midDivSession.HasDivergence && midDivSession.State == ReplayState.Paused
                && midDivSession.CurrentRequestIndex == 2 && !midDivSession.IsCompleted
                && midDivSession.AcceptedActionsReplayed == 1 && midDivSession.EventFeed.Count == 2
                && weiHpBeforeAttack == weiTroopsBefore,
                "[REPLAY-PLAYBACK] mid-match observation mismatch stops RunToCompletion fail-closed before submitting attack_army");

            // 4a-diag: Explicit diagnostic mode (PauseOnDivergence = false) continues past mismatch
            var diagSession = new ReplaySession(divBundle) { PauseOnDivergence = false };
            diagSession.RunToCompletion();
            Check(diagSession.HasDivergence && diagSession.ObservationHashMismatches == 1
                && diagSession.IsCompleted && diagSession.AcceptedActionsReplayed == 3
                && diagSession.EventFeed.Count == completeBundle.Requests.Count,
                "[REPLAY-PLAYBACK] diagnostic mode (PauseOnDivergence=false) continues past mismatch and replays remaining actions");

            // 4b. Rich context regression: two different event caps (trim=0 >30 events vs trim=3 capped at 30) + missing truncation fails clearly
            string dirRich = Path.Combine(root, "rich_caps");
            Directory.CreateDirectory(dirRich);
            var richRepliesList = new List<string>();
            // Play 4 seasons of reclaim + train + end_turn per seat so obs.Events.Count exceeds 30 by Season 5
            for (int s = 0; s < 4; s++)
            {
                richRepliesList.Add("{\"type\":\"reclaim\",\"target_id\":\"city-wei\",\"summary\":\"s0 rec\"}");
                richRepliesList.Add("{\"type\":\"train\",\"target_id\":\"city-wei\",\"summary\":\"s0 trn\"}");
                richRepliesList.Add("{\"type\":\"end_turn\",\"summary\":\"s0 end\"}");
                richRepliesList.Add("{\"type\":\"reclaim\",\"target_id\":\"city-shu\",\"summary\":\"s1 rec\"}");
                richRepliesList.Add("{\"type\":\"train\",\"target_id\":\"city-shu\",\"summary\":\"s1 trn\"}");
                richRepliesList.Add("{\"type\":\"end_turn\",\"summary\":\"s1 end\"}");
            }
            int s4NoteIdx = richRepliesList.Count;
            richRepliesList.Add("{\"type\":\"patrol\",\"target_id\":\"city-wei\",\"summary\":\"s5 pat\"}");
            int s4EndIdx = richRepliesList.Count;
            richRepliesList.Add("{\"type\":\"patrol\",\"target_id\":\"city-wei\",\"summary\":\"s5 pat2\"}");
            richRepliesList.Add("{\"type\":\"end_turn\",\"summary\":\"done rich\"}");
            richRepliesList.Add("{\"type\":\"end_turn\",\"summary\":\"s1 done\"}");
            string[] richReplies = richRepliesList.ToArray();

            LlmConfig BuildRichCfg(int maxPromptChars)
            {
                var c = LlmConfig.RichDeliberation();
                c.Provider = "fake";
                c.Model = "fake-replay-test";
                c.EnableAttack = true;
                c.EnableResearch = true;
                c.EnableDiplomacy = true;
                c.MaxPromptChars = maxPromptChars;
                c.ContextEventItems = 60;
                return c;
            }

            string dirProbe = Path.Combine(root, "rich_probe");
            Directory.CreateDirectory(dirProbe);
            RecordSyntheticMatch(dirProbe, 5, richReplies, out string probeRunId, false, BuildRichCfg(100000));
            ReplayBundle probeBundle = ReplayLoader.LoadByRunId(probeRunId, dirProbe);
            int ceiling = 30000;
            string[] pLines = File.ReadAllLines(probeBundle.JsonlPath);
            if (pLines.Length > s4EndIdx
                && StrictJson.TryParseLenientNumbers(pLines[s4NoteIdx], out JsonNode n0, out _) && n0 is JsonObject o0
                && o0.TryGet("context_report", out JsonNode cr0) && cr0 is JsonObject crObj0
                && crObj0.TryGet("prompt_chars", out JsonNode pc0) && pc0 is JsonInt pci0
                && StrictJson.TryParseLenientNumbers(pLines[s4EndIdx], out JsonNode n1, out _) && n1 is JsonObject o1
                && o1.TryGet("context_report", out JsonNode cr1) && cr1 is JsonObject crObj1
                && crObj1.TryGet("prompt_chars", out JsonNode pc1) && pc1 is JsonInt pci1)
            {
                ceiling = (int)((pci0.Value + pci1.Value) / 2);
            }
            RecordSyntheticMatch(dirRich, 5, richReplies, out string richRunId, false, BuildRichCfg(ceiling));
            ReplayBundle richBundle = ReplayLoader.LoadByRunId(richRunId, dirRich);
            Check(richBundle.Requests.Count > s4EndIdx, $"[REPLAY-PLAYBACK] rich cap test recorded >{s4EndIdx} requests (got {richBundle.Requests.Count})");
            Check(richBundle.Requests[s4NoteIdx].RecordedObservationEventsIncluded > 30,
                $"[REPLAY-PLAYBACK] rich req {s4NoteIdx + 1} recorded >30 events (got {richBundle.Requests[s4NoteIdx].RecordedObservationEventsIncluded})");
            Check(richBundle.Requests[s4EndIdx].RecordedObservationEventsIncluded == PromptBuilder.MaxPromptObservationEvents,
                $"[REPLAY-PLAYBACK] rich req {s4EndIdx + 1} trimmed to {PromptBuilder.MaxPromptObservationEvents} events (got {richBundle.Requests[s4EndIdx].RecordedObservationEventsIncluded})");

            var richSession = new ReplaySession(richBundle) { PauseOnDivergence = true };
            richSession.RunToCompletion();
            Check(richSession.ObservationHashesMatched == richSession.TotalRequests && !richSession.HasDivergence,
                $"[REPLAY-PLAYBACK] rich two-cap replay matched all {richSession.TotalRequests} observation hashes (matched={richSession.ObservationHashesMatched}, div={richSession.DivergenceSummary})");

            // Missing truncation decision on a rich prompt must fail clearly rather than guessing
            string dirMissingCap = Path.Combine(root, "rich_missing_cap");
            Directory.CreateDirectory(dirMissingCap);
            foreach (string f in Directory.GetFiles(dirRich))
                File.Copy(f, Path.Combine(dirMissingCap, Path.GetFileName(f)));
            string mcJsonl = Path.Combine(dirMissingCap, $"{richRunId}.jsonl");
            string[] mcLines = File.ReadAllLines(mcJsonl);
            mcLines[0] = mcLines[0].Replace("\"context_report\":", "\"context_report_omitted\":");
            File.WriteAllLines(mcJsonl, mcLines);
            string mcEx = Path.Combine(dirMissingCap, $"{richRunId}-exchanges.ndjson");
            File.WriteAllText(mcEx, File.ReadAllText(mcEx).Replace("observation events (newest kept)", "observation events omitted"));
            ReplayBundle mcBundle = ReplayLoader.LoadByRunId(richRunId, dirMissingCap);
            var mcSession = new ReplaySession(mcBundle) { PauseOnDivergence = true };
            var mcCityBefore = mcSession.Engine.State.FindCity("city-wei");
            int mcPopBefore = mcCityBefore.Population, mcFoodBefore = mcCityBefore.Food, mcGoldBefore = mcCityBefore.Gold;
            int mcLogBefore = mcSession.Engine.Log.Records.Count;
            mcSession.Play();
            mcSession.Tick(1.0f);
            var mcCityAfter = mcSession.Engine.State.FindCity("city-wei");
            Check(mcSession.HasDivergence && mcSession.State == ReplayState.Paused && mcSession.ObservationHashMismatches == 1
                && mcSession.CurrentRequestIndex == 0 && !mcSession.IsCompleted
                && mcSession.AcceptedActionsReplayed == 0 && mcSession.EventFeed.Count == 0
                && mcCityAfter.Population == mcPopBefore && mcCityAfter.Food == mcFoodBefore && mcCityAfter.Gold == mcGoldBefore
                && mcSession.Engine.Log.Records.Count == mcLogBefore
                && mcSession.DivergenceSummary != null && mcSession.DivergenceSummary.Contains("missing recorded observation event truncation decision"),
                "[REPLAY-PLAYBACK] missing truncation decision on rich prompt fails clearly without guessing or submitting reclaim");

            // 4c. Non-default city IDs (city-wei-1, city-wei-2, city-shu-1, city-shu-2, city-shu-3) + failed initialization
            string dirCustomCities = Path.Combine(root, "custom_cities");
            Directory.CreateDirectory(dirCustomCities);
            var customEngine = BuildReplayTestEngine(54321, GameIdentity.NewMatchId());
            customEngine.State.Cities.Clear();
            customEngine.State.Cities.Add(new CityState { Id = "city-wei-1", Name = "Luoyang", X = 0, Z = 0, OwnerSeatId = "seat-0", Population = 9000, Food = 2500, Gold = 800, Garrison = 500, Morale = 60, Defense = 1, Training = 1 });
            customEngine.State.Cities.Add(new CityState { Id = "city-wei-2", Name = "Xuchang", X = 0, Z = 2, OwnerSeatId = "seat-0", Population = 8000, Food = 2200, Gold = 700, Garrison = 500, Morale = 60, Defense = 1, Training = 1 });
            customEngine.State.Cities.Add(new CityState { Id = "city-shu-1", Name = "Chengdu", X = 4, Z = 0, OwnerSeatId = "seat-1", Population = 9000, Food = 2500, Gold = 800, Garrison = 500, Morale = 60, Defense = 1, Training = 1 });
            customEngine.State.Cities.Add(new CityState { Id = "city-shu-2", Name = "Hanzhong", X = 4, Z = 1, OwnerSeatId = "seat-1", Population = 8000, Food = 2200, Gold = 700, Garrison = 500, Morale = 60, Defense = 1, Training = 1 });
            customEngine.State.Cities.Add(new CityState { Id = "city-shu-3", Name = "Jianye", X = 4, Z = 2, OwnerSeatId = "seat-1", Population = 7500, Food = 2000, Gold = 650, Garrison = 500, Morale = 60, Defense = 1, Training = 1 });
            var customCfg = new LlmConfig { Provider = "fake", Model = "fake-replay-test", MaxActionsPerTurn = 4, RequestTimeoutMs = 5000, EnableAttack = true, EnableResearch = true, EnableDiplomacy = true };
            var customRec = new LlmRunRecorder(dirCustomCities);
            var customReplies = new[]
            {
                "{\"type\":\"reclaim\",\"target_id\":\"city-wei-2\",\"summary\":\"reclaim city-wei-2\"}",
                "{\"type\":\"end_turn\",\"summary\":\"done wei\"}",
                "{\"type\":\"reclaim\",\"target_id\":\"city-shu-3\",\"summary\":\"reclaim city-shu-3\"}",
                "{\"type\":\"end_turn\",\"summary\":\"done shu\"}"
            };
            var customTransport = new ScriptedReplayTransport(customReplies);
            var customRunner = new LlmTurnRunner(customTransport, customCfg, customRec, null);
            var customSpec = new SpectatorSession(customEngine, customRunner, 1, 54321, customRec);
            customRec.WriteReplayBootstrap(customEngine, 54321, customCfg, "test-rev", "spectator", 1);
            while (customSpec.State != SpectatorState.Completed && customSpec.State != SpectatorState.Paused && customSpec.State != SpectatorState.Failed)
            {
                customSpec.Tick();
                Thread.Sleep(1);
            }
            SpectatorSummary.Write(dirCustomCities, customSpec, customCfg, customTransport);
            string customRunId = customRec.RunId;

            ReplayBundle customBundle = ReplayLoader.LoadByRunId(customRunId, dirCustomCities);
            // Pass a distinct hostMap to prove bootstrap-backed replay reconstructs its own cities/state
            var dummyHostMap = new MapData { Width = 2, Height = 2, Walkable = new bool[4], Cost = new int[4], Neighbors = new int[4][] };
            var customSession = new ReplaySession(customBundle, dummyHostMap) { PauseOnDivergence = true };
            customSession.RunToCompletion();
            Check(customSession.IsCompleted && !customSession.HasDivergence
                && customSession.ObservationHashesMatched == customBundle.Requests.Count
                && customSession.AcceptedActionsReplayed == 2
                && customSession.Engine.State.FindCity("city-wei-2") != null
                && customSession.Engine.State.FindCity("city-shu-3") != null
                && customSession.Engine.State.FindCity("city-wei") == null,
                "[REPLAY-PLAYBACK] non-default city IDs (city-wei-2, city-shu-3) replay cleanly from bootstrap");

            // Failed initialization: corrupted bootstrap must throw cleanly without leaving a session
            ReplayBundle corruptBundle = ReplayLoader.LoadByRunId(customRunId, dirCustomCities);
            corruptBundle.Bootstrap = new ReplayBootstrapData { RunId = customRunId };
            bool initThrew = false;
            ReplaySession failedSession = null;
            try { failedSession = new ReplaySession(corruptBundle); }
            catch (Exception) { initThrew = true; }
            Check(initThrew && failedSession == null, "[REPLAY-PLAYBACK] corrupted bootstrap fails initialization cleanly");

            // 4d. Inspecting a previous request from the other seat + empty length-limited reply formatting
            string dirPersona = Path.Combine(root, "persona_evidence");
            Directory.CreateDirectory(dirPersona);
            var personaCfg = new LlmConfig { Provider = "fake", Model = "fake-replay-test", MaxActionsPerTurn = 4, RequestTimeoutMs = 5000, EnablePersonas = true };
            RecordSyntheticMatch(dirPersona, 1, new[]
            {
                "{\"type\":\"reclaim\",\"target_id\":\"city-wei\",\"summary\":\"wei reclaim\"}",
                "{\"type\":\"end_turn\",\"summary\":\"done wei\"}",
                "{\"type\":\"reclaim\",\"target_id\":\"city-shu\",\"summary\":\"shu reclaim\"}",
                "{\"type\":\"end_turn\",\"summary\":\"done shu\"}"
            }, out string personaRunId, false, personaCfg);
            ReplayBundle personaBundle = ReplayLoader.LoadByRunId(personaRunId, dirPersona);
            var personaSession = new ReplaySession(personaBundle) { PauseOnDivergence = true };
            personaSession.StepRequest(); // seat-0 reclaim
            personaSession.StepRequest(); // seat-0 end_turn -> advances to seat-1
            Check(personaSession.CurrentSeatId == "seat-1" && personaSession.CurrentPersonaId == LlmPersona.ShuPersonaId,
                $"[REPLAY-PLAYBACK] playback advanced to seat-1 ({personaSession.CurrentPersonaId})");
            ReplayRequestRecord prevWeiReq = personaBundle.Requests[0];
            Check(prevWeiReq.SeatId == "seat-0" && personaSession.GetPersonaIdForRequest(prevWeiReq) == LlmPersona.WeiPersonaId,
                $"[REPLAY-PLAYBACK] inspecting seat-0 request during seat-1 playback resolves '{personaSession.GetPersonaIdForRequest(prevWeiReq)}' (not seat-1)");

            // When the selected request's persona cannot be established, display "unknown" (never current playback seat's persona)
            var unknownPersonaTurns = new List<ReplayTurnSummary>();
            foreach (var t in personaBundle.Summary.Turns)
            {
                unknownPersonaTurns.Add(new ReplayTurnSummary
                {
                    Season = t.Season, SeatId = t.SeatId, Faction = t.Faction,
                    Requests = t.Requests, Actions = t.Actions, Accepted = t.Accepted, Rejected = t.Rejected, Invalid = t.Invalid,
                    LastSummary = t.LastSummary,
                    PersonaId = string.Equals(t.SeatId, "seat-0", StringComparison.Ordinal) ? "" : t.PersonaId,
                    LastCommandJson = t.LastCommandJson, LastResult = t.LastResult, EndedBy = t.EndedBy, EndReason = t.EndReason, EndReasonText = t.EndReasonText
                });
            }
            var unknownSummary = new ReplaySummaryData
            {
                RunId = personaBundle.Summary.RunId, Seed = personaBundle.Summary.Seed,
                BalanceProfileKey = personaBundle.Summary.BalanceProfileKey, ManualLookupEnabled = personaBundle.Summary.ManualLookupEnabled,
                SeasonsPlanned = personaBundle.Summary.SeasonsPlanned, SeasonsPlayed = personaBundle.Summary.SeasonsPlayed,
                StopReason = personaBundle.Summary.StopReason, State = personaBundle.Summary.State,
                ConfigRedacted = "provider=fake; model=fake",
                Scoreboards = personaBundle.Summary.Scoreboards, Turns = unknownPersonaTurns
            };
            var unknownPersonaBundle = new ReplayBundle
            {
                Summary = unknownSummary, Bootstrap = personaBundle.Bootstrap, Requests = personaBundle.Requests
            };
            var unknownPersonaSession = new ReplaySession(unknownPersonaBundle) { PauseOnDivergence = true };
            unknownPersonaSession.StepRequest();
            unknownPersonaSession.StepRequest();
            Check(unknownPersonaSession.CurrentSeatId == "seat-1" && unknownPersonaSession.CurrentPersonaId == LlmPersona.ShuPersonaId
                && unknownPersonaSession.GetPersonaIdForRequest(unknownPersonaBundle.Requests[0]) == "unknown",
                "[REPLAY-PLAYBACK] unestablished persona for seat-0 returns 'unknown' instead of seat-1's persona");

            // Empty length-limited reply must never be labeled "end_turn"
            var emptyLengthReq = new ReplayRequestRecord
            {
                RunId = customRunId, SeatId = "seat-0", Season = 1, TurnToken = 1, RequestIndex = 1,
                RawModelText = "", FinishReason = "length", ParseOk = false, ResultOk = false, ResultRecorded = true
            };
            var emptyUnknownReq = new ReplayRequestRecord
            {
                RunId = customRunId, SeatId = "seat-0", Season = 1, TurnToken = 1, RequestIndex = 2,
                RawModelText = "   ", FinishReason = null, ParseOk = false, ResultOk = false, ResultRecorded = true
            };
            var validEndTurnReq = new ReplayRequestRecord
            {
                RunId = customRunId, SeatId = "seat-0", Season = 1, TurnToken = 1, RequestIndex = 3,
                RawModelText = "{\"type\":\"end_turn\",\"summary\":\"done\"}", FinishReason = "stop", ParseOk = true, ResultOk = true, ResultRecorded = true
            };
            var emptyBundle = new ReplayBundle
            {
                Summary = customBundle.Summary,
                Bootstrap = customBundle.Bootstrap,
                Requests = new List<ReplayRequestRecord> { emptyLengthReq }
            };
            var emptySession = new ReplaySession(emptyBundle);
            string emptyLenLabel = emptySession.FormatRecordedActionLabel(emptyLengthReq);
            string emptyLenSnapshot = emptySession.GetDisplayedSummarySnapshot();
            string emptyUnkLabel = emptySession.FormatRecordedActionLabel(emptyUnknownReq);
            string validEndLabel = emptySession.FormatRecordedActionLabel(validEndTurnReq);
            Check(!emptyLenLabel.Contains("end_turn", StringComparison.OrdinalIgnoreCase)
                && emptyLenLabel.Contains("empty recorded response", StringComparison.OrdinalIgnoreCase)
                && emptyLenLabel.Contains("finish_reason=length", StringComparison.OrdinalIgnoreCase)
                && !emptyLenSnapshot.Contains("end_turn", StringComparison.OrdinalIgnoreCase)
                && emptyLenSnapshot.Contains("finish_reason=length", StringComparison.OrdinalIgnoreCase)
                && !emptyUnkLabel.Contains("end_turn", StringComparison.OrdinalIgnoreCase)
                && emptyUnkLabel.Contains("finish_reason=unknown", StringComparison.OrdinalIgnoreCase)
                && validEndLabel.Contains("end_turn", StringComparison.Ordinal),
                $"[REPLAY-PLAYBACK] empty length-limited reply labeled explicitly without end_turn ('{emptyLenLabel}')");

            // 5. Real saved run (run-20261117050620701-0270f853) if present in diplomacy-scenario-prep/runs
            string realRunId = "run-20261117050620701-0270f853";
            if (ReplayLoader.TryResolveRunFiles(realRunId, null, out string rs, out string rj, out string re, out string rb, out _))
            {
                ReplayBundle realBundle = ReplayLoader.Load(rs, rj, re, realRunId, rb);
                Check(realBundle.Requests.Count == 63, $"[REAL-RUN] loaded 63 requests (got {realBundle.Requests.Count})");

                // Fail-closed check under .NET CoreCLR: float truncation vs Unity Mono causes an early obs mismatch and pauses before submitting
                var realSession = new ReplaySession(realBundle) { PauseOnDivergence = true };
                realSession.RunToCompletion();
                Check(realSession.HasDivergence && realSession.State == ReplayState.Paused && !realSession.IsCompleted
                    && realSession.ObservationHashMismatches == 1 && realSession.CurrentRequestIndex < realBundle.Requests.Count,
                    $"[REAL-RUN] fail-closed pauses at first CoreCLR float-truncation mismatch (reqIdx={realSession.CurrentRequestIndex}, state={realSession.State})");

                // Diagnostic mode (PauseOnDivergence=false) continues through all 63 requests on CoreCLR
                var diagReal = new ReplaySession(realBundle) { PauseOnDivergence = false };
                diagReal.RunToCompletion();
                var rpt = diagReal.FinalReport;
                Console.WriteLine($"  [REAL-RUN diag] obs={rpt.ObservationHashesMatched}/{rpt.TotalRequests} mismatch={rpt.ObservationHashMismatches} acc={rpt.AcceptedActionsReplayed}/{rpt.ExpectedAcceptedActions} actMismatch={rpt.ActionOutcomeMismatches} incomplete={rpt.IsIncompleteRun} stop='{rpt.StopReason}' allSeasonsExact={rpt.AllSeasonsScoreboardExactMatch} pausedDiv={diagReal.HasDivergence}");
                Check(diagReal.IsIncompleteRun, "[REAL-RUN] flagged as incomplete/paused run");
                Check(rpt.ActionOutcomeMismatches == 0, $"[REAL-RUN] 0 action outcome mismatches (got {rpt.ActionOutcomeMismatches})");

                ReplayFeedEvent inqEvt = null;
                foreach (var fe in diagReal.EventFeed)
                {
                    if (!string.IsNullOrEmpty(fe.InquiryTool)) { inqEvt = fe; break; }
                }
                Check(inqEvt != null && inqEvt.Season == 7 && inqEvt.RequestIndex == 1
                    && inqEvt.ActionType == "manual_lookup" && inqEvt.ResultText == "inquiry",
                    $"[REAL-RUN] season-7 field_combat inquiry linked to S{inqEvt?.Season} R{inqEvt?.RequestIndex} ({inqEvt?.ActionType}/{inqEvt?.ResultText})");
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
