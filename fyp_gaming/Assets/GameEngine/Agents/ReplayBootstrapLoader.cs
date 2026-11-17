using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    public sealed class ReplayBootstrapFeatures
    {
        public bool Attack;
        public bool Research;
        public bool Diplomacy;
        public bool ManualLookup;
        public bool Deliberation;
        public bool VisibleMarchRoutes;
    }

    public sealed class ReplayBootstrapData
    {
        public string Schema;
        public string Status;
        public string RunId;
        public string GameId;
        public int Seed;
        public string ScenarioId;
        public string HostMode;
        public int PlannedSeasons;
        public BalanceProfile BalanceProfile;
        public ReplayBootstrapFeatures Features = new ReplayBootstrapFeatures();
        public string InitialMapSha256;
        public string InitialWorldSha256;
        public JsonObject RawMap;
        public JsonObject RawWorld;
    }

    /// <summary>
    /// Loads local-only <c>-bootstrap.json</c> snapshots and reconstructs an authoritative
    /// <see cref="GameEngine"/> at the exact pre-request initial state. Never pass this data to
    /// any model or prompt builder.
    /// </summary>
    public static class ReplayBootstrapLoader
    {
        public static ReplayBootstrapData LoadFromFile(string bootstrapPath, string expectedRunId, int? expectedSeed = null)
        {
            if (string.IsNullOrWhiteSpace(bootstrapPath) || !File.Exists(bootstrapPath))
                throw new FileNotFoundException("Replay bootstrap file missing: " + bootstrapPath);

            string text = File.ReadAllText(bootstrapPath, Encoding.UTF8);
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode rootNode, out string err) || !(rootNode is JsonObject root))
                throw new InvalidDataException("Invalid replay bootstrap JSON: " + err);

            string schema = Str(root, "schema");
            if (!string.Equals(schema, ReplayBootstrap.Schema, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported replay bootstrap schema '{schema}' (expected '{ReplayBootstrap.Schema}').");

            string runId = Str(root, "run_id");
            if (!string.IsNullOrEmpty(expectedRunId) && !string.Equals(runId, expectedRunId, StringComparison.Ordinal))
                throw new InvalidDataException($"Bootstrap run_id '{runId}' does not match expected '{expectedRunId}'.");

            if (!root.TryGet("seed", out JsonNode seedNode) || !(seedNode is JsonInt seedInt))
                throw new InvalidDataException("Bootstrap missing integer 'seed'.");
            int seed = (int)seedInt.Value;
            if (expectedSeed.HasValue && seed != expectedSeed.Value)
                throw new InvalidDataException($"Bootstrap seed {seed} does not match expected {expectedSeed.Value}.");

            string mapSha = Str(root, "initial_map_sha256");
            string worldSha = Str(root, "initial_world_sha256");
            string rawMapSlice = ExtractRawJsonObjectSlice(text, "\"initial_map\":");
            string rawWorldSlice = ExtractRawJsonObjectSlice(text, "\"initial_world\":");
            if (string.IsNullOrEmpty(rawMapSlice) || string.IsNullOrEmpty(rawWorldSlice))
                throw new InvalidDataException("Bootstrap missing initial_map or initial_world object.");

            if (!string.IsNullOrEmpty(mapSha) && !string.Equals(Sha256(rawMapSlice), mapSha, StringComparison.Ordinal))
                throw new InvalidDataException("Bootstrap initial_map_sha256 mismatch (snapshot tampered or corrupted).");
            if (!string.IsNullOrEmpty(worldSha) && !string.Equals(Sha256(rawWorldSlice), worldSha, StringComparison.Ordinal))
                throw new InvalidDataException("Bootstrap initial_world_sha256 mismatch (snapshot tampered or corrupted).");

            if (!root.TryGet("initial_map", out JsonNode mapNode) || !(mapNode is JsonObject mapObj))
                throw new InvalidDataException("Bootstrap initial_map is not an object.");
            if (!root.TryGet("initial_world", out JsonNode worldNode) || !(worldNode is JsonObject worldObj))
                throw new InvalidDataException("Bootstrap initial_world is not an object.");

            BalanceProfile profile = ParseBalanceProfile(root);
            ReplayBootstrapFeatures features = ParseFeatures(root);

            return new ReplayBootstrapData
            {
                Schema = schema,
                Status = Str(root, "status"),
                RunId = runId,
                GameId = Str(root, "game_id"),
                Seed = seed,
                ScenarioId = Str(root, "scenario_id", null),
                HostMode = Str(root, "host_mode"),
                PlannedSeasons = Int(root, "planned_seasons"),
                BalanceProfile = profile,
                Features = features,
                InitialMapSha256 = mapSha,
                InitialWorldSha256 = worldSha,
                RawMap = mapObj,
                RawWorld = worldObj,
            };
        }

        public static GameEngine CreateEngine(ReplayBootstrapData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            JsonObject w = data.RawWorld;
            string gameId = Str(w, "game_id", data.GameId);
            var engine = new GameEngine(data.Seed, gameId, data.BalanceProfile);
            engine.State.Map = ParseMap(data.RawMap);
            engine.State.ScenarioId = Str(w, "scenario_id", data.ScenarioId);
            if (string.IsNullOrEmpty(engine.State.ScenarioId)) engine.State.ScenarioId = null;
            engine.State.Season = Int(w, "season", 1);
            engine.State.Phase = Str(w, "phase", "player_orders");
            string winner = Str(w, "winner_seat_id", null);
            engine.State.WinnerSeatId = string.IsNullOrEmpty(winner) ? null : winner;

            ParseFactions(w, engine.State);
            ParseCities(w, engine.State);
            ParseArmies(w, engine.State);
            ParseWatchTowers(w, engine.State);
            ParseDiplomacy(w, engine.State);
            ParseInitialEvents(w, engine);
            return engine;
        }

        private static BalanceProfile ParseBalanceProfile(JsonObject root)
        {
            if (!root.TryGet("balance_profile", out JsonNode bpNode) || !(bpNode is JsonObject bp))
                return BalanceProfile.Standard;
            string key = Str(bp, "profile");
            if (BalanceProfile.TryGet(key, out BalanceProfile known)) return known;
            throw new InvalidDataException($"Unknown balance profile '{key}' in replay bootstrap.");
        }

        private static ReplayBootstrapFeatures ParseFeatures(JsonObject root)
        {
            var f = new ReplayBootstrapFeatures();
            if (root.TryGet("features", out JsonNode n) && n is JsonObject obj)
            {
                f.Attack = Bool(obj, "attack");
                f.Research = Bool(obj, "research");
                f.Diplomacy = Bool(obj, "diplomacy");
                f.ManualLookup = Bool(obj, "manual_lookup");
                f.Deliberation = Bool(obj, "deliberation");
                f.VisibleMarchRoutes = Bool(obj, "visible_march_routes");
            }
            return f;
        }

        private static MapData ParseMap(JsonObject m)
        {
            int w = Int(m, "width");
            int h = Int(m, "height");
            int count = w * h;
            if (w <= 0 || h <= 0) throw new InvalidDataException("Bootstrap map dimensions invalid.");

            if (!m.TryGet("walkable", out JsonNode walkN) || !(walkN is JsonArray walkA) || walkA.Items.Count != count)
                throw new InvalidDataException("Bootstrap map walkable array invalid.");
            if (!m.TryGet("cost", out JsonNode costN) || !(costN is JsonArray costA) || costA.Items.Count != count)
                throw new InvalidDataException("Bootstrap map cost array invalid.");
            if (!m.TryGet("neighbors", out JsonNode neighN) || !(neighN is JsonArray neighA) || neighA.Items.Count != count)
                throw new InvalidDataException("Bootstrap map neighbors array invalid.");

            var map = new MapData
            {
                Width = w,
                Height = h,
                Walkable = new bool[count],
                Cost = new int[count],
                Neighbors = new int[count][],
            };
            for (int i = 0; i < count; i++)
            {
                map.Walkable[i] = walkA.Items[i] is JsonBool b && b.Value;
                map.Cost[i] = costA.Items[i] is JsonInt ci ? (int)ci.Value : 1;
                if (neighA.Items[i] is JsonArray na)
                {
                    var ns = new int[na.Items.Count];
                    for (int j = 0; j < na.Items.Count; j++)
                        ns[j] = na.Items[j] is JsonInt ni ? (int)ni.Value : 0;
                    map.Neighbors[i] = ns;
                }
                else map.Neighbors[i] = Array.Empty<int>();
            }
            return map;
        }

        private static void ParseFactions(JsonObject w, GameState state)
        {
            if (!w.TryGet("factions", out JsonNode fn) || !(fn is JsonArray arr)) return;
            foreach (JsonNode item in arr.Items)
            {
                if (!(item is JsonObject fWrap)) continue;
                JsonObject s = fWrap.TryGet("state", out JsonNode sn) && sn is JsonObject so ? so : fWrap;
                var fac = new FactionState
                {
                    SeatId = Str(s, "seat_id"),
                    Name = Str(s, "name"),
                    Controller = Str(s, "controller_type", ControllerType.Llm),
                    CommandPoints = Int(s, "command_points"),
                    Prestige = Int(s, "prestige", BalanceConfig.PrestigeStart),
                    ResearchPoints = Int(s, "research_points"),
                    RevoltChanceMult = (float)Double(fWrap, "revolt_chance_mult", 1.0),
                };
                if (s.TryGet("research", out JsonNode rn) && rn is JsonObject ro)
                {
                    foreach (string line in ResearchLine.All)
                    {
                        int lvl = Int(ro, line, 0);
                        if (lvl > 0) fac.Research[line] = lvl;
                    }
                }
                if (fWrap.TryGet("scouted_cities", out JsonNode scn) && scn is JsonArray sca)
                {
                    foreach (JsonNode scItem in sca.Items)
                    {
                        if (scItem is JsonObject scObj)
                            fac.AddScoutReport(Str(scObj, "city_id"), Int(scObj, "until_season"));
                    }
                }
                state.Factions.Add(fac);
            }
        }

        private static void ParseCities(JsonObject w, GameState state)
        {
            if (!w.TryGet("cities", out JsonNode cn) || !(cn is JsonArray arr)) return;
            foreach (JsonNode item in arr.Items)
            {
                if (!(item is JsonObject c)) continue;
                string owner = Str(c, "owner_seat_id", null);
                state.Cities.Add(new CityState
                {
                    Id = Str(c, "id"),
                    Name = Str(c, "name"),
                    X = Int(c, "x"),
                    Z = Int(c, "z"),
                    OwnerSeatId = string.IsNullOrEmpty(owner) ? null : owner,
                    Population = Int(c, "population"),
                    Food = Int(c, "food"),
                    Gold = Int(c, "gold"),
                    Garrison = Int(c, "garrison"),
                    GarrisonType = Str(c, "garrison_type", UnitTypes.Infantry),
                    Morale = Int(c, "morale", BalanceConfig.MoraleStart),
                    Defense = Int(c, "defense", BalanceConfig.DefenseStart),
                    Training = Int(c, "training", BalanceConfig.TrainingStart),
                    ReclaimPct = Int(c, "reclaim_pct", BalanceConfig.ReclaimPctStart),
                    ReclaimedThisSeason = Bool(c, "reclaimed_this_season"),
                    GarrisonMarchedThisSeason = Bool(c, "garrison_marched_this_season"),
                });
            }
        }

        private static void ParseArmies(JsonObject w, GameState state)
        {
            if (!w.TryGet("armies", out JsonNode an) || !(an is JsonArray arr)) return;
            foreach (JsonNode item in arr.Items)
            {
                if (!(item is JsonObject a)) continue;
                state.Armies.Add(new ArmyState
                {
                    Id = Str(a, "id"),
                    Name = Str(a, "name"),
                    OwnerSeatId = Str(a, "owner_seat_id"),
                    Troops = Int(a, "troops"),
                    UnitType = Str(a, "unit_type", UnitTypes.Infantry),
                    X = Int(a, "x"),
                    Z = Int(a, "z"),
                    Morale = Int(a, "morale", BalanceConfig.ArmyMoraleStart),
                    MarchedThisSeason = Bool(a, "marched_this_season"),
                    MarchCountThisSeason = Int(a, "march_count_this_season"),
                });
            }
        }

        private static void ParseWatchTowers(JsonObject w, GameState state)
        {
            if (!w.TryGet("watch_towers", out JsonNode tn) || !(tn is JsonArray arr)) return;
            foreach (JsonNode item in arr.Items)
            {
                if (!(item is JsonObject t)) continue;
                string owner = Str(t, "owner_seat_id", null);
                state.WatchTowers.Add(new WatchTowerState
                {
                    Id = Str(t, "id"),
                    Name = Str(t, "name"),
                    X = Int(t, "x"),
                    Z = Int(t, "z"),
                    OwnerSeatId = string.IsNullOrEmpty(owner) ? null : owner,
                });
            }
        }

        private static void ParseDiplomacy(JsonObject w, GameState state)
        {
            if (!w.TryGet("diplomacy", out JsonNode dn) || !(dn is JsonObject d)) return;
            if (d.TryGet("trust", out JsonNode trN) && trN is JsonObject trObj)
                foreach (var kv in trObj.Items)
                    if (kv.Value is JsonInt iv) state.Diplomacy.Trust[kv.Key] = (int)iv.Value;

            if (d.TryGet("war_with", out JsonNode warN) && warN is JsonArray warArr)
                foreach (JsonNode wn in warArr.Items)
                    if (wn is JsonString ws && !string.IsNullOrEmpty(ws.Value)) state.Diplomacy.WarWith.Add(ws.Value);

            if (d.TryGet("treaties", out JsonNode tN) && tN is JsonArray tArr)
                foreach (JsonNode tn in tArr.Items)
                    if (tn is JsonObject to)
                        state.Diplomacy.Treaties.Add(new TreatyRecord
                        {
                            Type = Str(to, "type"),
                            FactionA = Str(to, "faction_a"),
                            FactionB = Str(to, "faction_b"),
                            ExpirySeason = Int(to, "expiry_season"),
                        });

            if (d.TryGet("proposals", out JsonNode pN) && pN is JsonArray pArr)
                foreach (JsonNode pn in pArr.Items)
                    if (pn is JsonObject po)
                        state.Diplomacy.Proposals.Add(new TreatyProposal
                        {
                            ProposalId = Str(po, "proposal_id"),
                            FromSeatId = Str(po, "from_seat_id"),
                            ToSeatId = Str(po, "to_seat_id"),
                            Type = Str(po, "type"),
                            DurationSeasons = Int(po, "duration_seasons"),
                            ProposedSeason = Int(po, "proposed_season"),
                            Text = Str(po, "text"),
                        });
        }

        private static void ParseInitialEvents(JsonObject w, GameEngine engine)
        {
            if (!w.TryGet("initial_events", out JsonNode en) || !(en is JsonArray arr)) return;
            foreach (JsonNode item in arr.Items)
            {
                if (!(item is JsonObject ev)) continue;
                var payload = new Dictionary<string, string>(StringComparer.Ordinal);
                if (ev.TryGet("payload", out JsonNode pn) && pn is JsonObject po)
                    foreach (var kv in po.Items) payload[kv.Key] = kv.Value is JsonString ps ? ps.Value : kv.Value.ToString();
                Dictionary<string, string> args = null;
                if (ev.TryGet("args", out JsonNode an) && an is JsonObject ao)
                {
                    args = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in ao.Items) args[kv.Key] = kv.Value is JsonString asv ? asv.Value : kv.Value.ToString();
                }
                string actor = Str(ev, "actor_seat_id", null);
                string ctrl = Str(ev, "controller_type", null);
                string src = Str(ev, "source_action_id", null);
                string txt = Str(ev, "text", null);
                engine.RestoreInitialEvent(
                    Str(ev, "game_id", engine.State.GameId),
                    Int(ev, "season", engine.State.Season),
                    Str(ev, "phase", engine.State.Phase),
                    string.IsNullOrEmpty(actor) ? null : actor,
                    string.IsNullOrEmpty(ctrl) ? null : ctrl,
                    Str(ev, "event_type"),
                    Str(ev, "visibility", "public"),
                    payload,
                    string.IsNullOrEmpty(src) ? null : src,
                    args,
                    string.IsNullOrEmpty(txt) ? null : txt);
            }
        }

        private static string ExtractRawJsonObjectSlice(string json, string keyMarker)
        {
            int idx = json.IndexOf(keyMarker, StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = json.IndexOf('{', idx + keyMarker.Length);
            if (start < 0) return null;
            int depth = 0;
            bool inStr = false;
            bool esc = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0) return json.Substring(start, i - start + 1);
                    }
                }
            }
            return null;
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string Str(JsonObject obj, string key, string fallback = "")
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonString s ? s.Value : fallback;
        private static int Int(JsonObject obj, string key, int fallback = 0)
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonInt i ? (int)i.Value : fallback;
        private static double Double(JsonObject obj, string key, double fallback = 0.0)
        {
            if (obj == null || !obj.TryGet(key, out JsonNode n)) return fallback;
            if (n is JsonInt i) return i.Value;
            if (n is JsonString s && double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv)) return dv;
            if (double.TryParse(n.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double dv2)) return dv2;
            return fallback;
        }
        private static bool Bool(JsonObject obj, string key, bool fallback = false)
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonBool b ? b.Value : fallback;
    }
}
