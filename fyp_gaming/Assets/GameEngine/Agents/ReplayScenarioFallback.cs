using System;
using System.Collections.Generic;
using System.IO;

namespace LuanShi.Engine
{
    public sealed class ReplayScenarioSetup
    {
        public string Schema;
        public string ScenarioId;
        public int Seed;
        public int ResearchPoints;
        public bool StageIncomingDiplomacy;
        public ReplayBootstrapFeatures Features;
        public List<ScenarioFaction> Factions;
        public List<ScenarioCity> Cities;
        public List<ScenarioArmy> Armies;
        public List<ScenarioTower> Towers;
        public string ScenarioPath;
        public string SettingsPath;

        public string GameId => $"controlled-{ScenarioId}-{Seed}";
    }

    /// <summary>
    /// Controlled-scenario fallback loader used only when a run lacks a recorded *-bootstrap.json
    /// but provides scenario.json (luanshi-controlled-scenario/1). Never fabricates map terrain;
    /// requires the host/scene MapData and records explicit setup/map provenance.
    /// </summary>
    public static class ReplayScenarioFallback
    {
        public const string ExpectedSchema = "luanshi-controlled-scenario/1";

        public static ReplayScenarioSetup Load(string scenarioPath, string settingsPath, ReplaySummaryData summary)
        {
            if (string.IsNullOrEmpty(scenarioPath) || !File.Exists(scenarioPath))
                throw new FileNotFoundException("Controlled-scenario fallback requires scenario.json: " + (scenarioPath ?? "(null)"));

            string text = File.ReadAllText(scenarioPath);
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode root, out string err) || !(root is JsonObject obj))
                throw new InvalidDataException("Invalid scenario.json: " + err);

            string schema = Str(obj, "schema");
            if (!string.Equals(schema, ExpectedSchema, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported scenario.json schema '{schema ?? "missing"}' (expected '{ExpectedSchema}').");

            string scenarioId = Str(obj, "scenario_id");
            if (string.IsNullOrEmpty(scenarioId))
                throw new InvalidDataException("scenario.json is missing 'scenario_id'.");
            if (summary != null && !string.IsNullOrEmpty(summary.ScenarioId)
                && !string.Equals(scenarioId, summary.ScenarioId, StringComparison.Ordinal))
                throw new InvalidDataException($"scenario.json scenario_id '{scenarioId}' does not match summary '{summary.ScenarioId}'.");

            int seed = Int(obj, "seed", 0);
            if (seed <= 0)
                throw new InvalidDataException("scenario.json is missing a valid positive 'seed'.");
            if (summary != null && summary.Seed != 0 && seed != summary.Seed)
                throw new InvalidDataException($"scenario.json seed {seed} does not match summary seed {summary.Seed}.");

            if (!obj.TryGet("setup", out JsonNode setupNode) || !(setupNode is JsonObject setupObj))
                throw new InvalidDataException("scenario.json is missing 'setup' object.");

            var factions = ParseFactions(setupObj);
            var cities = ParseCities(setupObj);
            var armies = ParseArmies(setupObj);
            var towers = ParseTowers(setupObj);
            int researchPoints = Int(setupObj, "research_points", 0);
            bool stageDip = Bool(setupObj, "stage_incoming_diplomacy", false);
            if (stageDip)
                throw new InvalidDataException("scenario.json with stage_incoming_diplomacy=true is not supported by scenario fallback without a recorded bootstrap.");

            ReplayBootstrapFeatures features = ParseFeatures(obj, settingsPath, summary);

            return new ReplayScenarioSetup
            {
                Schema = schema,
                ScenarioId = scenarioId,
                Seed = seed,
                ResearchPoints = researchPoints,
                StageIncomingDiplomacy = stageDip,
                Features = features,
                Factions = factions,
                Cities = cities,
                Armies = armies,
                Towers = towers,
                ScenarioPath = Path.GetFullPath(scenarioPath),
                SettingsPath = (!string.IsNullOrEmpty(settingsPath) && File.Exists(settingsPath)) ? Path.GetFullPath(settingsPath) : null,
            };
        }

        public static GameEngine CreateEngine(ReplayScenarioSetup setup, ReplaySummaryData summary, MapData hostMap)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (hostMap == null)
                throw new InvalidOperationException("Scenario fallback replay requires host/scene MapData because scenario.json does not record initial_map.");

            string profileKey = summary != null && !string.IsNullOrEmpty(summary.BalanceProfileKey)
                ? summary.BalanceProfileKey
                : null;
            if (string.IsNullOrEmpty(profileKey) || !BalanceProfile.TryGet(profileKey, out BalanceProfile profile))
                throw new InvalidDataException($"Scenario fallback requires a valid balance_profile in summary.json (got '{profileKey ?? "missing"}').");

            var engine = new GameEngine(setup.Seed, setup.GameId, profile);
            engine.State.Map = hostMap;
            var compact = new CompactScenario(setup.ScenarioId, setup.ScenarioId, setup.Factions, setup.Cities, setup.Armies, setup.Towers);
            compact.Apply(engine, _ => ControllerType.Llm);
            foreach (var f in engine.State.Factions)
                f.ResearchPoints = setup.ResearchPoints;
            engine.BeginSeason();
            return engine;
        }

        private static List<ScenarioFaction> ParseFactions(JsonObject setupObj)
        {
            if (!setupObj.TryGet("factions", out JsonNode fNode) || !(fNode is JsonArray fArr) || fArr.Items.Count == 0)
                throw new InvalidDataException("scenario.json setup is missing 'factions'.");
            var list = new List<ScenarioFaction>();
            foreach (JsonNode item in fArr.Items)
            {
                if (!(item is JsonObject o)) continue;
                string seatId = Str(o, "seat_id");
                string name = Str(o, "name");
                if (string.IsNullOrEmpty(seatId) || string.IsNullOrEmpty(name))
                    throw new InvalidDataException("scenario.json faction entry requires seat_id and name.");
                list.Add(new ScenarioFaction { SeatId = seatId, Name = name, DefaultController = ControllerType.Llm });
            }
            return list;
        }

