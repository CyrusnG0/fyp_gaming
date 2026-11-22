using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsScenarios
    {
        public const string GarrisonChoiceId = "cs-garrison-choice-v1";
        private static CsScenarioSpec GarrisonChoice(int? seed)
        {
            var spec = TacticalChoice(seed);
            spec.ScenarioId = GarrisonChoiceId;
            spec.DisplayName = "City garrison and field army";
            spec.EnableGarrisonConversion = true;
            spec.Goal = null;
            spec.Armies.RemoveAll(a => a.OwnerSeatId == "seat-0");
            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("{\"type\":\"recruit\",\"target_id\":\"cs-city-home\",\"args\":{\"unit_type\":\"infantry\"}}"),
                CsScenarioFactory.Step("{\"type\":\"convert_garrison\",\"target_id\":\"cs-city-home\",\"param_a\":1000}"),
                CsScenarioFactory.Step("{\"type\":\"march\",\"target_id\":\"army-seat-0-1\",\"param_a\":7,\"param_b\":8}"),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture")),
            };
            spec.Scripts["seat-1"] = CsScenarioFactory.OpponentScript(spec.Seasons);
            return spec;
        }
    }
}
