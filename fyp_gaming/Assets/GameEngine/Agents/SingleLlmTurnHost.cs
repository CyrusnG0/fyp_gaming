using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Pure-C# turn host for single-LLM mode, shared by <see cref="KingdomDemo"/> and the headless
    /// harness. Implements <see cref="ISpatialPlanningHost"/> so per-run spatial settings reach
    /// movement planning and prompts without global mutable state.
    /// </summary>
    public sealed class SingleLlmTurnHost : IAttackPlanningHost, IPersonaHost, ISpatialPlanningHost
    {
        private readonly GameEngine _engine;
        private readonly LlmConfig _config;
        private readonly string _seatId;
        private readonly Func<Observation> _observe;
        private readonly Func<ActionCommand, ValidationResult> _submit;
        private readonly Action _endTurn;

        public SingleLlmTurnHost(
            GameEngine engine,
            LlmConfig config,
            string seatId,
            Func<Observation> observe,
            Func<ActionCommand, ValidationResult> submit,
            Action endTurn)
        {
            _engine = engine;
            _config = config;
            _seatId = seatId;
            _observe = observe;
            _submit = submit;
            _endTurn = endTurn;
            if (_engine?.State != null && _config != null && _config.EffectiveVisibleMarchRoutes)
                _engine.State.RequireVisibleMarchRoutes = true;
        }

        public string FactionKey => _engine?.State?.FindFaction(_seatId)?.Name;

        public Observation Observe() => _observe != null ? _observe() : _engine?.Observe(_seatId);

        public ValidationResult Submit(ActionCommand cmd)
            => _config != null && _config.EffectiveVisibleMarchRoutes
               && !MovementPlanner.AllowsVisibleRoute(_engine, _seatId, cmd)
                ? ValidationResult.Fail(FeedbackPolicy.DestinationNotReachable)
                : (_submit != null ? _submit(cmd) : ValidationResult.Fail("host submit unavailable"));

        public void EndTurn() => _endTurn?.Invoke();

        /// <summary>Read-only, seat-filtered candidates for this agent seat.</summary>
        public MovementView PlanMovement() => PlanMovement(_config?.SpatialContext);

        /// <summary>Read-only, seat-filtered candidates honoring explicit per-run spatial options.</summary>
        public MovementView PlanMovement(SpatialContextOptions spatialOptions)
        {
            // Keep captures first, then spread the advertised destinations across compass sectors so
            // far reachable cells are offered too. Cheap-first selection shows mostly adjacent cells
            // and makes a full-budget march look like a one-tile move. Spatial tags stay off unless
            // the caller enabled spatial context.
            return MovementPlanner.BuildDiversified(_engine, _seatId,
                MovementPlanner.DefaultMaxOptionsPerArmy, null, spatialOptions);
        }

        /// <summary>Read-only, seat-filtered attack/assault candidates.</summary>
        public AttackView PlanAttacks() => AttackPlanner.Build(_engine, _seatId);

        /// <summary>Authoritative terminal signal from the engine, not a cached observation.</summary>
        public bool IsGameOver => _engine != null && _engine.State.WinnerSeatId != null;
    }
}
