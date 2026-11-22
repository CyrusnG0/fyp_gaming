using System.Linq;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static void CityApproachChecks(TestLog log)
        {
            var spec = Builtin(CsScenarios.CityApproachId, 5151);
            log.Check(spec.Goal == null && spec.EnableGarrisonConversion && spec.EnableAttack
                && spec.EnableSpatial && spec.EnableDeliberation && spec.EnableManualLookup,
                "[APPROACH] ordinary prompt with enabled conversion, attacks, spatial and inquiry");
            var game = CsWorld.Create(spec, "city-approach-check");
            var observed = game.Observe("seat-0");
            log.Check(observed.Cities.Any(c => c.Id == CsScenarioFactory.EnemyCityId
                && c.OwnerSeatId == "seat-1"),
                "[APPROACH] enemy city is visible before any model action");
            log.Check(!game.State.Armies.Any(a => a.OwnerSeatId == "seat-0"),
                "[APPROACH] the study seat starts without a field army");

            var recruit = game.Submit(new ActionCommand { Type = ActionType.Recruit,
                ActorSeatId = "seat-0", TargetId = CsScenarioFactory.HomeCityId,
                Args = new System.Collections.Generic.Dictionary<string, string> { { "unit_type", "infantry" } } });
            var convert = game.Submit(new ActionCommand { Type = ActionType.ConvertGarrison,
                ActorSeatId = "seat-0", TargetId = CsScenarioFactory.HomeCityId, ParamA = 2000 });
            var march = game.Submit(new ActionCommand { Type = ActionType.March,
                ActorSeatId = "seat-0", TargetId = "army-seat-0-1", ParamA = 8, ParamB = 8 });
            log.Check(recruit.Ok && convert.Ok && march.Ok,
                "[APPROACH] engine accepts recruit, conversion and march",
                "recruit=" + recruit.Error + "; convert=" + convert.Error + "; march=" + march.Error);
            AttackView attacks = AttackPlanner.Build(game, "seat-0");
            bool advertised = attacks.Armies.Any(a => a.Options != null && a.Options.Any(o =>
                o.Kind == ActionType.AttackCity && o.TargetCityId == CsScenarioFactory.EnemyCityId));
            var assault = game.Submit(new ActionCommand { Type = ActionType.AttackCity,
                ActorSeatId = "seat-0", TargetId = "army-seat-0-1", ParamA = 9, ParamB = 8 });
            log.Check(advertised && assault.Ok, "[APPROACH] offered city assault is accepted",
                "advertised=" + advertised + "; error=" + assault.Error);
            log.Check(game.State.FindCity(CsScenarioFactory.EnemyCityId).OwnerSeatId == "seat-0",
                "[APPROACH] seed 5151 fixture captures the city");
            log.Check(game.State.FindCity("cs-city-shu-reserve").OwnerSeatId == "seat-1",
                "[APPROACH] the opposing faction retains a city for follow-up turns");
        }
    }
}
