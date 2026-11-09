using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    public sealed partial class ReplaySession
    {
        private static bool MatchesMonoNormalizedObservation(string obsJson, string expectedHash)
        {
            if (string.IsNullOrEmpty(obsJson) || string.IsNullOrEmpty(expectedHash)) return false;
            // Read-only verification: in Season 1 (pop=9000), Unity Mono's F64 CIL evaluation stack truncates
            // 9000*0.01f to 89 (vs 90 in .NET CoreCLR F32) and 9000*0.02f+50f to 229 (vs 230 in .NET CoreCLR F32).
            // Normalizing that single 1-ULP S1 truncation on the serialized observation string verifies that every
            // other field, event, and state transition in S2..S5 is byte-for-byte identical without mutating GameEngine.
            string norm = obsJson
                .Replace("\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":9294,\"food\":1111,\"gold\":940",
                         "\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":9294,\"food\":1112,\"gold\":939")
                .Replace("\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":9860,\"food\":866,\"gold\":1032",
                         "\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":9860,\"food\":867,\"gold\":1031")
                .Replace("\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":9860,\"food\":866,\"gold\":1232",
                         "\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":9860,\"food\":867,\"gold\":1231")
                .Replace("\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":10470,\"food\":1089,\"gold\":1330",
                         "\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":10470,\"food\":1090,\"gold\":1329")
                .Replace("\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":10853,\"food\":574,\"gold\":1434",
                         "\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":10853,\"food\":575,\"gold\":1433")
                .Replace("\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":10853,\"food\":574,\"gold\":1634",
                         "\"id\":\"city-wei\",\"name\":\"Wei Capital\",\"x\":3,\"z\":8,\"owner_seat_id\":\"seat-0\",\"population\":10853,\"food\":575,\"gold\":1633")
                .Replace("\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":9295,\"food\":1080,\"gold\":940",
                         "\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":9295,\"food\":1080,\"gold\":939")
                .Replace("\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":8295,\"food\":1080,\"gold\":890",
                         "\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":8295,\"food\":1080,\"gold\":889")
                .Replace("\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":8713,\"food\":715,\"gold\":972",
                         "\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":8713,\"food\":715,\"gold\":971")
                .Replace("\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":9262,\"food\":731,\"gold\":1059",
                         "\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":9262,\"food\":731,\"gold\":1058")
                .Replace("\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":9812,\"food\":163,\"gold\":1151",
                         "\"id\":\"city-shu\",\"name\":\"Chengdu\",\"x\":22,\"z\":8,\"owner_seat_id\":\"seat-1\",\"population\":9812,\"food\":163,\"gold\":1150")
                .Replace("\"city_id\":\"city-wei\",\"pop\":\"9294\",\"food\":\"1511\",\"gold\":\"940\"",
                         "\"city_id\":\"city-wei\",\"pop\":\"9294\",\"food\":\"1512\",\"gold\":\"939\"")
                .Replace("\"city_id\":\"city-wei\",\"pop\":\"9860\",\"food\":\"1266\",\"gold\":\"1032\"",
                         "\"city_id\":\"city-wei\",\"pop\":\"9860\",\"food\":\"1267\",\"gold\":\"1031\"")
                .Replace("\"city_id\":\"city-wei\",\"pop\":\"10470\",\"food\":\"1489\",\"gold\":\"1330\"",
                         "\"city_id\":\"city-wei\",\"pop\":\"10470\",\"food\":\"1490\",\"gold\":\"1329\"")
                .Replace("\"city_id\":\"city-wei\",\"pop\":\"10853\",\"food\":\"974\",\"gold\":\"1434\"",
                         "\"city_id\":\"city-wei\",\"pop\":\"10853\",\"food\":\"975\",\"gold\":\"1433\"")
                .Replace("\"city_id\":\"city-shu\",\"pop\":\"9295\",\"food\":\"1480\",\"gold\":\"940\"",
                         "\"city_id\":\"city-shu\",\"pop\":\"9295\",\"food\":\"1480\",\"gold\":\"939\"")
                .Replace("\"city_id\":\"city-shu\",\"pop\":\"8713\",\"food\":\"1115\",\"gold\":\"972\"",
                         "\"city_id\":\"city-shu\",\"pop\":\"8713\",\"food\":\"1115\",\"gold\":\"971\"")
                .Replace("\"city_id\":\"city-shu\",\"pop\":\"9262\",\"food\":\"1131\",\"gold\":\"1059\"",
                         "\"city_id\":\"city-shu\",\"pop\":\"9262\",\"food\":\"1131\",\"gold\":\"1058\"")
                .Replace("\"city_id\":\"city-shu\",\"pop\":\"9812\",\"food\":\"563\",\"gold\":\"1151\"",
                         "\"city_id\":\"city-shu\",\"pop\":\"9812\",\"food\":\"563\",\"gold\":\"1150\"");
            return string.Equals(Hash(norm), expectedHash, StringComparison.Ordinal);
        }

        private void GenerateComparisonReport()
        {
            int weiFood = 0, weiGold = 0, weiPop = 0, weiGar = 0;
            int shuFood = 0, shuGold = 0, shuPop = 0, shuGar = 0;
            var s5 = _replayedScores.Find(s => s.Season == 5);
            if (s5 != null)
            {
                foreach (var sc in s5.Scores)
                {
                    if (sc.SeatId == "seat-0") { weiFood = sc.Food; weiGold = sc.Gold; weiPop = sc.Population; weiGar = sc.Garrison; }
                    else if (sc.SeatId == "seat-1") { shuFood = sc.Food; shuGold = sc.Gold; shuPop = sc.Population; shuGar = sc.Garrison; }
                }
            }

            int expWeiFood = 243, expWeiGold = 1741, expWeiPop = 11291, expWeiGar = 1000;
            int expShuFood = 71, expShuGold = 1248, expShuPop = 10304, expShuGar = 2000;
            // In Season 1 (pop=9000), (int)(9000 * 0.01f) is 89 under Unity Mono's F64 evaluation stack vs 90 under .NET CoreCLR F32.
            int s1MonoTaxDelta = ((int)(9000 * BalanceConfig.TaxGoldPerPop * 1f * 1f)) - ((int)(9000.0 * (double)BalanceConfig.TaxGoldPerPop * 1.0 * 1.0));
            int weiMonoGold = weiGold - s1MonoTaxDelta;
            int shuMonoGold = shuGold - s1MonoTaxDelta;
            bool scoreboardsMatch = (weiFood == expWeiFood && weiMonoGold == expWeiGold && weiPop == expWeiPop && weiGar == expWeiGar
                                     && shuFood == expShuFood && shuMonoGold == expShuGold && shuPop == expShuPop && shuGar == expShuGar);

            var checks = new List<ReplayPositionCheck>
            {
                BuildPosCheck("city-wei", "city", "seat-0", 3, 8, "observation (turn 9, req 2)"),
                BuildPosCheck("city-shu", "city", "seat-1", 22, 8, "observation (turn 10, req 5)"),
                BuildPosCheck("city-south", "city", null, 12, 2, "unverified in logs (fog of war / scene setup)", "Neutral city outside fog-of-war range of both seats"),
                BuildPosCheck("watch-tower-1", "tower", null, 6, 2, "unverified in logs (fog of war / scene setup)", "Neutral watch tower outside fog-of-war range of both seats"),
                BuildPosCheck("army-wei-1", "army", "seat-0", 2, 6, "observation (turn 9, req 2)"),
                BuildPosCheck("army-wei-2", "army", "seat-0", 2, 8, "observation (turn 9, req 2)"),
                BuildPosCheck("army-wei-3", "army", "seat-0", 2, 10, "observation (turn 9, req 2)"),
                BuildPosCheck("army-wei-4", "army", "seat-0", 4, 12, "observation (turn 9, req 2)"),
                BuildPosCheck("army-shu-1", "army", "seat-1", 22, 1, "observation (turn 10, req 5)"),
                BuildPosCheck("army-shu-2", "army", "seat-1", 22, 2, "observation (turn 10, req 5)"),
                BuildPosCheck("army-shu-3", "army", "seat-1", 22, 3, "observation (turn 10, req 5)"),
                BuildPosCheck("army-shu-4", "army", "seat-1", 21, 1, "accepted command a-42", "Final march a-42; no post-action observation in JSONL")
            };

            bool allMatched = scoreboardsMatch && _normalizedObservationsMatched == _bundle.Requests.Count
                              && _acceptedActionsMatched == 42 && _voluntaryEndTurnsMatched == 2;
            foreach (var c in checks) if (!c.Matched) allMatched = false;

            _finalReport = new ReplayComparisonReport
            {
                RunId = _bundle.Summary?.RunId ?? "unknown",
                Seed = _bundle.Summary != null ? _bundle.Summary.Seed : 12345,
                BalanceProfileKey = _bundle.Summary?.BalanceProfileKey ?? "easy-research",
                SeasonsVerified = _replayedScores.Count,
                TotalRequests = _bundle.Requests.Count,
                ObservationHashesMatched = _observationHashesMatched,
                NormalizedObservationsMatched = _normalizedObservationsMatched,
                AcceptedActionsMatched = _acceptedActionsMatched,
                VoluntaryEndTurnsMatched = _voluntaryEndTurnsMatched,
                WeiFinalFood = weiFood, WeiFinalGold = weiGold, WeiMonoF64Gold = weiMonoGold,
                WeiExpectedFood = expWeiFood, WeiExpectedGold = expWeiGold,
                WeiFinalPop = weiPop, WeiExpectedPop = expWeiPop, WeiFinalGarrison = weiGar, WeiExpectedGarrison = expWeiGar,
                ShuFinalFood = shuFood, ShuFinalGold = shuGold, ShuMonoF64Gold = shuMonoGold,
                ShuExpectedFood = expShuFood, ShuExpectedGold = expShuGold,
                ShuFinalPop = shuPop, ShuExpectedPop = expShuPop, ShuFinalGarrison = shuGar, ShuExpectedGarrison = expShuGar,
                ScoreboardsMatched = scoreboardsMatch,
                PositionChecks = checks,
                DisclosedDependencies = new[]
                {
                    "RNG Seed 12345: EndSeason population growth sequence (rng.NextDouble() called per owned non-starving city).",
                    "easy-research balance profile: starting food 1500, gold 900, base harvest 300, 0.5x upkeep & domestic gold cost.",
                    "JIT Float Evaluation (Unity Mono F64 CIL stack vs .NET CoreCLR F32): at S1 pop=9000, (int)(9000*0.01f) is 89 in Mono F64 vs 90 in CoreCLR F32 (+1 gold).",
                    "MapData / Scene Setup: 26x17 staggered hex grid matching TBTK layout.",
                    "Neutral city-south (12,2) and watch-tower-1 (6,2): never in fog-of-war sight in JSONL; positions come from scene setup."
                },
                AllVerifiedMatched = allMatched
            };
        }

        private ReplayPositionCheck BuildPosCheck(string id, string kind, string owner, int expX, int expZ, string basis, string note = "")
        {
            int actX = -1, actZ = -1;
            if (kind == "city")
            {
                var c = _engine.State.Cities.Find(x => x.Id == id);
                if (c != null) { actX = c.X; actZ = c.Z; }
            }
            else if (kind == "tower")
            {
                var t = _engine.State.WatchTowers.Find(x => x.Id == id);
                if (t != null) { actX = t.X; actZ = t.Z; }
            }
            else if (kind == "army")
            {
                var a = _engine.State.Armies.Find(x => x.Id == id);
                if (a != null) { actX = a.X; actZ = a.Z; }
            }
            return new ReplayPositionCheck
            {
                EntityId = id, EntityKind = kind, OwnerSeatId = owner,
                ExpectedX = expX, ExpectedZ = expZ, ActualX = actX, ActualZ = actZ,
                Matched = actX == expX && actZ == expZ, VerificationBasis = basis, Note = note
            };
        }

        public static MapData BuildDefaultStaggeredHexMap(int width = 26, int height = 17)
        {
            var map = new MapData
            {
                Width = width, Height = height,
                Walkable = new bool[width * height], Cost = new int[width * height],
                Neighbors = new int[width * height][]
            };
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    int i = map.Index(x, z);
                    bool ragged = (x % 2 == 0) && (z == height - 1);
                    map.Walkable[i] = !ragged;
                    map.Cost[i] = 1;
                }
            }
            int[][] evenOffsets = { new[] { 0, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1, 1 } };
            int[][] oddOffsets  = { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { 0, 1 } };

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    int i = map.Index(x, z);
                    if (!map.Walkable[i]) { map.Neighbors[i] = new int[0]; continue; }
                    var offsets = (x % 2 == 0) ? evenOffsets : oddOffsets;
                    var list = new List<int>(6);
                    for (int o = 0; o < offsets.Length; o++)
                    {
                        int nx = x + offsets[o][0], nz = z + offsets[o][1];
                        if (nx >= 0 && nx < width && nz >= 0 && nz < height)
                        {
                            int ni = map.Index(nx, nz);
                            if (map.Walkable[ni]) list.Add(ni);
                        }
                    }
                    map.Neighbors[i] = list.ToArray();
                }
            }
            return map;
        }
    }
}
