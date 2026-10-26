using System;
using System.Collections.Generic;
using System.Text;
using LuanShi.Engine;

// [LS-003] Detached observations and immutable history.
//
// Pins the guarantees added for LS-003: observations carry only detached views, emitted history cannot
// be rewritten through anything the engine returns, caller-owned payload/args are snapshotted, and old
// snapshots stay frozen while canonical state keeps moving. Every assertion name carries the [LS-003]
// tag so the new count stays distinguishable in the shared harness output.

internal static partial class Harness
{
    private static void RunObservationDetachment()
    {
        Section("[new] LS-003 detached observations and immutable history");

        NestedViewDetachment();
        CollectionMutationAttempts();
        StaleSnapshots();
        CallerOwnedPayloadsAndArgs();
        ProjectedEventDetachment();
        MovedAndDestroyedSubjects();
        DeterministicAndCanonicalIsolation();
    }

    /// <summary>
    /// Seat A sees city-a (0), army-a (4) and its tower (1); city-b (8) and army-b (5) are outside the
    /// 3-hex city / 2-hex army radii, while the tower's 5-hex radius reveals army-b but not city-b.
    /// </summary>
    private static GameEngine DetachWorld(int seed)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(14);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 3000, Garrison = 800, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 3000, Garrison = 0, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-c", Name = "City C", X = 13, Z = 0, OwnerSeatId = "seat-c",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 333, Morale = 100,
        });
        e.State.WatchTowers.Add(new WatchTowerState
        {
            Id = "tower-a", Name = "Tower", X = 1, Z = 0, OwnerSeatId = "seat-a",
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "A Army", OwnerSeatId = "seat-a", Troops = 5000, X = 4, Z = 0,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-b", Name = "B Army", OwnerSeatId = "seat-b", Troops = 40, X = 5, Z = 0,
        });
        e.BeginSeason();
        e.State.Season = 2;
        e.State.Phase = "player_orders";
        return e;
    }

    private static void AddTreaty(GameEngine e)
        => e.State.Diplomacy.Treaties.Add(new TreatyRecord
        {
            Type = TreatyType.Nap, FactionA = "seat-a", FactionB = "seat-b", ExpirySeason = 4,
        });

    private static void AddProposal(GameEngine e)
        => e.State.Diplomacy.Proposals.Add(new TreatyProposal
        {
            ProposalId = "p-9", FromSeatId = "seat-b", ToSeatId = "seat-a", Type = TreatyType.Truce,
            DurationSeasons = 3, ProposedSeason = e.State.Season,
        });

    private static string CanonicalFingerprint(GameEngine e)
    {
        var sb = new StringBuilder();
        sb.Append('s').Append(e.State.Season).Append('|').Append(e.State.Phase)
          .Append('|').Append(e.State.WinnerSeatId ?? "-").Append('\n');
        foreach (var c in e.State.Cities) sb.Append(c.ToJson()).Append('\n');
        foreach (var a in e.State.Armies) sb.Append(a.ToJson()).Append('\n');
        foreach (var t in e.State.WatchTowers) sb.Append(t.ToJson()).Append('\n');
        foreach (var f in e.State.Factions) sb.Append(f.ToJson()).Append('\n');
        sb.Append(e.State.Diplomacy.ToJson()).Append('\n');
        sb.Append(e.Log.ToJsonLines());
        return sb.ToString();
    }

    private static bool Throws<T>(Action mutation) where T : Exception
    {
        try { mutation(); return false; }
        catch (T) { return true; }
    }

    // ---- nested views -------------------------------------------------------

    private static void NestedViewDetachment()
    {
        var e = DetachWorld(701);
        AddTreaty(e);
        AddProposal(e);
        var obs = e.Observe("seat-a");

        Check(obs != null && obs.FindCity("city-a") != null,
              "[LS-003] the fixture exposes an own city");
        Check(obs.FindCity("city-b") == null && obs.FindArmy("army-b") != null,
              "[LS-003] the fixture hides city-b while the tower reveals army-b");

        int garrison = e.State.FindCity("city-a").Garrison;
        int food = e.State.FindCity("city-a").Food;
        var city = obs.FindCity("city-a");
        city.Garrison = 1; city.Food = 1; city.OwnerSeatId = "seat-b";
        Check(e.State.FindCity("city-a").Garrison == garrison && e.State.FindCity("city-a").Food == food
           && e.State.FindCity("city-a").OwnerSeatId == "seat-a",
              "[LS-003] mutating a city view cannot change canonical state");

        var army = obs.FindArmy("army-a");
        army.Troops = 1; army.X = 99;
        Check(e.State.FindArmy("army-a").Troops == 5000 && e.State.FindArmy("army-a").X == 4,
              "[LS-003] mutating an army view cannot change canonical state");

        obs.FindWatchTower("tower-a").OwnerSeatId = "seat-c";
        Check(e.State.WatchTowers[0].OwnerSeatId == "seat-a",
              "[LS-003] mutating a watch-tower view cannot change canonical state");

        obs.Treaties.Find(t => t.Type == TreatyType.Nap).ExpirySeason = 99;
        Check(e.State.Diplomacy.Treaties[0].ExpirySeason == 4,
              "[LS-003] mutating a treaty view cannot change canonical diplomacy");

        obs.Proposals[0].DurationSeasons = 99;
        obs.Proposals[0].Accepted = true;
        Check(e.State.Diplomacy.Proposals[0].DurationSeasons == 3 && !e.State.Diplomacy.Proposals[0].Accepted,
              "[LS-003] mutating a proposal view cannot change canonical diplomacy");

        string name = e.State.FindFaction("seat-a").Name;
        int prestige = e.State.FindFaction("seat-a").Prestige;
        obs.Factions[0].Name = "Hacked";
        obs.Factions[0].Prestige = 999;
        Check(e.State.FindFaction("seat-a").Name == name && e.State.FindFaction("seat-a").Prestige == prestige,
              "[LS-003] mutating a faction view cannot change canonical state");

        obs.Relation("seat-b").MyTrust = 999;
        obs.Relation("seat-b").AtWar = true;
        Check(e.State.Diplomacy.GetTrust("seat-a", "seat-b") == BalanceConfig.TrustStart
           && !e.State.Diplomacy.IsAtWar("seat-a", "seat-b"),
              "[LS-003] mutating a relation view cannot change canonical diplomacy");

        obs.Research[ResearchLine.Agri] = 5;
        Check(e.State.FindFaction("seat-a").ResearchLevel(ResearchLine.Agri) == 0,
              "[LS-003] mutating an observation's research map cannot change the tech tree");

        var fresh = e.Observe("seat-a");
        Check(fresh.FindCity("city-a").Garrison == garrison
           && fresh.FindArmy("army-a").Troops == 5000
           && fresh.Faction("seat-a").Name == name,
              "[LS-003] a fresh observation is unaffected by a mutated earlier view");
    }

    // ---- collections and dictionaries ---------------------------------------

    private static void CollectionMutationAttempts()
    {
        var e = DetachWorld(702);
        AddTreaty(e);
        AddProposal(e);
        var obs = e.Observe("seat-a");
        int cities = obs.Cities.Count, armies = obs.Armies.Count, towers = obs.WatchTowers.Count;
        int events = obs.Events.Count, logCount = e.Log.Records.Count;

        var firstEvent = obs.Events.Count > 0 ? obs.Events[0] : null;
        obs.Cities.Clear();
        obs.Armies.Clear();
        obs.WatchTowers.Clear();
        obs.Treaties.Clear();
        obs.Proposals.Clear();
        obs.Events.Clear();
        obs.Cities.Add(CityView.From(e.State.FindCity("city-b")));   // even a fabricated hidden-city view
        if (firstEvent != null) obs.Events.Add(firstEvent);

        Check(e.State.Cities.Count == 3 && e.State.Armies.Count == 2 && e.State.WatchTowers.Count == 1,
              "[LS-003] clearing or extending observation collections cannot change canonical entities");
        Check(e.State.Diplomacy.Treaties.Count == 1 && e.State.Diplomacy.Proposals.Count == 1,
              "[LS-003] clearing observation diplomacy collections cannot change canonical diplomacy");
        Check(e.Log.Records.Count == logCount, "[LS-003] clearing an observation's event list cannot truncate the log");

        var fresh = e.Observe("seat-a");
        Check(fresh.Cities.Count == cities && fresh.Armies.Count == armies
           && fresh.WatchTowers.Count == towers && fresh.Events.Count == events,
              "[LS-003] a fresh observation rebuilds every collection from the filtered truth");
        Check(fresh.FindCity("city-b") == null,
              "[LS-003] a fabricated view cannot make a hidden city visible to the seat");
    }

    // ---- stale snapshots ----------------------------------------------------

    private static void StaleSnapshots()
    {
        var e = DetachWorld(703);
        var first = e.Observe("seat-a");
        string firstJson = first.ToJson();
        int training = first.FindCity("city-a").Training;
        int armyX = first.FindArmy("army-a").X;

        var train = e.Submit(new ActionCommand
        {
            Type = ActionType.Train, ActorSeatId = "seat-a", TargetId = "city-a",
        });
        var march = e.Submit(new ActionCommand
        {
            Type = ActionType.March, ActorSeatId = "seat-a", TargetId = "army-a", ParamA = 3, ParamB = 0,
        });
        var second = e.Observe("seat-a");

        Check(train.Ok && second.FindCity("city-a").Training == training + 1,
              $"[LS-003] the new snapshot reflects the resolved order ({train.Error})");
        Check(first.FindCity("city-a").Training == training,
              "[LS-003] an old snapshot keeps the values it was built with");
        Check(march.Ok && second.FindArmy("army-a").X == 3 && first.FindArmy("army-a").X == armyX,
              $"[LS-003] movement updates the new snapshot but not the old one ({march.Error})");
        Check(first.ToJson() == firstJson,
              "[LS-003] an old snapshot's JSON is unchanged after later engine actions");
        Check(second.ToJson() != firstJson,
              "[LS-003] the new snapshot differs, so the previous assertion is not vacuous");
    }
}

