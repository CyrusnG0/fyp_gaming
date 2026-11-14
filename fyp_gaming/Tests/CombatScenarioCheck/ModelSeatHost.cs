using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.CombatScenarioCheck
{
    /// <summary>
    /// The same host contract the Unity demo and the spectator session implement (observe a filtered
    /// world, submit through the engine's one validator, end the seat's turn), plus the seat-safe
    /// attack query. Nothing here invents a command: a model reply is parsed by the runner and only
    /// the engine decides whether it is legal.
    /// </summary>
    internal sealed class ModelSeatHost : IAttackPlanningHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;

        public readonly int Token;
        public readonly List<ActionCommand> Submitted = new List<ActionCommand>();
        public readonly List<ValidationResult> Results = new List<ValidationResult>();
        public int PlanAttackCalls;

        public ModelSeatHost(GameEngine engine, string seat)
        {
            _engine = engine;
            _seat = seat;
            Token = engine.BeginSeatTurn(seat);
        }

        public Observation Observe() => _engine.Observe(_seat);

        public ValidationResult Submit(ActionCommand cmd)
        {
            var result = _engine.SubmitAs(_seat, Token, cmd);
            Submitted.Add(cmd);
            Results.Add(result);
            return result;
        }

        public void EndTurn() => _engine.EndSeatTurn(Token);

        public MovementView PlanMovement() => MovementPlanner.Build(_engine, _seat);

        public AttackView PlanAttacks()
        {
            PlanAttackCalls++;
            return AttackPlanner.Build(_engine, _seat);
        }

        public bool IsGameOver => _engine.State.WinnerSeatId != null;
    }
}
