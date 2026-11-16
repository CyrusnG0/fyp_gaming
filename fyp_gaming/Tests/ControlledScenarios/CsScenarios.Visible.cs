using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Situation 1: a visible enemy at first contact while legal movement is available.
    /// Staged: own army 2000 at (8,8), enemy army 2000 at (10,8), distance 2 (= FogArmyRange).
    /// The fake fixture first submits an illegal move (a labelled staged reject), then marches to
    /// (7,8); neither reply is a pass condition and neither is model behaviour.
    /// </summary>
    public static partial class CsScenarios
    {
        private static CsScenarioSpec VisibleContact(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-visible-contact-v1", "Visible contact with legal movement", seed ?? 0, 1,
                attack: false, research: false, manualLookup: false, deliberation: false,
                researchPoints: 0,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Vanguard", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 2000,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Enemy Scout", OwnerSeatId = "seat-1",
                    X = 10, Z = 8, Troops = 2000,
                },
                studyScript: new List<CsScriptStep>
                {
                    // Staged reject fixture: (0,0) is not a legal move destination.
                    CsScenarioFactory.Step(
                        "{\"type\":\"march\",\"target_id\":\"cs-army-own\",\"param_a\":0,\"param_b\":0,"
                        + "\"summary\":\"staged reject fixture: illegal destination\"}"),
                    CsScenarioFactory.Step(CsScenarioFactory.March(7, 8)),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("contact observed")),
                },
                opponentScript: null);
        }
    }
}
