using System;
using System.Linq;
using LuanShi.Engine;

// [RES-3] Feature off: the real runner must refuse research exactly as it did before this slice.
//
// Nothing here needs the opt-in switch beyond its default of "off", so the disabled contract holds
// with or without Codex's production wiring. RunLlmResearchRunner calls this before the [RES-2]
// wiring gate for exactly that reason.

internal static partial class Harness
{
    private static void ResRunnerFeatureOff()
    {
        var engine = ResRunnerWorld(9800, 100);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(ResJson(ResearchLine.Agri));
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, ResRunnerConfig(research: false), null);
        runner.Begin(host, host.Token, "seat-a", 3);

        string system = fake.Requests[0].System;
        string user = fake.Requests[0].User;
        ResRunnerCheck(!system.Contains("RESEARCH ORDERS")
           && !user.Contains("type=research")
           && user.Contains("Prompt version: " + PromptBuilder.PromptVersion + "\n")
           && system == PromptBuilder.SystemPromptFor(true),
            "with research off the prompt carries no research rules, candidates or identity");

        LlmDrain(runner);
        var seat = engine.State.FindFaction("seat-a");
        bool logged = engine.Log.Records.Any(r => r.EventType == "research"
            || (r.EventType == "action" && r.Payload.TryGetValue("action", out string type)
                && string.Equals(type, ActionType.Research, StringComparison.Ordinal)));
        ResRunnerCheck(host.SubmitCalls == 0
           && runner.RecentHistory.Contains("invalid model response -> unsupported action type"),
            "a research reply is an unsupported action type and never reaches the engine");
        ResRunnerCheck(seat.ResearchLevel(ResearchLine.Agri) == 0 && seat.ResearchPoints == 100
           && seat.CommandPoints == BalanceConfig.CommandPointsPerSeason && !logged,
            "the rejected reply buys no level, spends no points or orders and logs no research action");
        ResRunnerCheck(runner.State == RunnerState.Finished
           && runner.EndReason == EndReason.ModelEndedTurn
           && fake.CallCount == 2 && runner.InvalidThisTurn == 1,
            "the turn continues from the rejected reply to the model's end_turn");
    }
}
