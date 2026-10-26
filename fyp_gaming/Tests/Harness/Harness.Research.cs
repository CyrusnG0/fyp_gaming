using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunResearch()
    {
        // ------------------------------------------------ 研究 research (P7)
        Section("research (design §9: 4 lines × 5 levels, 20/40/70/110/160 points)");

        Check(BalanceConfig.ResearchLevelCosts[0] == 20 && BalanceConfig.ResearchLevelCosts[1] == 40
           && BalanceConfig.ResearchLevelCosts[2] == 70 && BalanceConfig.ResearchLevelCosts[3] == 110
           && BalanceConfig.ResearchLevelCosts[4] == 160 && BalanceConfig.ResearchMaxLevel == 5,
              "level costs are the design's 20/40/70/110/160 over 5 levels");
        Check(BalanceConfig.ResearchLevelCost(1) == 20 && BalanceConfig.ResearchLevelCost(5) == 160,
              "ResearchLevelCost maps 1..5 to the table");

        // ---- point income at rollover (+15, ×1.25 with 吏治 L4)
        {
            var e = Build(4, 1, morale: 100, food: 1000);
            var fac = e.State.FindFaction("seat-a");
            Check(fac.ResearchPoints == BalanceConfig.ResearchPointsPerSeason,
                  $"BeginSeason grants {fac.ResearchPoints} points (expected {BalanceConfig.ResearchPointsPerSeason})");
            e.EndSeason();
            Check(fac.ResearchPoints == 15, "EndSeason grants no points — income is a rollover event");

            SetLevel(e, "seat-a", ResearchLine.Governance, 4);
            e.BeginSeason();
            Check(fac.ResearchPoints == 15 + 19, $"吏治 L4 學堂 pays 19/season, not 15 (points {fac.ResearchPoints})");
            Check(BalanceConfig.ResearchIncome(0) == 15 && BalanceConfig.ResearchIncome(3) == 15
               && BalanceConfig.ResearchIncome(4) == 19, "ResearchIncome: 15 base, 19 with 吏治 L4 (×1.25 rounded)");
        }

        // ---- purchase pipeline: validation, cost, event
        {
            var e = Build(4, 1, morale: 100, food: 1000);
            var fac = e.State.FindFaction("seat-a");
            fac.ResearchPoints = 500;

            var bad = Research(e, "seat-a", "gold");
            Check(!bad.Ok && bad.Error.Contains("unknown research line"), $"unknown line is refused ({bad.Error})");
            var missing = e.Submit(new ActionCommand { Type = ActionType.Research, ActorSeatId = "seat-a" });
            Check(!missing.Ok && missing.Error.Contains("args.line is required"),
                  $"missing args.line is refused ({missing.Error})");
            var poor = Research(e, "seat-b", ResearchLine.Agri);
            Check(!poor.Ok && poor.Error.Contains("unknown seat"), "an unknown seat cannot research");

            var ok = Research(e, "seat-a", ResearchLine.Agri);
            Check(ok.Ok, $"a purchase with enough points succeeds ({ok.Error})");
            Check(fac.ResearchLevel(ResearchLine.Agri) == 1, "agri reaches level 1");
            Check(fac.ResearchPoints == 480, $"level 1 deducts 20 points (500 → {fac.ResearchPoints})");
            Check(fac.CommandPoints == BalanceConfig.CommandPointsPerSeason - BalanceConfig.ResearchCommandPoints,
                  $"research costs {BalanceConfig.ResearchCommandPoints} order (CP {fac.CommandPoints})");

            var rec = Find(e.Log, "research");
            Check(rec != null && rec.Payload["line"] == "agri" && rec.Payload["level"] == "1"
               && rec.Payload["cost"] == "20" && rec.Payload["research_points"] == "480",
                  "research event carries line/level/cost/points");
            Check(rec != null && rec.Visibility == "owner_only", "research is owner-only (your tech tree is secret)");
            e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
            Check(e.SeesEvent("seat-a", rec) && !e.SeesEvent("seat-b", rec),
                  "only the researching seat sees the research event");

            // level 2 costs 40, not 20
            Research(e, "seat-a", ResearchLine.Agri);
            Check(fac.ResearchPoints == 440 && fac.ResearchLevel(ResearchLine.Agri) == 2,
                  $"level 2 costs 40 (points {fac.ResearchPoints}, level {fac.ResearchLevel(ResearchLine.Agri)})");

            // insufficient points
            fac.ResearchPoints = 10;
            var broke = Research(e, "seat-a", ResearchLine.Agri);
            Check(!broke.Ok && broke.Error.Contains("not enough research points"), $"short of points ({broke.Error})");

            // max level
            fac.ResearchPoints = 1000;
            for (int i = 0; i < 3; i++) Research(e, "seat-a", ResearchLine.Agri);
            Check(fac.ResearchLevel(ResearchLine.Agri) == 5, "agri climbs to level 5");
            fac.CommandPoints = BalanceConfig.CommandPointsPerSeason;   // the max-level rule, not the CP rule
            var maxed = Research(e, "seat-a", ResearchLine.Agri);
            Check(!maxed.Ok && maxed.Error.Contains("already at max level"), $"level 6 is refused ({maxed.Error})");

            // CP gate
            fac.CommandPoints = 0;
            var noCp = Research(e, "seat-a", ResearchLine.Military);
            Check(!noCp.Ok && noCp.Error == "no command points left", $"no 號令 left ({noCp.Error})");
        }

        // ---- 農政 (agri): harvest ×1.10 at L1, a further ×1.25 at L5
        {
            // summer (×1.0 season), no 開墾: food = 1000 + harvest − eaten(200)
            var plain = Build(9, 2, morale: 100, food: 1000);
            plain.EndSeason();
            Check(plain.State.FindCity("city-a").Food == 1000, $"no research: harvest 200 (food {plain.State.FindCity("city-a").Food})");

            var l1 = Build(9, 2, morale: 100, food: 1000);
            SetLevel(l1, "seat-a", ResearchLine.Agri, 1);
            l1.EndSeason();
            Check(l1.State.FindCity("city-a").Food == 1020, $"農政 L1 harvest ×1.10 (food {l1.State.FindCity("city-a").Food}, expected 1020)");

            var l5 = Build(9, 2, morale: 100, food: 1000);
            SetLevel(l5, "seat-a", ResearchLine.Agri, 5);
            l5.EndSeason();
            Check(l5.State.FindCity("city-a").Food == 1075, $"農政 L5 adds ×1.25 (food {l5.State.FindCity("city-a").Food}, expected 1075)");
        }

        // ---- 兵法 L1: training cap 5 → 6 for that faction only
        {
            var e = Build(9, 2, morale: 100, food: 5000);
            var city = e.State.FindCity("city-a");
            city.Training = BalanceConfig.TrainingMax;
            var blocked = e.Submit(new ActionCommand { Type = ActionType.Train, ActorSeatId = "seat-a", TargetId = "city-a" });
            Check(!blocked.Ok && blocked.Error.Contains("max (5)"), $"without 兵法, training stops at 5 ({blocked.Error})");

            SetLevel(e, "seat-a", ResearchLine.Military, 1);
            var ok = e.Submit(new ActionCommand { Type = ActionType.Train, ActorSeatId = "seat-a", TargetId = "city-a" });
            Check(ok.Ok && city.Training == 6, $"兵法 L1 lifts the cap to 6 (training {city.Training}, {ok.Error})");
            var again = e.Submit(new ActionCommand { Type = ActionType.Train, ActorSeatId = "seat-a", TargetId = "city-a" });
            Check(!again.Ok && again.Error.Contains("max (6)"), $"the raised cap holds ({again.Error})");
        }

        // ---- 兵法 L4: 戰力 ×1.15, applied on that faction's side only
        {
            var baseline = Battle(31, 0);
            var boosted = Battle(31, 4);
            Check(baseline.Rounds == 5 && boosted.Rounds == 5,
                  $"both battles resolve over {BalanceConfig.CombatRounds} rounds ({baseline.Rounds}/{boosted.Rounds})");
            Check(baseline.Round1AttackerLosses == boosted.Round1AttackerLosses,
                  $"the side without 兵法 takes identical first-round losses ({baseline.Round1AttackerLosses} vs {boosted.Round1AttackerLosses})");
            double ratio = boosted.Round1DefenderLosses / (double)baseline.Round1DefenderLosses;
            Check(Math.Abs(ratio - 1.15) < 0.005,
                  $"兵法 L4 multiplies that faction's 戰力 by ×1.15 in round 1 (ratio {ratio:F4})");
            Check(boosted.DefenderLosses > baseline.DefenderLosses,
                  $"and the boosted side keeps taking more over the battle ({boosted.DefenderLosses} vs {baseline.DefenderLosses})");
        }

        // ---- 吏治 (governance): L1 tax ×1.10, L2 民心 +5/季, L3 revolt −50%, L5 +10 威望
        {
            var plain = Build(9, 2, morale: 60, food: 1000);
            plain.EndSeason();
            Check(plain.State.FindCity("city-a").Gold == 1100, $"no research: tax 100 (gold {plain.State.FindCity("city-a").Gold})");

            var l1 = Build(9, 2, morale: 60, food: 1000);
            SetLevel(l1, "seat-a", ResearchLine.Governance, 1);
            l1.EndSeason();
            Check(l1.State.FindCity("city-a").Gold == 1110, $"吏治 L1 tax ×1.10 (gold {l1.State.FindCity("city-a").Gold}, expected 1110)");
            Check(l1.State.FindCity("city-a").Morale == 60, "吏治 L1 does not touch 民心");
        }

        {
            var e = Build(9, 2, morale: 60, food: 1000);
            SetLevel(e, "seat-a", ResearchLine.Governance, 2);
            var report = e.EndSeason();
            Check(e.State.FindCity("city-a").Morale == 65, $"吏治 L2 grants 民心 +5/季 (morale {e.State.FindCity("city-a").Morale})");
            bool noted = false;
            foreach (var line in report.Lines) if (line.Contains("Administration 2: morale +5")) noted = true;
            Check(noted, "the grant is named on the city's report line");
        }

        {
            var e = Build(9, 2, morale: 60, food: 1000);
            var fac = e.State.FindFaction("seat-a");
            SetLevel(e, "seat-a", ResearchLine.Governance, 3);
            Check(fac.RevoltChanceMult == 1.0f, "吏治 L3 does not change the multiplier mid-season");
            e.BeginSeason();
            Check(fac.RevoltChanceMult == 0.5f, $"吏治 L3 律法 syncs RevoltChanceMult = {fac.RevoltChanceMult} at rollover");
            SetLevel(e, "seat-a", ResearchLine.Governance, 2);
            e.BeginSeason();
            Check(fac.RevoltChanceMult == 1.0f, "dropping below L3 would restore 1.0 (level-derived, not hardcoded)");
        }

        {
            var e = Build(9, 2, morale: 60, food: 1000);
            var fac = e.State.FindFaction("seat-a");
            fac.ResearchPoints = 500;
            for (int i = 0; i < 4; i++) Research(e, "seat-a", ResearchLine.Governance);
            Check(fac.Prestige == BalanceConfig.PrestigeStart, $"吏治 L4 still leaves 威望 at {fac.Prestige}");
            Research(e, "seat-a", ResearchLine.Governance);
            Check(fac.Prestige == BalanceConfig.PrestigeStart + 10,
                  $"吏治 L5 九品 grants a one-time 威望 +10 ({fac.Prestige})");
        }

        // ---- the observation payload an LLM/HandoverBundle sees
        {
            var e = Build(9, 2, morale: 100, food: 1000);
            var fac = e.State.FindFaction("seat-a");
            fac.ResearchPoints = 500;
            Research(e, "seat-a", ResearchLine.Agri);
            Research(e, "seat-a", ResearchLine.Agri);
            var obs = e.Observe("seat-a");
            string json = obs.ToJson();
            Check(obs.ResearchPoints == 440 && obs.ResearchLevel(ResearchLine.Agri) == 2,
                  $"observation carries the seat's own points/levels ({obs.ResearchPoints}/{obs.ResearchLevel(ResearchLine.Agri)})");
            Check(json.Contains("\"research\":{\"agri\":2,\"military\":0,\"espionage\":0,\"governance\":0}"),
                  "observation JSON carries all four lines in schema order");
            Check(json.Contains("\"research_points\":440"), "observation JSON carries research_points");
            e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
            var other = e.Observe("seat-b");
            Check(other.ResearchLevel(ResearchLine.Agri) == 0 && other.ResearchPoints == 0,
                  "another seat's observation carries none of it (research is per-seat)");
            Check(other.ToJson().Contains("\"research\":{\"agri\":0,\"military\":0,\"espionage\":0,\"governance\":0}"),
                  "the other seat still gets a well-formed (empty) research block");
        }

        // ---- ScriptedController: one 農政 purchase a season, only with spare 號令
        {
            var rich = ScriptedResearchState(100);
            var plan = ScriptedController.PlanTurn(rich, "seat-a");
            int purchases = 0;
            foreach (var cmd in plan) if (cmd.Type == ActionType.Research) purchases++;
            bool agri = false;
            foreach (var cmd in plan)
                if (cmd.Type == ActionType.Research && cmd.Args["line"] == ResearchLine.Agri) agri = true;
            Check(purchases == 1, $"the scripted seat plans exactly one research order ({purchases})");
            Check(agri, "and it buys the 農政 line");

            var poor = ScriptedResearchState(BalanceConfig.ResearchLevelCost(1) - 1);
            bool anyResearch = false;
            foreach (var cmd in ScriptedController.PlanTurn(poor, "seat-a"))
                if (cmd.Type == ActionType.Research) anyResearch = true;
            Check(!anyResearch, "with too few points it plans no research at all");
        }

        // ---- determinism: identical seeds replay byte-identical through the research path
        {
            string runA = ResearchReplay(1234), runB = ResearchReplay(1234);
            Check(runA == runB && runA.Length > 0, "two identical seeds produce byte-identical event logs");
            Check(ResearchReplay(1234) != ResearchReplay(99), "a different seed diverges (the check is not vacuous)");
        }
    }

    /// <summary>Submits one research order for `line` (empty args when line is null).</summary>
    private static ValidationResult Research(GameEngine e, string seat, string line)
        => e.Submit(new ActionCommand
        {
            Type = ActionType.Research,
            ActorSeatId = seat,
            Args = line == null ? null : new Dictionary<string, string> { { "line", line } },
        });

    private static void SetLevel(GameEngine e, string seat, string line, int level)
        => e.State.FindFaction(seat).Research[line] = level;

    private sealed class BattleResult
    {
        public int AttackerLosses, DefenderLosses, Rounds;
        public int Round1AttackerLosses, Round1DefenderLosses;
    }

    /// <summary>Two equal armies on a corridor fight one field battle; seat-a has `military` research.</summary>
    private static BattleResult Battle(int seed, int seatAMilitaryLevel)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(10);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.FindFaction("seat-a").Research[ResearchLine.Military] = seatAMilitaryLevel;
        var attacker = new ArmyState { Id = "army-a", Name = "A", OwnerSeatId = "seat-a", Troops = 5000, X = 0, Z = 0 };
        var defender = new ArmyState { Id = "army-b", Name = "B", OwnerSeatId = "seat-b", Troops = 5000, X = 1, Z = 0 };
        e.State.Armies.Add(attacker);
        e.State.Armies.Add(defender);
        e.BeginSeason();
        e.State.Phase = "player_orders";

        e.Submit(new ActionCommand
        {
            Type = ActionType.AttackArmy, ActorSeatId = "seat-a", TargetId = attacker.Id, ParamA = 1, ParamB = 0,
        });
        var rec = Find(e.Log, "battle_field");
        if (rec == null) return new BattleResult { AttackerLosses = -1, DefenderLosses = -1, Rounds = -1 };
        int.TryParse(rec.Payload["attacker_losses"], out int attackerLosses);
        int.TryParse(rec.Payload["defender_losses"], out int defenderLosses);
        int.TryParse(rec.Payload["rounds"], out int rounds);

        // round_log is "a-{toAttacker}/d-{toDefender}" per round, joined by ';' — read round 1 exactly
        int a1 = -1, d1 = -1;
        string[] firstRound = rec.Payload["round_log"].Split(';')[0].Split('/');
        if (firstRound.Length == 2)
        {
            int.TryParse(firstRound[0].Replace("a-", ""), out a1);
            int.TryParse(firstRound[1].Replace("d-", ""), out d1);
        }
        return new BattleResult
        {
            AttackerLosses = attackerLosses, DefenderLosses = defenderLosses, Rounds = rounds,
            Round1AttackerLosses = a1, Round1DefenderLosses = d1,
        };
    }

    /// <summary>A scripted seat with a city that needs nothing, so only research can claim the last 號令.</summary>
    private static GameState ScriptedResearchState(int points)
    {
        var e = Build(17, 2, morale: 100, food: 5000);
        var fac = e.State.FindFaction("seat-a");
        fac.ResearchPoints = points;
        var city = e.State.FindCity("city-a");
        city.Gold = 1000;                        // no 徵稅
        city.ReclaimPct = BalanceConfig.ReclaimPctCap;   // no 開墾
        city.Training = BalanceConfig.TrainingMax;       // no 練兵
        city.Defense = BalanceConfig.DefenseMax;         // no 修城
        return e.State;
    }

    /// <summary>Eight seasons of buy-what-you-can research on one seed; returns the full event log.</summary>
    private static string ResearchReplay(int seed)
    {
        var e = Build(seed, 1, morale: 100, food: 5000);
        for (int season = 0; season < 8; season++)
        {
            // one purchase by hand — walk the lines so the replay covers all four
            e.Submit(new ActionCommand
            {
                Type = ActionType.Research, ActorSeatId = "seat-a",
                Args = new Dictionary<string, string> { { "line", ResearchLine.All[season % ResearchLine.All.Length] } },
            });
            foreach (var cmd in ScriptedController.PlanTurn(e.State, "seat-a")) e.Submit(cmd);
            e.EndSeason();
            e.BeginSeason();
        }
        return e.Log.ToJsonLines();
    }
}
