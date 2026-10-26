using System;

namespace LuanShi.Engine
{
    /// <summary>The kind of work the next step of a season's rotation needs from the host.</summary>
    public enum TurnFlowAction
    {
        HumanSeat,
        LlmSeat,
        ScriptedSeat,
        ResolveSeason,
        GameOver,
    }

    /// <summary>
    /// One host-visible step after a seat finishes. <see cref="SeatId"/> is set only for seat steps;
    /// it is null for <see cref="TurnFlowAction.ResolveSeason"/> and <see cref="TurnFlowAction.GameOver"/>.
    /// </summary>
    public readonly struct TurnFlowStep
    {
        public TurnFlowAction Action { get; }
        public string SeatId { get; }

        public TurnFlowStep(TurnFlowAction action, string seatId = null)
        {
            Action = action;
            SeatId = seatId;
        }

        public bool IsSeat => Action == TurnFlowAction.HumanSeat
                           || Action == TurnFlowAction.LlmSeat
                           || Action == TurnFlowAction.ScriptedSeat;
    }

    /// <summary>
    /// Pure, Unity-free seat-rotation decision used by the UI host and the headless tests.
    /// The host owns the actual work (opening a human chair, starting a runner, executing scripted
    /// orders, resolving the season); this planner only answers what that work is.
    /// </summary>
    public static class TurnFlowPlanner
    {
        /// <summary>The first seat of a season or, when the game is already decided, GameOver.</summary>
        public static TurnFlowStep First(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.WinnerSeatId != null) return new TurnFlowStep(TurnFlowAction.GameOver);

            string seatId = SeatRotation.FirstSeat(state);
            return seatId == null
                ? new TurnFlowStep(TurnFlowAction.ResolveSeason)
                : StepForSeat(state, seatId);
        }

        /// <summary>
        /// What the host should do after <paramref name="endedSeat"/> has finished its turn.
        /// A seat may be followed by another seat, by season resolution, or by game over.
        /// </summary>
        public static TurnFlowStep NextAfter(GameState state, string endedSeat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.WinnerSeatId != null) return new TurnFlowStep(TurnFlowAction.GameOver);

            string next = SeatRotation.NextAfter(state, endedSeat);
            return next == null
                ? new TurnFlowStep(TurnFlowAction.ResolveSeason)
                : StepForSeat(state, next);
        }

        private static TurnFlowStep StepForSeat(GameState state, string seatId)
        {
            FactionState faction = state.FindFaction(seatId);
            // A malformed roster should fail safe rather than loop forever on a seat that cannot act.
            if (faction == null) return new TurnFlowStep(TurnFlowAction.ResolveSeason);

            switch (faction.Controller)
            {
                case ControllerType.Human:
                    return new TurnFlowStep(TurnFlowAction.HumanSeat, seatId);
                case ControllerType.Llm:
                    return new TurnFlowStep(TurnFlowAction.LlmSeat, seatId);
                default:
                    return new TurnFlowStep(TurnFlowAction.ScriptedSeat, seatId);
            }
        }
    }
}
