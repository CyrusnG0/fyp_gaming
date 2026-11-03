using System.Linq;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void InquiryFakePlanActsAfterLookup()
    {
        GameEngine engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        Observation before = engine.Observe("seat-a");
        int points = before.CommandPoints;
        int gold = engine.State.FindCity("city-a").Gold;
        var plan = FakeTurnPlan.Build(null, before, "seat-a", true);
        var fake = new FakeLlmTransport();
        foreach (string response in plan) fake.EnqueueSuccess(response);
        var runner = new LlmTurnRunner(fake, InquiryConfig(true), null);
        runner.Begin(host, host.Token, "seat-a", 99);
        LlmDrain(runner);

        IQCheck(plan.Count == 3 && plan[0] == InquiryManualRecruit,
            "the opt-in fake rehearsal starts with lookup before its domestic order");
        IQCheck(runner.EndReason == EndReason.ModelEndedTurn && fake.CallCount == 3
                && runner.ActionsThisTurn == 1 && host.SubmitCalls == 1 && host.EndTurnCalls == 1,
            "lookup then a legal action then end_turn completes within existing request limits");
        IQCheck(InquirySection(fake.Requests[1].User) != null
                && InquirySection(fake.Requests[2].User) == null,
            "the action decision receives the answer exactly once");
        IQCheck(engine.State.FindCity("city-a").Gold == gold + BalanceConfig.TaxLevyAmount
                && engine.Observe("seat-a").CommandPoints
                   == points - BalanceConfig.CommandPointCost(ActionType.LevyTax),
            "only the accepted game action spends command points and changes city resources");
        IQCheck(FakeTurnPlan.Build(null, before, "seat-a").All(s => !s.Contains("\"tool\"")),
            "the disabled fake rehearsal remains action-only");
    }
}
