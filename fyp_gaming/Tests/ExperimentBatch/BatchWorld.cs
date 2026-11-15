using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>Builds the engine and the production LlmConfig for one arm from its resolved manifest.</summary>
    public static class BatchWorld
    {
        public static GameEngine Create(BatchManifest m, string gameId, int seed)
        {
            if (!CompactScenario.TryGet(m.Scenario, out CompactScenario scenario))
                throw new InvalidOperationException("unknown scenario " + m.Scenario);
            if (!BalanceProfile.TryGet(m.BalanceProfile, out BalanceProfile profile))
                throw new InvalidOperationException("unknown balance profile " + m.BalanceProfile);
            var engine = new GameEngine(seed, gameId, profile);
            engine.State.Map = DemoMap();
            scenario.Apply(engine, faction => ControllerType.Llm);
            foreach (FactionState faction in engine.State.Factions)
                if (m.Setup.ResearchPoints > 0) faction.ResearchPoints = m.Setup.ResearchPoints;
            engine.BeginSeason();
            return engine;
        }

        public static LlmConfig Config(BatchManifest m)
        {
            FeatureFlags f = m.Features;
            MatchCeilings c = m.Ceilings;
            LlmConfig config = f.RichProfile ? LlmConfig.RichDeliberation() : new LlmConfig();
            config.Provider = m.Provider.Kind;
            config.Model = m.Provider.Model;
            config.EnableDeliberation = f.Deliberation;
            config.EnableManualLookup = f.ManualLookup;
            config.EnableResearch = f.Research;
            config.EnableAttack = f.Attack;
            config.RequireVisibleMarchRoutes = f.VisibleMarchRoutes;
            config.EnablePersonas = m.Personas.Enabled;
            foreach (KeyValuePair<string, string> kv in m.Personas.BySeat) config.SetPersonaOverride(kv.Key, kv.Value);
            config.MaxRequestsPerTurn = c.MaxRequestsPerTurn.Value;
            config.MaxTotalRequests = c.MaxTotalRequests.Value;
            config.MaxActionsPerTurn = c.MaxActionsPerTurn.Value;
            config.MaxAcceptedActionsPerSession = c.MaxAcceptedActionsPerSession.Value;
            config.MaxInquiriesPerTurn = c.MaxInquiriesPerTurn.Value;
            config.MaxInquiriesPerSession = c.MaxInquiriesPerSession.Value;
            config.MaxTurnSeconds = c.MaxTurnSeconds.Value;
            config.MaxSessionSeconds = c.MaxSessionSeconds.Value;
            config.RequestTimeoutMs = c.RequestTimeoutMs.Value;
            config.MaxOutputTokens = c.MaxOutputTokens.Value;
            config.MaxSessionInputTokens = c.MaxSessionInputTokens.Value;
            config.MaxSessionOutputTokens = c.MaxSessionOutputTokens.Value;
            config.MaxPromptChars = c.MaxPromptChars.Value;
            return config;
        }

        /// <summary>Researcher-side end-of-match facts, read directly from canonical state (never a prompt input).</summary>
        public static List<string> FinalState(GameEngine engine)
        {
            var lines = new List<string>();
            foreach (FactionState faction in engine.State.Factions)
            {
                int cities = 0, gold = 0, food = 0;
                foreach (CityState city in engine.State.Cities)
                    if (city.OwnerSeatId == faction.SeatId) { cities++; gold += city.Gold; food += city.Food; }
                var armies = new List<string>();
                foreach (ArmyState army in engine.State.Armies)
                    if (army.OwnerSeatId == faction.SeatId) armies.Add(army.Id + "@" + army.X + "," + army.Z);
                lines.Add(faction.SeatId + " cities=" + cities + " gold=" + gold + " food=" + food
                          + " research_agri=" + faction.ResearchLevel(ResearchLine.Agri)
                          + " armies=" + string.Join(";", armies));
            }
            return lines;
        }

        private static MapData DemoMap()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "demo-map.json");
            if (!File.Exists(path)) throw new FileNotFoundException("demo map fixture missing", path);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = doc.RootElement;
                int width = root.GetProperty("width").GetInt32(), height = root.GetProperty("height").GetInt32();
                var map = new MapData
                {
                    Width = width, Height = height, Walkable = new bool[width * height],
                    Cost = new int[width * height], Neighbors = new int[width * height][],
                };
                for (int i = 0; i < map.Cost.Length; i++) map.Cost[i] = 1;
                foreach (JsonElement node in root.GetProperty("nodes").EnumerateArray())
                {
                    int x = node.GetProperty("x").GetInt32(), z = node.GetProperty("z").GetInt32();
                    map.Walkable[map.Index(x, z)] = node.GetProperty("walkable").GetBoolean();
                    map.Cost[map.Index(x, z)] = node.GetProperty("cost").GetInt32();
                }
                foreach (JsonProperty pair in root.GetProperty("neighbors").EnumerateObject())
                {
                    string[] parts = pair.Name.Split(',');
                    var list = new List<int>();
                    foreach (JsonElement n in pair.Value.EnumerateArray()) list.Add(map.Index(n[0].GetInt32(), n[1].GetInt32()));
                    map.Neighbors[map.Index(int.Parse(parts[0]), int.Parse(parts[1]))] = list.ToArray();
                }
                return map;
            }
        }
    }
}
