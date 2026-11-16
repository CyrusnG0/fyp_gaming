using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// One fake-provider reply. It is a FIXTURE ("fake_scripted"), never evidence of model
    /// behaviour. Fault is one of provider_error / missing_usage / empty_reply /
    /// length_truncated / request_timeout (a delay that ignores cancellation, bounded only by
    /// the runner's request timeout); null means a normal reply.
    /// </summary>
    public sealed class CsScriptStep
    {
        [JsonPropertyName("text")] public string Text { get; set; }
        [JsonPropertyName("delay_ms")] public int DelayMs { get; set; }
        [JsonPropertyName("input_tokens")] public int? InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int? OutputTokens { get; set; }
        [JsonPropertyName("finish_reason")] public string FinishReason { get; set; }
        [JsonPropertyName("fault")] public string Fault { get; set; }
    }

    public sealed class CsFactionSetup
    {
        [JsonPropertyName("seat_id")] public string SeatId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
    }

    public sealed class CsCitySetup
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("z")] public int Z { get; set; }
        [JsonPropertyName("owner_seat_id")] public string OwnerSeatId { get; set; }
        [JsonPropertyName("population")] public int Population { get; set; }
        [JsonPropertyName("food")] public int? Food { get; set; }
        [JsonPropertyName("gold")] public int? Gold { get; set; }
        [JsonPropertyName("garrison")] public int Garrison { get; set; }
    }

    public sealed class CsArmySetup
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("owner_seat_id")] public string OwnerSeatId { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("z")] public int Z { get; set; }
        [JsonPropertyName("troops")] public int Troops { get; set; }
    }

    public sealed class CsTowerSetup
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("z")] public int Z { get; set; }
        [JsonPropertyName("owner_seat_id")] public string OwnerSeatId { get; set; }
    }

    /// <summary>In-memory specification of one controlled situation (staged data + fake fixture).</summary>
    public sealed class CsScenarioSpec
    {
        public string ScenarioId;
        public string DisplayName;
        public int Seed;
        public string StudySeat = "seat-0";
        public string OpponentSeat = "seat-1";
        public int Seasons = 1;
        public bool EnableAttack;
        public bool EnableResearch;
        public bool EnableManualLookup;
        public bool EnableDeliberation;
        public int ResearchPoints;
        public List<CsFactionSetup> Factions = new List<CsFactionSetup>();
        public List<CsCitySetup> Cities = new List<CsCitySetup>();
        public List<CsArmySetup> Armies = new List<CsArmySetup>();
        public List<CsTowerSetup> Towers = new List<CsTowerSetup>();
        public Dictionary<string, List<CsScriptStep>> Scripts =
            new Dictionary<string, List<CsScriptStep>>(StringComparer.Ordinal);
        public int MaxRequests;
        public int MaxWallSeconds;
        public int RequestTimeoutMs;

        public List<CsScriptStep> ScriptFor(string seat)
            => Scripts.TryGetValue(seat, out List<CsScriptStep> script) ? script : new List<CsScriptStep>();

        /// <summary>Converts the staged data to the production scenario object (positions/owners only).</summary>
        public CompactScenario ToCompactScenario()
        {
            var factions = new List<ScenarioFaction>();
            foreach (CsFactionSetup f in Factions)
                factions.Add(new ScenarioFaction { SeatId = f.SeatId, Name = f.Name });
            var cities = new List<ScenarioCity>();
            foreach (CsCitySetup c in Cities)
                cities.Add(new ScenarioCity
                {
                    Id = c.Id, Name = c.Name, X = c.X, Z = c.Z, OwnerSeatId = c.OwnerSeatId,
                    Population = c.Population, FoodOverride = c.Food, GoldOverride = c.Gold,
                    Garrison = c.Garrison,
                });
            var armies = new List<ScenarioArmy>();
            foreach (CsArmySetup a in Armies)
                armies.Add(new ScenarioArmy
                {
                    Id = a.Id, Name = a.Name, OwnerSeatId = a.OwnerSeatId,
                    X = a.X, Z = a.Z, Troops = a.Troops,
                });
            var engineTowers = new List<ScenarioTower>();
            foreach (CsTowerSetup t in Towers)
                engineTowers.Add(new ScenarioTower
                {
                    Id = t.Id, Name = t.Name, X = t.X, Z = t.Z, OwnerSeatId = t.OwnerSeatId,
                });
            return new CompactScenario(ScenarioId, DisplayName, factions, cities, armies, engineTowers);
        }
    }

    /// <summary>On-disk scenario file: staged setup and fake fixture are separate blocks.</summary>
    public sealed class CsScenarioFile
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "luanshi-controlled-scenario/1";
        [JsonPropertyName("scenario_id")] public string ScenarioId { get; set; }
        [JsonPropertyName("display_name")] public string DisplayName { get; set; }
        [JsonPropertyName("seed")] public int Seed { get; set; }
        [JsonPropertyName("study_seat")] public string StudySeat { get; set; }
        [JsonPropertyName("opponent_seat")] public string OpponentSeat { get; set; }
        [JsonPropertyName("seasons")] public int Seasons { get; set; }
        [JsonPropertyName("setup")] public CsScenarioSetupBlock Setup { get; set; }
        [JsonPropertyName("staged")] public CsScenarioStagedBlock Staged { get; set; }

        public CsScenarioSpec ToSpec()
        {
            var spec = new CsScenarioSpec
            {
                ScenarioId = ScenarioId, DisplayName = DisplayName, Seed = Seed,
                StudySeat = StudySeat, OpponentSeat = OpponentSeat, Seasons = Seasons,
                Factions = Setup?.Factions ?? new List<CsFactionSetup>(),
                Cities = Setup?.Cities ?? new List<CsCitySetup>(),
                Armies = Setup?.Armies ?? new List<CsArmySetup>(),
                Towers = Setup?.Towers ?? new List<CsTowerSetup>(),
                ResearchPoints = Setup?.ResearchPoints ?? 0,
                EnableAttack = Staged?.Features?.Attack ?? false,
                EnableResearch = Staged?.Features?.Research ?? false,
                EnableManualLookup = Staged?.Features?.ManualLookup ?? false,
                EnableDeliberation = Staged?.Features?.Deliberation ?? false,
                MaxRequests = Staged?.Ceilings?.MaxRequests ?? 0,
                MaxWallSeconds = Staged?.Ceilings?.MaxWallSeconds ?? 0,
                RequestTimeoutMs = Staged?.Ceilings?.RequestTimeoutMs ?? 0,
                Scripts = Staged?.Scripts ?? new Dictionary<string, List<CsScriptStep>>(StringComparer.Ordinal),
            };
            return spec;
        }

        public static CsScenarioFile FromSpec(CsScenarioSpec spec)
        {
            return new CsScenarioFile
            {
                ScenarioId = spec.ScenarioId, DisplayName = spec.DisplayName, Seed = spec.Seed,
                StudySeat = spec.StudySeat, OpponentSeat = spec.OpponentSeat, Seasons = spec.Seasons,
                Setup = new CsScenarioSetupBlock
                {
                    Factions = spec.Factions, Cities = spec.Cities, Armies = spec.Armies,
                    Towers = spec.Towers, ResearchPoints = spec.ResearchPoints,
                },
                Staged = new CsScenarioStagedBlock
                {
                    Features = new CsFeatureBlock
                    {
                        Attack = spec.EnableAttack, Research = spec.EnableResearch,
                        ManualLookup = spec.EnableManualLookup, Deliberation = spec.EnableDeliberation,
                    },
                    Scripts = spec.Scripts,
                    Fixture = true,
                    Source = "fake_scripted",
                    Ceilings = new CsScenarioCeilings
                    {
                        MaxRequests = spec.MaxRequests, MaxWallSeconds = spec.MaxWallSeconds,
                        RequestTimeoutMs = spec.RequestTimeoutMs,
                    },
                },
            };
        }
    }

    public sealed class CsScenarioSetupBlock
    {
        [JsonPropertyName("factions")] public List<CsFactionSetup> Factions { get; set; }
        [JsonPropertyName("cities")] public List<CsCitySetup> Cities { get; set; }
        [JsonPropertyName("armies")] public List<CsArmySetup> Armies { get; set; }
        [JsonPropertyName("towers")] public List<CsTowerSetup> Towers { get; set; }
        [JsonPropertyName("research_points")] public int ResearchPoints { get; set; }
    }

    public sealed class CsFeatureBlock
    {
        [JsonPropertyName("attack")] public bool Attack { get; set; }
        [JsonPropertyName("research")] public bool Research { get; set; }
        [JsonPropertyName("manual_lookup")] public bool ManualLookup { get; set; }
        [JsonPropertyName("deliberation")] public bool Deliberation { get; set; }
    }

    public sealed class CsScenarioStagedBlock
    {
        [JsonPropertyName("features")] public CsFeatureBlock Features { get; set; }
        [JsonPropertyName("scripts")] public Dictionary<string, List<CsScriptStep>> Scripts { get; set; }
        [JsonPropertyName("fixture")] public bool Fixture { get; set; } = true;
        [JsonPropertyName("source")] public string Source { get; set; } = "fake_scripted";
        [JsonPropertyName("ceilings")] public CsScenarioCeilings Ceilings { get; set; }
    }

    /// <summary>Shared staged shell and helpers for the six built-in situations.</summary>
    internal static class CsScenarioFactory
    {
        public const string HomeCityId = "cs-city-home";
        public const string EnemyCityId = "cs-city-enemy";
        public const string HiddenArmyId = "cs-army-hidden";
        public const string OwnArmyId = "cs-army-own";
        public const string EnemyArmyId = "cs-army-enemy";

        public static CsScenarioSpec Common(string id, string displayName, int seed, int seasons,
            bool attack, bool research, bool manualLookup, bool deliberation, int researchPoints,
            CsArmySetup ownArmy, CsArmySetup enemyArmy,
            List<CsScriptStep> studyScript, List<CsScriptStep> opponentScript)
        {
            var spec = new CsScenarioSpec
            {
                ScenarioId = id, DisplayName = displayName, Seed = seed, Seasons = seasons,
                EnableAttack = attack, EnableResearch = research,
                EnableManualLookup = manualLookup, EnableDeliberation = deliberation,
                ResearchPoints = researchPoints,
                Factions = new List<CsFactionSetup>
                {
                    new CsFactionSetup { SeatId = "seat-0", Name = "Wei" },
                    new CsFactionSetup { SeatId = "seat-1", Name = "Shu" },
                },
                Cities = new List<CsCitySetup>
                {
                    new CsCitySetup
                    {
                        Id = HomeCityId, Name = "Home", X = 6, Z = 8, OwnerSeatId = "seat-0",
                        Population = 10000, Food = 5000, Gold = 5000, Garrison = 500,
                    },
                    new CsCitySetup
                    {
                        Id = EnemyCityId, Name = "Enemy Seat", X = 20, Z = 8, OwnerSeatId = "seat-1",
                        Garrison = 500,
                    },
                },
                Armies = new List<CsArmySetup> { ownArmy, enemyArmy,
                    new CsArmySetup
                    {
                        Id = HiddenArmyId, Name = "Hidden Enemy Host", OwnerSeatId = "seat-1",
                        X = 22, Z = 9, Troops = 500,
                    } },
                Towers = new List<CsTowerSetup>(),
                Scripts = new Dictionary<string, List<CsScriptStep>>(StringComparer.Ordinal)
                {
                    { "seat-0", studyScript ?? new List<CsScriptStep>() },
                    { "seat-1", opponentScript ?? OpponentScript(seasons) },
                },
            };
            return spec;
        }

        public static CsScriptStep Step(string text, string fault = null, int delayMs = 0,
            string finishReason = null, int? inputTokens = null, int? outputTokens = null)
            => new CsScriptStep
            {
                Text = text, Fault = fault, DelayMs = delayMs, FinishReason = finishReason,
                InputTokens = inputTokens, OutputTokens = outputTokens,
            };

        /// <summary>The opponent fixture: one end_turn per season. It is plumbing, not behaviour.</summary>
        public static List<CsScriptStep> OpponentScript(int seasons)
        {
            var script = new List<CsScriptStep>();
            for (int i = 0; i < Math.Max(1, seasons); i++)
                script.Add(Step("{\"type\":\"end_turn\",\"summary\":\"opponent holds\"}"));
            return script;
        }

        public static string EndTurn(string summary) =>
            "{\"type\":\"end_turn\",\"summary\":" + JsonText(summary) + "}";

        public static string March(int x, int z) =>
            "{\"type\":\"march\",\"target_id\":\"" + OwnArmyId + "\",\"param_a\":" + x
            + ",\"param_b\":" + z + ",\"summary\":\"advance\"}";

        public static string AttackArmy(int x, int z) =>
            "{\"type\":\"attack_army\",\"target_id\":\"" + OwnArmyId + "\",\"param_a\":" + x
            + ",\"param_b\":" + z + ",\"summary\":\"attack the visible enemy\"}";

        private static string JsonText(string value)
        {
            var sb = new System.Text.StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
