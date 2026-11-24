using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi;
using LuanShi.Engine;

namespace LuanShi.HandoverUiCheck
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            TestDisabledAdapterMode();
            TestEngineQueuePendingCancelAndBoundaries();
            TestOperatorSeatEligibilityAndAuthority();
            TestFullProductionFakeCyclePrivacyAndDiplomacy();
            TestSharedRunnerTelemetryAttribution();
            TestLongHistoryTruncationAndNoHiddenCounts();
            TestProviderFailureAndStaleReplies();

            Console.WriteLine($"HandoverUiCheck: {_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(bool cond, string label, string detail = null)
        {
            if (cond) { _passed++; Console.WriteLine("  PASS  " + label); }
            else { _failed++; Console.WriteLine("  FAIL  " + label + (string.IsNullOrEmpty(detail) ? "" : " [" + detail + "]")); }
        }

        private static GameEngine CreateTwoSeatGame(string seat0Ctrl, string seat1Ctrl, bool seat1First = false)
        {
            var map = new MapData
            {
                Width = 10,
                Height = 10,
                Walkable = new bool[100],
                Cost = new int[100],
                Neighbors = new int[100][],
            };
            for (int i = 0; i < 100; i++)
            {
                map.Walkable[i] = true;
                map.Cost[i] = 1;
                int x = i / 10, z = i % 10;
                var n = new List<int>();
                if (x > 0) n.Add((x - 1) * 10 + z);
                if (x < 9) n.Add((x + 1) * 10 + z);
                if (z > 0) n.Add(x * 10 + (z - 1));
                if (z < 9) n.Add(x * 10 + (z + 1));
                map.Neighbors[i] = n.ToArray();
            }

            var engine = new GameEngine(12345, "handover-ui-test", BalanceProfile.Standard);
            engine.State.Map = map;
            var wei = new FactionState { SeatId = "seat-0", Name = "Wei", Controller = seat0Ctrl };
            var shu = new FactionState { SeatId = "seat-1", Name = "Shu", Controller = seat1Ctrl };
            if (seat1First)
            {
                engine.State.Factions.Add(shu);
                engine.State.Factions.Add(wei);
            }
            else
            {
                engine.State.Factions.Add(wei);
                engine.State.Factions.Add(shu);
            }
            engine.State.Cities.Add(new CityState
            {
                Id = "city-wei", Name = "Wei Capital", X = 1, Z = 1, OwnerSeatId = "seat-0",
                Population = 10000, Food = 500, Gold = 300, Garrison = 500, Morale = 70
            });
            engine.State.Cities.Add(new CityState
            {
                Id = "city-shu", Name = "Chengdu", X = 8, Z = 8, OwnerSeatId = "seat-1",
                Population = 10000, Food = 500, Gold = 300, Garrison = 500, Morale = 70
            });
            engine.BeginSeason();
            return engine;
        }

        private static void DrainRunner(LlmTurnRunner runner)
        {
            for (int i = 0; i < 200 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
            {
                runner.Tick();
                System.Threading.Thread.Sleep(5);
            }
        }

        private static void TestDisabledAdapterMode()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine, new[] { "seat-0" }, allowHandover: false);

            Check(!state.HasEligibleSeats && !state.RequestHandoverToAi(engine, "seat-0", out _),
                "disabled: replay/spectator mode mints no handover operator and rejects requests");
            Check(!state.TryGetPending(engine, "seat-0", out _)
                  && engine.State.FindFaction("seat-0").Controller == ControllerType.Human,
                "disabled: FactionState.Controller stays Human and nothing is pending");
        }

        private static void TestEngineQueuePendingCancelAndBoundaries()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine, new[] { "seat-0" });

            int tok0 = engine.BeginSeatTurn("seat-0");
            Check(state.RequestHandoverToAi(engine, "seat-0", out _),
                "queue: request handover while active queues on authoritative engine");
            HandoverStatus engineStatus = engine.GetHandoverStatus("seat-0");
            Check(engineStatus.HasPending && state.TryGetPending(engine, "seat-0", out var uiPending)
                  && uiPending.RequestId == engineStatus.PendingRequestId,
                "queue: UI pending view mirrors single authoritative engine queue");
            Check(!state.RequestHandoverToAi(engine, "seat-0", out _),
                "queue: repeated handover request rejected without implicit supersession");
            Check(!state.RequestReclaimToHuman(engine, "seat-0", out _),
                "queue: reclaim while still Human (or already pending) rejected");

            Check(state.CancelPending(engine, "seat-0", out _),
                "queue: explicit cancellation clears pending request in engine");
            Check(!engine.GetHandoverStatus("seat-0").HasPending && !state.TryGetPending(engine, "seat-0", out _),
                "queue: neither engine nor UI retains cancelled request");
            Check(!state.CancelPending(engine, "seat-0", out _),
                "queue: cancelling again fails cleanly");

            // Re-request, then close seat-0's turn -> after_turn transfer
            Check(state.RequestHandoverToAi(engine, "seat-0", out _),
                "boundary: re-requested handover after cancel");
            HandoverTransferOutcome midTurn = state.PrepareAndSyncBoundary(engine, "seat-0", engine.Observe("seat-0"));
            Check(!midTurn.Applied && engine.State.FindFaction("seat-0").Controller == ControllerType.Human,
                "boundary: PrepareAndSyncBoundary refuses transfer while seat-0 turn is active");

            state.CapturePreHandoverBaselineIfPending(engine, "seat-0", engine.Observe("seat-0"));
            engine.EndSeatTurn(tok0);
            HandoverTransferOutcome synced = state.SyncBoundaryAfterEngineChange(engine, "seat-0", engine.Observe("seat-0"));
            Check(synced.Applied && engine.State.FindFaction("seat-0").Controller == ControllerType.Llm,
                "boundary: closing seat-0 applies handover via after_turn and opens delegation episode");
            Check(state.GetEpisode("seat-0") != null && state.GetEpisode("seat-0").HasPreHandoverBaseline,
                "boundary: delegation episode recorded pre-handover baseline");

            // Now test between-turn isolation: request reclaim while seat-1 is active -> must NOT apply on seat-1 end
            int tok1 = engine.BeginSeatTurn("seat-1");
            Check(state.RequestReclaimToHuman(engine, "seat-0", out _),
                "boundary: idle reclaim queued while seat-1 turn is active");
            Check(!state.PrepareAndSyncBoundary(engine, "seat-0", engine.Observe("seat-0")).Applied,
                "boundary: cannot apply seat-0 handover while seat-1 turn gate is active");
            engine.EndSeatTurn(tok1);
            Check(engine.State.FindFaction("seat-0").Controller == ControllerType.Llm
                  && state.TryGetPending(engine, "seat-0", out _),
                "boundary: seat-1 ending never triggers seat-0's pending reclaim");

            HandoverTransferOutcome beforeTurn = state.PrepareAndSyncBoundary(engine, "seat-0", engine.Observe("seat-0"));
            Check(beforeTurn.Applied && engine.State.FindFaction("seat-0").Controller == ControllerType.Human
                  && state.HasPendingBriefing("seat-0"),
                "boundary: PrepareAndSyncBoundary applies seat-0 reclaim cleanly before seat-0's next turn");
        }

        private static void TestOperatorSeatEligibilityAndAuthority()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine, new[] { "seat-0" });

            Check(!state.IsEligibleSeat("seat-1"),
                "eligibility: opponent LLM seat-1 is not operator-owned");
            Check(!state.RequestReclaimToHuman(engine, "seat-1", out _),
                "eligibility: cannot reclaim non-owned opponent seat-1");
            Check(!state.RequestHandoverToAi(engine, "seat-1", out _),
                "eligibility: cannot hand over non-owned opponent seat-1");
            Check(!state.RequestHandoverToAi(engine, "unknown-seat", out _),
                "eligibility: unknown seat is rejected");
        }

        private static void TestFullProductionFakeCyclePrivacyAndDiplomacy()
        {
            // Place seat-1 (Shu) first in play order so in Season 1/3 Shu acts before Wei.
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm, seat1First: true);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine, new[] { "seat-0" });
            int levyCost = BalanceConfig.CommandPointCost(ActionType.LevyTax);

            // Season 1: Shu does private Research; then Wei human acts, cancels once, re-requests.
            engine.State.FindFaction("seat-1").ResearchPoints = 50;
            int tokB1 = engine.BeginSeatTurn("seat-1");
            engine.SubmitAs("seat-1", tokB1, ControllerType.Llm, new ActionCommand
            {
                Type = ActionType.Research,
                Reason = "shu-secret-research-canary",
                Args = new Dictionary<string, string> { { "line", ResearchLine.Military } },
            });
            engine.EndSeatTurn(tokB1);

            int tokA1 = engine.BeginSeatTurn("seat-0");
            int cpBeforeHuman = engine.State.FindFaction("seat-0").CommandPoints;
            ValidationResult humanAct = engine.SubmitAs("seat-0", tokA1, ControllerType.Human, new ActionCommand
            {
                Type = ActionType.LevyTax,
                TargetId = "city-wei",
                Reason = "human-season1-tax",
            });
            // Controller-bound rejection: wrong controller during Human turn
            ValidationResult wrongCtrl = engine.SubmitAs("seat-0", tokA1, ControllerType.Llm, new ActionCommand
            {
                Type = ActionType.LevyTax,
                TargetId = "city-wei",
            });
            Check(humanAct.Ok && !wrongCtrl.Ok && wrongCtrl.Error == "controller mismatch"
                  && cpBeforeHuman - engine.State.FindFaction("seat-0").CommandPoints == levyCost,
                "cycle: human order succeeds, wrong-controller submission rejected at zero CP cost");

            Check(state.RequestHandoverToAi(engine, "seat-0", out _)
                  && state.CancelPending(engine, "seat-0", out _)
                  && state.RequestHandoverToAi(engine, "seat-0", out _),
                "cycle: request -> cancel -> re-request succeeded before turn close");
            Check(engine.State.FindFaction("seat-0").CommandPoints == cpBeforeHuman - levyCost,
                "cycle: handover request/cancel/re-request consume zero CP");

            state.CapturePreHandoverBaselineIfPending(engine, "seat-0", engine.Observe("seat-0"));
            engine.EndSeatTurn(tokA1);
            Check(state.SyncBoundaryAfterEngineChange(engine, "seat-0", engine.Observe("seat-0")).Applied
                  && engine.State.FindFaction("seat-0").Controller == ControllerType.Llm,
                "cycle: seat-0 transitions to LLM at EndSeatTurn");
            engine.EndSeason();
            engine.BeginSeason();

            // Season 2: Shu runs LLM on shared runner (secret rationale + NAP offer to Wei), then Wei runs LLM on same runner.
            var fake = new FakeLlmTransport();
            LlmConfig config = LlmConfig.RichDeliberation();
            config.Provider = "fake";
            config.Model = "fake-model";
            config.EnableDiplomacy = true;
            var runner = new LlmTurnRunner(fake, config, null);

            int tokB2 = engine.BeginSeatTurn("seat-1");
            bool endedB2 = false;
            var hostB2 = new SingleLlmTurnHost(
                engine, config, "seat-1",
                () => engine.Observe("seat-1"),
                cmd => engine.SubmitAs("seat-1", tokB2, ControllerType.Llm, cmd),
                () => { endedB2 = true; engine.EndSeatTurn(tokB2); });
            fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-shu\",\"summary\":\"SHU-SECRET-RATIONALE\"}", 0, 100, 20);
            fake.EnqueueSuccess("{\"type\":\"propose_treaty\",\"target_id\":\"seat-0\",\"args\":{\"treaty_type\":\"nap\",\"duration\":\"4\"},\"text\":\"wei-shu-nap-offer-s2\",\"summary\":\"SHU-NAP-SUMMARY\"}", 0, 100, 20);
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"SHU-DONE\"}", 0, 100, 20);
            runner.Begin(hostB2, tokB2, "seat-1", 12345);
            DrainRunner(runner);
            if (!endedB2) engine.EndSeatTurn(tokB2);

            // Wei runs LLM turn (with async delay so RequestReclaimToHuman happens while UI lock is active)
            fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-wei\",\"summary\":\"Reinforce Wei Capital walls\",\"reason\":\"Keep capital safe\"}", 60, 100, 20);
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"Done for season 2\"}", 20, 100, 20);
            int tokA2 = engine.BeginSeatTurn("seat-0");
            bool endedA2 = false;
            var hostA2 = new SingleLlmTurnHost(
                engine, config, "seat-0",
                () => engine.Observe("seat-0"),
                cmd => engine.SubmitAs("seat-0", tokA2, ControllerType.Llm, cmd),
                () => { endedA2 = true; engine.EndSeatTurn(tokA2); });
            runner.Begin(hostA2, tokA2, "seat-0", 12345);

            // Reclaim requested mid-LLM turn while UI game input would be locked: must NOT interrupt active runner!
            Check(state.RequestReclaimToHuman(engine, "seat-0", out _),
                "cycle: reclaim requested while seat-0 LLM turn is active");
            Check(runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing,
                "cycle: active LLM turn is NOT interrupted by reclaim request");

            DrainRunner(runner);
            if (!endedA2) engine.EndSeatTurn(tokA2);

            string weiPrompt = fake.Requests[3].User;
            Check(weiPrompt.Contains("human-season1-tax") && weiPrompt.Contains("wei-shu-nap-offer-s2")
                  && weiPrompt.Contains("controller_handover_effective"),
                "fairness: incoming Wei LLM prompt carries entitled human action, pending diplomacy and handover event");
            Check(!weiPrompt.Contains("SHU-SECRET-RATIONALE") && !weiPrompt.Contains("SHU-NAP-SUMMARY")
                  && !weiPrompt.Contains("city-shu") && !weiPrompt.Contains("shu-secret-research-canary"),
                "privacy: incoming Wei LLM prompt excludes Shu private rationale, city and research");

            state.RecordLlmTurnForSeat(
                "seat-0", 2, engine.Observe("seat-0"), runner.ActivityForSeat("seat-0"),
                runner.EndReasonText, runner.FailureReason, runner.RequestsThisTurn, runner.ActionsThisTurn,
                runner.ContextReportJsonForSeat("seat-0"), runner.SpatialTruncationForSeat("seat-0"),
                PromptBuilder.MaxPromptObservationEvents);
            Check(state.SyncBoundaryAfterEngineChange(engine, "seat-0", engine.Observe("seat-0")).Applied
                  && engine.State.FindFaction("seat-0").Controller == ControllerType.Human
                  && state.HasPendingBriefing("seat-0"),
                "cycle: reclaim applied automatically at seat-0 EndSeatTurn and briefing marked ready");

            engine.EndSeason();
            engine.BeginSeason();

            // Season 3: Shu acts first and proposes fresh NAP to Wei so returning human sees pending incoming proposal.
            int tokB3 = engine.BeginSeatTurn("seat-1");
            engine.SubmitAs("seat-1", tokB3, ControllerType.Llm, new ActionCommand
            {
                Type = ActionType.ProposeTreaty,
                TargetId = "seat-0",
                Args = new Dictionary<string, string> { { "treaty_type", TreatyType.Nap }, { "duration", "4" } },
                Text = "wei-shu-nap-offer-s3",
            });
            engine.EndSeatTurn(tokB3);

            // Build Wei's briefing upon reclaim and verify accuracy + privacy
            HandoverReclaimBriefing briefing = HandoverBriefingBuilder.Build(
                "seat-0", engine.Observe("seat-0"), state.GetEpisode("seat-0"));
            string text = briefing.FormatPlainText();
            Check(text.Contains("Reinforce Wei Capital walls") && text.Contains("controller_handover_effective"),
                "briefing: shows Wei's model-authored statement and handover effective event", text);
            Check(text.Contains("Incoming proposal: Shu offers nap (4 seasons)"),
                "briefing: shows Shu's pending NAP proposal to Wei");
            Check(!text.Contains("SHU-SECRET-RATIONALE") && !text.Contains("SHU-DONE"),
                "privacy: Wei briefing excludes Shu's LLM statements");
            Check(!text.Contains("shu-secret-research-canary") && !text.Contains("military") && !text.Contains("research"),
                "privacy: Wei briefing excludes Shu's private research");

            IReadOnlyList<LlmActivity> weiChats = state.GetEpisodeActivities("seat-0");
            Check(weiChats.Count > 0 && weiChats.All(a => a.SeatId == "seat-0"),
                "privacy: GetEpisodeActivities('seat-0') isolates Wei's chat from Shu");

            // Returning human acts on seat-0 with fresh token; stale LLM token rejected
            int tokA3 = engine.BeginSeatTurn("seat-0");
            ValidationResult staleLlm = engine.SubmitAs("seat-0", tokA2, ControllerType.Llm, new ActionCommand
            {
                Type = ActionType.Reclaim,
                TargetId = "city-wei",
            });
            ValidationResult reclaimAct = engine.SubmitAs("seat-0", tokA3, ControllerType.Human, new ActionCommand
            {
                Type = ActionType.Reclaim,
                TargetId = "city-wei",
            });
            engine.EndSeatTurn(tokA3);
            Check(!staleLlm.Ok && staleLlm.Error == "stale turn token" && reclaimAct.Ok,
                "cycle: stale LLM token rejected while reclaimed human Reclaim order executes cleanly");

            var seat0Actions = engine.Log.Records.Where(r => r.EventType == "action" && r.ActorSeatId == "seat-0").ToList();
            Check(seat0Actions.Select(r => r.ControllerType).SequenceEqual(
                      new[] { ControllerType.Human, ControllerType.Llm, ControllerType.Human }),
                "cycle: accepted action stamps trace human -> llm -> human");
        }

        private static void TestSharedRunnerTelemetryAttribution()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Llm, ControllerType.Llm);
            var fake = new FakeLlmTransport();
            LlmConfig richConfig = LlmConfig.RichDeliberation();
            richConfig.Provider = "fake";
            richConfig.Model = "fake-model";
            var runner = new LlmTurnRunner(fake, richConfig, null);

            int tA = engine.BeginSeatTurn("seat-0");
            bool endedA = false;
            var hostA = new SingleLlmTurnHost(engine, richConfig, "seat-0",
                () => engine.Observe("seat-0"),
                cmd => engine.SubmitAs("seat-0", tA, ControllerType.Llm, cmd),
                () => { endedA = true; engine.EndSeatTurn(tA); });
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 50, 10);
            runner.Begin(hostA, tA, "seat-0", 1);
            DrainRunner(runner);
            if (!endedA) engine.EndSeatTurn(tA);

            string seat0Ctx = runner.ContextReportJsonForSeat("seat-0");
            Check(!string.IsNullOrEmpty(seat0Ctx) && runner.LastContextSeatId == "seat-0",
                "telemetry: seat-0 rich context report attributed to seat-0");

            // Now seat-1 begins with a prompt-size failure or immediate end; telemetry must NOT attribute seat-0's report to seat-1.
            Check(runner.ContextReportJsonForSeat("seat-1") == null,
                "telemetry: unrun seat-1 has null context report");

            int tB = engine.BeginSeatTurn("seat-1");
            bool endedB = false;
            var hostB = new SingleLlmTurnHost(engine, richConfig, "seat-1",
                () => engine.Observe("seat-1"),
                cmd => engine.SubmitAs("seat-1", tB, ControllerType.Llm, cmd),
                () => { endedB = true; engine.EndSeatTurn(tB); });
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 50, 10);
            runner.Begin(hostB, tB, "seat-1", 1);
            DrainRunner(runner);
            if (!endedB) engine.EndSeatTurn(tB);

            Check(runner.LastContextSeatId == "seat-1" && !string.IsNullOrEmpty(runner.ContextReportJsonForSeat("seat-1")),
                "telemetry: seat-1 telemetry attributed exclusively to seat-1 after seat-1 runs");
        }

        private static void TestLongHistoryTruncationAndNoHiddenCounts()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            // Emit >30 seat-0 events and 15 secret seat-1 events
            for (int i = 0; i < 35; i++)
            {
                int ta = engine.BeginSeatTurn("seat-0");
                engine.SubmitAs("seat-0", ta, ControllerType.Human, new ActionCommand
                {
                    Type = ActionType.LevyTax,
                    TargetId = "city-wei",
                    Reason = "h-marker-" + i,
                });
                engine.EndSeatTurn(ta);
                engine.State.FindFaction("seat-0").CommandPoints = 3;
            }
            for (int i = 0; i < 15; i++)
            {
                int tb = engine.BeginSeatTurn("seat-1");
                engine.SubmitAs("seat-1", tb, ControllerType.Llm, new ActionCommand
                {
                    Type = ActionType.Scout,
                    TargetId = "city-shu",
                });
                engine.EndSeatTurn(tb);
                engine.State.FindFaction("seat-1").CommandPoints = 3;
            }

            Observation obsWei = engine.Observe("seat-0");
            int canonicalCount = engine.Log.Records.Count;
            string standardPrompt = PromptBuilder.UserPrompt(
                obsWei, ActionCatalogue.Build(obsWei), Array.Empty<string>(), 4, 3);
            Check(obsWei.Events.Count > PromptBuilder.MaxPromptObservationEvents
                  && canonicalCount > obsWei.Events.Count
                  && standardPrompt.Contains($"Observation events truncated to newest {PromptBuilder.MaxPromptObservationEvents} of {obsWei.Events.Count} seat-permitted events.")
                  && !standardPrompt.Contains($"of {canonicalCount}"),
                "truncation: standard prompt labels permitted-event truncation without revealing canonical total",
                $"obs={obsWei.Events.Count} canonical={canonicalCount}");

            var episode = new HandoverDelegationEpisode
            {
                SeatId = "seat-0",
                FactionName = "Wei",
                StartedSeason = 2,
                HasPreHandoverBaseline = false,
                ActivityRetentionDropped = true,
            };
            episode.Turns.Add(new HandoverSeatTurnCapture
            {
                SeatId = "seat-0",
                Season = 2,
                EndReasonText = "Finished - action limit (2/2)",
                RequestsThisTurn = 2,
                ActionsThisTurn = 2,
                ActivityGapDetected = true,
                ContextReportJson =
                    "{\"trim_level\":3,\"fits\":true,\"prompt_chars\":39000,\"max_prompt_chars\":40000," +
                    "\"sections\":[{\"name\":\"observation events (newest kept)\",\"included\":30,\"available\":45,\"truncated\":true}]}",
                SpatialTruncation = new SpatialTruncationReport
                {
                    Truncated = true,
                    MoveOptionsDropped = 12,
                    VisibleCellRangesDropped = 5,
                    StaleIntelDropped = 1,
                },
                PromptEventCap = 30,
                ObservationEventsAtTurnEnd = 45,
            });

            HandoverReclaimBriefing briefing = HandoverBriefingBuilder.Build("seat-0", null, episode);
            string text = briefing.FormatPlainText();
            Check(text.Contains("Truncated sections: observation events (newest kept) (30/45)")
                  && text.Contains("omitted 12 own move options")
                  && text.Contains("Runner activity trace was unavailable or evicted")
                  && text.Contains("Other factions' private tech, notes, and unseen events: hidden (not shown and never counted)"),
                "truncation: briefing labels rich/spatial/activity omissions without hidden counts");
        }

        private static void TestProviderFailureAndStaleReplies()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Llm, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine, new[] { "seat-0" }, allowHandover: true);

            // Simulate deferred reply that arrives after runner cancellation and reclaim
            var fake = new FakeLlmTransport();
            var deferred = fake.EnqueueDeferred();
            var cfg = new LlmConfig { MaxTurnSeconds = 30 };
            var runner = new LlmTurnRunner(fake, cfg, null);

            int tok1 = engine.BeginSeatTurn("seat-0");
            bool ended = false;
            var host = new SingleLlmTurnHost(engine, cfg, "seat-0",
                () => engine.Observe("seat-0"),
                cmd => engine.SubmitAs("seat-0", tok1, ControllerType.Llm, cmd),
                () => { ended = true; engine.EndSeatTurn(tok1); });
            runner.Begin(host, tok1, "seat-0", 1);
            runner.Cancel("provider timeout simulation");
            if (!ended) engine.EndSeatTurn(tok1);

            state.RecordLlmTurnForSeat(
                "seat-0", 1, engine.Observe("seat-0"), runner.ActivityForSeat("seat-0"),
                runner.EndReasonText, "network unreachable", 1, 0,
                runner.ContextReportJsonForSeat("seat-0"), runner.SpatialTruncationForSeat("seat-0"),
                PromptBuilder.MaxPromptObservationEvents);

            Check(!state.PrepareAndSyncBoundary(engine, "seat-0", engine.Observe("seat-0")).Applied
                  && engine.State.FindFaction("seat-0").Controller == ControllerType.Llm,
                "failure: provider failure does not automatically revert control to Human");

            Check(state.RequestReclaimToHuman(engine, "seat-0", out _),
                "failure: human can explicitly request reclaim after provider failure");
            HandoverTransferOutcome explicitReclaim = state.PrepareAndSyncBoundary(engine, "seat-0", engine.Observe("seat-0"));
            Check(explicitReclaim.Applied && engine.State.FindFaction("seat-0").Controller == ControllerType.Human,
                "failure: explicit human reclaim transfers control cleanly after failure");

            int tokHuman = engine.BeginSeatTurn("seat-0");
            int cpBefore = engine.State.FindFaction("seat-0").CommandPoints;
            deferred.TrySetResult(new LlmResponse { Ok = true, Text = "{\"type\":\"levy_tax\",\"target_id\":\"city-wei\"}" });
            DrainRunner(runner);
            ValidationResult lateSubmit = engine.SubmitAs("seat-0", tok1, ControllerType.Llm, new ActionCommand
            {
                Type = ActionType.LevyTax,
                TargetId = "city-wei",
            });
            Check(!lateSubmit.Ok && engine.State.FindFaction("seat-0").CommandPoints == cpBefore,
                "stale: deferred late reply and stale token submit execute nothing and preserve CP");
        }
    }
}
