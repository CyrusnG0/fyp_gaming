using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Situation 10: the route-choice setup played over several seasons with attack enabled. It is a
    /// NEW run from the original staged setup, not a continuation of any recorded match. The fake
    /// fixture below is plumbing only (a note and an inquiry in season 1, a capture of the granary by
    /// the opponent seat, then holds); in a live run every reply comes from the provider and nothing
    /// forces a fight, a tool or a note.
    /// </summary>
    public static partial class CsScenarios
    {
        public const string RouteSeasonsId = "cs-route-seasons-v1";
        public const int RouteSeasonsCount = 3;

        private static CsScenarioSpec RouteSeasons(int? seed)
        {
            CsScenarioSpec spec = RouteChoice(seed);
            spec.ScenarioId = RouteSeasonsId;
            spec.DisplayName = "Route choice over several seasons (attack enabled)";
            spec.Seasons = RouteSeasonsCount;
            spec.EnableAttack = true;
            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("{\"tool\":\"manual_lookup\",\"topic\":\"fog_of_war\"}"),
                CsScenarioFactory.Step("{\"note\":\"fixture note: watch the ford\"}"),
                CsScenarioFactory.Step(CsScenarioFactory.March(15, 10)),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: season 1 done")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: season 2 hold")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: season 3 hold")),
            };
            spec.Scripts["seat-1"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step(CsScenarioFactory.MarchEnemy(14, 11)),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: season 1 done")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: season 2 hold")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: season 3 hold")),
            };
            return spec;
        }
    }
}
