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
    }
}
