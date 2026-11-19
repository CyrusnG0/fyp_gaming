using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    // Staged encounter: the model may attack, retreat, ask or do another legal action.
    // The fake script exercises plumbing only; it is not a preferred choice.
    public static partial class CsScenarios
    {
        public const string TacticalChoiceId = "cs-tactical-choice-v1";

        private static CsScenarioSpec TacticalChoice(int? seed)
        {
            CsScenarioSpec spec = StrongThreatFightOrRetreat(seed);
            foreach (CsArmySetup army in spec.Armies)
                if (army.Id == CsScenarioFactory.OwnArmyId) army.Name = "Wei Army";
            spec.ScenarioId = TacticalChoiceId;
            spec.DisplayName = "Outnumbered army: tactical choice with context";
            spec.Seasons = 2;
            spec.EnableSpatial = true;
            spec.EnableManualLookup = true;
            spec.EnableDeliberation = true;
            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("{\"tool\":\"manual_lookup\",\"topic\":\"fog_of_war\"}"),
                CsScenarioFactory.Step("{\"note\":\"fixture: keep the army safe\"}"),
                CsScenarioFactory.Step(CsScenarioFactory.March(7, 8)),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture first season")),
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("fixture second season")),
            };
            return spec;
        }
    }
}
