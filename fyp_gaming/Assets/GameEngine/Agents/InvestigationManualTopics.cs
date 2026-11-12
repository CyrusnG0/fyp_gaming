using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Manual topics that document the implemented mechanics beyond the frozen LS-018 foundation
    /// index: the seasonal economy, army supply, field battle, city assault, unit types, diplomacy,
    /// research and movement. <see cref="All"/> is <see cref="InvestigationCatalogue.Topics"/> plus
    /// <see cref="Additional"/>, so the catalogue file itself stays untouched.
    ///
    /// Every added topic is a rules reference, never a model action offer: <c>ActionType</c> is null,
    /// <c>LlmActionExposed</c> is false, and the answer carries <see cref="ActionCaveat"/>.
    /// </summary>
    internal static class InvestigationManualTopics
    {
        /// <summary>Stated on every broad mechanics topic so a rule can never read as an offer.</summary>
        internal const string ActionCaveat =
            "Described mechanics do not imply command availability; the current action catalogue is authoritative.";

        /// <summary>Only the topics added by this manual expansion, in index order.</summary>
        public static IReadOnlyList<InvestigationTopicDefinition> Additional { get; } = BuildAdditional();

        /// <summary>
        /// The complete discoverable topic list: catalogue topics first, new topics after.
        /// Declared after <see cref="Additional"/> so static initialisation sees it built.
        /// </summary>
        public static IReadOnlyList<InvestigationTopicDefinition> All { get; } = BuildAll();

        private static IReadOnlyList<InvestigationTopicDefinition> BuildAll()
        {
            var all = new List<InvestigationTopicDefinition>(InvestigationCatalogue.Topics);
            all.AddRange(Additional);
            return all.AsReadOnly();
        }

        private static IReadOnlyList<InvestigationTopicDefinition> BuildAdditional()
        {
            var topics = new List<InvestigationTopicDefinition>
            {
                new InvestigationTopicDefinition
                {
                    Id = "seasonal_economy",
                    Title = "Seasonal food, gold and revolt economy",
                    Category = "economy",
                    Aliases = new[]
                    {
                        "economy", "season economy", "food economy", "harvest", "famine",
                        "revolt", "rebellion", "population", "gold income", "seasonal_economy",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "army_supply",
                    Title = "Army supply, upkeep and desertion",
                    Category = "army",
                    Aliases = new[]
                    {
                        "supply", "upkeep", "desertion", "deserters", "ration", "rations",
                        "army food", "logistics", "army_supply",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "field_combat",
                    Title = "Field battle (野戰)",
                    Category = "combat",
                    Aliases = new[]
                    {
                        "combat", "battle", "field battle", "field combat", "fight", "野战",
                        "attack army", "attack_army", "field_combat",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "city_assault",
                    Title = "City assault (攻城)",
                    Category = "combat",
                    Aliases = new[]
                    {
                        "assault", "siege", "storm", "attack city", "attack_city", "take city",
                        "city assault", "攻城", "city_assault",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "unit_type_modifiers",
                    Title = "Unit type combat modifiers (兵種)",
                    Category = "combat",
                    Aliases = new[]
                    {
                        "unit type", "unit types", "unit_type", "troop type", "兵种", "counter",
                        "counters", "matchup", "siege unit", "攻城器械", "unit_type_modifiers",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "diplomacy",
                    Title = "Diplomacy, treaties and reputation",
                    Category = "diplomacy",
                    Aliases = new[]
                    {
                        "diplomacy", "treaty", "treaties", "war", "trust", "reputation",
                        "prestige", "gift", "alliance", "truce", "peace", "外交",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "research",
                    Title = "Research lines and effects",
                    Category = "research",
                    Aliases = new[]
                    {
                        "research", "研究", "technology", "tech", "research points",
                        "research tree", "research lines", "research_levels",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "movement",
                    Title = "Army movement and capture on march",
                    Category = "movement",
                    Aliases = new[]
                    {
                        "movement", "move", "march", "pathfinding", "path", "terrain",
                        "capture", "行军", "movement_rules",
                    },
                },
            };
            return topics.AsReadOnly();
        }
    }
}
