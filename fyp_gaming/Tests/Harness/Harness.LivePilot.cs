using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using LuanShi.Engine;

internal static partial class Harness
{
    // ---- live LLM-vs-LLM pilot (run only with --live-pilot <config> [seasons]) ----

    private const int LivePilotDefaultSeasons = 3;

    /// <summary>
    /// Headless two-seat spectator pilot against the configured provider. Program dispatches here
    /// only for <c>--live-pilot</c>; the normal suite proves the same entry point offline with a
    /// loopback HttpListener stub. The key and HTTP headers are never printed.
    /// </summary>
    private static int RunLivePilot(string configPath, int seasons)
        => RunLivePilot(configPath, seasons, Console.Out,
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "runs")));

    private static int RunLivePilot(string configPath, int seasons, TextWriter output,
        string runDirectory)
    {
        if (output == null) output = Console.Out;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            output.WriteLine("LIVE-PILOT error: missing config path");
            return 2;
        }
        if (seasons <= 0 || seasons > 24)
        {
            output.WriteLine("LIVE-PILOT error: seasons must be between 1 and 24");
            return 2;
        }

        LlmConfig config;
        try { config = LlmConfig.LoadFromFile(configPath); }
        catch (Exception)
        {
            output.WriteLine("LIVE-PILOT error: config could not be loaded");
            return 2;
        }
        if (!config.HasApiKey)
        {
            output.WriteLine("LIVE-PILOT error: API key is not configured");
            output.WriteLine("LIVE-PILOT config: " + config.Redacted());
            return 2;
        }

        // Spend guards for the pilot, even if the local file is generous. A three-season run has
        // at most 60 provider calls under this cap.
        config.MaxTotalRequests = LiveCap(config.MaxTotalRequests, LivePilotRequestCap(seasons));
        config.MaxRequestsPerTurn = LiveCap(config.MaxRequestsPerTurn, 4);
        config.MaxActionsPerTurn = LiveCap(config.MaxActionsPerTurn, 2);
        config.MaxInvalidPerTurn = LiveCap(config.MaxInvalidPerTurn, 1);
        config.MaxTurnSeconds = LiveCap(config.MaxTurnSeconds, 60);
        config.RequestTimeoutMs = LiveCap(config.RequestTimeoutMs, 25000);

        GameEngine engine;
        try { engine = LivePilotEngine(); }
        catch (Exception)
        {
            output.WriteLine("LIVE-PILOT error: fixture could not be built");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(runDirectory))
            runDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "runs"));
        LlmRunRecorder recorder;
        OpenAiChatTransport transport;
        LlmTurnRunner runner;
        SpectatorSession session;
        try
        {
            Directory.CreateDirectory(runDirectory);
            recorder = new LlmRunRecorder(runDirectory);
            transport = new OpenAiChatTransport(config);
            runner = new LlmTurnRunner(transport, config, recorder);
            session = new SpectatorSession(engine, runner, seasons, 20261027);
        }
        catch (Exception)
        {
            output.WriteLine("LIVE-PILOT error: pilot setup failed");
            return 1;
        }

        output.WriteLine("LIVE-PILOT config: " + config.Redacted());
        output.WriteLine("LIVE-PILOT provider=" + transport.Provider
                         + " model=" + transport.Model);
        output.WriteLine("LIVE-PILOT fixture: seats=2 cities=2 armies=0 seasons="
                         + seasons.ToString(CultureInfo.InvariantCulture));

        DateTime started = DateTime.UtcNow;
        try { session.Start(); }
        catch (Exception)
        {
            output.WriteLine("LIVE-PILOT error: session could not start");
            return 1;
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(120, seasons * 120));
        while (session.State == SpectatorState.Running && DateTime.UtcNow < deadline)
        {
            session.Tick();
            if (session.State == SpectatorState.Running) Thread.Sleep(50);
        }
        bool timedOut = session.State == SpectatorState.Running;
        if (timedOut) session.Stop();

        foreach (SpectatorTurnRecord record in session.TurnRecords)
        {
            output.WriteLine("LIVE-PILOT S" + record.Season.ToString(CultureInfo.InvariantCulture)
                + " " + SafeLine(record.FactionName) + " (" + SafeLine(record.SeatId) + ")"
                + ": requests=" + record.Requests.ToString(CultureInfo.InvariantCulture)
                + " actions=" + record.Actions.ToString(CultureInfo.InvariantCulture)
                + " accepted=" + record.Accepted.ToString(CultureInfo.InvariantCulture)
                + " rejected=" + record.Rejected.ToString(CultureInfo.InvariantCulture)
                + " last=" + SafeLine(record.LastResult)
                + " ended=" + SafeLine(record.EndedBy));
        }
        foreach (string action in session.RecentActions)
            output.WriteLine("LIVE-PILOT action " + SafeLine(action));

        string summaryPath = null;
        try
        {
            summaryPath = new SpectatorSummary(runDirectory)
                .Write(session, config, transport, runner);
        }
        catch (Exception)
        {
            output.WriteLine("LIVE-PILOT error: summary could not be written");
        }

        output.WriteLine("LIVE-PILOT requests=" + session.TotalRequests.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("LIVE-PILOT tokens=input=" + TokenText(session.TotalInputTokens)
                         + " output=" + TokenText(session.TotalOutputTokens));
        output.WriteLine("LIVE-PILOT cost="
                         + (session.CostUsd.HasValue
                             ? "$" + session.CostUsd.Value.ToString("0.######", CultureInfo.InvariantCulture)
                             : "unknown"));
        output.WriteLine("LIVE-PILOT actions=" + session.TotalActions.ToString(CultureInfo.InvariantCulture)
                         + " accepted=" + session.TotalAccepted.ToString(CultureInfo.InvariantCulture)
                         + " rejected=" + session.TotalRejected.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("LIVE-PILOT summary=" + (summaryPath ?? "(not written)"));
        output.WriteLine("LIVE-PILOT started="
                         + started.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                         + " ended="
                         + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        if (timedOut) output.WriteLine("LIVE-PILOT error: pilot timed out");
        else if (session.State == SpectatorState.Paused)
            output.WriteLine("LIVE-PILOT paused: " + SafeLine(session.PauseReason));
        output.WriteLine("LIVE-PILOT state=" + session.State);
        output.WriteLine("LIVE-PILOT seasons completed: "
                         + session.SeasonsPlayed.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("LIVE-PILOT state-changing actions: "
                         + session.TotalAccepted.ToString(CultureInfo.InvariantCulture));

        return session.State == SpectatorState.Completed && !timedOut ? 0 : 1;
    }

    private static int LivePilotRequestCap(int seasons)
    {
        int cap = seasons * 20;
        if (cap < 20) cap = 20;
        if (cap > 60) cap = 60;   // the approved demo budget, even for longer requests
        return cap;
    }

    private static GameEngine LivePilotEngine()
    {
        var engine = new GameEngine(20261027, "pilot-live");
        engine.State.Map = Corridor(10);
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Llm,
        });
        engine.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 9, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.BeginSeason();
        return engine;
    }

    private static string TokenText(long? value)
        => value.HasValue && value.Value != 0
            ? value.Value.ToString(CultureInfo.InvariantCulture) : "unknown";

    private static bool TryParseLiveSeasons(string text, out int seasons)
    {
        seasons = LivePilotDefaultSeasons;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.StartsWith("seasons=", StringComparison.Ordinal)) text = text.Substring(8);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seasons)
               && seasons > 0 && seasons <= 24;
    }

    private static int RunLivePilotFromArgs(string[] args, int index)
    {
        if (args == null || index + 1 >= args.Length)
        {
            Console.Error.WriteLine("usage: EngineHarness --live-pilot <configPath> [seasons=3]");
            return 2;
        }
        int seasons = LivePilotDefaultSeasons;
        if (index + 2 < args.Length && !TryParseLiveSeasons(args[index + 2], out seasons))
        {
            Console.Error.WriteLine("usage: EngineHarness --live-pilot <configPath> [seasons=3]");
            return 2;
        }
        return RunLivePilot(args[index + 1], seasons);
    }
}
