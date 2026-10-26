namespace LuanShi.Engine
{
    /// <summary>
    /// A turn host that can also offer seat-safe movement candidates and report a terminal game
    /// state. The runner enables the <c>march</c> vocabulary, the bounded MOVEMENT OPTIONS prompt
    /// section and the terminal stop only for hosts implementing this interface; a plain
    /// <see cref="ITurnHost"/> keeps the domestic-only demo-slice-3 behaviour exactly.
    ///
    /// <see cref="PlanMovement"/> must be a read-only, seat-filtered query (see
    /// <see cref="MovementPlanner.Build"/>). <see cref="IsGameOver"/> must be authoritative (for the
    /// engine, <c>State.WinnerSeatId != null</c>), never a cached display value.
    /// </summary>
    public interface IPlanningHost : ITurnHost
    {
        MovementView PlanMovement();
        bool IsGameOver { get; }
    }
}
