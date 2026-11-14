using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Focused, in-process checks for the acceptance criteria. No provider, no network, no Unity.
    /// Run with: dotnet run --project Tests/Playground/Playground.csproj -c Release -- test
    /// </summary>
    internal static partial class PlaygroundSelfTests
    {
        private sealed class TestLog
        {
            public int Passed;
            public int Failed;

            public void Check(bool ok, string name, string detail = null)
            {
                if (ok) { Passed++; Console.WriteLine("  PASS  " + name); }
                else
                {
                    Failed++;
                    Console.WriteLine("  FAIL  " + name + (detail == null ? "" : " :: " + detail));
                }
            }
        }

        public static int Run()
        {
            var log = new TestLog();
            Console.WriteLine("LuanShi playground self-tests");

            DeterministicHappyPath(log);
            PromptAndTraceContext(log);
            InquirySpendsNoActionPoint(log);
            CheckLeavesStateUnchanged(log);
            CheckOnlyNeverExecutes(log);
            SeatObservationAndNoteIsolation(log);
            NotePersistence(log);
            HiddenEntityOracle(log);
            NoteTooLongRejected(log);

            MalformedJsonExecutesNothing(log);
            EmptyResponseExecutesNothing(log);
            TimeoutExecutesNothing(log);
            ExhaustedInquiryBudgetExecutesNothing(log);
            ExhaustedRequestBudgetExecutesNothing(log);
            ExhaustedWallTimeBudgetExecutesNothing(log);

            Console.WriteLine("\n" + log.Passed + " passed, " + log.Failed + " failed");
            return log.Failed == 0 ? 0 : 1;
        }

        private static PlaygroundRunRecord RunSession(PlaygroundConfig config,
            FakeLlmTransport transport = null, bool enqueueBeforeRun = false,
            ILlmMemoryStore memoryStore = null, IPlaygroundNoteStore noteStore = null)
        {
            var session = new PlaygroundSession(config, null, transport, new PlaygroundClock(),
                memoryStore, noteStore);
            if (enqueueBeforeRun && transport != null)
                EnqueueFaultResponses(transport, config);
            return session.Run();
        }

        private static PlaygroundConfig HappyConfig()
        {
            PlaygroundConfig config = PlaygroundConfig.Defaults();
            config.UseBuiltInFakePlan = true;
            config.Seasons = 2;
            return config;
        }

        private static void DeterministicHappyPath(TestLog log)
        {
            PlaygroundRunRecord first = RunSession(HappyConfig());
            PlaygroundRunRecord second = RunSession(HappyConfig());
            string firstJson = PlaygroundOutput.ResultsJson(first);
            string secondJson = PlaygroundOutput.ResultsJson(second);

            log.Check(firstJson == secondJson,
                "same scenario + seed + settings produce byte-identical results.json");
            log.Check(first.Requests.Count > 0 && first.Requests.Count == second.Requests.Count,
                "the fake session sends a bounded number of requests",
                "requests=" + first.Requests.Count);
            log.Check(first.Inquiries.Count >= 4,
                "all four read-only inquiry tools run in the happy path",
                "inquiries=" + first.Inquiries.Count);
            log.Check(first.AcceptedActions >= 2 && first.RejectedActions == 0,
                "the fake session reaches legal actions (march + domestic)",
                "accepted=" + first.AcceptedActions + " rejected=" + first.RejectedActions);
            log.Check(first.Notes.Count > 0, "the fake session saves strategic notes");
            log.Check(first.EndStateHash != first.StartStateHash,
                "executed actions advance the authoritative state");
        }

        private static void PromptAndTraceContext(TestLog log)
        {
            PlaygroundRunRecord record = RunSession(HappyConfig());
            PlaygroundRequestRecord request = record.Requests[0];
            log.Check(!string.IsNullOrEmpty(request.System) && !string.IsNullOrEmpty(request.User),
                "every request records the exact system and user context");
            log.Check(request.Seed == record.Seed && request.PromptBaseVersion != null
                && request.PromptAddendumVersion == PlaygroundConfig.PromptAddendumVersion,
                "each request records the seed and prompt version");
            log.Check(request.User.Contains("CURRENT SEAT-FILTERED OBSERVATION")
                && request.User.Contains("CANDIDATE OPTIONS")
                && request.User.Contains("RELEVANT LONG-TERM MEMORY"),
                "the user context shows observation, options and memory sections");
            log.Check(request.ObservationJson.Contains("\"seat_id\":\"seat-a\"")
                && request.OfferedActions.Contains("end_turn"),
                "the recorded components are the filtered observation and offered actions");
            log.Check(request.ConfigJson.Contains("\"seed\":4242")
                && request.ConfigJson.Contains("\"max_requests_per_turn\""),
                "each request records the relevant configuration");

            string json = PlaygroundOutput.ResultsJson(record);
            string trace = PlaygroundOutput.TraceText(record);
            log.Check(json.Contains("\"system\":") && json.Contains("\"user\":")
                && json.Contains("\"prompt_version\":")
                && json.Contains("\"seed\":4242"),
                "results.json carries exact context, prompt version and seed");
            log.Check(trace.Contains("--- SYSTEM (exact) ---")
                && trace.Contains("--- USER (exact) ---")
                && trace.Contains("prompt_version: demo-slice-4+playground-deliberation-1")
                && trace.Contains("seed: 4242"),
                "trace.txt carries exact context, prompt version and seed");
        }

        private static void InquirySpendsNoActionPoint(TestLog log)
        {
            PlaygroundRunRecord record = RunSession(HappyConfig());
            bool any = false;
            bool ok = true;
            foreach (PlaygroundInquiryRecord inquiry in record.Inquiries)
            {
                any = true;
                if (inquiry.CpBefore != inquiry.CpAfter
                    || inquiry.ActionAdmissionsBefore != inquiry.ActionAdmissionsAfter)
                    ok = false;
            }
            log.Check(any && ok, "inquiry spends no command point and no action admission");

            bool resultArrived = false;
            foreach (PlaygroundRequestRecord request in record.Requests)
            {
                if (request.Outcome != "inquiry_result") continue;
                PlaygroundRequestRecord next = record.FindRequest(request.Index + 1);
                if (next != null && next.User.Contains("PREVIOUS INQUIRY RESULT")) resultArrived = true;
            }
            log.Check(resultArrived, "an inquiry result reaches the next request context");
        }

        private static void CheckLeavesStateUnchanged(TestLog log)
        {
            PlaygroundRunRecord record = RunSession(HappyConfig());
            bool any = false;
            bool ok = true;
            foreach (PlaygroundActionRecord check in record.Checks)
            {
                any = true;
                if (!check.ReplayMatched
                    || !string.Equals(check.LiveStateHashBeforeCheck,
                        check.LiveStateHashAfterCheck, StringComparison.Ordinal)
                    || !check.CheckOk)
                    ok = false;
            }
            log.Check(any && ok,
                "Check validates on an isolated replay and leaves the authoritative state unchanged");

            PlaygroundActionRecord executed = null;
            foreach (PlaygroundActionRecord action in record.Actions)
                if (action.Executed && action.Accepted) { executed = action; break; }
            log.Check(executed != null && executed.LiveStateHashAfterExecute
                != executed.LiveStateHashBeforeExecute,
                "Execute then advances the authoritative state through the normal path");
        }

        private static void CheckOnlyNeverExecutes(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            config.ActionMode = PlaygroundActionMode.CheckOnly;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(CaptureMarchJson(config.Seed, config.GameId), 0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            var session = new PlaygroundSession(config, null, fake, new PlaygroundClock(),
                new InMemoryLlmMemoryStore(), new InMemoryPlaygroundNoteStore());
            PlaygroundRunRecord record = session.Run();
            PlaygroundActionRecord action = record.Actions.Count > 0 ? record.Actions[0] : null;
            PlaygroundTurnRecord turn = record.FindTurn("seat-a", 1);
            log.Check(action != null && action.CheckOk && !action.Executed,
                "check-only mode validates the order without executing it");
            log.Check(turn != null && string.Equals(turn.StateHashBefore, turn.StateHashAfter,
                StringComparison.Ordinal),
                "check-only mode leaves the authoritative turn state unchanged");
            log.Check(session.Engine.State.CityAt(3, 0) != null
                && session.Engine.State.CityAt(3, 0).IsNeutral,
                "check-only mode does not capture the neutral city");
        }

        private static void SeatObservationAndNoteIsolation(TestLog log)
        {
            var memory = new InMemoryLlmMemoryStore();
            var notes = new InMemoryPlaygroundNoteStore();
            PlaygroundRunRecord record = RunSession(HappyConfig(), null, false, memory, notes);

            Observation a = PlaygroundScenario.Create(HappyConfig()).Observe("seat-a");
            Observation b = PlaygroundScenario.Create(HappyConfig()).Observe("seat-b");
            log.Check(a.FindCity("city-b") == null && a.FindArmy("army-b-1") == null
                && b.FindCity("city-a") == null && b.FindArmy("army-a-1") == null,
                "neither faction's observation contains the other's hidden city or army");
            log.Check(a.FindCity("city-neutral") != null && b.FindCity("city-neutral") == null,
                "the neutral city is visible to seat-a and hidden from seat-b");

            string aPrompts = PromptsFor(record, "seat-a");
            string bPrompts = PromptsFor(record, "seat-b");
            log.Check(!aPrompts.Contains("city-b") && !aPrompts.Contains("army-b-1")
                && !bPrompts.Contains("city-a") && !bPrompts.Contains("army-a-1"),
                "prompt context never carries the other seat's hidden entities");
            log.Check(!aPrompts.Contains("avoid the neutral city") && !bPrompts.Contains("Crossroads"),
                "a strategic note is shown only to the faction that saved it");
            log.Check(notes.Load(record.GameId, "seat-a") != null
                && notes.Load(record.GameId, "seat-b") != null
                && notes.Load(record.GameId, "seat-a") != notes.Load(record.GameId, "seat-b"),
                "notes are stored separately per match and faction");

            PlaygroundRequestRecord season2 = null;
            foreach (PlaygroundRequestRecord request in record.Requests)
                if (request.SeatId == "seat-a" && request.Season == 2) { season2 = request; break; }
            log.Check(season2 != null && season2.NoteShown != null
                && season2.User.Contains("STRATEGIC NOTE FROM YOUR PREVIOUS TURN"),
                "seat-a's note is shown to seat-a on its next turn");
        }

        private static void NotePersistence(TestLog log)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "luanshi-playground-notes-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = System.IO.Path.Combine(directory, "notes.jsonl");
                var writer = new FilePlaygroundNoteStore(path);
                log.Check(writer.Save("match-x", "seat-a", "hold the crossing", 2),
                    "file note store saves a bounded note");
                var reader = new FilePlaygroundNoteStore(path);
                log.Check(reader.Load("match-x", "seat-a") == "hold the crossing",
                    "a fresh note store instance reloads the saved note");
                log.Check(reader.Load("match-x", "seat-b") == null
                    && reader.Load("match-y", "seat-a") == null,
                    "file note store is isolated by match and faction");
            }
            finally
            {
                try { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
                catch (Exception) { }
            }
        }

        private static void HiddenEntityOracle(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            config.MaxInvalidPerTurn = 3;
            config.MaxInquiryPerTurn = 4;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(EndTurnJson("seat-a done"), 0, 1200, 60);
            fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-neutral\"}",
                0, 1200, 60);
            fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-does-not-exist\"}",
                0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            PlaygroundInquiryRecord hidden = null, missing = null;
            foreach (PlaygroundInquiryRecord inquiry in record.Inquiries)
            {
                if (inquiry.SeatId != "seat-b") continue;
                if (hidden == null) hidden = inquiry;
                else if (missing == null) missing = inquiry;
            }
            log.Check(hidden != null && missing != null
                && !hidden.Ok && !missing.Ok
                && hidden.ResultJson == missing.ResultJson
                && hidden.ResultJson.Contains("entity_not_found"),
                "a hidden entity and a nonexistent entity return the same bounded result");
        }

        private static void NoteTooLongRejected(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            config.MaxNoteChars = 20;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"note\":\"this note is deliberately far too long for the bound\"}",
                0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);
            PlaygroundRunRecord record = RunSession(config, fake);
            bool saved = false;
            foreach (PlaygroundNoteRecordView note in record.Notes) if (note.Event == "saved") saved = true;
            log.Check(!saved && record.Requests[0].Outcome == "note_rejected:note_too_long",
                "an over-length strategic note is rejected without an action");
        }

        private static string PromptsFor(PlaygroundRunRecord record, string seatId)
        {
            var text = new System.Text.StringBuilder();
            foreach (PlaygroundRequestRecord request in record.Requests)
                if (request.SeatId == seatId) text.Append(request.User).Append('\n');
            return text.ToString();
        }

        private static PlaygroundConfig FaultConfig()
        {
            PlaygroundConfig config = PlaygroundConfig.Defaults();
            config.UseBuiltInFakePlan = false;
            config.SeedFixtureMemories = false;
            config.Seasons = 1;
            config.MaxInvalidPerTurn = 1;
            config.MaxRequestsPerTurn = 6;
            config.MaxInquiryPerTurn = 4;
            return config;
        }

        private static string CaptureMarchJson(int seed, string gameId)
        {
            GameEngine engine = PlaygroundScenario.Create(seed, gameId);
            engine.BeginSeason();
            MovementView movement = MovementPlanner.Build(engine, "seat-a");
            if (movement != null && movement.Armies != null)
                foreach (ArmyMovePlan plan in movement.Armies)
                    if (plan != null && plan.Options != null)
                        foreach (MoveOption option in plan.Options)
                            if (option != null && option.Kind == "capture_neutral_city")
                                return "{\"type\":\"march\",\"target_id\":" + MiniJson.Str(plan.ArmyId)
                                    + ",\"param_a\":" + MiniJson.Num(option.X)
                                    + ",\"param_b\":" + MiniJson.Num(option.Z)
                                    + ",\"summary\":\"capture the crossroads\"}";
            return "{\"type\":\"end_turn\"}";
        }

        private static string EndTurnJson(string why)
            => "{\"type\":\"end_turn\",\"summary\":" + MiniJson.Str(why) + "}";

        private static void EnqueueFaultResponses(FakeLlmTransport transport, PlaygroundConfig config)
        {
            // Replaced by each fault test; this keeps the shared helper honest if expanded later.
            transport.EnqueueSuccess(EndTurnJson("fallback"), 0, 1200, 60);
        }
    }
}
