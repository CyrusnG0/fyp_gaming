using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsScenarios
    {
        public const string CityApproachId = "cs-city-approach-v1";

        // The scene supplies positions and resources, not an instruction to attack. Only the
        // fake-provider sequence below chooses these actions; a live model decides freely.
        private static CsScenarioSpec CityApproach(int? seed)
        {
            var spec = GarrisonChoice(seed);
            spec.ScenarioId = CityApproachId;
            spec.DisplayName = "Nearby city";
            spec.Seasons = 3;
            spec.Goal = null;
            spec.EnableResearch = true;
            spec.EnableDiplomacy = true;
            spec.ResearchPoints = 30;

            foreach (var city in spec.Cities)
            {
                if (city.Id == CsScenarioFactory.HomeCityId)
                {
                    city.Garrison = 1500;
                    city.Food = 5000;
                }
                if (city.Id == CsScenarioFactory.EnemyCityId)
                {
                    city.X = 9; city.Z = 8;
                    city.Garrison = 500;
                    city.Food = 5000;
                }
            }
            foreach (var army in spec.Armies)
                if (army.Id == CsScenarioFactory.EnemyArmyId) { army.X = 19; army.Z = 8; }

            // Keep the opposing faction alive if the model or fake captures the nearby city.
            spec.Cities.Add(new CsCitySetup
            {
                Id = "cs-city-shu-reserve", Name = "Shu City", X = 20, Z = 8,
                OwnerSeatId = "seat-1", Population = 10000, Food = 5000, Gold = 500,
                Garrison = 500,
            });

            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("{\"type\":\"recruit\",\"target_id\":\"cs-city-home\",\"args\":{\"unit_type\":\"infantry\"}}"),
                CsScenarioFactory.Step("{\"type\":\"convert_garrison\",\"target_id\":\"cs-city-home\",\"param_a\":2000}"),
                CsScenarioFactory.Step("{\"type\":\"march\",\"target_id\":\"army-seat-0-1\",\"param_a\":8,\"param_b\":8}"),
                CsScenarioFactory.Step("{\"type\":\"attack_city\",\"target_id\":\"army-seat-0-1\",\"param_a\":9,\"param_b\":8}"),
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
