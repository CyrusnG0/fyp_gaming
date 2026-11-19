using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsScenarios
    {
        public const string LongDemoId = "cs-long-demo-v1";

        private static CsScenarioSpec LongDemo(int? seed)
        {
            var spec = RouteChoice(seed);
            spec.ScenarioId = LongDemoId;
            spec.DisplayName = "River crossings";
            spec.Goal = null;
            spec.Seasons = 10;
            spec.EnableAttack = true;
            spec.EnableResearch = true;
            spec.EnableDiplomacy = true;
            spec.ResearchPoints = 30;
            foreach (var army in spec.Armies)
                if (army.Id == CsScenarioFactory.HiddenArmyId) army.Name = "Shu Guard";
            spec.Cities.Add(new CsCitySetup
            {
                Id = "cs-city-westford", Name = "Westford", X = 11, Z = 11, OwnerSeatId = null,
            });
            spec.Cities.Add(new CsCitySetup
            {
                Id = "cs-city-southford", Name = "Southford", X = 15, Z = 6, OwnerSeatId = null,
            });
            var wei = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("{\"tool\":\"manual_lookup\",\"topic\":\"fog_of_war\"}"),
                CsScenarioFactory.Step("{\"note\":\"fixture note\"}"),
                CsScenarioFactory.Step("{\"type\":\"research\",\"args\":{\"line\":\"agri\"}}"),
                CsScenarioFactory.Step("{\"type\":\"send_message\",\"target_id\":\"seat-1\",\"text\":\"Fixture message.\"}"),
                CsScenarioFactory.Step(CsScenarioFactory.March(15, 10)),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture season one")),
                CsScenarioFactory.Step(CsScenarioFactory.AttackArmy(16, 10)),
            };
            for (int i = 1; i < spec.Seasons; i++)
                wei.Add(CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture")));
            spec.Scripts["seat-0"] = wei;
            spec.Scripts["seat-1"] = CsScenarioFactory.OpponentScript(spec.Seasons);
            return spec;
        }
    }
}
