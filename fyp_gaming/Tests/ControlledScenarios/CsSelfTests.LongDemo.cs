using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static void LongDemoChecks(TestLog log)
        {
            var spec = Builtin(CsScenarios.LongDemoId, 5151);
            log.Check(spec.Seasons == 10 && spec.EnableAttack && spec.EnableResearch
                && spec.EnableDiplomacy && spec.EnableSpatial && spec.EnableDeliberation
                && spec.EnableManualLookup && string.IsNullOrEmpty(spec.Goal),
                "[LONG] ten seasons, requested features and no added goal");
            var game = CsWorld.Create(spec, "long-demo-capture");
            var result = game.Submit(new ActionCommand
            {
                Type = ActionType.March, ActorSeatId = "seat-0",
                TargetId = CsScenarioFactory.OwnArmyId, ParamA = 11, ParamB = 11,
            });
            log.Check(result.Ok && game.State.FindCity("cs-city-westford").OwnerSeatId == "seat-0",
                "[LONG] engine accepts march and captures neutral Westford", "error=" + result.Error);
        }
    }
}
