using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsScenarios
    {
        public const string AssaultMemoryId = "cs-assault-memory-v1";

        // Only the starting state is staged. The fake replies are plumbing fixtures;
        // a live provider may choose any permitted action or inquiry.
        private static CsScenarioSpec AssaultMemory(int? seed)
        {
            CsScenarioSpec spec = TacticalChoice(seed);
            spec.ScenarioId = AssaultMemoryId;
            spec.DisplayName = "Visible city and later memory";
            spec.Seasons = 3;
            spec.Goal = null;
            foreach (CsArmySetup army in spec.Armies)
            {
                if (army.Id == CsScenarioFactory.OwnArmyId) army.Troops = 3000;
                if (army.Id == CsScenarioFactory.EnemyArmyId) { army.X = 19; army.Z = 8; }
            }
            foreach (CsCitySetup city in spec.Cities)
                if (city.Id == CsScenarioFactory.EnemyCityId) { city.X = 9; city.Z = 8; city.Garrison = 500; }
            // Keep another city so a successful first assault does not end the match
            // before the follow-up memory turn.
            spec.Cities.Add(new CsCitySetup
            {
                Id = "cs-city-shu-reserve", Name = "Shu City", X = 20, Z = 8,
                OwnerSeatId = "seat-1", Garrison = 500,
            });
            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("{\"note\":\"fixture memory marker\"}"),
                CsScenarioFactory.Step("{\"type\":\"attack_city\",\"target_id\":\"cs-army-own\",\"param_a\":9,\"param_b\":8,\"summary\":\"fixture assault\"}"),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture season one")),
                CsScenarioFactory.Step("{\"tool\":\"recall_memory\",\"query\":\"attack_city\",\"limit\":5}"),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture season two")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture season three")),
            };
            spec.Scripts["seat-1"] = CsScenarioFactory.OpponentScript(spec.Seasons);
            return spec;
        }
    }
}
