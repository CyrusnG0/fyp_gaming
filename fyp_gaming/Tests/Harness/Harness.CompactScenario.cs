using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LuanShi.Engine;

// [SCN-1] Opt-in compact scenario: profile-derived economy, mirrored fairness, fail-closed validation
// and the combat command path on the shipped demo map.
//
// The scenario is data, not decisions: every march below is an ordinary command the engine validates,
// and the moving policy exists only to measure that a legal route reaches contact in a few seasons.
// Registered as RunCompactScenario() in Program.cs.

internal static partial class Harness
{
    private static void RunCompactScenario()
    {
        Section("[SCN-1] compact-contact-v1 scenario");
        CompactScenarioMap();
        CompactScenarioProfileSemantics();
        CompactScenarioValidationAtomicity();
        CompactScenarioFairness();
        CompactScenarioCombat();
        CompactScenarioEvidence();
    }

    private static void ScnCheck(bool ok, string name) => Check(ok, "[SCN-1] " + name);

    // ---- the shipped map ---------------------------------------------------------

    private static MapData DemoMap()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "demo-map.json");
        if (!File.Exists(path)) throw new FileNotFoundException("demo map fixture missing", path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        int width = root.GetProperty("width").GetInt32();
        int height = root.GetProperty("height").GetInt32();
        var map = new MapData
        {
            Width = width, Height = height,
            Walkable = new bool[width * height],
            Cost = new int[width * height],
            Neighbors = new int[width * height][],
        };
        for (int i = 0; i < map.Cost.Length; i++) map.Cost[i] = 1;
        foreach (var node in root.GetProperty("nodes").EnumerateArray())
        {
            int x = node.GetProperty("x").GetInt32(), z = node.GetProperty("z").GetInt32();
            map.Walkable[map.Index(x, z)] = node.GetProperty("walkable").GetBoolean();
            map.Cost[map.Index(x, z)] = node.GetProperty("cost").GetInt32();
        }
        foreach (var pair in root.GetProperty("neighbors").EnumerateObject())
        {
            string[] parts = pair.Name.Split(',');
            var list = new List<int>();
            foreach (var n in pair.Value.EnumerateArray())
                list.Add(map.Index(n[0].GetInt32(), n[1].GetInt32()));
            map.Neighbors[map.Index(int.Parse(parts[0]), int.Parse(parts[1]))] = list.ToArray();
        }
        return map;
    }

    private static GameEngine ScenarioEngine(CompactScenario scenario, BalanceProfile profile, int seed = 24681)
    {
        var engine = new GameEngine(seed, "scenario-check", profile);
        engine.State.Map = DemoMap();
        scenario.Apply(engine);
        engine.BeginSeason();
        return engine;
    }

    private static void CompactScenarioMap()
    {
        var map = DemoMap();
        int walkable = 0;
        for (int i = 0; i < map.Walkable.Length; i++) if (map.Walkable[i]) walkable++;
        ScnCheck(map.Width == 26 && map.Height == 17 && walkable == 394,
            "the fixture is the shipped demo map (26x17, 394 walkable hexes)");

        var scenario = CompactScenario.CompactContact;
        var problems = scenario.Validate(map);
        ScnCheck(problems.Count == 0, "compact-contact-v1 places cleanly on the demo map: "
            + string.Join("; ", problems));

        var engine = ScenarioEngine(scenario, BalanceProfile.Standard);
        ScnCheck(engine.State.Factions.Count == 2 && engine.State.Cities.Count == 5
                 && engine.State.Armies.Count == 4 && engine.State.WatchTowers.Count == 1,
            "2 factions, 5 cities, 4 armies and 1 neutral watchtower are placed");
        ScnCheck(engine.State.ScenarioId == CompactScenario.CompactContactId
                 && engine.Observe("seat-0").ScenarioId == CompactScenario.CompactContactId,
            "the applied scenario is recorded on the state and in the seat observation");
        ScnCheck(CompactScenario.TryGet("compact-contact-v1", out var resolved) && resolved == scenario
                 && !CompactScenario.TryGet("compact-contact-typo", out _),
            "scenario keys resolve exactly; an unknown key is rejected");
    }

    // ---- finding 1: the balance profile decides the opening economy ---------------

    private static void CompactScenarioProfileSemantics()
    {
        var standard = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard);
        var foodEasy = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.FoodEasy);
        var easyResearch = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.EasyResearch);

        var s = standard.State.FindCity("city-wei");
        var f = foodEasy.State.FindCity("city-wei");
        var e = easyResearch.State.FindCity("city-wei");
        ScnCheck(s.Food == BalanceConfig.StartFood && s.Gold == BalanceConfig.StartGold,
            "standard keeps the shipped opening stock (" + s.Food + " food, " + s.Gold + " gold)");
        ScnCheck(f.Food == BalanceConfig.FoodEasyStartFood && f.Gold == BalanceConfig.FoodEasyStartGold,
            "food-easy gives every owned city its own opening stock (" + f.Food + " food, " + f.Gold + " gold)");
        ScnCheck(e.Food == BalanceConfig.EasyStartFood && e.Gold == BalanceConfig.EasyStartGold,
            "easy-research gives its own opening stock (" + e.Food + " food, " + e.Gold + " gold)");
        ScnCheck(s.Food != f.Food && f.Food == 3000,
            "the profile, not a hardcoded scenario value, decides starting food");

        // Every owned city follows the profile; the neutral objective stays identical everywhere.
        bool ownedAll = foodEasy.State.CitiesOf("seat-0").TrueForAll(c => c.Food == BalanceConfig.FoodEasyStartFood)
                        && foodEasy.State.CitiesOf("seat-1").TrueForAll(c => c.Food == BalanceConfig.FoodEasyStartFood);
        ScnCheck(ownedAll, "all four owned cities use the profile's food, not a per-city constant");
        var neutralStandard = standard.State.FindCity("city-neutral");
        var neutralFoodEasy = foodEasy.State.FindCity("city-neutral");
        ScnCheck(neutralStandard.Food == neutralFoodEasy.Food && neutralStandard.Gold == neutralFoodEasy.Gold,
            "the neutral objective keeps its fixed placeholder stock under every profile");

        // A deliberate override still wins where a scenario says so.
        var overridden = new CompactScenario(
            "override-check", "Override check",
            new List<ScenarioFaction> { new ScenarioFaction { SeatId = "seat-0", Name = "Wei" },
                                        new ScenarioFaction { SeatId = "seat-1", Name = "Shu" } },
            new List<ScenarioCity>
            {
                new ScenarioCity { Id = "city-a", Name = "A", X = 6, Z = 8, OwnerSeatId = "seat-0",
                    FoodOverride = 42, GoldOverride = 7, Population = 12345 },
                new ScenarioCity { Id = "city-b", Name = "B", X = 20, Z = 8, OwnerSeatId = "seat-1" },
            },
            new List<ScenarioArmy>(),
            new List<ScenarioTower>());
        var overriddenEngine = new GameEngine(1, "override", BalanceProfile.FoodEasy);
        overriddenEngine.State.Map = DemoMap();
        overridden.Apply(overriddenEngine);
        var a = overriddenEngine.State.FindCity("city-a");
        var b = overriddenEngine.State.FindCity("city-b");
        ScnCheck(a.Food == 42 && a.Gold == 7 && a.Population == 12345,
            "an explicit scenario override is applied verbatim (food 42, gold 7, population 12345)");
        ScnCheck(b.Food == BalanceConfig.FoodEasyStartFood && b.Gold == BalanceConfig.FoodEasyStartGold,
            "a city without an override still follows the selected profile");
    }

    // ---- finding 4: validation fails closed, before any state is cleared ----------

    private static void CompactScenarioValidationAtomicity()
    {
        var map = DemoMap();
        var good = CompactScenario.CompactContact;

        // Sentinel world: an unrelated match already in progress.
        GameEngine Sentinel()
        {
            var engine = new GameEngine(7, "sentinel", BalanceProfile.Standard);
            engine.State.Map = map;
            engine.State.Factions.Add(new FactionState { SeatId = "sentinel", Name = "Sentinel", Controller = ControllerType.Human });
            engine.State.Cities.Add(new CityState { Id = "sentinel-city", Name = "S", X = 1, Z = 1, OwnerSeatId = "sentinel", Population = 1000, Food = 10, Gold = 5 });
            engine.State.Armies.Add(new ArmyState { Id = "sentinel-army", Name = "S", OwnerSeatId = "sentinel", X = 2, Z = 2, Troops = 100 });
            engine.BeginSeason();
            return engine;
        }

        bool SentinelIntact(GameEngine e)
            => e.State.Factions.Count == 1 && e.State.Factions[0].SeatId == "sentinel"
               && e.State.Cities.Count == 1 && e.State.Cities[0].Id == "sentinel-city"
               && e.State.Armies.Count == 1 && e.State.Armies[0].Id == "sentinel-army"
               && e.State.WatchTowers.Count == 0 && e.State.ScenarioId == null;

        CompactScenario Mutate(Action<List<ScenarioFaction>, List<ScenarioCity>, List<ScenarioArmy>, List<ScenarioTower>> edit)
        {
            var factions = good.Factions.Select(f => new ScenarioFaction { SeatId = f.SeatId, Name = f.Name }).ToList();
            var cities = good.Cities.Select(c => new ScenarioCity
            {
                Id = c.Id, Name = c.Name, X = c.X, Z = c.Z, OwnerSeatId = c.OwnerSeatId,
                Population = c.Population, FoodOverride = c.FoodOverride, GoldOverride = c.GoldOverride,
                Garrison = c.Garrison, GarrisonType = c.GarrisonType, Morale = c.Morale,
                Training = c.Training, Defense = c.Defense, ReclaimPct = c.ReclaimPct,
                NeutralPopulation = c.NeutralPopulation, NeutralFood = c.NeutralFood, NeutralGold = c.NeutralGold,
            }).ToList();
            var armies = good.Armies.Select(a => new ScenarioArmy
            {
                Id = a.Id, Name = a.Name, OwnerSeatId = a.OwnerSeatId, X = a.X, Z = a.Z,
                Troops = a.Troops, UnitType = a.UnitType, Morale = a.Morale,
            }).ToList();
            var towers = good.WatchTowers.Select(t => new ScenarioTower { Id = t.Id, Name = t.Name, X = t.X, Z = t.Z, OwnerSeatId = t.OwnerSeatId }).ToList();
            edit(factions, cities, armies, towers);
            return new CompactScenario("invalid-case", "invalid case", factions, cities, armies, towers);
        }

        void Rejects(string label, Action<List<ScenarioFaction>, List<ScenarioCity>, List<ScenarioArmy>, List<ScenarioTower>> edit)
        {
            var scenario = Mutate(edit);
            var engine = Sentinel();
            bool threw = false;
            try { scenario.Apply(engine); }
            catch (InvalidOperationException) { threw = true; }
            ScnCheck(threw && SentinelIntact(engine),
                "invalid data fails closed before any state change: " + label);
        }

        Rejects("negative owned food override", (f, c, a, t) => c[0].FoodOverride = -1);
        Rejects("negative owned gold override", (f, c, a, t) => c[0].GoldOverride = -5);
        Rejects("negative neutral food", (f, c, a, t) => c.Find(x => x.OwnerSeatId == null).NeutralFood = -1);
        Rejects("negative neutral gold", (f, c, a, t) => c.Find(x => x.OwnerSeatId == null).NeutralGold = -1);
        Rejects("negative population", (f, c, a, t) => c[0].Population = -1);
        Rejects("negative garrison", (f, c, a, t) => c[0].Garrison = -1);
        Rejects("morale above the range", (f, c, a, t) => c[0].Morale = BalanceConfig.MoraleMax + 1);
        Rejects("morale below the range", (f, c, a, t) => c[0].Morale = BalanceConfig.MoraleMin - 1);
        Rejects("army morale out of range", (f, c, a, t) => a[0].Morale = BalanceConfig.MoraleMax + 1);
        Rejects("reclaim above the cap", (f, c, a, t) => c[0].ReclaimPct = BalanceConfig.ReclaimPctCap + 1);
        Rejects("training above the cap", (f, c, a, t) => c[0].Training = BalanceConfig.TrainingMax + 1);
        Rejects("defense above the cap", (f, c, a, t) => c[0].Defense = BalanceConfig.DefenseMax + 1);
        Rejects("two armies on one hex", (f, c, a, t) => { a[1].X = a[0].X; a[1].Z = a[0].Z; });
        Rejects("nine factions exceed the maximum", (f, c, a, t) =>
        {
            for (int i = 0; i < 9; i++)
                f.Add(new ScenarioFaction { SeatId = "extra-" + i, Name = "Extra " + i });
        });
        Rejects("unknown army owner", (f, c, a, t) => a[0].OwnerSeatId = "nobody");

        var eight = Mutate((f, c, a, t) =>
        {
            f.Clear();
            for (int i = 0; i < 8; i++) f.Add(new ScenarioFaction { SeatId = "seat-" + i, Name = "F" + i });
            c.Clear();
            a.Clear();
            t.Clear();
            c.Add(new ScenarioCity { Id = "only-city", Name = "Only", X = 6, Z = 8, OwnerSeatId = "seat-0" });
        });
        var eightEngine = new GameEngine(3, "eight", BalanceProfile.Standard);
        eightEngine.State.Map = map;
        string eightError = null;
        try { eight.Apply(eightEngine); }
        catch (InvalidOperationException e) { eightError = e.Message; }
        ScnCheck(eightError == null && eightEngine.State.Factions.Count == 8,
            "exactly eight factions are accepted (the engine's documented maximum)");
    }

    // ---- finding 3: mirrored openings and the real first-move effect --------------

    private static void CompactScenarioFairness()
    {
        var map = DemoMap();
        var scenario = CompactScenario.CompactContact;
        var city = scenario.Cities.FirstOrDefault(c => string.IsNullOrEmpty(c.OwnerSeatId));
        var weiCity = scenario.Cities.FirstOrDefault(c => c.Id == "city-wei");
        var shuCity = scenario.Cities.FirstOrDefault(c => c.Id == "city-shu");
        var weiVanguard = scenario.Armies.FirstOrDefault(a => a.Id == "army-wei-1");
        var shuVanguard = scenario.Armies.FirstOrDefault(a => a.Id == "army-shu-1");

        int weiToCity = map.HexDistance(weiVanguard.X, weiVanguard.Z, city.X, city.Z, 64);
        int shuToCity = map.HexDistance(shuVanguard.X, shuVanguard.Z, city.X, city.Z, 64);
        int weiCapitalToCity = map.HexDistance(weiCity.X, weiCity.Z, city.X, city.Z, 64);
        int shuCapitalToCity = map.HexDistance(shuCity.X, shuCity.Z, city.X, city.Z, 64);
        ScnCheck(weiToCity == shuToCity && weiToCity == 5,
            "both vanguards are exactly " + weiToCity + " hexes from the neutral city (mirrored)");
        ScnCheck(weiCapitalToCity == shuCapitalToCity && weiCapitalToCity == 7,
            "both capitals are exactly " + weiCapitalToCity + " hexes from the neutral city (mirrored)");

        // Real marches, real turn order: seat-0 orders first every season.
        var race = ScenarioEngine(scenario, BalanceProfile.Standard, 24681);
        var (captureSeason, captureSeat, adjacentSeason, log) = RunRace(race);
        foreach (string line in log) Console.WriteLine("    " + line);
        ScnCheck(adjacentSeason == 3 && captureSeason == 3 && captureSeat == "seat-0",
            "with mirrored distance the first-moving seat takes the city in season " + captureSeason
            + " and the other arrives adjacent (documented first-move effect)");

        // Side swap: same geography, the first-moving seat now plays the other faction.
        ScnCheck(CompactScenario.TryGet(CompactScenario.CompactContactSwappedId, out var swappedScenario)
                 && swappedScenario.ScenarioId == CompactScenario.CompactContactSwappedId,
            "the host can select the registered swapped scenario key");
        if (swappedScenario == null) return;
        var swapped = ScenarioEngine(swappedScenario, BalanceProfile.Standard, 24681);
        ScnCheck(swapped.State.FindFaction("seat-0").Name == "Shu"
                 && swapped.State.FindFaction("seat-1").Name == "Wei",
            "WithSeatsSwapped exchanges the factions between the two seats");
        var (swappedCaptureSeason, swappedCaptureSeat, swappedAdjacent, swappedLog) = RunRace(swapped);
        foreach (string line in swappedLog) Console.WriteLine("    " + line);
        string firstChair = SeatRotation.PlayOrder(swapped.State)[0];
        ScnCheck(firstChair == "seat-0" && swapped.State.FindFaction(firstChair).Name == "Shu",
            "after the swap Shu takes the first move");
        ScnCheck(swappedCaptureSeason == 3 && swappedCaptureSeat == firstChair && swappedAdjacent == 3,
            "the same season-3 outcome follows the first-move chair, now seat " + swappedCaptureSeat
            + " playing " + swapped.State.FindFaction(swappedCaptureSeat).Name
            + "; the second seat arrives adjacent");
        ScnCheck(swapped.State.FindCity("city-neutral").OwnerSeatId == firstChair,
            "the swapped match gives the opening advantage to the other faction");
    }

    /// <summary>
    /// One season at a time: seat-0 acts, then seat-1, then the season resolves. Each army advances with
    /// an ordinary march command toward the neutral city, only to a hex its own sight covers (the engine
    /// refuses anything else). This measures a legal route and the turn order, not a model's choice.
    /// </summary>
    private static (int captureSeason, string captureSeat, int adjacentSeason, List<string> log) RunRace(GameEngine engine)
    {
        var map = engine.State.Map;
        var city = engine.State.FindCity("city-neutral");
        var order = SeatRotation.PlayOrder(engine.State);
        string seatA = order[0];
        string seatB = order[1];
        var vanguardA = engine.State.ArmiesOf(seatA)[0];
        var vanguardB = engine.State.ArmiesOf(seatB)[0];
        var log = new List<string>();
        int captureSeason = 0, adjacentSeason = 0;
        string captureSeat = null;

        for (int season = 1; season <= 6; season++)
        {
            Advance(engine, seatA, vanguardA, city, log);
            Advance(engine, seatB, vanguardB, city, log);
            if (city.OwnerSeatId != null && captureSeason == 0)
            {
                captureSeason = season;
                captureSeat = city.OwnerSeatId;
            }
            if (map.AreAdjacent(vanguardA.X, vanguardA.Z, vanguardB.X, vanguardB.Z) && adjacentSeason == 0)
                adjacentSeason = season;
            if (captureSeason != 0 && adjacentSeason != 0) break;
            engine.EndSeason();
            if (engine.State.WinnerSeatId != null) break;
            engine.BeginSeason();
            vanguardA = engine.State.FindArmy(vanguardA.Id);
            vanguardB = engine.State.FindArmy(vanguardB.Id);
            city = engine.State.FindCity("city-neutral");
        }
        return (captureSeason, captureSeat, adjacentSeason, log);
    }

    private static void Advance(GameEngine engine, string seatId, ArmyState army, CityState city, List<string> log)
    {
        if (army == null || city == null) return;
        var map = engine.State.Map;
        int budget = BalanceConfig.MarchBudget(engine.State.Season, army.UnitType);
        int currentDistance = map.HexDistance(army.X, army.Z, city.X, city.Z, 64);
        int bestX = -1, bestZ = -1, bestDistance = currentDistance, bestCost = int.MaxValue;
        for (int z = 0; z < map.Height; z++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (x == army.X && z == army.Z) continue;
                if (!map.IsWalkable(x, z)) continue;
                if (!GameEngine.InSight(engine.State, seatId, x, z)) continue;
                var blocker = engine.State.ArmyAt(x, z);
                if (blocker != null && blocker.Id != army.Id) continue;
                var cityAt = engine.State.CityAt(x, z);
                if (cityAt != null && !cityAt.IsNeutral && cityAt.OwnerSeatId != seatId) continue;
                int cost = map.PathCost(army.X, army.Z, x, z, budget);
                if (cost < 0) continue;
                int distance = map.HexDistance(x, z, city.X, city.Z, 64);
                bool better = distance < bestDistance
                    || (distance == bestDistance && cost < bestCost)
                    || (distance == bestDistance && cost == bestCost && bestX >= 0
                        && (Math.Abs(z - army.Z) < Math.Abs(bestZ - army.Z)
                            || (Math.Abs(z - army.Z) == Math.Abs(bestZ - army.Z)
                                && (z < bestZ || (z == bestZ && x < bestX)))));
                if (better) { bestX = x; bestZ = z; bestDistance = distance; bestCost = cost; }
            }
        }
        if (bestX < 0) return;
        int token = engine.BeginSeatTurn(seatId);
        var result = engine.SubmitAs(seatId, token, new ActionCommand
        {
            Type = ActionType.March, TargetId = army.Id, ParamA = bestX, ParamB = bestZ,
        });
        engine.EndSeatTurn(token);
        log.Add("season " + engine.State.Season + " " + seatId + " march " + army.Id + " -> ("
            + bestX + "," + bestZ + ") " + (result.Ok ? "accepted" : "REJECTED: " + result.Error));
    }

    // ---- the combat command path on this scenario --------------------------------

    private static void CompactScenarioCombat()
    {
        var engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.FoodEasy);
        var wei = engine.State.FindArmy("army-wei-1");
        var shu = engine.State.FindArmy("army-shu-1");
        wei.X = 12; wei.Z = 8;
        shu.X = 13; shu.Z = 8;

        var host = new LlmAttackHost(engine, "seat-0");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(AtkJson("attack_army", wei.Id, 13, 8));
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(), null);
        runner.Begin(host, host.Token, "seat-0", 7);
        LlmDrain(runner);
        var battle = Find(engine.Log, "battle_field");
        ScnCheck(host.SubmitCalls == 1 && host.Commands.Count == 1 && battle != null
                 && engine.State.FindArmy(wei.Id) != null,
            "a model-shaped attack_army is accepted and resolved on the scenario world");

        // No troop-ratio gate: 2,000 troops may assault a 1,000-garrison capital.
        var city = engine.State.FindCity("city-shu");
        var hostArmy = engine.State.FindArmy("army-wei-2");
        hostArmy.X = 19; hostArmy.Z = 8;
        city.Garrison = 1000;
        int token = engine.BeginSeatTurn("seat-0");
        var weak = engine.SubmitAs("seat-0", token, AtkCityOrder("seat-0", hostArmy.Id, city.X, city.Z));
        engine.EndSeatTurn(token);
        ScnCheck(weak.Ok && Find(engine.Log, "battle_siege") != null,
            "an assault no longer needs a 3:1 troop edge (2,000 vs 1,000): " + weak.Error);

        // A comfortable assault captures the scenario capital.
        var capture = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.FoodEasy);
        var captureCity = capture.State.FindCity("city-shu");
        var captureHost = capture.State.FindArmy("army-wei-2");
        captureHost.X = 19; captureHost.Z = 8;
        captureCity.Garrison = 500;
        token = capture.BeginSeatTurn("seat-0");
        var assault = capture.SubmitAs("seat-0", token, AtkCityOrder("seat-0", captureHost.Id, captureCity.X, captureCity.Z));
        capture.EndSeatTurn(token);
        ScnCheck(assault.Ok && captureCity.OwnerSeatId == "seat-0"
                 && Find(capture.Log, "battle_siege")?.Payload["outcome"] == "captured",
            "a legal assault captures the scenario capital");
    }

    // ---- match evidence records both selections ---------------------------------

    private static void CompactScenarioEvidence()
    {
        var engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.FoodEasy);
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var config = new LlmConfig();
        string dir = LlmTempDir("scenario-evidence");
        try
        {
            var recorder = new LlmRunRecorder(dir);
            var runner = new LlmTurnRunner(fake, config, recorder);
            var session = new SpectatorSession(engine, runner, 1, 12);
            session.Start();
            SpectatorDrain(session);

            string[] jsonlFiles = Directory.GetFiles(dir, "*.jsonl");
            string jsonl = jsonlFiles.Length == 1 ? File.ReadAllText(jsonlFiles[0]) : "";
            ScnCheck(jsonl.Contains("\"scenario_id\":\"compact-contact-v1\""),
                "the run record carries the selected scenario id");
            ScnCheck(jsonl.Contains("\"balance_profile\":{\"profile\":\"food-easy\"")
                     && jsonl.Contains("\"starting_food\":3000"),
                "the run record carries the selected balance profile");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
