using System;
using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Situations 4-6: an affordable research purchase, an available information inquiry, and a
    /// follow-up turn that reloads the player's own note/memory. Every scripted reply is a labelled
    /// fixture; the tests assert plumbing and evidence, never a "good" choice.
    /// </summary>
    public static partial class CsScenarios
    {
        private static readonly string[] AllIds =
        {
            "cs-visible-contact-v1", "cs-advantageous-attack-v1", "cs-strong-threat-retreat-v1",
            "cs-research-purchase-v1", "cs-inquiry-manual-v1", "cs-memory-note-followup-v1",
            "cs-strong-threat-fight-or-retreat-v1",
        };

        public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(AllIds);

        public static bool TryGet(string scenarioId, int? seed, out CsScenarioSpec spec)
        {
            switch (scenarioId)
            {
                case "cs-visible-contact-v1": spec = VisibleContact(seed); return true;
                case "cs-advantageous-attack-v1": spec = AdvantageousAttack(seed); return true;
                case "cs-strong-threat-retreat-v1": spec = StrongThreatRetreat(seed); return true;
                case "cs-strong-threat-fight-or-retreat-v1":
                    spec = StrongThreatFightOrRetreat(seed); return true;
                case "cs-research-purchase-v1": spec = ResearchPurchase(seed); return true;
                case "cs-inquiry-manual-v1": spec = InquiryManual(seed); return true;
                case "cs-memory-note-followup-v1": spec = MemoryNoteFollowup(seed); return true;
                default: spec = null; return false;
            }
        }

        /// <summary>
        /// Situation 4: research points are staged (30 + the season's income) and research is
        /// enabled, so ActionCatalogue advertises every affordable level-1 line. The fixture buys
        /// agri; the engine remains the authority on affordability and acceptance.
        /// </summary>
        private static CsScenarioSpec ResearchPurchase(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-research-purchase-v1", "Affordable research purchase", seed ?? 0, 1,
                attack: false, research: true, manualLookup: false, deliberation: false,
                researchPoints: 30,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Home Guard", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 2000,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Enemy Scout", OwnerSeatId = "seat-1",
                    X = 10, Z = 8, Troops = 2000,
                },
                studyScript: new List<CsScriptStep>
                {
                    CsScenarioFactory.Step(
                        "{\"type\":\"research\",\"args\":{\"line\":\"agri\"},\"summary\":\"invest in food\"}"),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("research ordered")),
                },
                opponentScript: null);
        }

        /// <summary>
        /// Situation 5: the manual_lookup addendum is active. The fixture asks about fog_of_war,
        /// then ends the turn. The bounded result is linked from the NEXT prompt's markers.
        /// </summary>
        private static CsScenarioSpec InquiryManual(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-inquiry-manual-v1", "Information inquiry available", seed ?? 0, 1,
                attack: false, research: false, manualLookup: true, deliberation: false,
                researchPoints: 0,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Home Guard", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 2000,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Enemy Scout", OwnerSeatId = "seat-1",
                    X = 10, Z = 8, Troops = 2000,
                },
                studyScript: new List<CsScriptStep>
                {
                    CsScenarioFactory.Step("{\"tool\":\"manual_lookup\",\"topic\":\"fog_of_war\"}"),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("inquiry read")),
                },
                opponentScript: null);
        }

        /// <summary>
        /// Situation 6: deliberation is on and the match runs two seasons. Season 1 the fixture
        /// writes a private note, spends an accepted action (levy) and ends the turn. Season 2 it
        /// recalls memory and ends. The note/memories are provider-written; only the two-season
        /// plan and the stores are staged.
        /// </summary>
        private static CsScenarioSpec MemoryNoteFollowup(int? seed)
        {
            return CsScenarioFactory.Common(
                "cs-memory-note-followup-v1", "Follow-up turn with own note and memory", seed ?? 0, 2,
                attack: false, research: false, manualLookup: false, deliberation: true,
                researchPoints: 0,
                ownArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.OwnArmyId, Name = "Home Guard", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 2000,
                },
                enemyArmy: new CsArmySetup
                {
                    Id = CsScenarioFactory.EnemyArmyId, Name = "Enemy Scout", OwnerSeatId = "seat-1",
                    X = 10, Z = 8, Troops = 2000,
                },
                studyScript: new List<CsScriptStep>
                {
                    CsScenarioFactory.Step("{\"note\":\"Keep the home city and watch the enemy scout.\"}"),
                    CsScenarioFactory.Step(
                        "{\"type\":\"levy_tax\",\"target_id\":\"" + CsScenarioFactory.HomeCityId
                        + "\",\"summary\":\"fund the defense\"}"),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("season one complete")),
                    CsScenarioFactory.Step("{\"tool\":\"recall_memory\",\"query\":\"levy\",\"limit\":5}"),
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("memory reviewed")),
                },
                opponentScript: null);
        }
    }
}
