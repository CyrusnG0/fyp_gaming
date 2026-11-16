using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>One advertised catalogue entry, captured from the same observation the prompt used.</summary>
    public sealed class CsCatalogueOption
    {
        public string Type { get; set; }
        public string TargetId { get; set; }
        public string Label { get; set; }
        public int Cost { get; set; }
        public bool LikelyAffordable { get; set; }
        public int? ParamA { get; set; }
        public int? ParamB { get; set; }
        public Dictionary<string, string> Args { get; set; }
    }

    /// <summary>
    /// Researcher-side snapshot at one dispatch boundary: the seat-filtered observation bytes, the
    /// canonical world digest, and (before a dispatch) the re-derived visible options. The prompts
    /// remain the authoritative bytes; these fields are a structured view of the same state.
    /// </summary>
    public sealed class CsWorldSnapshot
    {
        public string SeatId { get; set; }
        public string ObservationJson { get; set; }
        public string ObservationSha256 { get; set; }
        public string WorldDigest { get; set; }
        public List<CsCatalogueOption> Catalogue { get; set; }
        public string CatalogueText { get; set; }
        public string MovementJson { get; set; }
        public string AttackJson { get; set; }
        public bool? MovementTruncated { get; set; }
        public bool? AttackTruncated { get; set; }
        public List<string> UnknownFields { get; set; } = new List<string>();
    }

    /// <summary>Builds the engine from staged data, reads the demo map fixture, and snapshots state.</summary>
    public static class CsWorld
    {
        public static GameEngine Create(CsScenarioSpec spec, string gameId)
        {
            var engine = new GameEngine(spec.Seed, gameId);
            engine.State.Map = DemoMap();
            CompactScenario scenario = spec.ToCompactScenario();
            scenario.Apply(engine, faction => ControllerType.Llm);
            foreach (FactionState faction in engine.State.Factions)
                faction.ResearchPoints = spec.ResearchPoints;
            engine.BeginSeason();
            return engine;
        }

        public static MapData DemoMap()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "demo-map.json");
            if (!File.Exists(path)) throw new FileNotFoundException("demo map fixture missing", path);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = doc.RootElement;
                int width = root.GetProperty("width").GetInt32();
                int height = root.GetProperty("height").GetInt32();
                var map = new MapData
                {
                    Width = width, Height = height, Walkable = new bool[width * height],
                    Cost = new int[width * height], Neighbors = new int[width * height][],
                };
                for (int i = 0; i < map.Cost.Length; i++) map.Cost[i] = 1;
                foreach (JsonElement node in root.GetProperty("nodes").EnumerateArray())
                {
                    int x = node.GetProperty("x").GetInt32();
                    int z = node.GetProperty("z").GetInt32();
                    map.Walkable[map.Index(x, z)] = node.GetProperty("walkable").GetBoolean();
                    map.Cost[map.Index(x, z)] = node.GetProperty("cost").GetInt32();
                }
                foreach (JsonProperty pair in root.GetProperty("neighbors").EnumerateObject())
                {
                    string[] parts = pair.Name.Split(',');
                    var list = new List<int>();
                    foreach (JsonElement n in pair.Value.EnumerateArray())
                        list.Add(map.Index(n[0].GetInt32(), n[1].GetInt32()));
                    map.Neighbors[map.Index(int.Parse(parts[0]), int.Parse(parts[1]))] = list.ToArray();
                }
                return map;
            }
        }

        /// <summary>
        /// Snapshot at a dispatch boundary. <paramref name="withOptions"/> re-derives the movement,
        /// attack and catalogue views exactly as the spectator host does, from this seat's observation.
        /// </summary>
        public static CsWorldSnapshot Capture(GameEngine engine, string seatId, LlmConfig config, bool withOptions)
        {
            var snap = new CsWorldSnapshot { SeatId = seatId };
            try { snap.WorldDigest = CanonicalDigest(engine.State); }
            catch (Exception) { snap.UnknownFields.Add("state.world_digest"); }

            Observation obs;
            try { obs = engine.Observe(seatId); }
            catch (Exception) { obs = null; }
            if (obs == null)
            {
                snap.UnknownFields.Add("state.observation_json");
                return snap;
            }
            snap.ObservationJson = PromptBuilder.BoundedObservationJson(obs);
            snap.ObservationSha256 = Sha256(snap.ObservationJson);
            if (!withOptions) return snap;

            bool spatialOn = config != null && config.SpatialContext != null && config.SpatialContext.IsEnabled;
            try
            {
                MovementView movement = config != null && (config.RequireVisibleMarchRoutes || spatialOn)
                    ? MovementPlanner.BuildDiversified(engine, seatId)
                    : MovementPlanner.Build(engine, seatId);
                snap.MovementJson = movement == null ? null : movement.ToJson();
                bool truncated = false;
                if (movement != null && movement.Armies != null)
                    foreach (ArmyMovePlan plan in movement.Armies)
                        if (plan != null && plan.Truncated) { truncated = true; break; }
                snap.MovementTruncated = truncated;
            }
            catch (Exception) { snap.UnknownFields.Add("prompt.visible_options.movement_json"); }

            AttackView attacks = null;
            if (config != null && config.EnableAttack)
            {
                try
                {
                    attacks = AttackPlanner.Build(engine, seatId);
                    snap.AttackJson = attacks == null ? null : attacks.ToJson();
                    snap.AttackTruncated = attacks != null && attacks.Truncated;
                }
                catch (Exception) { snap.UnknownFields.Add("prompt.visible_options.attacks_json"); }
            }

            try
            {
                List<ActionOption> options = ActionCatalogue.Build(obs, attacks, config != null && config.EnableResearch);
                snap.Catalogue = new List<CsCatalogueOption>();
                foreach (ActionOption option in options)
                {
                    snap.Catalogue.Add(new CsCatalogueOption
                    {
                        Type = option.Type, TargetId = option.TargetId, Label = option.Label,
                        Cost = option.CommandPointCost, LikelyAffordable = option.LikelyAffordable,
                        ParamA = option.ParamA, ParamB = option.ParamB, Args = option.Args,
                    });
                }
                snap.CatalogueText = ActionCatalogue.ToPromptText(options);
            }
            catch (Exception) { snap.UnknownFields.Add("prompt.visible_options.catalogue"); }
            return snap;
        }

        public static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Deterministic digest of the canonical state by walking public fields/properties. Scope:
        /// GameState and the balance profile, excluding private fields (RNG stream and counters) and
        /// the event log. Equal digests mean "the same canonical state", not "the same future".
        /// Researcher-side only; never a prompt input.
        /// </summary>
        public static string CanonicalDigest(object value)
        {
            var sb = new StringBuilder(32 * 1024);
            Walk(sb, "state", value, new List<object>(), 0);
            return Sha256(sb.ToString());
        }

        private const int MaxDepth = 24;

        private static void Walk(StringBuilder sb, string path, object value, List<object> ancestors, int depth)
        {
            sb.Append(path).Append('=');
            if (value == null) { sb.Append("null\n"); return; }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal)
            {
                sb.Append(value is double d ? d.ToString("R", CultureInfo.InvariantCulture)
                    : value is float f ? f.ToString("R", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
                return;
            }
            if (value is Delegate) { sb.Append("<delegate>\n"); return; }
            if (depth >= MaxDepth) { sb.Append("<depth>\n"); return; }
            foreach (object a in ancestors)
                if (ReferenceEquals(a, value)) { sb.Append("<cycle>\n"); return; }
            ancestors.Add(value);
            try
            {
                if (value is IDictionary dict)
                {
                    var parts = new List<KeyValuePair<string, string>>();
                    foreach (DictionaryEntry entry in dict)
                    {
                        var one = new StringBuilder();
                        Walk(one, "v", entry.Value, ancestors, depth + 1);
                        parts.Add(new KeyValuePair<string, string>(
                            Convert.ToString(entry.Key, CultureInfo.InvariantCulture), one.ToString()));
                    }
                    parts.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                    sb.Append("{count=").Append(parts.Count).Append("}\n");
                    foreach (var part in parts) sb.Append(part.Key).Append(':').Append(part.Value);
                    return;
                }
                if (value is IEnumerable items)
                {
                    bool set = IsSet(type);
                    var parts = new List<string>();
                    int index = 0;
                    foreach (object item in items)
                    {
                        var one = new StringBuilder();
                        Walk(one, set ? "{}" : "[" + index++ + "]", item, ancestors, depth + 1);
                        parts.Add(one.ToString());
                    }
                    if (set) parts.Sort(StringComparer.Ordinal);
                    sb.Append("[count=").Append(parts.Count).Append("]\n");
                    foreach (string part in parts) sb.Append(part);
                    return;
                }
                sb.Append("{\n");
                var members = new List<KeyValuePair<string, object>>();
                foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    members.Add(new KeyValuePair<string, object>(f.Name, f.GetValue(value)));
                foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                    object v;
                    try { v = p.GetValue(value); } catch (Exception) { v = null; }
                    members.Add(new KeyValuePair<string, object>(p.Name, v));
                }
                members.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var member in members) Walk(sb, member.Key, member.Value, ancestors, depth + 1);
                sb.Append("}\n");
            }
            finally { ancestors.RemoveAt(ancestors.Count - 1); }
        }

        private static bool IsSet(Type type)
        {
            foreach (Type i in type.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>)) return true;
            return false;
        }
    }
}
