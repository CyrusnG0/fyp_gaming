using System.Collections.Generic;
using LuanShi.Engine;

// [LS-003] Report text built from the observation, not canonical state.
//
// Pins the two review findings of LS-003: a city may be named only when the seat can see it or holds a
// live 偵察 report (never from the canonical state), and a capture line names the event's actor rather
// than the city's later owner. Every assertion name carries the [LS-003] tag.

internal static partial class Harness
{
    private static void RunReportText()
    {
        Section("[new] LS-003 report text uses only the observation");

        CityLabels();
        ScoutedCaptureLabel();
        HiddenNamesNeverLeak();
        SuccessiveCaptures();
        CaptorFallsBackToSeatId();
        VisibleCaptureWording();
    }

    // ---- city labels -------------------------------------------------------

    private static void CityLabels()
    {
        var e = PrivacyWorld(801);
        e.State.FindCity("city-b").Name = "Hidden B";
        e.State.FindFaction("seat-a").AddScoutReport("city-b", e.State.Season);
        var obs = e.Observe("seat-a");

        Check(ReportText.CityLabel(obs, "city-a") == "City A",
              "[LS-003] CityLabel names a city the seat can see");
        Check(obs.FindCity("city-b") == null && obs.FindScoutedCity("city-b") != null,
              "[LS-003] fixture: city-b is out of sight but has a live scout report");
        Check(ReportText.CityLabel(obs, "city-b") == "Hidden B",
              "[LS-003] CityLabel uses the scouted name for an out-of-sight city");
        Check(ReportText.CityLabel(obs, "city-b") != "city-b",
              "[LS-003] the scouted label is the name, not the public city id");
        Check(ReportText.CityLabel(obs, "city-ghost") == "city-ghost",
              "[LS-003] CityLabel falls back to the city id when the seat knows nothing about it");
        Check(ReportText.CityLabel(null, "city-b") == "city-b",
              "[LS-003] CityLabel is null-safe and returns the city id without an observation");

        var secret = PrivacyWorld(802);
        secret.State.FindCity("city-b").Name = "Unseen Secret";
        var secretObs = secret.Observe("seat-a");
        Check(secretObs.FindCity("city-b") == null && secretObs.FindScoutedCity("city-b") == null,
              "[LS-003] fixture: the secret city is neither visible nor scouted");
        string label = ReportText.CityLabel(secretObs, "city-b");
        Check(label == "city-b" && label != "Unseen Secret",
              "[LS-003] CityLabel never falls back to the canonical hidden name");
    }

    // ---- scouted capture lines ---------------------------------------------

    /// <summary>
    /// Seat A cannot see city-b, but holds a live 偵察 report covering it when seat B captures it. The
    /// capture line must therefore print the scouted name the report carries, not the public city id.
    /// </summary>
    private static void ScoutedCaptureLabel()
    {
        var e = ReportCaptureWorld(814, "Scouted B");
        var capture = e.Submit(MarchOrder(e, "seat-b", "army-b", 6, 0));
        e.State.FindFaction("seat-a").AddScoutReport("city-b", e.State.Season);
        var obs = e.Observe("seat-a");
        var rec = ObsEvent(obs, "city_captured");
        Check(capture.Ok && rec != null, $"[LS-003] a hidden city is captured while scouted ({capture.Error})");
        Check(obs.FindCity("city-b") == null && obs.FindScoutedCity("city-b") != null,
              "[LS-003] fixture: the captured city is out of sight but has a live scout report");
        string line = ReportText.CaptureLine(obs, rec);
        Check(line == "* Shu army occupied Scouted B, expanding its territory!",
              "[LS-003] the capture line uses the scouted canonical name");
        Check(!line.Contains("city-b"),
              "[LS-003] the scouted capture line is not just the public city id");
    }

    // ---- hidden-world capture lines ----------------------------------------

