using LuanShi.Engine;

// [DEMO-D] Pure rotation planner used by the Unity host. These cases pin the resume path after an
// LLM seat without needing UnityEngine: human -> LLM -> season, LLM first, two LLM seats, and game over.

internal static partial class Harness
{
    private static void RunTurnFlowPlanner()
    {
        Section("[DEMO-D] turn-flow planner (human / LLM / scripted rotation)");

        // Human seat-a -> LLM seat-b -> season resolution.
        {
            var e = TurnGateScenario();
            var first = TurnFlowPlanner.First(e.State);
            Check(first.Action == TurnFlowAction.HumanSeat && first.SeatId == "seat-a",
                  "[DEMO-D] first seat of the demo roster is the human Wei seat");

            var llm = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            Check(llm.Action == TurnFlowAction.LlmSeat && llm.SeatId == "seat-b",
                  "[DEMO-D] after the human, the rotation stops for the LLM seat");

            var end = TurnFlowPlanner.NextAfter(e.State, "seat-b");
            Check(end.Action == TurnFlowAction.ResolveSeason && end.SeatId == null,
                  "[DEMO-D] after the last LLM seat, the rotation resolves the season");
        }

        // The first seat can itself be an LLM (no human-first special case in the planner).
        {
            var e = TurnGateScenario();
            e.State.Factions[0].Controller = ControllerType.Llm;
            var first = TurnFlowPlanner.First(e.State);
            Check(first.Action == TurnFlowAction.LlmSeat && first.SeatId == "seat-a",
                  "[DEMO-D] an LLM can be the first seat of a season");
        }

        // Two consecutive LLM seats: the resume path must start the second one, then resolve.
        {
            var e = TurnGateScenario();
            e.State.Factions[0].Controller = ControllerType.Llm;
            e.State.Factions[1].Controller = ControllerType.Llm;

            var first = TurnFlowPlanner.First(e.State);
            var second = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            var end = TurnFlowPlanner.NextAfter(e.State, "seat-b");
            Check(first.Action == TurnFlowAction.LlmSeat && first.SeatId == "seat-a"
                  && second.Action == TurnFlowAction.LlmSeat && second.SeatId == "seat-b",
                  "[DEMO-D] two consecutive LLM seats are both scheduled");
            Check(end.Action == TurnFlowAction.ResolveSeason,
                  "[DEMO-D] the second LLM seat is followed by season resolution");
        }

        // A scripted seat stays synchronous in the host but is still planned explicitly.
        {
            var e = TurnGateScenario();
            e.State.Factions[1].Controller = ControllerType.ScriptedAi;
            var scripted = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            var end = TurnFlowPlanner.NextAfter(e.State, "seat-b");
            Check(scripted.Action == TurnFlowAction.ScriptedSeat && scripted.SeatId == "seat-b",
                  "[DEMO-D] a scripted seat is delegated synchronously by the host");
            Check(end.Action == TurnFlowAction.ResolveSeason,
                  "[DEMO-D] a scripted seat still advances to season resolution");
        }

        // Game over wins over any remaining seat, both at the start and mid-rotation.
        {
            var e = TurnGateScenario();
            e.State.WinnerSeatId = "seat-a";
            var first = TurnFlowPlanner.First(e.State);
            var after = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            Check(first.Action == TurnFlowAction.GameOver && after.Action == TurnFlowAction.GameOver,
                  "[DEMO-D] game over stops the rotation before any remaining seat acts");
            Check(!first.IsSeat && !after.IsSeat, "[DEMO-D] game-over steps carry no seat id");
        }

        RunTurnFlowPlannerPlayability();
    }

    // [DEMO-S2] The host must stop at the season boundary when nobody can open the next chair.
    private static void RunTurnFlowPlannerPlayability()
    {
        Section("[DEMO-S2] turn-flow planner (playable-roster guard)");

        // An all-scripted roster still plans its one synchronous pass, then resolves once.
        {
            var e = TurnGateScenario();
            e.State.Factions[0].Controller = ControllerType.ScriptedAi;
            e.State.Factions[1].Controller = ControllerType.ScriptedAi;

            var first = TurnFlowPlanner.First(e.State);
            var second = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            var end = TurnFlowPlanner.NextAfter(e.State, "seat-b");
            Check(first.Action == TurnFlowAction.ScriptedSeat && first.SeatId == "seat-a"
                  && second.Action == TurnFlowAction.ScriptedSeat && second.SeatId == "seat-b",
                  "[DEMO-S2] an all-scripted roster plans both seats in play order");
            Check(end.Action == TurnFlowAction.ResolveSeason,
                  "[DEMO-S2] an all-scripted roster reaches the season boundary");
            Check(!TurnFlowPlanner.HasHumanOrRunnableLlm(e.State),
                  "[DEMO-S2] an all-scripted roster has no human or runnable LLM seat");
        }

        // A scripted seat before the first human must act first (play order, not human-first).
        {
            var e = TurnGateScenario();
            e.State.Factions[0].Controller = ControllerType.ScriptedAi;
            e.State.Factions[1].Controller = ControllerType.Human;

            var first = TurnFlowPlanner.First(e.State);
            var after = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            var end = TurnFlowPlanner.NextAfter(e.State, "seat-b");
            Check(first.Action == TurnFlowAction.ScriptedSeat && first.SeatId == "seat-a",
                  "[DEMO-S2] a scripted seat before the human starts the season");
            Check(after.Action == TurnFlowAction.HumanSeat && after.SeatId == "seat-b",
                  "[DEMO-S2] the human follows the scripted seat in play order");
            Check(end.Action == TurnFlowAction.ResolveSeason
                  && TurnFlowPlanner.HasHumanOrRunnableLlm(e.State),
                  "[DEMO-S2] a scripted-then-human roster is playable");
        }

        // LLM-only rosters are playable only while each model seat is runnable.
        {
            var e = TurnGateScenario();
            e.State.Factions[0].Controller = ControllerType.Llm;
            e.State.Factions[1].Controller = ControllerType.Llm;

            var first = TurnFlowPlanner.First(e.State);
            var after = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            var end = TurnFlowPlanner.NextAfter(e.State, "seat-b");
            Check(first.Action == TurnFlowAction.LlmSeat && first.SeatId == "seat-a"
                  && after.Action == TurnFlowAction.LlmSeat && after.SeatId == "seat-b"
                  && end.Action == TurnFlowAction.ResolveSeason,
                  "[DEMO-S2] an LLM-only roster plans both model seats");
            Check(TurnFlowPlanner.HasHumanOrRunnableLlm(e.State),
                  "[DEMO-S2] an LLM-only roster is playable by default");
            Check(!TurnFlowPlanner.HasHumanOrRunnableLlm(e.State, _ => false),
                  "[DEMO-S2] an LLM-only roster with no runnable runner is not playable");
            Check(TurnFlowPlanner.HasHumanOrRunnableLlm(e.State, id => id == "seat-b"),
                  "[DEMO-S2] the runnable-seat predicate can accept one LLM seat");
        }

        // Game over beats both the first-seat plan and the boundary guard.
        {
            var e = TurnGateScenario();
            e.State.WinnerSeatId = "seat-a";
            var first = TurnFlowPlanner.First(e.State);
            var after = TurnFlowPlanner.NextAfter(e.State, "seat-a");
            Check(first.Action == TurnFlowAction.GameOver && after.Action == TurnFlowAction.GameOver,
                  "[DEMO-S2] game over stops before seat planning");
            Check(!TurnFlowPlanner.HasHumanOrRunnableLlm(e.State),
                  "[DEMO-S2] a decided game is not playable");
        }
    }
}
