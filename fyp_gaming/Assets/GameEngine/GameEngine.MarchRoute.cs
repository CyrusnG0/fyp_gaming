namespace LuanShi.Engine
{
    public sealed partial class GameEngine
    {
        /// <summary>
        /// The march route cost the validator charges for <paramref name="seatId"/>: -1 when the destination is
        /// out of reach within <paramref name="budget"/>. With <see cref="GameState.RequireVisibleMarchRoutes"/>
        /// the path may only cross hexes the seat can see; otherwise the plain map path applies. The validator,
        /// movement planner and UI highlights all read this so every controller follows one rule.
        /// </summary>
        public int MarchPathCost(string seatId, int fromX, int fromZ, int toX, int toZ, int budget)
            => State.RequireVisibleMarchRoutes
                ? SpatialGeometry.VisiblePathCost(this, seatId, State.Map, fromX, fromZ, toX, toZ, budget)
                : State.Map.PathCost(fromX, fromZ, toX, toZ, budget);
    }
}