    /// <summary>
    /// Seat A watches city-a at 0 and can see no further than hex 3. Seat B's army at 4 marches onto the
    /// neutral city-b at 6, so A receives the public capture news while the city itself stays hidden. The
    /// city's canonical name is the caller's parameter: the only per-world difference.
    /// </summary>
    private static GameEngine ReportCaptureWorld(int seed, string cityBName)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(20);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = cityBName, X = 6, Z = 0, OwnerSeatId = null,
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 50, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-b", Name = "B Army", OwnerSeatId = "seat-b", Troops = 1000, X = 4, Z = 0,
        });
        e.BeginSeason();
        e.State.Season = 3;
        return e;
    }

    private static void HiddenNamesNeverLeak()
    {
        var alpha = ReportCaptureWorld(811, "Alpha Secret");
        var omega = ReportCaptureWorld(811, "Omega Secret");
        var captureA = alpha.Submit(MarchOrder(alpha, "seat-b", "army-b", 6, 0));
        var captureB = omega.Submit(MarchOrder(omega, "seat-b", "army-b", 6, 0));
        Check(captureA.Ok && captureB.Ok, $"[LS-003] both hidden-world captures resolve ({captureA.Error})");

        var obsA = alpha.Observe("seat-a");
        var obsB = omega.Observe("seat-a");
        var recA = ObsEvent(obsA, "city_captured");
        var recB = ObsEvent(obsB, "city_captured");
        Check(recA != null && recB != null,
              "[LS-003] the capture is public news to the observing seat");
        Check(obsA.FindCity("city-b") == null && obsA.FindScoutedCity("city-b") == null,
              "[LS-003] fixture: city-b stays out of sight and unscouted for the observer");
        Check(alpha.State.FindCity("city-b").Name == "Alpha Secret"
           && omega.State.FindCity("city-b").Name == "Omega Secret",
              "[LS-003] fixture: the two worlds differ only in the hidden canonical name");

        string lineA = ReportText.CaptureLine(obsA, recA);
        string lineB = ReportText.CaptureLine(obsB, recB);
        string labelA = ReportText.CityLabel(obsA, "city-b");
        string labelB = ReportText.CityLabel(obsB, "city-b");
        Check(lineA == lineB && labelA == labelB,
              "[LS-003] a hidden canonical name cannot change the seat's report text");
        Check(labelA == "city-b" && lineA.Contains("city-b"),
              "[LS-003] the hidden city is reported by its public id");
        Check(!lineA.Contains("Alpha Secret") && !lineB.Contains("Omega Secret"),
              "[LS-003] neither report text leaks its world's hidden city name");
    }

    // ---- captures in one season --------------------------------------------

    /// <summary>
    /// Seat A observes while seat B captures city-b and seat C captures city-c in the same season. Both
    /// cities are later reassigned on the canonical state, so a report that read the city's current
    /// owner would name the wrong captor.
    /// </summary>
    private static GameEngine ReportTwoCaptureWorld(int seed)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(24);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 6, Z = 0, OwnerSeatId = null,
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 50, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-c", Name = "City C", X = 18, Z = 0, OwnerSeatId = null,
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 50, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-b", Name = "B Army", OwnerSeatId = "seat-b", Troops = 1000, X = 4, Z = 0,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-c", Name = "C Army", OwnerSeatId = "seat-c", Troops = 1000, X = 16, Z = 0,
        });
        e.BeginSeason();
        e.State.Season = 3;
        return e;
    }

    private static void SuccessiveCaptures()
    {
        var e = ReportTwoCaptureWorld(812);
        var first = e.Submit(MarchOrder(e, "seat-b", "army-b", 6, 0));
        var second = e.Submit(MarchOrder(e, "seat-c", "army-c", 18, 0));
        Check(first.Ok && second.Ok,
              $"[LS-003] two seats capture a city each in the same season ({first.Error}; {second.Error})");

        // Both cities change hands again after the reported captures. The report must still name the
        // captor recorded on each event, not the owner found on the canonical state now.
        e.State.FindCity("city-b").OwnerSeatId = "seat-c";
        e.State.FindCity("city-c").OwnerSeatId = "seat-b";
        Check(e.State.FindCity("city-b").OwnerSeatId == "seat-c"
           && e.State.FindCity("city-c").OwnerSeatId == "seat-b",
              "[LS-003] fixture: both captured cities have a different later owner");

        var obs = e.Observe("seat-a");
        var lines = new List<string>();
        foreach (var rec in obs.Events)
            if (rec.EventType == "city_captured") lines.Add(ReportText.CaptureLine(obs, rec));

        Check(lines.Count == 2, "[LS-003] the seat receives both capture lines");
        Check(lines.Count == 2 && lines[0] == "* Shu army occupied city-b, expanding its territory!",
              "[LS-003] the first line names the event's captor, not city-b's later owner");
        Check(lines.Count == 2 && lines[1] == "* Wu army occupied city-c, expanding its territory!",
              "[LS-003] the second line names its own captor, not city-c's later owner");
    }

    // ---- captor absent from the observation --------------------------------

    /// <summary>
    /// Seat B captures city-b, then leaves the world's faction list before seat A takes a snapshot. The
    /// public capture record (its audience frozen at emit) still reaches A, but A's observation no longer
    /// carries seat B, so the line must fall back to the raw seat id rather than invent a name.
    /// </summary>
    private static void CaptorFallsBackToSeatId()
    {
        var e = ReportCaptureWorld(815, "Hidden C");
        var capture = e.Submit(MarchOrder(e, "seat-b", "army-b", 6, 0));
        e.State.Factions.Remove(e.State.FindFaction("seat-b"));
        var obs = e.Observe("seat-a");
        var rec = ObsEvent(obs, "city_captured");
        Check(capture.Ok && rec != null && rec.ActorSeatId == "seat-b",
              $"[LS-003] the capture record survives its actor's removal ({capture.Error})");
        Check(obs != null && obs.Faction("seat-b") == null,
              "[LS-003] fixture: the captor faction is absent from the observer's snapshot");
        Check(ReportText.CaptureLine(obs, rec) == "* seat-b army occupied city-b, expanding its territory!",
              "[LS-003] the capture line falls back to the seat id when the captor is unknown");
    }

    // ---- visible city wording ----------------------------------------------

    private static void VisibleCaptureWording()
    {
        var e = new GameEngine(813);
        e.State.Map = Corridor(20);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-near", Name = "Near Town", X = 3, Z = 0, OwnerSeatId = null,
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 50, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-far", Name = "Far City", X = 18, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 500, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "A Army", OwnerSeatId = "seat-a", Troops = 1000, X = 2, Z = 0,
        });
        e.BeginSeason();
        e.State.Season = 3;

        var capture = e.Submit(MarchOrder(e, "seat-a", "army-a", 3, 0));
        var obs = e.Observe("seat-a");
        var rec = ObsEvent(obs, "city_captured");
        Check(capture.Ok && rec != null, $"[LS-003] a visible city is captured ({capture.Error})");
        Check(obs.FindCity("city-near") != null, "[LS-003] fixture: the captured city stays in the seat's sight");
        Check(ReportText.CaptureLine(obs, rec) == "* Wei army occupied Near Town, expanding its territory!",
              "[LS-003] the capture line keeps its exact wording for a visible city");
    }
}
