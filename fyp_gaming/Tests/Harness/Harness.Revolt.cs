using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunRevolt()
    {
        // ------------------------------------------------ 民變 revolt
        Section("revolt (design §4.1 民心 <25, §10.2 產出停擺)");

        int seed0 = FindRevoltSeed();
        Check(seed0 > 0, $"a Morale-0 city revolts within 400 seeds (seed {seed0})");
        if (seed0 > 0)
        {
            var e = Build(seed0, 1, morale: 0, food: 5000, garrison: 500);
            var city = e.State.FindCity("city-a");
            int foodBefore = city.Food, goldBefore = city.Gold;
            var report = e.EndSeason();
            var revolt = Find(e.Log, "revolt");

            Check(revolt != null && revolt.Payload["city_id"] == "city-a", "revolt event carries city_id");
            Check(revolt != null && revolt.Visibility == "public", "revolt is public");
            // revolt zeroes this tick's tax (gold untouched) and harvest (food only loses what was eaten)
            Check(city.Gold == goldBefore, $"revolt zeroes the tax (gold {goldBefore} → {city.Gold})");
            Check(city.Food == foodBefore - 250, $"revolt zeroes the harvest (food {foodBefore} → {city.Food}, expected 4750)");
            Check(city.Garrison == 450, $"revolt costs 10% of the garrison (500 → {city.Garrison})");
            Check(city.Morale == 15, $"民心 +15 after the venting (0 → {city.Morale})");
            Check(report.SeasonName == "Spring", "report carries the season name (Spring)");
            bool lineMentions = false;
            foreach (var l in report.Lines) if (l.Contains("Revolt!")) lineMentions = true;
            Check(lineMentions, "report line names the revolt");
        }

        // a control run just above the threshold keeps its yields
        {
            var e = Build(seed0, 1, morale: 25, food: 5000, garrison: 500);
            var city = e.State.FindCity("city-a");
            e.EndSeason();
            Check(CountEvents(e.Log, "revolt") == 0, "no revolt at 民心 == 25 (threshold is exclusive)");
            Check(city.Gold > 1000, "yields continue at 民心 == 25");
        }

        // no revolt at or above the threshold, over many seeds
        {
            int revolts = 0, runs = 0;
            for (int seed = 1; seed <= 64; seed++)
            {
                foreach (int morale in new[] { 25, 26, 30, 100 })
                {
                    var e = Build(seed, 1, morale, 5000, 500);
                    e.EndSeason();
                    runs++;
                    revolts += CountEvents(e.Log, "revolt");
                }
            }
            Check(revolts == 0, $"no revolt at/above 民心 25 across {runs} runs");
        }

        // RevoltChanceMult is read (the P7 吏治 hook)
        {
            int revolts = 0;
            for (int seed = 1; seed <= 64; seed++)
            {
                var e = Build(seed, 1, morale: 0, food: 5000, garrison: 500);
                e.State.FindFaction("seat-a").RevoltChanceMult = 0f;
                e.EndSeason();
                revolts += CountEvents(e.Log, "revolt");
            }
            Check(revolts == 0, "RevoltChanceMult = 0 suppresses every revolt (hook is read)");
            Check(new FactionState().RevoltChanceMult == 1f, "RevoltChanceMult defaults to 1.0");
        }
    }

    /// <summary>First seed (1..400) at which a Morale-0 city actually rises, so revolt tests are deterministic.</summary>
    private static int FindRevoltSeed()
    {
        for (int seed = 1; seed <= 400; seed++)
        {
            var e = Build(seed, 1, morale: 0, food: 5000, garrison: 500);
            e.EndSeason();
            if (Find(e.Log, "revolt") != null) return seed;
        }
        return -1;
    }
}
