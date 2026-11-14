using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

namespace LuanShi.CombatScenarioCheck
{
    /// <summary>
    /// Engine-level check for the compact contact scenario and the combat command path.
    ///
    /// Everything below goes through the engine's own predicates and validator: marches are real
    /// <c>march</c> commands, battles are real <c>attack_army</c> / <c>attack_city</c> commands parsed
    /// from model-shaped JSON by <see cref="LlmTurnRunner"/> with a fake transport. The moving policy in
    /// section 3 exists only to prove a legal route exists and to measure contact timing; it is not the
    /// scenario and it is not a model decision.
    /// </summary>
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static void Section(string title) => Console.WriteLine("\n== " + title + " ==");

        private static int Main()
        {
            var scenario = CompactScenario.CompactContact;
            Console.WriteLine("LuanShi compact contact scenario check");
            Console.WriteLine("  scenario: " + scenario.ScenarioId + " (" + scenario.DisplayName + ")");
            Console.WriteLine("  map     : Tests/CombatScenarioCheck/demo-map.json (demo scene grid)");
            Console.WriteLine();

            ScenarioAndMap(scenario);
            StartVisibility(scenario);
            ContactTiming();
            ModelFieldBattle();
            AssaultAndCapture();
            UnificationVictory();
            ProposedScoring();
            WhatTheModelSees();

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        // ---- 1: the scenario is placeable on the shipped map -------------------

        private static void ScenarioAndMap(CompactScenario scenario)
        {
            Section("1. scenario on the demo map");
            var map = MapFixture.LoadFixture();
            Check(map.Width == 26 && map.Height == 17 && map.Neighbors.Length == 26 * 17,
                "the fixture is the demo map shape (26x17, 442 logical cells)");

            var walkable = 0;
            for (int i = 0; i < map.Walkable.Length; i++) if (map.Walkable[i]) walkable++;
            Check(walkable == 394, $"the demo map has {walkable} walkable hexes (the other 35 are the ragged cells)");

            var problems = scenario.Validate(map);
            Check(problems.Count == 0,
                "every scenario hex is walkable and every owner/target is consistent ("
                + string.Join("; ", problems.ToArray()) + ")");

            var engine = new GameEngine(24681, "scenario-check");
            engine.State.Map = map;
            scenario.Apply(engine);
            Check(engine.State.Factions.Count == 2 && engine.State.Cities.Count == 5
                  && engine.State.Armies.Count == 4 && engine.State.WatchTowers.Count == 1,
                "2 factions, 5 cities, 4 armies and 1 neutral watchtower are placed");
            Check(engine.State.FindFaction("seat-0").Controller == ControllerType.Llm
                  && engine.State.FindFaction("seat-1").Controller == ControllerType.Llm,
                "both seats default to model control so the scenario is playable by two LLMs");

            var occupied = new HashSet<string>();
            foreach (var c in engine.State.Cities) occupied.Add(c.X + "," + c.Z);
            bool armyOnCity = engine.State.Armies.Any(a => occupied.Contains(a.X + "," + a.Z));
            Check(!armyOnCity, "no starting army stands on a city hex");
        }

        // ---- 2: what each seat can see at the opening whistle ------------------

        private static void StartVisibility(CompactScenario scenario)
        {
            Section("2. opening visibility per seat - server-side fog");
            var engine = Fresh(scenario);
            var wei = engine.Observe("seat-0");
            var shu = engine.Observe("seat-1");

            Check(wei.Cities.Count == 2 && wei.Cities.All(c => c.OwnerSeatId == "seat-0")
                  && wei.Armies.Count == 2 && wei.Armies.All(a => a.OwnerSeatId == "seat-0"),
                "Wei sees only its own 2 cities and 2 armies at season 1 ("
                + wei.Cities.Count + " cities, " + wei.Armies.Count + " armies)");
            Check(shu.Cities.Count == 2 && shu.Armies.Count == 2,
                "Shu sees only its own 2 cities and 2 armies ("
                + shu.Cities.Count + " cities, " + shu.Armies.Count + " armies)");
            Check(wei.FindCity("city-neutral") == null && shu.FindCity("city-neutral") == null,
                "the neutral city at (13,8) is hidden from both seats at the start");
            Check(wei.FindCity("city-shu") == null && shu.FindCity("city-wei") == null
                  && wei.FindArmy("army-shu-1") == null && shu.FindArmy("army-wei-1") == null,
                "neither seat can see the other's capital or vanguard in its opening observation");
            Console.WriteLine("    Wei sees: " + Names(wei));
            Console.WriteLine("    Shu sees: " + Names(shu));
        }

        // ---- 3: legal contact, measured with real march commands --------------

        private static void ContactTiming()
        {
            Section("3. legal advance to contact - real march commands, measured");
            var engine = Fresh(CompactScenario.CompactContact);
            var map = engine.State.Map;
            var weiArmy = engine.State.FindArmy("army-wei-1");
            var shuArmy = engine.State.FindArmy("army-shu-1");
            int contactSeason = 0;
            var log = new List<string>();
            bool allAccepted = true;

            for (int season = 1; season <= 8 && contactSeason == 0; season++)
            {
                allAccepted &= MarchToward(engine, "seat-0", weiArmy, "city-neutral", log);
                allAccepted &= MarchToward(engine, "seat-1", shuArmy, "city-neutral", log);
                if (map.AreAdjacent(weiArmy.X, weiArmy.Z, shuArmy.X, shuArmy.Z)) { contactSeason = season; break; }
                engine.EndSeason();
                if (engine.State.WinnerSeatId != null) break;
                engine.BeginSeason();
            }

            foreach (string line in log) Console.WriteLine("    " + line);
            Check(allAccepted, "every march order in the advance was accepted by the engine (no refused moves)");
            Check(contactSeason > 0 && contactSeason <= 4,
                $"legal contact (adjacent armies) is reached in season {contactSeason} (target: 1-4)");
            Check(engine.Observe("seat-0").FindArmy("army-shu-1") != null
                  && engine.Observe("seat-1").FindArmy("army-wei-1") != null,
                "at contact each seat sees the other's vanguard in its own filtered observation");

            var neutral = engine.State.FindCity("city-neutral");
            Console.WriteLine("    contact hexes: Wei " + weiArmy.X + "," + weiArmy.Z
                + " / Shu " + shuArmy.X + "," + shuArmy.Z
                + "; neutral city owner: " + (neutral.OwnerSeatId ?? "neutral"));
        }

        // ---- 4: a model-shaped field battle ------------------------------------

        private static void ModelFieldBattle()
        {
            Section("4. field battle from model JSON - fake transport, engine validates");
            var engine = ContactWorld(out string attackerId, out string defenderId);
            var attacker = engine.State.FindArmy(attackerId);
            var defender = engine.State.FindArmy(defenderId);
            Check(engine.State.Map.AreAdjacent(attacker.X, attacker.Z, defender.X, defender.Z),
                "the two vanguards start the section adjacent (contact is a legal attack position)");

            var host = new ModelSeatHost(engine, "seat-0");
            var view = host.PlanAttacks();
            var option = view.Armies.SelectMany(a => a.Options)
                .FirstOrDefault(o => o.Kind == AttackPlanner.KindArmy && o.TargetArmyId == defenderId);
            Check(option != null,
                "the seat-safe attack planner advertises the adjacent enemy army (target "
                + defender.X + "," + defender.Z + ")");
            Check(option != null && option.Distance == 1 && option.TargetTroops == defender.Troops,
                "the advertised option carries its real distance and the enemy's observed troops");

            var runner = new LlmTurnRunner(ModelTransport(
                    "{\"type\":\"attack_army\",\"target_id\":\"" + attackerId + "\",\"param_a\":" + defender.X
                    + ",\"param_b\":" + defender.Z + ",\"summary\":\"Take the crossroads.\"}"),
                new LlmConfig { MaxRequestsPerTurn = 12, MaxActionsPerTurn = 5, MaxInvalidPerTurn = 2, MaxTotalRequests = 40, EnableAttack = true },
                null);
            runner.Begin(host, host.Token, "seat-0", 7);
            Drain(runner);

            var battle = engine.Log.Records.FirstOrDefault(r => r.EventType == "battle_field");
            Check(host.Submitted.Count == 1 && host.Results[0].Ok
                  && host.Submitted[0].Type == ActionType.AttackArmy,
                "the model's attack_army was accepted by the engine on the first submission");
            Check(battle != null
                  && battle.Payload["attacker_army_id"] == attackerId
                  && battle.Payload["defender_army_id"] == defenderId,
                "the engine resolved a real field battle between the two named armies");
            if (battle != null)
            {
                Console.WriteLine("    outcome=" + battle.Payload["outcome"]
                    + " rounds=" + battle.Payload["rounds"]
                    + " attacker " + battle.Payload["attacker_troops_before"] + "->" + battle.Payload["attacker_troops_after"]
                    + " defender " + battle.Payload["defender_troops_before"] + "->" + battle.Payload["defender_troops_after"]
                    + " (round log " + battle.Payload["round_log"] + ")");
            }
            Check(engine.Observe("seat-1").Events.Any(e => e.EventType == "battle_field"),
                "the defender's own filtered history contains the public battle result");
            Check(runner.RecentHistory.Any(h => h.Contains("attack_army") && h.Contains("ok")),
                "the runner fed the accepted order back to the model");
        }

        // ---- 5: assault, the 3:1 rule, and a captured city ---------------------

        private static void AssaultAndCapture()
        {
            Section("5. city assault and the 3:1 rule");
            var engine = Fresh(CompactScenario.CompactContact);
            var host = new ModelSeatHost(engine, "seat-0");
            var army = engine.State.FindArmy("army-wei-2");
            var target = engine.State.FindCity("city-shu");

            // Fixture: the main host is already at the enemy capital's gate (the multi-season march is
            // covered in section 3). The command under test is still an ordinary model-shaped order.
            army.X = 19; army.Z = 8;
            Check(engine.State.Map.AreAdjacent(army.X, army.Z, target.X, target.Z),
                "the assaulting host is adjacent to Chengdu (20,8) at (19,8)");

            target.Garrison = 1000;   // fixture: 3:1 => 3,000 needed, the host has 2,000
            var view = host.PlanAttacks();
            var advertised = view.Armies.SelectMany(a => a.Options)
                .Any(o => o.Kind == AttackPlanner.KindCity && o.TargetCityId == target.Id);
            Check(!advertised, "the planner does not advertise an assault the 3:1 rule would refuse");

            var rejectRunner = new LlmTurnRunner(
                ModelTransport("{\"type\":\"attack_city\",\"target_id\":\"army-wei-2\",\"param_a\":20,\"param_b\":8}"),
                AttackConfig(maxInvalid: 5), null);
            rejectRunner.Begin(host, host.Token, "seat-0", 8);
            Drain(rejectRunner);
            Check(host.Results.Count == 1 && !host.Results[0].Ok
                  && (host.Results[0].Error ?? string.Empty).StartsWith("assault needs 3:1"),
                "the engine refuses the under-strength assault with the ratio message: "
                + (host.Results.Count == 0 ? "(none)" : host.Results[0].Error));
            Check(rejectRunner.RecentHistory.Any(h => h.EndsWith("rejected: assault ratio too low")),
                "the model is told 'assault ratio too low' without being shown the hidden numbers");

            target.Garrison = 500;    // fixture: 3:1 => 1,500 needed, the host qualifies
            host = new ModelSeatHost(engine, "seat-0");
            var captureRunner = new LlmTurnRunner(
                ModelTransport("{\"type\":\"attack_city\",\"target_id\":\"army-wei-2\",\"param_a\":20,\"param_b\":8}"),
                AttackConfig(), null);
            captureRunner.Begin(host, host.Token, "seat-0", 9);
            Drain(captureRunner);

            var siege = engine.Log.Records.FirstOrDefault(r => r.EventType == "battle_siege");
            Check(siege != null && siege.Payload["outcome"] == "captured",
                "the legal assault resolved and captured: outcome="
                + (siege == null ? "(none)" : siege.Payload["outcome"])
                + " rounds=" + (siege == null ? "-" : siege.Payload["rounds"])
                + " garrison " + (siege == null ? "-" : siege.Payload["garrison_before"] + "->" + siege.Payload["garrison_after"]));
            Check(engine.State.FindCity("city-shu").OwnerSeatId == "seat-0",
                "ownership of Chengdu moved to seat-0 in canonical state");
            Check(engine.Observe("seat-0").FindCity("city-shu")?.OwnerSeatId == "seat-0"
                  && engine.Observe("seat-1").FindCity("city-shu")?.OwnerSeatId == "seat-0",
                "both seats' observations report the new owner (capture is public information)");
            Check(engine.Log.Records.Any(r => r.EventType == "city_captured")
                  && engine.Observe("seat-1").Events.Any(e => e.EventType == "city_captured"),
                "the defeat is logged and visible to the losing seat");
        }

        // ---- 6: capture-target victory through the existing engine rule -------

        private static void UnificationVictory()
        {
            Section("6. capture-target victory - existing unification rule only");
            var engine = Fresh(CompactScenario.CompactContact);
            var map = engine.State.Map;

            // Wei: take the neutral city, then both Shu cities. Small deterministic steps, each one an
            // ordinary accepted command; Shu never acts, so this measures the rule, not the match.
            var vanguard = engine.State.FindArmy("army-wei-1");
            var host = engine.State.FindArmy("army-wei-2");
            var neutral = engine.State.FindCity("city-neutral");
            vanguard.X = 12; vanguard.Z = 8;
            bool tookNeutral = MarchHex(engine, "seat-0", vanguard, neutral.X, neutral.Z);
            Check(tookNeutral && neutral.OwnerSeatId == "seat-0",
                "marching onto the neutral city captures it (movement capture path)");

            var shuCity = engine.State.FindCity("city-shu");
            var shuCity2 = engine.State.FindCity("city-shu-2");
            host.X = 19; host.Z = 8;
            bool seized1 = Assault(engine, "army-wei-2", shuCity, out string outcome1);
            Check(seized1 && shuCity.OwnerSeatId == "seat-0", "Chengdu is stormed and taken (" + outcome1 + ")");

            // 5 號令 per faction per season: the second offensive is next season's business.
            engine.EndSeason();
            engine.BeginSeason();
            Check(engine.State.FindFaction("seat-0").CommandPoints == BalanceConfig.CommandPointsPerSeason,
                "the new season restores the victor's command points (" + engine.State.FindFaction("seat-0").CommandPoints + ")");

            // The victor's army occupies the captured hex; one march reaches the second city's gate.
            var occupier = engine.State.FindArmy("army-wei-2");
            bool gate = MarchHex(engine, "seat-0", occupier, 21, 9);
            Check(gate && map.AreAdjacent(occupier.X, occupier.Z, shuCity2.X, shuCity2.Z),
                "the victor marches to (21,9), adjacent to Jiangzhou (21,10)");

            bool seized2 = Assault(engine, "army-wei-2", shuCity2, out string outcome2);
            Check(seized2, "Jiangzhou is stormed and taken (" + outcome2 + ")");
            Check(engine.State.WinnerSeatId == "seat-0",
                "owning every city triggers the existing unification victory: WinnerSeatId=" + (engine.State.WinnerSeatId ?? "none"));
            var over = engine.Log.Records.FirstOrDefault(r => r.EventType == "game_over");
            Check(over != null && over.Payload["reason"] == "unification"
                  && engine.Observe("seat-1").Events.Any(e => e.EventType == "game_over"),
                "game_over is logged with reason 'unification' and the losing seat sees it");
            int lateToken = engine.BeginSeatTurn("seat-0");
            var after = engine.SubmitAs("seat-0", lateToken, new ActionCommand
                { Type = ActionType.LevyTax, ActorSeatId = "seat-0", TargetId = "city-wei" });
            engine.EndSeatTurn(lateToken);
            Check(!after.Ok && after.Error == "game over",
                "every later command is refused at the terminal state (" + after.Error + ")");
        }

        // ---- 7: proposed season-cap scoring (proposal only) -------------------

        private static void ProposedScoring()
        {
            Section("7. PROPOSAL: season-cap scoring and tie handling - not enabled in the engine");
            var engine = Fresh(CompactScenario.CompactContact);
            var wei = Score(engine, "seat-0");
            var shu = Score(engine, "seat-1");
            Check(wei.Total == shu.Total,
                "the opening position scores level for both seats (" + wei.Total + " each) — no inherited advantage");
            Console.WriteLine("    proposed score = cities*10 + neutral objectives*5 + prestige/10; tie -> troops, food, seat id");
            Console.WriteLine("    season 1: Wei " + wei + " / Shu " + shu);

            engine.State.FindCity("city-neutral").OwnerSeatId = "seat-0";
            var wei2 = Score(engine, "seat-0");
            var shu2 = Score(engine, "seat-1");
            Check(wei2.Total > shu2.Total, "taking the neutral city moves the proposed score: Wei " + wei2 + " / Shu " + shu2);
            Check(TieBreakKey(wei2).CompareTo(TieBreakKey(shu2)) != 0
                  && TieBreakKey(wei2) != TieBreakKey(shu2),
                "the proposed tie-break is total and deterministic (no random winner)");
            Console.WriteLine("    NOTE: this section calls no engine scoring code; the engine today only"
                + " decides unification, so a season-cap rule needs an explicit engine change.");
        }

        private sealed class ScoreLine
        {
            public string SeatId;
            public int Cities, Objectives, Prestige, Total, Troops, Food;
            public override string ToString()
                => SeatId + " cities=" + Cities + " objectives=" + Objectives + " prestige=" + Prestige
                   + " total=" + Total + " troops=" + Troops + " food=" + Food;
        }

        private static ScoreLine Score(GameEngine engine, string seatId)
        {
            var line = new ScoreLine { SeatId = seatId };
            foreach (var city in engine.State.Cities)
            {
                if (city.OwnerSeatId != seatId) continue;
                line.Cities++;
                line.Food += city.Food;
            }
            foreach (var tower in engine.State.WatchTowers)
                if (tower.OwnerSeatId == seatId) line.Objectives++;
            var fac = engine.State.FindFaction(seatId);
            line.Prestige = fac == null ? 0 : fac.Prestige;
            foreach (var army in engine.State.Armies)
                if (army.OwnerSeatId == seatId) line.Troops += army.Troops;
            line.Total = line.Cities * 10 + line.Objectives * 5 + line.Prestige / 10;
            return line;
        }

        private static string TieBreakKey(ScoreLine line)
            => line.Total.ToString("D6") + "|" + line.Troops.ToString("D6") + "|"
               + line.Food.ToString("D6") + "|" + line.SeatId;

        // ---- 8: the exact prompt a seat receives, and what it lacks -------------

        private static void WhatTheModelSees()
        {
            Section("8. what a seat actually receives about targets, bounds and movement");
            var engine = ContactWorld(out string attackerId, out string defenderId);
            engine.State.FindCity("city-neutral").OwnerSeatId = "seat-1";   // fixture: Shu marched in first
            var host = new ModelSeatHost(engine, "seat-0");
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var runner = new LlmTurnRunner(fake, AttackConfig(), null);
            runner.Begin(host, host.Token, "seat-0", 11);
            for (int i = 0; i < 5 && fake.Requests.Count == 0; i++) runner.Tick();
            Drain(runner);

            string system = fake.Requests.Count > 0 ? fake.Requests[0].System : "";
            string user = fake.Requests.Count > 0 ? fake.Requests[0].User : "";
            Check(user.Contains("MOVEMENT OPTIONS") && user.Contains("type=attack_army"),
                "the prompt carries movement candidates and attack candidates at contact");
            Check(user.Contains("garrison\":300") && !user.Contains("type=attack_city"),
                "the enemy-held city is visible with its garrison 300, but no assault is offered: the "
                + "defending 2,000-troop army inside it makes the 3:1 rule fail, so the planner stays honest");
            Check(user.Contains("target_id=army-wei-1 param_a=13 param_b=8")
                  && user.Contains("Attack seat-1 infantry army"),
                "the field target is named in the option label; target_id is always the attacker's army");
            Check(!user.Contains("width") && !user.Contains("height") && !user.Contains("bounds"),
                "MISSING: the prompt never states the map size, edges or how many hexes remain");
            Check(!user.Contains("food_yield") && !user.Contains("upkeep") && !user.Contains("forecast"),
                "MISSING: no food inflow/upkeep/forecast, so starvation and relief cannot be planned");
            Check(!user.Contains("distance_to") && !user.Contains("direction"),
                "MISSING: no distance or direction from an army to a known objective");
            Check(!system.Contains("unexplored") && !user.Contains("unexplored"),
                "MISSING: absence of a city/army is indistinguishable from 'outside sight'");
            Console.WriteLine("    --- first prompt excerpt (user message, first 1200 chars) ---");
            string excerpt = user.Length > 1200 ? user.Substring(0, 1200) : user;
            Console.WriteLine("    " + excerpt.Replace("\r", "").Replace("\n", "\n    "));
        }

        // ---- helpers -----------------------------------------------------------

        private static GameEngine Fresh(CompactScenario scenario)
        {
            var engine = new GameEngine(24681, "scenario-check");
            engine.State.Map = MapFixture.LoadFixture();
            scenario.Apply(engine);
            engine.BeginSeason();
            return engine;
        }

        /// <summary>World where the two vanguards are already adjacent, both controllable.</summary>
        private static GameEngine ContactWorld(out string attackerId, out string defenderId)
        {
            var engine = Fresh(CompactScenario.CompactContact);
            var wei = engine.State.FindArmy("army-wei-1");
            var shu = engine.State.FindArmy("army-shu-1");
            wei.X = 12; wei.Z = 8;
            shu.X = 13; shu.Z = 8;
            attackerId = wei.Id;
            defenderId = shu.Id;
            return engine;
        }

        /// <summary>
        /// One ordinary march command that moves <paramref name="army"/> at most two hexes toward
        /// <paramref name="targetCityId"/> while staying inside the seat's own sight. Legal-destination
        /// probing only; the engine still decides.
        /// </summary>
        private static bool MarchToward(GameEngine engine, string seatId, ArmyState army,
            string targetCityId, List<string> log)
        {
            var city = engine.State.FindCity(targetCityId);
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
            if (bestX < 0) return true;   // already as close as sight allows this season

            int token = engine.BeginSeatTurn(seatId);
            var result = engine.SubmitAs(seatId, token, new ActionCommand
            {
                Type = ActionType.March, TargetId = army.Id, ParamA = bestX, ParamB = bestZ,
            });
            engine.EndSeatTurn(token);
            log.Add("season " + engine.State.Season + " " + seatId + " march " + army.Id
                + " -> (" + bestX + "," + bestZ + ") " + (result.Ok ? "accepted" : "REJECTED: " + result.Error));
            return result.Ok;
        }

        private static bool MarchHex(GameEngine engine, string seatId, ArmyState army, int x, int z)
        {
            int token = engine.BeginSeatTurn(seatId);
            var result = engine.SubmitAs(seatId, token, new ActionCommand
            {
                Type = ActionType.March, TargetId = army.Id, ParamA = x, ParamB = z,
            });
            engine.EndSeatTurn(token);
            return result.Ok;
        }

        private static bool Assault(GameEngine engine, string armyId, CityState city, out string outcome)
        {
            int token = engine.BeginSeatTurn("seat-0");
            var result = engine.SubmitAs("seat-0", token, new ActionCommand
            {
                Type = ActionType.AttackCity, TargetId = armyId, ParamA = city.X, ParamB = city.Z,
            });
            engine.EndSeatTurn(token);
            var siege = engine.Log.Records.LastOrDefault(r => r.EventType == "battle_siege");
            outcome = siege == null ? (result.Ok ? "no event" : "rejected: " + result.Error)
                                    : siege.Payload["outcome"];
            return result.Ok;
        }

        private static FakeLlmTransport ModelTransport(params string[] replies)
        {
            var fake = new FakeLlmTransport();
            foreach (string reply in replies) fake.EnqueueSuccess(reply);
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"done\"}");
            return fake;
        }

        private static LlmConfig AttackConfig(int maxInvalid = 2)
            => new LlmConfig
            {
                MaxRequestsPerTurn = 12, MaxActionsPerTurn = 5, MaxInvalidPerTurn = maxInvalid,
                MaxTotalRequests = 40, EnableAttack = true,
            };

        private static void Drain(LlmTurnRunner runner)
        {
            for (int i = 0; i < 200 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
                runner.Tick();
        }

        private static string Names(Observation obs)
        {
            var names = new List<string>();
            foreach (var c in obs.Cities) names.Add("city " + c.Name);
            foreach (var a in obs.Armies) names.Add("army " + a.Name);
            foreach (var t in obs.WatchTowers) names.Add("tower " + t.Name);
            return string.Join(", ", names.ToArray());
        }
    }
}
