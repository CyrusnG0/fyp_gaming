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
        /// Situation 3a: a stronger visible threat with attacks disabled, so the only offered
        /// response is retreat. The title and saved display_name say "retreat-only" explicitly;
        /// the scenario id stays stable. Staged: own army 500 at (8,8), enemy army 3000 at (9,8).
        /// The fixture retreats to (7,8); it is a labelled fake_scripted fixture, not a pass
        /// condition and not model behaviour.
        /// </summary>
        private static CsScenarioSpec StrongThreatRetreat(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-strong-threat-retreat-v1", "Strong threat, retreat-only (attack disabled)",
                seed ?? 0, 1,
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

        /// <summary>
        /// Situation 3b: the same stronger visible threat with attacks enabled, so the engine's
        /// legal actions include BOTH an attack on the enemy army and a retreat move. Staged: own
        /// army 500 at (8,8), enemy army 3000 at (9,8). The fixture retreats to (7,8); it is a
        /// labelled fake_scripted fixture, not a pass condition and not model behaviour.
        /// </summary>
        private static CsScenarioSpec StrongThreatFightOrRetreat(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-strong-threat-fight-or-retreat-v1",
                "Strong threat, fight or retreat (attack enabled)", seed ?? 0, 1,
                attack: true, research: false, manualLookup: false, deliberation: false,
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
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("retreat fixture completed")),
                },
                opponentScript: null);
        }
    }
}
