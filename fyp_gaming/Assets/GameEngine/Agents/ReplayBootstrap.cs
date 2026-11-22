using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Private, local-only input snapshot for a future deterministic replay. This is deliberately
    /// separate from observations and is never passed to a controller or prompt builder.
    /// </summary>
    public static class ReplayBootstrap
    {
        public const string Schema = "luanshi-replay-bootstrap/1";
        public const string NotReplayVerified = "capture_only_not_replay_verified";

        public static string BuildJson(GameEngine engine, int seed, string runId,
            LlmConfig config, string buildRevision, string hostMode, int plannedSeasons)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (engine.State.Map == null) throw new InvalidOperationException("replay bootstrap unavailable: map missing");
            if (string.IsNullOrWhiteSpace(runId)) throw new InvalidOperationException("replay bootstrap unavailable: run id missing");

            bool configVisible = config != null && config.EffectiveVisibleMarchRoutes;
            bool engineVisible = engine.State.RequireVisibleMarchRoutes;
            if (configVisible && !engineVisible)
                throw new InvalidOperationException(
                    "replay bootstrap unavailable: visible_march_routes config diverges from engine state rule");

            string world = WorldJson(engine);
            string map = MapJson(engine.State.Map);
            var sb = new StringBuilder(world.Length + map.Length + 1024);
            sb.Append("{\"schema\":").Append(MiniJson.Str(Schema));
            sb.Append(",\"status\":").Append(MiniJson.Str(NotReplayVerified));
            sb.Append(",\"local_only\":true,\"contains_hidden_canonical_state\":true,\"safe_for_prompts\":false");
            sb.Append(",\"run_id\":").Append(MiniJson.Str(runId));
            sb.Append(",\"game_id\":").Append(MiniJson.Str(engine.State.GameId));
            sb.Append(",\"seed\":").Append(MiniJson.Num(seed));
            sb.Append(",\"scenario_id\":").Append(MiniJson.Str(engine.State.ScenarioId));
            sb.Append(",\"build_revision\":").Append(MiniJson.Str(string.IsNullOrWhiteSpace(buildRevision) ? "unknown" : buildRevision.Trim()));
            sb.Append(",\"build_revision_status\":").Append(MiniJson.Str(string.IsNullOrWhiteSpace(buildRevision) ? "unavailable_use_assembly_mvid" : "provided"));
            sb.Append(",\"engine_assembly_mvid\":").Append(MiniJson.Str(typeof(GameEngine).GetTypeInfo().Assembly.ManifestModule.ModuleVersionId.ToString("D")));
            sb.Append(",\"runtime_version\":").Append(MiniJson.Str(Environment.Version.ToString()));
            sb.Append(",\"host_mode\":").Append(MiniJson.Str(hostMode));
            sb.Append(",\"planned_seasons\":").Append(MiniJson.Num(Math.Max(0, plannedSeasons)));
            sb.Append(",\"features\":{\"attack\":").Append(B(config != null && config.EnableAttack));
            sb.Append(",\"research\":").Append(B(config != null && config.EnableResearch));
            if (config != null && config.EnableGarrisonConversion)
                sb.Append(",\"garrison_conversion\":true");
            sb.Append(",\"diplomacy\":").Append(B(config != null && config.EnableDiplomacy));
            sb.Append(",\"manual_lookup\":").Append(B(config != null && config.EnableManualLookup));
            sb.Append(",\"deliberation\":").Append(B(config != null && config.EnableDeliberation));
            sb.Append(",\"visible_march_routes\":").Append(B(engineVisible || configVisible)).Append('}');
            sb.Append(",\"balance_profile\":").Append(engine.Balance.ToJson());
            sb.Append(",\"initial_map_sha256\":").Append(MiniJson.Str(Sha256(map)));
            sb.Append(",\"initial_world_sha256\":").Append(MiniJson.Str(Sha256(world)));
            sb.Append(",\"initial_map\":").Append(map);
            sb.Append(",\"initial_world\":").Append(world);
            sb.Append('}');
            return sb.ToString();
        }

        private static string WorldJson(GameEngine engine)
        {
            GameState s = engine.State;
            var factions = new List<string>();
            foreach (FactionState f in s.Factions)
            {
                var scouts = new List<string>();
                foreach (ScoutReport r in f.ScoutedCities)
                    scouts.Add("{\"city_id\":" + MiniJson.Str(r.CityId) + ",\"until_season\":" + MiniJson.Num(r.UntilSeason) + "}");
                factions.Add("{\"state\":" + f.ToJson() + ",\"revolt_chance_mult\":" + f.RevoltChanceMult.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"scouted_cities\":" + MiniJson.Array(scouts) + "}");
            }
            var cities = new List<string>();
            foreach (CityState c in s.Cities)
                cities.Add(c.ToJson().Substring(0, c.ToJson().Length - 1)
                    + ",\"reclaimed_this_season\":" + B(c.ReclaimedThisSeason)
                    + ",\"garrison_marched_this_season\":" + B(c.GarrisonMarchedThisSeason) + "}");
            var armies = new List<string>();
            foreach (ArmyState a in s.Armies)
                armies.Add(a.ToJson().Substring(0, a.ToJson().Length - 1)
                    + ",\"marched_this_season\":" + B(a.MarchedThisSeason)
                    + ",\"march_count_this_season\":" + MiniJson.Num(a.MarchCountThisSeason) + "}");
            var towers = new List<string>();
            foreach (WatchTowerState t in s.WatchTowers) towers.Add(t.ToJson());

            return "{\"game_id\":" + MiniJson.Str(s.GameId)
                + ",\"scenario_id\":" + MiniJson.Str(s.ScenarioId)
                + ",\"require_visible_march_routes\":" + B(s.RequireVisibleMarchRoutes)
                + ",\"season\":" + MiniJson.Num(s.Season)
                + ",\"phase\":" + MiniJson.Str(s.Phase)
                + ",\"winner_seat_id\":" + MiniJson.Str(s.WinnerSeatId)
                + ",\"factions\":" + MiniJson.Array(factions)
                + ",\"cities\":" + MiniJson.Array(cities)
                + ",\"armies\":" + MiniJson.Array(armies)
                + ",\"watch_towers\":" + MiniJson.Array(towers)
                + ",\"diplomacy\":" + s.Diplomacy.ToJson()
                + ",\"initial_events\":" + MiniJson.Array(new List<string>(engine.Log.ToJsonLines().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))) + "}";
        }

        private static string MapJson(MapData map)
        {
            int count = map.Width * map.Height;
            if (map.Width <= 0 || map.Height <= 0 || map.Walkable == null || map.Walkable.Length != count
                || map.Cost == null || map.Cost.Length != count || map.Neighbors == null || map.Neighbors.Length != count)
                throw new InvalidOperationException("replay bootstrap unavailable: map arrays are incomplete");
            var walk = new List<string>();
            var cost = new List<string>();
            var neighbors = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (map.Neighbors[i] == null || map.Cost[i] < 1)
                    throw new InvalidOperationException("replay bootstrap unavailable: map cell data is incomplete");
                walk.Add(map.Walkable[i] ? "true" : "false");
                cost.Add(MiniJson.Num(map.Cost[i]));
                var ns = new List<string>();
                foreach (int n in map.Neighbors[i])
                {
                    if (n < 0 || n >= count)
                        throw new InvalidOperationException("replay bootstrap unavailable: map neighbor index is invalid");
                    ns.Add(MiniJson.Num(n));
                }
                neighbors.Add(MiniJson.Array(ns));
            }
            return "{\"width\":" + MiniJson.Num(map.Width) + ",\"height\":" + MiniJson.Num(map.Height)
                + ",\"walkable\":" + MiniJson.Array(walk) + ",\"cost\":" + MiniJson.Array(cost)
                + ",\"neighbors\":" + MiniJson.Array(neighbors) + "}";
        }

        private static string B(bool value) => value ? "true" : "false";
        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
