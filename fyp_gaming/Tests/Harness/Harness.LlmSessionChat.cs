using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmSessionChat()
    {
        RunChatScrollLogic();
        RunChatEntries();
        RunChatTerminalReasons();
        RunChatPrivacy();
        RunChatStaleDisplayReset();
        RunChatPromptVersions();
        RunChatSeasonMeasurements();
    }

    private static void RunChatScrollLogic()
    {
        Section("Live session chat: scroll clamp and follow decision [CHAT]");

        Check(ChatScrollLogic.MaxScroll(500f, 200f) == 300f
              && ChatScrollLogic.MaxScroll(100f, 200f) == 0f,
            "[CHAT] empty and overflowing content give a non-negative scroll range");

        Check(ChatScrollLogic.ClampOffset(-12f, 300f) == 0f
              && ChatScrollLogic.ClampOffset(320f, 300f) == 300f
              && ChatScrollLogic.ClampOffset(25f, -5f) == 0f,
            "[CHAT] scroll offsets are clamped to [0, max]");

        Check(ChatScrollLogic.OffsetAfterWheel(100f, 1f, 24f, 300f) == 124f
              && ChatScrollLogic.OffsetAfterWheel(200f, -3f, 24f, 300f) == 128f
              && ChatScrollLogic.OffsetAfterWheel(300f, 1f, 24f, 300f) == 300f,
            "[CHAT] one wheel notch moves by the line step and cannot leave the range");

        Check(ChatScrollLogic.FollowsAfterScroll(300f, 300f)
              && ChatScrollLogic.FollowsAfterScroll(0f, 0f)
              && ChatScrollLogic.FollowsAfterScroll(299.6f, 300f)
              && !ChatScrollLogic.FollowsAfterScroll(299f, 300f),
            "[CHAT] follow is on only at the bottom, with a half-pixel tolerance");
    }

    private static void RunChatEntries()
    {
        Section("Live session chat: progress, explanation, command, result, end reason [CHAT]");

        var engine = LlmEngine();
        var fake = new FakeLlmTransport();
        var config = LlmKeyedConfig();
        config.MaxRequestsPerTurn = 6;
        config.MaxActionsPerTurn = 5;
        config.MaxTotalRequests = 50;
        var runner = new LlmTurnRunner(fake, config, null);
        var host = new LlmHarnessHost(engine, "seat-a");
        fake.EnqueueSuccess("{\"type\":\"recruit\",\"target_id\":\"city-a\","
            + "\"args\":{\"unit_type\":\"infantry\"},"
            + "\"summary\":\"I will recruit infantry to defend the city.\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\","
            + "\"summary\":\"I will end the turn because no further useful action remains.\"}");

        runner.Begin(host, host.Token, "seat-a", 99);
        LlmDrain(runner);

        var entries = runner.Activity;
        var firstRequest = entries.FirstOrDefault(e => e.Kind == "request");
        Check(firstRequest != null
              && firstRequest.Text.Contains("Request 1 of 6")
              && firstRequest.Text.Contains("waiting for provider")
              && firstRequest.Season > 0,
            "[CHAT] request progress is honest status text and carries the season");
        Check(entries.Any(e => e.Kind == "decision"
                  && e.Text.StartsWith("I will recruit", StringComparison.Ordinal)),
            "[CHAT] the model's brief explanation is retained as a decision entry");
        var command = entries.FirstOrDefault(e => e.Kind == "command");
        Check(command != null && command.Text == "recruit city-a (infantry)",
            "[CHAT] the chosen command names the target and key argument");
        Check(entries.Any(e => e.Kind == "result" && e.Text == "Accepted by engine."),
            "[CHAT] engine acceptance is shown in safe language");
        Check(runner.EndReason == EndReason.ModelEndedTurn
              && runner.EndReasonText == "Finished - model ended turn",
            "[CHAT] the runner exposes a stable voluntary-end reason");
        Check(entries.Last().Kind == "finished"
              && entries.Last().Text == runner.EndReasonText,
            "[CHAT] the turn completion entry carries the clear end reason");

        var longFake = new FakeLlmTransport();
        var longRunner = new LlmTurnRunner(longFake, LlmKeyedConfig(), null);
        longFake.EnqueueError(LlmErrorKind.Network, new string('x', 500) + LlmFakeKey);
        var longHost = new LlmHarnessHost(LlmEngine(), "seat-a");
        longRunner.Begin(longHost, longHost.Token, "seat-a", 99);
        LlmDrain(longRunner);
        var errorEntry = longRunner.Activity.LastOrDefault(e => e.Kind == "error");
        Check(errorEntry != null && errorEntry.Text.Length <= 320
              && !errorEntry.Text.Contains(LlmFakeKey),
            "[CHAT] entry text is redacted and bounded to 320 characters");
    }

    private static void RunChatTerminalReasons()
    {
        Section("Live session chat: every terminal path has one clear end entry [CHAT]");

        CheckChatTerminal("model end_turn", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            LlmDrain(runner);
            return (runner, EndReason.ModelEndedTurn, "Finished - model ended turn");
        });

        CheckChatTerminal("action limit", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxRequestsPerTurn = 5, MaxActionsPerTurn = 1 }, null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            LlmDrain(runner);
            return (runner, EndReason.ActionLimit, "Finished - action limit (1/1)");
        });

        CheckChatTerminal("request limit", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxRequestsPerTurn = 1, MaxActionsPerTurn = 5 }, null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            LlmDrain(runner);
            return (runner, EndReason.RequestLimit, "Finished - request limit (1/1)");
        });

        CheckChatTerminal("invalid-response limit", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("not json");
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxInvalidPerTurn = 1 }, null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            LlmDrain(runner);
            return (runner, EndReason.InvalidResponseLimit, "Stopped - invalid-response limit");
        });

        CheckChatTerminal("time limit", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            var runner = new LlmTurnRunner(fake, new LlmConfig { MaxTurnSeconds = 0 }, null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            return (runner, EndReason.TurnTimeLimit, "Stopped - time limit");
        });

        CheckChatTerminal("session request budget", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var runner = new LlmTurnRunner(fake, new LlmConfig { MaxTotalRequests = 1 }, null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            LlmDrain(runner);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 2, "seat-a", 5);
            return (runner, EndReason.SessionRequestLimit, "Stopped - session request limit");
        });

        CheckChatTerminal("provider failure", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueError(LlmErrorKind.Timeout, "slow");
            var runner = new LlmTurnRunner(fake, LlmKeyedConfig(), null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            LlmDrain(runner);
            return (runner, EndReason.ProviderError, "Failed - provider timeout");
        });

        CheckChatTerminal("host observation failure", () =>
        {
            var engine = SpectatorEngine();
            var fake = new FakeLlmTransport();
            var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
            runner.Begin(new B4WrongSeatHost(engine, "seat-a", "seat-b"), 1, "seat-a", 5);
            return (runner, EndReason.HostError, "Failed - host observation seat mismatch");
        });

        CheckChatTerminal("host cancellation", () =>
        {
            var engine = LlmEngine();
            var fake = new FakeLlmTransport();
            fake.EnqueueDeferred();
            var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
            runner.Begin(new LlmHarnessHost(engine, "seat-a"), 1, "seat-a", 5);
            runner.Cancel("user cancelled");
            return (runner, EndReason.Cancelled, "Cancelled - user cancelled");
        });
    }

    private static void CheckChatTerminal(string label,
        Func<(LlmTurnRunner Runner, EndReason Reason, string Text)> scenario)
    {
        var result = scenario();
        LlmTurnRunner runner = result.Runner;
        var finals = runner.Activity.Where(e => e.Kind == "finished").ToList();
        int turnToken = finals.Count == 0 ? 0 : finals[finals.Count - 1].TurnToken;
        var currentFinals = finals.Where(e => e.TurnToken == turnToken).ToList();
        Check(runner.EndReason == result.Reason
              && runner.EndReasonText == result.Text
              && currentFinals.Count == 1
              && currentFinals[0].Text == result.Text,
            "[CHAT] " + label + " records exactly one clear end reason");
    }
}
