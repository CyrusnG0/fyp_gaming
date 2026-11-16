using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Situation 2: an advantageous legal field attack. Staged: own army 2000 at (8,8), enemy
    /// army 500 at (9,8), adjacent, attack enabled; 4:1 is staged, but the engine still rolls
    /// combat. The fixture chooses attack_army; that is fixture plumbing, not a pass condition.
    /// </summary>
    public static partial class CsScenarios
    {
        private static CsScenarioSpec AdvantageousAttack(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-advantageous-attack-v1", "Advantageous legal field attack", seed ?? 0, 1,
                attack: true, research: false, manualLookup: false, deliberation: false,
                researchPoints: 0,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Main Host", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 2000,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Enemy Detachment", OwnerSeatId = "seat-1",
                    X = 9, Z = 8, Troops = 500,
                },
                studyScript: new List<CsScriptStep>
                {
                    CsScenarioFactory.Step(CsScenarioFactory.AttackArmy(9, 8)),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("attack resolved")),
                },
                opponentScript: null);
        }

        /// <summary>
        /// Situation 3: a stronger visible threat with legal movement/retreat available. Staged:
        /// own army 500 at (8,8), enemy army 3000 at (9,8). The fixture retreats to (7,8); the
        /// fixture is not a pass condition and proves only that the retreat path is advertised.
        /// </summary>
        private static CsScenarioSpec StrongThreatRetreat(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-strong-threat-retreat-v1", "Strong threat with legal retreat", seed ?? 0, 1,
                attack: false, research: false, manualLookup: false, deliberation: false,
                researchPoints: 0,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Outnumbered Host", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 500,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Enemy Main Host", OwnerSeatId = "seat-1",
                    X = 9, Z = 8, Troops = 3000,
                },
                studyScript: new List<CsScriptStep>
                {
                    CsScenarioFactory.Step(CsScenarioFactory.March(7, 8)),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("retreat completed")),
                },
                opponentScript: null);
        }
    }
}
