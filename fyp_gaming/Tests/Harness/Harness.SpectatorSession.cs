using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunSpectatorSession()
    {
        RunSpectatorPerSeatMemory();
        RunSpectatorThreeSeasons();
        RunSpectatorSerialAndStop();
        RunSpectatorResume();
        RunSpectatorFailurePolicy();
        RunSpectatorSummary();
        RunSpectatorPromptIsolation();
        RunSpectatorDeterministic();
        RunSpectatorLivePilotOffline();
    }

    // ---- fixtures -----------------------------------------------------------

    private static GameEngine SpectatorEngine(int seed = 20261026)
    {
        // Two symmetric LLM seats, one city each and no armies. The cities are out of each other's
        // sight, so a prompt can only carry its own seat's resources; Scoreboard() may read both.
        var engine = new GameEngine(seed, "pilot-e1");
        engine.State.Map = Corridor(10);
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Llm,
        });
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 9, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.BeginSeason();
        return engine;
    }

    private static void SpectatorQueueTurn(FakeLlmTransport fake, string seat, int season,
        int inputTokens = 10, int outputTokens = 4)
    {
        string city = seat == "seat-a" ? "city-a" : "city-b";
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"" + city
            + "\",\"summary\":\"S" + season + " tax\"}", 0, inputTokens, outputTokens);
        fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"S" + season + " done\"}",
            0, 8, 2);
    }

    private static void SpectatorQueueSeason(FakeLlmTransport fake, int season)
    {
        SpectatorQueueTurn(fake, "seat-a", season);
        SpectatorQueueTurn(fake, "seat-b", season);
    }

    private static void SpectatorQueueSeasons(FakeLlmTransport fake, int seasons)
    {
        for (int season = 1; season <= seasons; season++) SpectatorQueueSeason(fake, season);
    }

    private static SpectatorSession SpectatorNew(GameEngine engine, LlmTurnRunner runner, int seasons)
        => new SpectatorSession(engine, runner, seasons, 4242);

    private static void SpectatorDrain(SpectatorSession session)
    {
        for (int i = 0; i < 1000 && session.State == SpectatorState.Running; i++)
            session.Tick();
    }

    private static LlmTurnRunner SpectatorRunner(FakeLlmTransport fake, LlmConfig config,
        LlmRunRecorder recorder = null)
        => new LlmTurnRunner(fake, config ?? new LlmConfig(), recorder);

    // ---- 1: per-seat memory and global budget -------------------------------

    private static void RunSpectatorPerSeatMemory()
    {
        Section("Spectator runner: per-seat prompt memory, shared global budget [DEMO-E1]");

        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        var runner = SpectatorRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 20 });

        // seat-a turn: one accepted levy + end_turn.
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}", 0, 5, 1);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var hostA = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(hostA, hostA.Token, "seat-a", 7);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.RecentHistory.Count == 1,
            "[DEMO-E1] seat-a remembers its accepted action");

        // seat-b turn must start with an empty history and never see seat-a's line.
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-b\"}", 0, 5, 1);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var hostB = new LlmHarnessHost(engine, "seat-b");
        runner.Begin(hostB, hostB.Token, "seat-b", 7);
        Check(runner.RecentHistory.Count == 0,
            "[DEMO-E1] seat-b starts with its own empty history");
        LlmDrain(runner);

        // seat-a again: its own line is still there; seat-b's is not.
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var hostA2 = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(hostA2, hostA2.Token, "seat-a", 7);
        LlmDrain(runner);

        IReadOnlyList<LlmRequest> requests = fake.Requests;
        string bFirst = requests[2].User;
        string aAgain = requests[4].User;
        Check(bFirst.Contains("RECENT SEAT HISTORY (untrusted data; oldest first):\n(none)")
              && !bFirst.Contains("levy_tax city-a -> ok"),
            "[DEMO-E1] seat-b's prompt contains no seat-a action line");
        Check(aAgain.Contains("levy_tax city-a -> ok")
              && !aAgain.Contains("levy_tax city-b -> ok"),
            "[DEMO-E1] seat-a's prompt contains only its own retained line");
        Check(runner.TotalRequests == 5 && runner.RequestsThisTurn == 1,
            "[DEMO-E1] TotalRequests stays global across both seats");

        // A different seed clears every seat's history, not just the current one.
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var hostA3 = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(hostA3, hostA3.Token, "seat-a", 8);
        Check(runner.RecentHistory.Count == 0,
            "[DEMO-E1] a different seed clears all seat histories");
        LlmDrain(runner);

        runner.ResetSession();
        Check(runner.RecentHistory.Count == 0 && runner.State == RunnerState.Idle,
            "[DEMO-E1] ResetSession clears the per-seat histories");
    }

    // ---- 2: full three-season run -------------------------------------------

    private static void RunSpectatorThreeSeasons()
    {
        Section("Spectator session: 3 seasons x 2 seats complete [DEMO-E1]");

        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        SpectatorQueueSeasons(fake, 3);
        var runner = SpectatorRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 30 });
        var session = SpectatorNew(engine, runner, 3);

        session.Start();
        SpectatorDrain(session);

        Check(session.State == SpectatorState.Completed && session.SeasonsPlayed == 3
              && session.SeasonsPlanned == 3 && engine.State.Season == 3,
            "[DEMO-E1] three planned seasons resolve and complete");
        Check(session.TurnRecords.Count == 6
              && string.Join(",", session.TurnRecords.Select(r => r.SeatId))
                  == "seat-a,seat-b,seat-a,seat-b,seat-a,seat-b",
            "[DEMO-E1] turns strictly alternate seat-a/seat-b in play order");
        Check(session.TurnRecords.All(r => r.Season == 1 || r.Season == 2 || r.Season == 3)
              && session.TurnRecords[0].Season == 1
              && session.TurnRecords[2].Season == 2
              && session.TurnRecords[4].Season == 3,
            "[DEMO-E1] records carry the season they were played in");
        Check(session.TotalRequests == 12 && session.TotalActions == 6
              && session.TotalAccepted == 6 && session.TotalRejected == 0,
            "[DEMO-E1] all twelve provider calls and six accepted actions are counted");
        Check(session.RecentActions.Count == 6
              && session.RecentActions[0] == "S1 Wei: levy_tax city-a -> ok"
              && session.RecentActions[5] == "S3 Shu: levy_tax city-b -> ok",
            "[DEMO-E1] the feed keeps this run's action -> result lines");
        Check(session.TurnRecords.All(r => r.EndedBy == "end_turn")
              && session.ActiveSeatId == null && session.PauseReason == null,
            "[DEMO-E1] every turn ended by end_turn with no active seat");
    }

    // ---- 3: serial request and Stop -----------------------------------------

    private static void RunSpectatorSerialAndStop()
    {
        Section("Spectator session: one request in flight; Stop discards the late answer [DEMO-E1]");

        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        var first = fake.EnqueueDeferred();
        var second = fake.EnqueueDeferred();
        var runner = SpectatorRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 20 });
        var session = SpectatorNew(engine, runner, 1);

        session.Start();
        Check(session.State == SpectatorState.Running && session.ActiveSeatId == "seat-a"
              && fake.CallCount == 1,
            "[DEMO-E1] Start dispatches exactly one seat-a request");
        session.Tick();
        Check(fake.CallCount == 1 && session.ActiveSeatId == "seat-a",
            "[DEMO-E1] no second request is dispatched while the first is in flight");

        first.SetResult(LlmResponse.Success("{\"type\":\"end_turn\"}", 0, 3, 1, "first"));
        System.Threading.Thread.Sleep(20);
        session.Tick();
        Check(fake.CallCount == 2 && session.ActiveSeatId == "seat-b"
              && session.TurnRecords.Count == 1 && session.TurnRecords[0].SeatId == "seat-a",
            "[DEMO-E1] completing seat-a begins seat-b, still with one request in flight");

        int goldBefore = engine.State.FindCity("city-b").Gold;
        int token = engine.TurnToken;
        session.Stop();
        Check(session.State == SpectatorState.Paused
              && !string.IsNullOrEmpty(session.PauseReason)
              && session.ActiveSeatId == null
              && engine.ActiveSeatId == null,
            "[DEMO-E1] Stop pauses and closes the active seat turn");
        Check(session.TurnRecords.Count == 2 && session.TurnRecords[1].EndedBy == "stopped",
            "[DEMO-E1] the stopped turn is recorded with endedBy=stopped");

        // A response that ignores cancellation and arrives after Stop must never execute.
        second.SetResult(LlmResponse.Success(
            "{\"type\":\"levy_tax\",\"target_id\":\"city-b\"}", 0, 3, 1, "late"));
        session.Tick();
        Check(session.State == SpectatorState.Paused
              && engine.State.FindCity("city-b").Gold == goldBefore
              && engine.TurnToken == token,
            "[DEMO-E1] a late post-Stop response is inert");
    }

    private static void RunSpectatorResume()
    {
        Section("Spectator session: Resume continues at the next seat boundary [DEMO-E1]");

        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        var first = fake.EnqueueDeferred();
        var runner = SpectatorRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 20 });
        var session = SpectatorNew(engine, runner, 1);

        session.Start();
        session.Stop();
        first.SetResult(LlmResponse.Success("{\"type\":\"end_turn\"}", 0, 3, 1, "late"));

        SpectatorQueueTurn(fake, "seat-b", 1);
        session.Resume();
        Check(session.State == SpectatorState.Running && session.ActiveSeatId == "seat-b"
              && fake.CallCount == 2,
            "[DEMO-E1] Resume skips the stopped seat and starts the next boundary");
        SpectatorDrain(session);
        Check(session.TurnRecords[0].EndedBy == "stopped"
              && session.TurnRecords[1].SeatId == "seat-b"
              && session.TurnRecords[1].EndedBy == "end_turn",
            "[DEMO-E1] play continues after the resumed turn");
        // The one-season session is now waiting on seat-a of the next season.
        session.Stop();
    }

    // ---- 4: failure policy ---------------------------------------------------

    private static void RunSpectatorFailurePolicy()
    {
        Section("Spectator session: same-seat failures, auth, cap and game over [DEMO-E1]");

        // Same seat failing twice pauses.
        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        fake.EnqueueError(LlmErrorKind.Network, "down-1");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 2, 1);
        fake.EnqueueError(LlmErrorKind.Network, "down-2");
        var runner = SpectatorRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 40 });
        var session = SpectatorNew(engine, runner, 2);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused
              && session.PauseReason != null
              && session.PauseReason.IndexOf("seat-a", StringComparison.Ordinal) >= 0
              && session.TurnRecords.Count == 3
              && session.TurnRecords[0].EndedBy.StartsWith("failed", StringComparison.Ordinal)
              && session.TurnRecords[2].EndedBy.StartsWith("failed", StringComparison.Ordinal),
            "[DEMO-E1] two failed turns by the same seat pause the session");

        // Auth and rate-limit errors pause immediately.
        foreach (LlmErrorKind kind in new[] { LlmErrorKind.Auth, LlmErrorKind.RateLimited })
        {
            engine = SpectatorEngine();
            fake = new FakeLlmTransport();
            fake.EnqueueError(kind, "provider says no");
            runner = SpectatorRunner(fake, new LlmConfig { MaxTotalRequests = 40 });
            session = SpectatorNew(engine, runner, 1);
            session.Start();
            SpectatorDrain(session);
            Check(session.State == SpectatorState.Paused
                  && session.PauseReason != null
                  && session.PauseReason.IndexOf("provider", StringComparison.Ordinal) >= 0
                  && session.TurnRecords.Count == 1,
                "[DEMO-E1] " + kind + " provider error pauses without an exception");
        }

        // Whole-session cap pauses with a clear reason.
        engine = SpectatorEngine();
        fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}", 0, 2, 1);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 2, 1);
        runner = SpectatorRunner(fake, new LlmConfig
        {
            MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 2,
        });
        session = SpectatorNew(engine, runner, 1);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused
              && session.PauseReason != null
              && session.PauseReason.IndexOf("session request limit", StringComparison.Ordinal) >= 0
              && runner.TotalRequests == 2 && fake.CallCount == 2,
            "[DEMO-E1] the shared session request cap pauses the pilot");

        // Game over completes.
        engine = SpectatorEngine();
        engine.State.WinnerSeatId = "seat-a";
        fake = new FakeLlmTransport();
        runner = SpectatorRunner(fake, new LlmConfig());
        session = SpectatorNew(engine, runner, 2);
        session.Start();
        Check(session.State == SpectatorState.Completed
              && session.StopReason == "game_over"
              && fake.CallCount == 0,
            "[DEMO-E1] an already-decided game completes without a provider call");
    }

    // ---- 5: summary ----------------------------------------------------------

    private static void RunSpectatorSummary()
    {
        Section("Spectator summary: StrictJson, totals, no secret [DEMO-E1]");

        string dir = LlmTempDir("pilot-summary");
        try
        {
            using (new LlmEnvScope(LlmFakeKey, null, null))
            {
                var config = LlmConfig.LoadFromFile(null);
                config.MaxRequestsPerTurn = 4;
                config.MaxActionsPerTurn = 2;
                config.MaxTotalRequests = 30;
                var engine = SpectatorEngine();
                var fake = new FakeLlmTransport();
                SpectatorQueueSeasons(fake, 1);
                var runner = SpectatorRunner(fake, config);
                var session = SpectatorNew(engine, runner, 1);
                session.Start();
                SpectatorDrain(session);

                string path = SpectatorSummary.Write(dir, session, config, fake, runner);
                string text = File.Exists(path) ? File.ReadAllText(path) : "";
                bool parsed = StrictJson.TryParse(text, out JsonNode root, out string error);
                Check(parsed && root is JsonObject,
                    "[DEMO-E1] the pilot summary is valid StrictJson"
                    + (parsed ? "" : ": " + error));
                var obj = root as JsonObject;
                Check(path.StartsWith(Path.Combine(dir, "pilot-"), StringComparison.Ordinal)
                      && path.EndsWith("-summary.json", StringComparison.Ordinal),
                    "[DEMO-E1] the summary file is pilot-<runId>-summary.json");
                Check(obj != null
                      && LlmInt(obj, "seed") == 4242
                      && LlmInt(obj, "seasons_planned") == 1
                      && LlmInt(obj, "seasons_played") == 1
                      && LlmStr(obj, "stop_reason") == "completed"
                      && LlmStr(obj, "provider") == "fake"
                      && LlmStr(obj, "model") == "fake-model"
                      && LlmStr(obj, "prompt_version") == PromptBuilder.PromptVersion
                      && LlmStr(obj, "schema_hash") == PromptBuilder.SchemaHash()
                      && LlmStr(obj, "config_redacted") != null
                      && LlmStr(obj, "start_utc") != null
                      && LlmStr(obj, "end_utc") != null,
                    "[DEMO-E1] summary carries run metadata, redacted config and UTC bounds");
                Check(obj != null && LlmInt(obj, "requests") == 4
                      && LlmInt(obj, "input_tokens") == 36
                      && LlmInt(obj, "output_tokens") == 12
                      && LlmNode(obj, "cost_usd") is JsonNull,
                    "[DEMO-E1] summary reports request and known token totals; cost is null without prices");
                JsonNode seasons = obj == null ? null : LlmNode(obj, "seasons");
                Check(seasons is JsonArray seasonArray && seasonArray.Count == 1
                      && seasonArray.Items[0] is JsonObject seasonObj
                      && LlmNode(seasonObj, "seats") is JsonArray seats
                      && seats.Count == 2
                      && LlmStr((JsonObject)seats.Items[0], "seat_id") == "seat-a"
                      && LlmNode((JsonObject)seats.Items[0], "commands") is JsonArray commands
                      && commands.Count > 0
                      && LlmNode((JsonObject)seats.Items[0], "results") is JsonArray results
                      && results.Count > 0,
                    "[DEMO-E1] summary groups per-season commands and results by seat");
                Check(!text.Contains(LlmFakeKey) && !text.Contains("Bearer "),
                    "[DEMO-E1] the summary never contains the API key or headers");
            }
        }
        finally { LlmDelete(dir); }
    }

    // ---- 6: no cross-seat data in prompts ------------------------------------

    private static void RunSpectatorPromptIsolation()
    {
        Section("Spectator session: Scoreboard data never enters a seat prompt [DEMO-E1]");

        var engine = SpectatorEngine();
        engine.State.FindCity("city-a").Population = 43210;
        engine.State.FindCity("city-b").Population = 54321;
        var fake = new FakeLlmTransport();
        SpectatorQueueSeasons(fake, 1);
        var runner = SpectatorRunner(fake, new LlmConfig { MaxTotalRequests = 30 });
        var session = SpectatorNew(engine, runner, 1);

        List<SpectatorScore> board = session.Scoreboard();
        Check(board.Count == 2
              && board.Any(s => s.SeatId == "seat-a" && s.Population == 43210)
              && board.Any(s => s.SeatId == "seat-b" && s.Population == 54321),
            "[DEMO-E1] Scoreboard is researcher data read from both factions");

        session.Start();
        SpectatorDrain(session);

        string prompts = string.Join("\n", fake.Requests.Select(r => r.User));
        string aPrompts = string.Join("\n", fake.Requests.Take(2).Select(r => r.User));
        string bPrompts = string.Join("\n", fake.Requests.Skip(2).Take(2).Select(r => r.User));
        Check(aPrompts.Contains("city-a") && !aPrompts.Contains("city-b")
              && !aPrompts.Contains("54321"),
            "[DEMO-E1] seat-a's prompt has only seat-a's cities/numbers");
        Check(bPrompts.Contains("city-b") && !bPrompts.Contains("city-a")
              && !bPrompts.Contains("43210"),
            "[DEMO-E1] seat-b's prompt has only seat-b's cities/numbers");
        Check(prompts.Contains("43210") || prompts.Contains("54321"),
            "[DEMO-E1] fixture: each seat's own population does appear in its own prompt");
    }

    // ---- 7: determinism ------------------------------------------------------

    private static void RunSpectatorDeterministic()
    {
        Section("Spectator session: fixed seed replays identical action logs [DEMO-E1]");

        string[] logs = new string[2];
        for (int run = 0; run < 2; run++)
        {
            var engine = SpectatorEngine(123456);
            var fake = new FakeLlmTransport();
            SpectatorQueueSeasons(fake, 3);
            var runner = SpectatorRunner(fake, new LlmConfig { MaxTotalRequests = 30 });
            var session = SpectatorNew(engine, runner, 3);
            session.Start();
            SpectatorDrain(session);
            logs[run] = string.Join("\n", session.RecentActions) + "\n"
                      + string.Join("\n", session.TurnRecords.Select(r =>
                            r.Season + "|" + r.SeatId + "|" + r.Accepted + "|"
                            + r.Rejected + "|" + r.LastCommandJson + "|" + r.LastResult))
                      + "\n" + session.TotalAccepted + "/" + session.TotalRejected;
        }
        Check(logs[0] == logs[1] && logs[0].Length > 0,
            "[DEMO-E1] two runs with the same seed have identical action logs");
    }

    // ---- 8: offline proof of the live-pilot entry ---------------------------

    private static void RunSpectatorLivePilotOffline()
    {
        Section("Live pilot: offline loopback proof of --live-pilot [DEMO-E1]");
        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        string dir = LlmTempDir("pilot-live");
        string runsDir = Path.Combine(dir, "runs");
        LlmLiveHttpStub stub = null;
        try
        {
            var bodies = new List<string>();
            for (int season = 1; season <= 3; season++)
            {
                bodies.Add(LlmLiveBody("pilot-a-" + season,
                    "{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"tax\"}", 10, 3));
                bodies.Add(LlmLiveBody("pilot-a-end-" + season, "{\"type\":\"end_turn\"}", 8, 2));
                bodies.Add(LlmLiveBody("pilot-b-" + season,
                    "{\"type\":\"levy_tax\",\"target_id\":\"city-b\",\"summary\":\"tax\"}", 10, 3));
                bodies.Add(LlmLiveBody("pilot-b-end-" + season, "{\"type\":\"end_turn\"}", 8, 2));
            }
            stub = new LlmLiveHttpStub(bodies.ToArray());
            string configPath = Path.Combine(dir, "llm.local.json");
            File.WriteAllText(configPath,
                "{\"provider\":\"stub-provider\",\"base_url\":\"" + stub.BaseUrl + "\","
                + "\"model\":\"stub-model\",\"api_key\":\"" + LlmFakeKey + "\","
                + "\"json_mode\":true,\"max_total_requests\":99,"
                + "\"max_requests_per_turn\":99,\"max_actions_per_turn\":99}");

            int code;
            string text;
            using (new LlmEnvScope(null, null, null))
            {
                var writer = new StringWriter();
                code = RunLivePilot(configPath, 3, writer, runsDir);
                text = writer.ToString();
                stub.WaitServed(10000);
            }

            Check(code == 0 && stub.RequestCount == 12,
                "[DEMO-E1] offline live pilot serves 12 canned requests and exits 0");
            Check(text.Contains("LIVE-PILOT state=Completed")
                  && text.Contains("LIVE-PILOT requests=12")
                  && text.Contains("LIVE-PILOT seasons completed: 3")
                  && text.Contains("LIVE-PILOT state-changing actions: 6"),
                "[DEMO-E1] live pilot prints completion, totals and state-changing actions");
            Check(text.Contains("S1 Wei: levy_tax city-a -> ok")
                  && text.Contains("S3 Shu: levy_tax city-b -> ok"),
                "[DEMO-E1] live pilot prints the per-season action -> result transcript");
            Check(!text.Contains(LlmFakeKey) && !text.Contains("Bearer ")
                  && text.Contains("api_key=<set>"),
                "[DEMO-E1] live pilot prints redacted config only");
            string summaryPath = LlmLiveField(text, "LIVE-PILOT summary=");
            Check(!string.IsNullOrEmpty(summaryPath) && File.Exists(summaryPath)
                  && summaryPath.StartsWith(runsDir, StringComparison.Ordinal)
                  && StrictJson.TryParse(File.ReadAllText(summaryPath), out JsonNode summary, out _)
                  && summary is JsonObject summaryObj
                  && LlmInt(summaryObj, "seasons_played") == 3
                  && !File.ReadAllText(summaryPath).Contains(LlmFakeKey),
                "[DEMO-E1] live pilot writes a valid secret-free summary under the supplied runs dir");
        }
        finally
        {
            if (stub != null) stub.Dispose();
            LlmDelete(dir);
        }
    }
}
