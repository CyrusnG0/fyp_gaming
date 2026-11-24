using System;
using System.Collections.Generic;
using LuanShi;
using LuanShi.Engine;

namespace LuanShi.HandoverUiCheck
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private sealed class SimulatedSeamAdapter : IHandoverEngineAdapter
        {
            public bool IsTransferSupported => true;
            public string UnavailableReason => null;
            public int Calls;

            public HandoverTransferOutcome TryTransferBetweenTurns(
                GameEngine engine, string seatId, string targetController, string reason)
            {
                Calls++;
                if (engine == null) return HandoverTransferOutcome.Failed("missing engine");
                FactionState fac = engine.State.FindFaction(seatId);
                if (fac == null) return HandoverTransferOutcome.Failed("unknown seat");
                fac.Controller = targetController;
                return HandoverTransferOutcome.Succeeded(seatId + " -> " + targetController);
            }
        }

        public static int Main()
        {
            TestBaselineDeferredAdapter();
            TestPendingCancelAndBoundaryIsolation();
            TestOperatorSeatEligibility();
            TestCycleBriefingAndCrossSeatPrivacy();
            TestTruncatedContextAndMissingActivityLabels();
            TestProviderFailureDoesNotAutoRevert();

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

        private static void TestBaselineDeferredAdapter()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine.State.Factions, new[] { "seat-0" });

            bool reqOk = state.RequestHandoverToAi("seat-0", "Wei", ControllerType.Human, true, 1, out _);
            Check(reqOk, "baseline: human can request handover to AI");

            var adapter = new BaselineDeferredHandoverAdapter();
            HandoverTransferOutcome outcome = state.ApplyPendingAtSafeBoundary(
                engine, "seat-0", null, adapter, engine.Observe("seat-0"));
            Check(outcome.Unsupported && !outcome.Applied,
                "baseline: BaselineDeferredHandoverAdapter returns Unsupported without faking transfer");
            Check(engine.State.FindFaction("seat-0").Controller == ControllerType.Human,
                "baseline: FactionState.Controller stays Human when adapter is unsupported");
            Check(!state.TryGetPending("seat-0", out _),
                "baseline: pending badge clears instead of stranding after deferred check");
        }

        private static void TestPendingCancelAndBoundaryIsolation()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine.State.Factions, new[] { "seat-0" });
            var adapter = new SimulatedSeamAdapter();

            Check(state.RequestHandoverToAi("seat-0", "Wei", ControllerType.Human, true, 1, out _),
                "pending: first handover request accepted");
            Check(!state.RequestHandoverToAi("seat-0", "Wei", ControllerType.Human, true, 1, out _),
                "pending: second request rejected without implicit supersession");
            Check(state.CancelPending("seat-0", out _),
                "pending: explicit cancellation removes pending handover");
            Check(!state.CancelPending("seat-0", out _),
                "pending: cancelling again fails cleanly");

            Check(state.RequestHandoverToAi("seat-0", "Wei", ControllerType.Human, true, 1, out _),
                "boundary: re-requested handover after cancel");
            HandoverTransferOutcome midTurn = state.ApplyPendingAtSafeBoundary(
                engine, "seat-0", "seat-0", adapter, engine.Observe("seat-0"));
            Check(!midTurn.Applied && adapter.Calls == 0,
                "boundary: refuses mid-turn transfer when turn gate is open");

            HandoverTransferOutcome wrongSeat = state.ApplyPendingAtSafeBoundary(
                engine, "seat-1", null, adapter, engine.Observe("seat-1"));
            Check(wrongSeat.NoPending && adapter.Calls == 0 && state.TryGetPending("seat-0", out _),
                "boundary: seat-1 ending never triggers seat-0's pending transfer");

            HandoverTransferOutcome safeBoundary = state.ApplyPendingAtSafeBoundary(
                engine, "seat-0", null, adapter, engine.Observe("seat-0"));
            Check(safeBoundary.Applied && engine.State.FindFaction("seat-0").Controller == ControllerType.Llm,
                "boundary: seat-0 applies handover at its own safe boundary");
        }

        private static void TestOperatorSeatEligibility()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine.State.Factions, new[] { "seat-0" });

            Check(!state.IsEligibleSeat("seat-1"),
                "eligibility: opponent LLM seat-1 is not operator-owned");
            Check(!state.RequestReclaimToHuman("seat-1", "Shu", ControllerType.Llm, false, 1, out _),
                "eligibility: cannot reclaim non-owned opponent seat-1");
            Check(!state.RequestHandoverToAi("seat-1", "Shu", ControllerType.Llm, false, 1, out _),
                "eligibility: cannot hand over non-owned opponent seat-1");
        }

        private static void TestCycleBriefingAndCrossSeatPrivacy()
        {
            // Place seat-1 (Shu) first in play order so in Season 3 Shu proposes NAP before Wei opens its reclaimed Human turn.
            GameEngine engine = CreateTwoSeatGame(ControllerType.Human, ControllerType.Llm, seat1First: true);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine.State.Factions, new[] { "seat-0" });
            var adapter = new SimulatedSeamAdapter();

            // Season 1: Shu acts first (private Research), then Wei acts Human -> requests handover -> delegates.
            engine.State.FindFaction("seat-1").ResearchPoints = 50;
            int tokB1 = engine.BeginSeatTurn("seat-1");
            engine.SubmitAs("seat-1", tokB1, new ActionCommand
            {
                Type = ActionType.Research,
                ActorSeatId = "seat-1",
                ControllerType = ControllerType.Llm,
                Args = new Dictionary<string, string> { { "line", ResearchLine.Military } },
            });
            engine.EndSeatTurn(tokB1);

            int tok0 = engine.BeginSeatTurn("seat-0");
            ValidationResult humanAct = engine.SubmitAs("seat-0", tok0, new ActionCommand
            {
                Type = ActionType.LevyTax,
                ActorSeatId = "seat-0",
                ControllerType = ControllerType.Human,
                TargetId = "city-wei",
            });
            Check(humanAct.Ok, "cycle: human acts on seat-0");
            Check(state.RequestHandoverToAi("seat-0", "Wei", ControllerType.Human, true, 1, out _),
                "cycle: human requests handover while active");
            engine.EndSeatTurn(tok0);
            Check(state.ApplyPendingAtSafeBoundary(engine, "seat-0", null, adapter, engine.Observe("seat-0")).Applied,
                "cycle: seat-0 delegates at safe boundary after EndSeatTurn");
            engine.EndSeason();
            engine.BeginSeason();

            // Season 2: Shu runs LLM on shared runner (secret rationale), then Wei runs LLM on same runner.
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
                cmd => engine.SubmitAs("seat-1", tokB2, cmd),
                () => { endedB2 = true; engine.EndSeatTurn(tokB2); });
            fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-shu\",\"summary\":\"SHU-SECRET-RATIONALE\"}", 0, 100, 20);
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"SHU-DONE\"}", 0, 100, 20);
            runner.Begin(hostB2, tokB2, "seat-1", 12345);
            while (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
            {
                runner.Tick();
                System.Threading.Thread.Sleep(5);
            }
            if (!endedB2) engine.EndSeatTurn(tokB2);

            // Wei runs LLM turn (with async delay so RequestReclaimToHuman occurs while runner is genuinely active).
            fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-wei\",\"summary\":\"Reinforce Wei Capital walls\",\"reason\":\"Keep capital safe\"}", 60, 100, 20);
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"Done for season 2\"}", 20, 100, 20);
            int tokA2 = engine.BeginSeatTurn("seat-0");
            bool endedA2 = false;
            var hostA2 = new SingleLlmTurnHost(
                engine, config, "seat-0",
                () => engine.Observe("seat-0"),
                cmd => engine.SubmitAs("seat-0", tokA2, cmd),
                () => { endedA2 = true; engine.EndSeatTurn(tokA2); });
            runner.Begin(hostA2, tokA2, "seat-0", 12345);

            // Reclaim requested mid-LLM turn: must NOT interrupt active runner!
            Check(state.RequestReclaimToHuman("seat-0", "Wei", ControllerType.Llm, true, 2, out _),
                "cycle: reclaim requested while seat-0 LLM turn is active");
            Check(runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing,
                "cycle: active LLM turn is NOT interrupted by reclaim request",
                $"state={runner.State} end={runner.EndReasonText} fail={runner.FailureReason}");

            while (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
            {
                runner.Tick();
                System.Threading.Thread.Sleep(5);
            }
            if (!endedA2) engine.EndSeatTurn(tokA2);

            // Capture seat-0 LLM turn and apply reclaim at safe boundary
            state.RecordLlmTurnForSeat(
                "seat-0", 2, engine.Observe("seat-0"), runner.ActivityForSeat("seat-0"),
                runner.EndReasonText, runner.FailureReason, runner.RequestsThisTurn, runner.ActionsThisTurn,
                runner.LastContextReportJson, runner.LastSpatialTruncation, PromptBuilder.MaxPromptObservationEvents);
            Check(state.ApplyPendingAtSafeBoundary(engine, "seat-0", null, adapter, engine.Observe("seat-0")).Applied,
                "cycle: reclaim commits cleanly at safe boundary after LLM turn closes");
            Check(state.HasPendingBriefing("seat-0"),
                "cycle: reclaim marks seat-0 briefing ready");

            engine.EndSeason();
            engine.BeginSeason();

            // Season 3: Shu acts first and proposes NAP to Wei; then Wei opens reclaimed Human turn.
            int tokB3 = engine.BeginSeatTurn("seat-1");
            engine.SubmitAs("seat-1", tokB3, new ActionCommand
            {
                Type = ActionType.ProposeTreaty,
                ActorSeatId = "seat-1",
                ControllerType = ControllerType.Llm,
                TargetId = "seat-0",
                Args = new Dictionary<string, string> { { "treaty_type", TreatyType.Nap }, { "duration", "4" } },
                Text = "secret-offer-hello",
            });
            engine.EndSeatTurn(tokB3);

            // 4. Build Wei's briefing upon reclaim and verify accuracy + privacy
            HandoverReclaimBriefing briefing = HandoverBriefingBuilder.Build(
                "seat-0", engine.Observe("seat-0"), state.GetEpisode("seat-0"));
            string text = briefing.FormatPlainText();
            Check(text.Contains("Reinforce Wei Capital walls"),
                "briefing: contains Wei's model-authored statement", text);
            Check(text.Contains("Incoming proposal: Shu offers nap (4 seasons)"),
                "briefing: shows Shu's pending NAP proposal to Wei");
            Check(!text.Contains("SHU-SECRET-RATIONALE") && !text.Contains("SHU-DONE"),
                "privacy: Wei briefing excludes Shu's LLM statements");
            Check(!text.Contains("military") && !text.Contains("research"),
                "privacy: Wei briefing excludes Shu's private research");

            // Verify Wei's episode activities stay strictly seat-0 only
            IReadOnlyList<LlmActivity> weiChats = state.GetEpisodeActivities("seat-0");
            bool anyForeignChat = false;
            for (int i = 0; i < weiChats.Count; i++)
                if (weiChats[i].SeatId != "seat-0") anyForeignChat = true;
            Check(weiChats.Count > 0 && !anyForeignChat,
                "privacy: GetEpisodeActivities('seat-0') isolates Wei's chat from Shu");

            // 5. Returning human acts on seat-0
            int tokA3 = engine.BeginSeatTurn("seat-0");
            ValidationResult reclaimAct = engine.SubmitAs("seat-0", tokA3, new ActionCommand
            {
                Type = ActionType.Reclaim,
                ActorSeatId = "seat-0",
                ControllerType = ControllerType.Human,
                TargetId = "city-wei",
            });
            engine.EndSeatTurn(tokA3);
            Check(reclaimAct.Ok, "cycle: reclaimed human submits Reclaim order cleanly");
        }

        private static void TestTruncatedContextAndMissingActivityLabels()
        {
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
                    "\"sections\":[{\"name\":\"observation events (newest kept)\",\"included\":30,\"available\":45,\"truncated\":true}," +
                    "{\"name\":\"other factions' private information\",\"included\":0,\"available\":null,\"truncated\":false}]}",
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
            Check(text.Contains("Truncated sections: observation events (newest kept) (30/45)"),
                "truncation: labels rich prompt section omissions with permitted counts only");
            Check(text.Contains("omitted 12 own move options"),
                "truncation: labels spatial movement omissions");
            Check(text.Contains("Runner activity trace was unavailable or evicted"),
                "truncation: labels missing runner activity trace");
            Check(text.Contains("Other factions' private tech, notes, and unseen events: hidden (not shown and never counted)"),
                "privacy: explicitly avoids exposing hidden event counts");
        }

        private static void TestProviderFailureDoesNotAutoRevert()
        {
            GameEngine engine = CreateTwoSeatGame(ControllerType.Llm, ControllerType.Llm);
            var state = new HandoverCoordinatorState();
            state.Initialize(engine.State.Factions, new[] { "seat-0" });
            var adapter = new SimulatedSeamAdapter();

            state.RecordLlmTurnForSeat(
                "seat-0", 1, engine.Observe("seat-0"), Array.Empty<LlmActivity>(),
                "Failed - transport error", "network unreachable", 1, 0,
                null, null, PromptBuilder.MaxPromptObservationEvents);

            HandoverTransferOutcome noAutoRevert = state.ApplyPendingAtSafeBoundary(
                engine, "seat-0", null, adapter, engine.Observe("seat-0"));
            Check(noAutoRevert.NoPending && adapter.Calls == 0
                  && engine.State.FindFaction("seat-0").Controller == ControllerType.Llm,
                "failure: provider failure does not automatically revert control to Human");

            Check(state.RequestReclaimToHuman("seat-0", "Wei", ControllerType.Llm, false, 1, out _),
                "failure: human can still explicitly request reclaim after provider failure");
            HandoverTransferOutcome explicitReclaim = state.ApplyPendingAtSafeBoundary(
                engine, "seat-0", null, adapter, engine.Observe("seat-0"));
            Check(explicitReclaim.Applied && engine.State.FindFaction("seat-0").Controller == ControllerType.Human,
                "failure: explicit human reclaim transfers control cleanly after failure");
        }
    }
}
