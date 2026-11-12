using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>One searchable English manual topic. The seven action topics mirror ActionCatalogue.</summary>
    public sealed class InvestigationTopicDefinition
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Category { get; internal set; }
        public string[] Aliases { get; internal set; }
        public bool LlmActionExposed { get; internal set; }
        public string ActionType { get; internal set; }   // ActionType.* for the seven domestic topics
    }

    /// <summary>
    /// Finite, discoverable catalogue for the read-only investigation manual.
    ///
    /// The frozen foundation is nine topics: the turn / command point and fog rules plus the seven
    /// domestic actions the current LLM action catalogue exposes. InvestigationManualTopics appends
    /// eight broad mechanics topics — seasonal economy, army supply, field combat, city assault,
    /// unit-type modifiers, diplomacy, research and movement — so manual_lookup serves 17 topics in
    /// all. Only the seven domestic topics are LLM-action-exposed: documenting a mechanic never
    /// implies its action is available to the model. Unsupported requests return the bounded index
    /// instead of invented rules.
    /// </summary>
    public static class InvestigationCatalogue
    {
        public static IReadOnlyList<InvestigationToolDescriptor> Tools { get; } = BuildTools();

        public static IReadOnlyList<InvestigationTopicDefinition> Topics { get; } = BuildTopics();

        private static IReadOnlyList<InvestigationToolDescriptor> BuildTools()
        {
            var tools = new List<InvestigationToolDescriptor>
            {
                new InvestigationToolDescriptor
                {
                    Name = "manual_lookup",
                    Title = "English rules lookup",
                    Summary = "Look up an implemented rule in the 17-topic English manual: nine "
                            + "foundation topics plus eight broad mechanics topics (economy, supply, "
                            + "combat, assault, unit types, diplomacy, research, movement). Only the "
                            + "seven domestic topics are model actions; rules never imply command "
                            + "availability. An empty topic returns the bounded English topic index.",
                },
                new InvestigationToolDescriptor
                {
                    Name = "inspect_entity",
                    Title = "Current observation entity inspection",
                    Summary = "Inspect one city or army that is present in this seat's current "
                            + "detached observation. Hidden and nonexistent ids are indistinguishable.",
                },
                new InvestigationToolDescriptor
                {
                    Name = "search_history",
                    Title = "Own filtered history search",
                    Summary = "Search only the seat's already filtered EventView history, newest "
                            + "first. An empty query returns the most recent events.",
                },
                new InvestigationToolDescriptor
                {
                    Name = "recall_memory",
                    Title = "Own seat memory recall",
                    Summary = "Search injected, detached, own-match/own-seat memories. Foreign, "
                            + "superseded and unsupported records are rejected; beliefs stay attributed.",
                },
            };
            return tools.AsReadOnly();
        }

        private static IReadOnlyList<InvestigationTopicDefinition> BuildTopics()
        {
            var topics = new List<InvestigationTopicDefinition>
            {
                new InvestigationTopicDefinition
                {
                    Id = "turn_and_command_points",
                    Title = "Turn and command point rules",
                    Category = "rules",
                    Aliases = new[]
                    {
                        "turn", "turns", "command points", "command_points", "cp", "orders",
                        "action budget", "action_budget", "season turn",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = "fog_of_war",
                    Title = "Fog of war and information limits",
                    Category = "rules",
                    Aliases = new[]
                    {
                        "fog", "fog of war", "information", "information limits", "visibility",
                        "observation", "scouting", "intel", "fog_of_war",
                    },
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.LevyTax,
                    Title = "Levy tax (徵稅)",
                    Category = "domestic",
                    Aliases = new[] { "tax", "taxes", "levy", "levy tax", "levy_tax" },
                    LlmActionExposed = true,
                    ActionType = ActionType.LevyTax,
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.LightenLabor,
                    Title = "Lighten labor (輕徭)",
                    Category = "domestic",
                    Aliases = new[] { "labor", "lighten labor", "lighten_labor", "reduce labor" },
                    LlmActionExposed = true,
                    ActionType = ActionType.LightenLabor,
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.Reclaim,
                    Title = "Reclaim land (開墾)",
                    Category = "domestic",
                    Aliases = new[] { "farm", "land", "reclaim", "reclaim land", "reclaim_land" },
                    LlmActionExposed = true,
                    ActionType = ActionType.Reclaim,
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.Recruit,
                    Title = "Recruit troops (募兵)",
                    Category = "domestic",
                    Aliases = new[] { "recruit", "recruitment", "raise troops", "troops", "recruit_troops" },
                    LlmActionExposed = true,
                    ActionType = ActionType.Recruit,
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.Train,
                    Title = "Train garrison (練兵)",
                    Category = "domestic",
                    Aliases = new[] { "train", "training", "drill", "train_garrison" },
                    LlmActionExposed = true,
                    ActionType = ActionType.Train,
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.Fortify,
                    Title = "Fortify city (修城)",
                    Category = "domestic",
                    Aliases = new[] { "fortify", "fortification", "defense", "walls", "fortify_city" },
                    LlmActionExposed = true,
                    ActionType = ActionType.Fortify,
                },
                new InvestigationTopicDefinition
                {
                    Id = ActionType.Relief,
                    Title = "Disaster relief (賑災)",
                    Category = "domestic",
                    Aliases = new[] { "relief", "disaster relief", "aid", "relieve", "disaster_relief" },
                    LlmActionExposed = true,
                    ActionType = ActionType.Relief,
                },
            };
            return topics.AsReadOnly();
        }
    }
}
