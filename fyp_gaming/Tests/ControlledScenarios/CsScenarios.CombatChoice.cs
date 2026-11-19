namespace LuanShi.ControlledScenarios
{
    public static partial class CsScenarios
    {
        public const string CombatChoiceId = "cs-combat-choice-v1";

        // Same features and normal prompt as TacticalChoice; only the staged troop strengths differ.
        private static CsScenarioSpec CombatChoice(int? seed)
        {
            CsScenarioSpec spec = TacticalChoice(seed);
            spec.ScenarioId = CombatChoiceId;
            spec.DisplayName = "Adjacent armies: combat choice";
            foreach (CsArmySetup army in spec.Armies)
            {
                if (army.Id == CsScenarioFactory.OwnArmyId) army.Troops = 2000;
                if (army.Id == CsScenarioFactory.EnemyArmyId) army.Troops = 500;
            }
            return spec;
        }
    }
}
