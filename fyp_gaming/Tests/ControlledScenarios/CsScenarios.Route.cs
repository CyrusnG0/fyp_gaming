using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Situation 9: a route choice across the demo map's river wall. Own army at (9,9) west of the
    /// wall; a neutral city (14,11) lies east of it (distance 5, path cost 8 around the wall), in sight of
    /// the own watch tower (10,9), with a stronger visible enemy army (16,10) nearby. The wall (11,6..8), (12,9) is unwalkable terrain in the engine's
    /// own MapData, so path cost, not straight-line distance, decides what is reachable. The enemy
    /// city and a second enemy host stay beyond the tower's sight (fog). Spatial context, manual
    /// lookup and deliberation (notes, memory) are all enabled; the fake script below is plumbing
    /// only and is never used for a live run, where every reply comes from the provider.
    /// </summary>
    public static partial class CsScenarios
    {
        public const string RouteChoiceId = "cs-route-choice-v1";
        public const string RouteObjectiveCityId = "cs-city-ford";
        public const string RouteGoal = "Secure Ford Granary to improve your faction's position. "
            + "Consider the visible enemy and terrain when deciding how to proceed.";

        private static CsScenarioSpec RouteChoice(int? seed)
        {
            CsScenarioSpec spec = CsScenarioFactory.Common(
                RouteChoiceId, "Route choice around a river wall", seed ?? 0, 1,
                attack: false, research: false, manualLookup: true, deliberation: true,
                researchPoints: 0,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Vanguard", OwnerSeatId = "seat-0",
                    X = 9, Z = 9, Troops = 2000,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Shu Vanguard", OwnerSeatId = "seat-1",
                    X = 16, Z = 10, Troops = 2500,
                },
                studyScript: new List<CsScriptStep>
                {
                    CsScenarioFactory.Step("{\"tool\":\"manual_lookup\",\"topic\":\"fog_of_war\"}"),
                    CsScenarioFactory.Step("{\"note\":\"fixture note: watch the ford\"}"),
                    CsScenarioFactory.Step(CsScenarioFactory.March(10, 9)),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture: plumbing only")),
                },
                opponentScript: null);
            spec.EnableSpatial = true;
            spec.Goal = RouteGoal;
            spec.Cities.Add(new CsCitySetup
            {
                Id = RouteObjectiveCityId, Name = "Ford Granary", X = 14, Z = 11, OwnerSeatId = null,
            });
            spec.Towers.Add(new CsTowerSetup
            {
                Id = "cs-tower-west", Name = "West Watch", X = 10, Z = 9, OwnerSeatId = "seat-0",
            });
            return spec;
        }
    }
}