        private static List<ScenarioCity> ParseCities(JsonObject setupObj)
        {
            if (!setupObj.TryGet("cities", out JsonNode cNode) || !(cNode is JsonArray cArr) || cArr.Items.Count == 0)
                throw new InvalidDataException("scenario.json setup is missing 'cities'.");
            var list = new List<ScenarioCity>();
            foreach (JsonNode item in cArr.Items)
            {
                if (!(item is JsonObject o)) continue;
                string id = Str(o, "id");
                string name = Str(o, "name");
                string owner = Str(o, "owner_seat_id");
                int x = Int(o, "x", -1);
                int z = Int(o, "z", -1);
                int pop = Int(o, "population", 0);
                int? food = NullableInt(o, "food");
                int? gold = NullableInt(o, "gold");
                int garrison = Int(o, "garrison", 0);
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || x < 0 || z < 0)
                    throw new InvalidDataException("scenario.json city entry is incomplete.");

                if (string.IsNullOrEmpty(owner))
                {
                    list.Add(new ScenarioCity
                    {
                        Id = id, Name = name, X = x, Z = z, OwnerSeatId = null, Garrison = garrison,
                        NeutralPopulation = pop > 0 ? pop : 6000,
                        NeutralFood = food ?? 200,
                        NeutralGold = gold ?? 100,
                    });
                }
                else
                {
                    list.Add(new ScenarioCity
                    {
                        Id = id, Name = name, X = x, Z = z, OwnerSeatId = owner, Garrison = garrison,
                        Population = pop, FoodOverride = food, GoldOverride = gold,
                    });
                }
            }
            return list;
        }

        private static List<ScenarioArmy> ParseArmies(JsonObject setupObj)
        {
            var list = new List<ScenarioArmy>();
            if (!setupObj.TryGet("armies", out JsonNode aNode) || !(aNode is JsonArray aArr))
                return list;
            foreach (JsonNode item in aArr.Items)
            {
                if (!(item is JsonObject o)) continue;
                list.Add(new ScenarioArmy
                {
                    Id = Str(o, "id"),
                    Name = Str(o, "name"),
                    OwnerSeatId = Str(o, "owner_seat_id"),
                    X = Int(o, "x", 0),
                    Z = Int(o, "z", 0),
                    Troops = Int(o, "troops", 0),
                });
            }
            return list;
        }

        private static List<ScenarioTower> ParseTowers(JsonObject setupObj)
        {
            var list = new List<ScenarioTower>();
            if (!setupObj.TryGet("towers", out JsonNode tNode) || !(tNode is JsonArray tArr))
                return list;
            foreach (JsonNode item in tArr.Items)
            {
                if (!(item is JsonObject o)) continue;
                list.Add(new ScenarioTower
                {
                    Id = Str(o, "id"),
                    Name = Str(o, "name"),
                    X = Int(o, "x", 0),
                    Z = Int(o, "z", 0),
                    OwnerSeatId = Str(o, "owner_seat_id"),
                });
            }
            return list;
        }

        private static ReplayBootstrapFeatures ParseFeatures(JsonObject scenarioObj, string settingsPath, ReplaySummaryData summary)
        {
            JsonObject flagsObj = null;
            if (!string.IsNullOrEmpty(settingsPath) && File.Exists(settingsPath))
            {
                string stText = File.ReadAllText(settingsPath);
                if (StrictJson.TryParseLenientNumbers(stText, out JsonNode stRoot, out string stErr) && stRoot is JsonObject stObj
                    && stObj.TryGet("flags", out JsonNode fNode) && fNode is JsonObject fObj)
                    flagsObj = fObj;
                else
                    throw new InvalidDataException("Invalid settings.json: " + (stErr ?? "missing 'flags' object"));
            }
            else if (scenarioObj.TryGet("staged", out JsonNode stagedNode) && stagedNode is JsonObject stagedObj
                && stagedObj.TryGet("features", out JsonNode sfNode) && sfNode is JsonObject sfObj)
            {
                flagsObj = sfObj;
            }

            if (flagsObj == null)
                throw new InvalidDataException("Neither settings.json flags nor scenario.json staged.features was found.");

            bool spatial = Bool(flagsObj, "spatial", false);
            bool visibleRoutes = spatial
                || (summary?.ConfigRedacted != null && summary.ConfigRedacted.IndexOf("visible_march_routes=", StringComparison.OrdinalIgnoreCase) >= 0);

            return new ReplayBootstrapFeatures
            {
                Attack = Bool(flagsObj, "attack", false),
                Research = Bool(flagsObj, "research", false),
                Diplomacy = Bool(flagsObj, "diplomacy", false),
                ManualLookup = Bool(flagsObj, "manual_lookup", false),
                Deliberation = Bool(flagsObj, "deliberation", false),
                VisibleMarchRoutes = visibleRoutes,
            };
        }

        private static string Str(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode n) && n is JsonString s ? s.Value : null;

        private static int Int(JsonObject obj, string key, int defVal)
            => obj.TryGet(key, out JsonNode n) && n is JsonInt i ? (int)i.Value : defVal;

        private static int? NullableInt(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode n) && n is JsonInt i ? (int)i.Value : (int?)null;

        private static bool Bool(JsonObject obj, string key, bool defVal)
            => obj.TryGet(key, out JsonNode n) && n is JsonBool b ? b.Value : defVal;
    }
}
