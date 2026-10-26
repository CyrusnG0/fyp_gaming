using System.Collections.Generic;

namespace LuanShi.Engine
{
    public enum SpectatorState
    {
        NotStarted,
        Running,
        Paused,
        Completed,
        Failed,
    }

    /// <summary>One completed (or stopped) seat turn in a spectator pilot.</summary>
    public sealed class SpectatorTurnRecord
    {
        public int Season { get; internal set; }
        public string SeatId { get; internal set; }
        public string FactionName { get; internal set; }
        public int Requests { get; internal set; }
        public int Actions { get; internal set; }
        public int Accepted { get; internal set; }
        public int Rejected { get; internal set; }
        public int Invalid { get; internal set; }
        public string LastSummary { get; internal set; }
        public string LastCommandJson { get; internal set; }
        public string LastResult { get; internal set; }
        public string EndedBy { get; internal set; }
        public EndReason EndReason { get; internal set; }
        public string EndReasonText { get; internal set; }
    }

    /// <summary>
    /// Researcher/spectator-only aggregate read directly from canonical state.
    /// This value is NEVER passed into a model prompt; only the seat-filtered
    /// <see cref="Observation"/> built by <see cref="GameEngine.Observe"/> may enter a prompt.
    /// </summary>
    public sealed class SpectatorScore
    {
        public string SeatId { get; internal set; }
        public string FactionName { get; internal set; }
        public int Gold { get; internal set; }
        public int Food { get; internal set; }
        public int Population { get; internal set; }
        public int Garrison { get; internal set; }
        public int Cities { get; internal set; }
    }

    /// <summary>Researcher-only scoreboard captured immediately after one season resolves.</summary>
    public sealed class SpectatorSeasonScore
    {
        public int Season { get; internal set; }
        public IReadOnlyList<SpectatorScore> Scores { get; internal set; }
    }
}
