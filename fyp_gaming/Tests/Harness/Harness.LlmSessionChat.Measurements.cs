using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunChatPrivacy()
    {
        Section("Live session chat: activity never enters prompts and is detached [CHAT]");

        const string sentinel = "OTHER-SEAT-ACTIVITY-SENTINEL";
        var engine = SpectatorEngine();
        var config = new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 20 };
        var fakeA = new FakeLlmTransport();
        var runnerA = new LlmTurnRunner(fakeA, config, null);

        fakeA.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"" + sentinel + "\"}");
        var hostB = new LlmHarnessHost(engine, "seat-b");
        runnerA.Begin(hostB, hostB.Token, "seat-b", 7);
        LlmDrain(runnerA);

        fakeA.EnqueueDeferred();
        var hostA = new LlmHarnessHost(engine, "seat-a");
        runnerA.Begin(hostA, hostA.Token, "seat-a", 7);

        var fakeB = new FakeLlmTransport();
        fakeB.EnqueueDeferred();
        var runnerB = new LlmTurnRunner(fakeB, config, null);
        var hostA2 = new LlmHarnessHost(engine, "seat-a");
        runnerB.Begin(hostA2, hostA2.Token, "seat-a", 7);

        LlmRequest withActivity = fakeA.Requests[fakeA.Requests.Count - 1];
        LlmRequest withoutActivity = fakeB.Requests[0];
        Check(withActivity.System == withoutActivity.System
              && withActivity.User == withoutActivity.User,
            "[CHAT] system+user prompts are byte-identical with and without activity present");
        Check(!withActivity.System.Contains(sentinel)
              && !withActivity.User.Contains(sentinel),
            "[CHAT] another seat's activity text never enters a prompt");

        var seatBRows = runnerA.ActivityForSeat("seat-b");
        var seatARows = runnerA.ActivityForSeat("seat-a");
        Check(seatBRows.Any(e => e.Text.Contains(sentinel))
              && seatARows.All(e => !e.Text.Contains(sentinel)),
            "[CHAT] filtered activity is separate per seat");

        bool readOnly = false;
        try { ((IList<LlmActivity>)seatBRows).Clear(); }
        catch (NotSupportedException) { readOnly = true; }
        Check(readOnly && runnerA.ActivityForSeat("seat-b").Count == seatBRows.Count,
            "[CHAT] ActivityForSeat returns a detached, read-only copy");

        runnerA.Cancel("test cleanup");
        runnerB.Cancel("test cleanup");
    }

    private static void RunChatStaleDisplayReset()
    {
        Section("Live session chat: previous-turn decision/command/result do not leak [CHAT]");

        var engine = LlmEngine();
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 5, MaxActionsPerTurn = 5 }, null);
        fake.EnqueueSuccess("{\"type\":\"train\",\"target_id\":\"city-a\","
            + "\"summary\":\"stale decision text\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var host = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(host, host.Token, "seat-a", 9);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished,
            "[CHAT] fixture turn finishes before the next Begin");

        fake.EnqueueDeferred();
        var next = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(next, next.Token, "seat-a", 9);
        Check(runner.LastSummary == null && runner.LastCommandJson == null
              && runner.LastResult == null && runner.EndReason == EndReason.None,
            "[CHAT] Begin clears every previous-turn display field and the end reason");

        runner.Cancel("test cleanup");
        Check(runner.LastSummary == null && runner.LastCommandJson == null
              && runner.LastResult == null,
            "[CHAT] cancellation does not resurrect an earlier turn's display text");
        Check(runner.Activity.Count > 0, "[CHAT] fixture: chat still has this session's entries");
        runner.ResetSession();
        Check(runner.LastSummary == null && runner.LastCommandJson == null
              && runner.LastResult == null && runner.EndReason == EndReason.None
              && runner.Activity.Count == 0,
            "[CHAT] ResetSession clears display fields and the session chat");

        fake.EnqueueDeferred();
        var newGame = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(newGame, newGame.Token, "seat-a", 10);
        Check(runner.Activity.Count == 2
              && runner.Activity[0].Kind == "turn"
              && runner.Activity[1].Kind == "request"
              && runner.Activity.All(e => e.TurnToken == newGame.Token),
            "[CHAT] a new game seed clears earlier chat entries");
        runner.Cancel("test cleanup");
    }

    private static void RunChatPromptVersions()
    {
        Section("Prompt guidance: demo-slice-4 is default, legacy is switch-only [CHAT]");

        PromptBuilder.ResetPromptVersion();
        string current = PromptBuilder.SystemPrompt();
        Check(PromptBuilder.PromptVersion == "demo-slice-4"
              && PromptBuilder.ActivePromptVersion == "demo-slice-4",
            "[CHAT] the default prompt version is demo-slice-4 (planning)");
        // Guidance must not recommend a particular order. Action names are allowed in the rules /
        // schema vocabulary ("Supported orders: ..."), so only an imperative recommendation is a
        // violation; checking each catalogue type keeps this robust as the wording evolves.
        string[] actionNames =
        {
            ActionType.LevyTax, ActionType.LightenLabor, ActionType.Reclaim, ActionType.Recruit,
            ActionType.Train, ActionType.Fortify, ActionType.Relief, "end_turn",
        };
        bool recommendsAnAction = actionNames.Any(name => Regex.IsMatch(current,
            @"\b(?:you should|you must|you ought to|prefer|always|never)\b[^.\n]{0,48}\b"
            + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase));
        Check(current.Contains("Spring harvest is x0.7")
              && current.Contains("Autumn harvest is x2.0")
              && current.Contains("Winter harvest is x0.3")
              && current.Contains("food reserve")
              && current.Contains("end_turn before spending all command points")
              && !recommendsAnAction,
            "[DEMO-S4] prompt states only true food-season rules and gives no per-action recommendation");

        bool switched = PromptBuilder.TryUsePromptVersion("demo-slice-1", out string error);
        string legacy = PromptBuilder.SystemPrompt();
        Check(switched && error == null
              && PromptBuilder.ActivePromptVersion == "demo-slice-1"
              && legacy == PromptBuilder.LegacySystemPrompt()
              && PromptBuilder.SchemaHash() == "fcd1dddae210dcbc"
              && !legacy.Contains("Spring harvest is x0.7"),
            "[CHAT] the harness switch selects the exact committed demo-slice-1 prompt");

        bool domesticSwitched = PromptBuilder.TryUsePromptVersion("demo-slice-3", out string dError)
              && PromptBuilder.ActivePromptVersion == "demo-slice-3"
              && PromptBuilder.SystemPrompt() == PromptBuilder.DomesticSystemPrompt()
              && !PromptBuilder.SystemPrompt().Contains("MOVEMENT OPTIONS");
        Check(domesticSwitched && dError == null,
            "[CHAT] the harness switch can select the exact domestic demo-slice-3 prompt");

        bool rejected = !PromptBuilder.TryUsePromptVersion("demo-slice-99", out _);
        PromptBuilder.ResetPromptVersion();
        Check(rejected && PromptBuilder.ActivePromptVersion == "demo-slice-4"
              && PromptBuilder.SystemPrompt() == current,
            "[CHAT] an unknown prompt version is rejected without changing the default");
    }

    private static void RunChatSeasonMeasurements()
    {
        Section("Pilot measurement: per-season scoreboard and end/rejection counts [CHAT]");

        string dir = LlmTempDir("chat-measure");
        try
        {
            var engine = SpectatorEngine();
            var fake = new FakeLlmTransport();
            SpectatorQueueSeason(fake, 1);
            var config = new LlmConfig
            {
                MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 30,
            };
            var runner = new LlmTurnRunner(fake, config, null);
            var session = SpectatorNew(engine, runner, 1);
            session.Start();
            SpectatorDrain(session);

            Check(session.TurnRecords.Count == 2
                  && session.TurnRecords.All(r => r.EndReason == EndReason.ModelEndedTurn)
                  && session.TurnRecords.All(r => r.EndReasonText == "Finished - model ended turn")
                  && session.VoluntaryEndTurns == 2 && session.LimitEndings == 0
                  && session.TotalInvalidResponses == 0,
                "[CHAT] spectator records expose end reason and voluntary/limit counts");

            string path = SpectatorSummary.Write(dir, session, config, fake, runner);
            string text = File.ReadAllText(path);
            bool parsed = StrictJson.TryParse(text, out JsonNode root, out _);
            var obj = root as JsonObject;
            var totals = obj == null ? null : LlmNode(obj, "totals") as JsonObject;
            Check(parsed && totals != null
                  && LlmInt(totals, "voluntary_end_turns") == 2
                  && LlmInt(totals, "limit_endings") == 0
                  && LlmInt(totals, "rejected_actions") == 0
                  && LlmInt(totals, "invalid_responses") == 0,
                "[CHAT] summary totals count voluntary endings, limits, rejects and invalid replies");

            JsonNode seasonsNode = obj == null ? null : LlmNode(obj, "seasons");
            var season0 = seasonsNode is JsonArray sa && sa.Count == 1
                ? sa.Items[0] as JsonObject : null;
            var board = season0 == null ? null : LlmNode(season0, "scoreboard") as JsonArray;
            // Spring 1 fixture, seed 20261026: levy_tax (+200) then resolution (+100 tax,
            // +140 harvest, -200 eaten). Population growth is seeded and therefore exact per seat.
            Check(board != null && board.Count == 2
                  && ScoreIs(board, "seat-a", 1300, 4940, 10540, 0)
                  && ScoreIs(board, "seat-b", 1300, 4940, 10322, 0),
                "[DEMO-S4] summary scoreboard carries the exact deterministic spring values");

            var limitedEngine = SpectatorEngine();
            var limitedFake = new FakeLlmTransport();
            limitedFake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}");
            limitedFake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-b\"}");
            var limitedRunner = new LlmTurnRunner(limitedFake,
                new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 1, MaxTotalRequests = 30 }, null);
            var limitedSession = SpectatorNew(limitedEngine, limitedRunner, 1);
            limitedSession.Start();
            SpectatorDrain(limitedSession);
            Check(limitedSession.LimitEndings == 2 && limitedSession.VoluntaryEndTurns == 0
                  && limitedSession.TurnRecords.All(r => r.EndReason == EndReason.ActionLimit),
                "[CHAT] limit endings are counted separately from voluntary end_turn");
        }
        finally { LlmDelete(dir); }
    }

    private static bool ScoreIs(JsonArray board, string seatId, int gold, int food, int population,
        int garrison)
    {
        foreach (JsonNode node in board.Items)
        {
            if (!(node is JsonObject score) || LlmStr(score, "seat_id") != seatId) continue;
            return LlmInt(score, "gold") == gold
                && LlmInt(score, "food") == food
                && LlmInt(score, "population") == population
                && LlmInt(score, "garrison") == garrison;
        }
        return false;
    }
}
