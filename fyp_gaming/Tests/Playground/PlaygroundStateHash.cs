using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Stable SHA-256 over the canonical fields that can affect validation. The replay equivalence
    /// hash deliberately excludes the event log; the authoritative hash adds the log record count so
    /// a Check that accidentally submitted on live state would be visible immediately.
    /// </summary>
    internal static class PlaygroundStateHash
    {
        public static string Canonical(GameEngine engine)
        {
            if (engine == null) return "none";
            GameState state = engine.State;
            var sb = new StringBuilder(8192);
            sb.Append("game=").Append(State(state.GameId)).Append('\n');
            sb.Append("season=").Append(state.Season.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("phase=").Append(State(state.Phase)).Append('\n');
            sb.Append("winner=").Append(State(state.WinnerSeatId)).Append('\n');

            var factions = new List<FactionState>(state.Factions);
            factions.Sort((a, b) => string.CompareOrdinal(a.SeatId, b.SeatId));
            foreach (FactionState faction in factions)
                sb.Append("faction=").Append(Faction(faction)).Append('\n');

            var cities = new List<CityState>(state.Cities);
            cities.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (CityState city in cities)
                sb.Append("city=").Append(City(city)).Append('\n');

            var armies = new List<ArmyState>(state.Armies);
            armies.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (ArmyState army in armies)
                sb.Append("army=").Append(Army(army)).Append('\n');

            var towers = new List<WatchTowerState>(state.WatchTowers);
            towers.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (WatchTowerState tower in towers)
                sb.Append("tower=").Append(Tower(tower)).Append('\n');

            sb.Append("diplomacy=").Append(state.Diplomacy.ToJson()).Append('\n');
            if (state.Map != null)
                sb.Append("map=").Append(Map(state.Map)).Append('\n');
            return Hash(sb.ToString());
        }

        public static string Authoritative(GameEngine engine)
        {
            if (engine == null) return "none";
            return Hash(Canonical(engine) + "|log="
                + engine.Log.Records.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static string Faction(FactionState faction)
        {
            var scouts = new List<string>();
            foreach (ScoutReport report in faction.ScoutedCities)
                scouts.Add(State(report.CityId) + ":" + report.UntilSeason.ToString(CultureInfo.InvariantCulture));
            scouts.Sort(StringComparer.Ordinal);
            return faction.ToJson() + "|revolt_mult="
                + faction.RevoltChanceMult.ToString("0.######", CultureInfo.InvariantCulture)
                + "|scouted=" + string.Join(",", scouts);
        }

        private static string City(CityState city)
            => city.ToJson()
               + "|reclaimed_this_season=" + (city.ReclaimedThisSeason ? "1" : "0")
               + "|garrison_marched=" + (city.GarrisonMarchedThisSeason ? "1" : "0");

        private static string Army(ArmyState army)
            => army.ToJson()
               + "|marched_this_season=" + (army.MarchedThisSeason ? "1" : "0")
               + "|march_count=" + army.MarchCountThisSeason.ToString(CultureInfo.InvariantCulture);

        private static string Tower(WatchTowerState tower) => tower.ToJson();

        private static string Map(MapData map)
        {
            var sb = new StringBuilder(map.Width * map.Height + 32);
            sb.Append(map.Width).Append('x').Append(map.Height).Append('|');
            for (int i = 0; i < map.Walkable.Length; i++)
                sb.Append(map.Walkable[i] ? (char)('0' + Math.Min(9, map.Cost[i])) : '#');
            return sb.ToString();
        }

        private static string State(string value) => value ?? "null";

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(64);
                foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
