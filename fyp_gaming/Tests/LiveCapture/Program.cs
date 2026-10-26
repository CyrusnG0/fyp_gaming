// LS-015 W6 - LiveCapture: ONE staged real-provider Shu turn (manual capability probe only).
// STAGED probe, not a natural play result: the demo map is TBTK hex, so this uses the harness
// MovementGrid shape (flat 12x10, all walkable, 4-neighbour, demo coordinates kept) instead.
// Extra staging: army-shu-2 starts at (6,6), 2 path steps from neutral city-south (6,4) and inside
// its own army's sight radius (2), so MovementPlanner advertises the capture. Scene armies use scene
// positions (fallback (9,5)); the other Shu armies are spread around Chengdu.
// Secrets: llm.local.json goes only to LlmConfig.LoadFromFile; no prompt body, API key, base_url or
// headers are printed. No network I/O except the real OpenAiChatTransport in the single runner turn.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using LuanShi.Engine;

internal static class Program
{
    private const string WeiSeat = "seat-0";
    private const string ShuSeat = "seat-1";
    private const int Seed = 12345;
    private const int WallClockGuardMs = 150000;
    private const int SouthX = 6, SouthZ = 4;      // city-south (demo)
    private const int StagedX = 6, StagedZ = 6;    // army-shu-2 (staged)

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: LiveCapture <llm.local.json> <runs-dir>");
            return 2;
        }
        LlmConfig config = LoadValidatedConfig(args[0]);
        if (config == null) return 2;   // refusal reason already printed
        config.MaxRequestsPerTurn = Math.Min(config.MaxRequestsPerTurn, 6);   // forced hard cap
        string runsDir = Path.GetFullPath(args[1]);
        try { Directory.CreateDirectory(runsDir); }
        catch (Exception)
        {
            Console.Error.WriteLine("refused: the runs directory could not be created");
            return 2;
        }
        GameEngine engine = BuildGame();
        // The runner builds exactly this view for its first prompt (same seat, same planner).
        MovementView firstView = MovementPlanner.Build(engine, ShuSeat);
        int optionCount = 0;
        MoveOption capture = null;
        foreach (ArmyMovePlan plan in firstView.Armies)
        {
            if (plan == null || plan.Options == null) continue;
            optionCount += plan.Options.Count;
            if (plan.ArmyId != "army-shu-2") continue;   // report the staged army's option
            foreach (MoveOption option in plan.Options)
                if (option != null && option.CityId == "city-south" && capture == null) capture = option;
        }
        bool inPromptSection = capture != null && PromptBuilder.MovementOptionsText(firstView)
            .Contains(capture.X + "," + capture.Z + " cost=" + capture.PathCost + " capture_neutral_city");
        Console.WriteLine("LS-015 live capture probe - one Shu turn, real provider");
        Console.WriteLine($"model={config.Model} max_requests_per_turn={config.MaxRequestsPerTurn} " +
                          $"max_total_requests={config.MaxTotalRequests}");
        Console.WriteLine($"staged: army-shu-2 at ({StagedX},{StagedZ}); city-south at ({SouthX},{SouthZ}); " +
                          $"shu_seat={ShuSeat}");
        Console.WriteLine($"movement_options={optionCount} " +
                          $"capture_option_in_first_view={(capture != null ? "yes" : "no")}");
        if (capture != null)
            Console.WriteLine($"advertised_capture: army_id={capture.ArmyId} x={capture.X} z={capture.Z} " +
                              $"cost={capture.PathCost} kind={capture.Kind} city_id={capture.CityId}");
        Console.WriteLine($"capture_advertised_in_prompt_section={(inPromptSection ? "yes" : "no")}");
        Console.Out.Flush();
        var recorder = new LlmRunRecorder(runsDir);
        var runner = new LlmTurnRunner(new OpenAiChatTransport(config), config, recorder);
        var host = new LiveHost(engine, ShuSeat);   // BeginSeatTurn(seat-1) in the constructor
        var clock = Stopwatch.StartNew();
        try { runner.Begin(host, host.Token, ShuSeat, Seed); }
        catch (Exception e)
        {
            Console.Error.WriteLine("runner could not start: " + e.GetType().Name);
            return 3;
        }
        // Exactly one turn, main thread, no second turn. Poll every 50 ms.
        while (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
        {
            runner.Tick();
            bool stillRunning = runner.State == RunnerState.Requesting
                                || runner.State == RunnerState.Executing;
            if (stillRunning && clock.ElapsedMilliseconds > WallClockGuardMs)
            {
                runner.Cancel("wall-clock guard (150 s)");
                if (host.EndTurnCalls == 0) engine.EndSeatTurn(host.Token);
                Console.WriteLine("wall-clock guard: cancelled after 150 s");
                PrintOutcome(runner, host, engine);
                return 3;
            }
            Thread.Sleep(50);
        }
        PrintOutcome(runner, host, engine);
        return 0;
    }

    /// <summary>File, JSON and key checks; prints the reason and returns null when refused.</summary>
    private static LlmConfig LoadValidatedConfig(string path)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine("refused: config file is missing"); return null; }
        LlmConfig config = LlmConfig.LoadFromFile(path);
        if (!string.IsNullOrEmpty(config.LoadWarning))
        { Console.Error.WriteLine("refused: config file is invalid"); return null; }
        if (!config.HasApiKey) { Console.Error.WriteLine("refused: config has no API key"); return null; }
        if (config.MaxTotalRequests <= 0 || config.MaxTotalRequests > 20)
        {
            Console.Error.WriteLine("refused: MaxTotalRequests must be 1..20 (found "
                                    + config.MaxTotalRequests + ")");
            return null;
        }
        if (config.MaxRequestsPerTurn <= 0)
        { Console.Error.WriteLine("refused: MaxRequestsPerTurn is not positive"); return null; }
        return config;
    }

    private static void PrintOutcome(LlmTurnRunner runner, LiveHost host, GameEngine engine)
    {
        Console.WriteLine($"end_reason={runner.EndReason} ({runner.EndReasonText})");
        Console.WriteLine($"requests_this_turn={runner.RequestsThisTurn} total_requests={runner.TotalRequests}");
        Console.WriteLine("tokens: input=" + Token(runner.TotalInputTokens)
                          + " output=" + Token(runner.TotalOutputTokens));
        if (runner.Activity.Count > 0)
        {
            Console.WriteLine("activity trace (commands, validation, results):");
            string summary = "";
            int commandIndex = 0;
            foreach (LlmActivity entry in runner.Activity)
            {
                if (entry.Kind == "decision") { summary = entry.Text ?? ""; continue; }
                if (entry.Kind == "command" && entry.Text == "end_turn")
                    Console.WriteLine($"  command: type=end_turn target_id=null param_a=0 param_b=0 " +
                                      $"summary=\"{summary}\"");
                else if (entry.Kind == "command" && commandIndex < host.Commands.Count)
                {
                    ActionCommand cmd = host.Commands[commandIndex++];
                    Console.WriteLine($"  command: type={cmd.Type} target_id={cmd.TargetId ?? "null"} " +
                                      $"param_a={cmd.ParamA} param_b={cmd.ParamB} summary=\"{summary}\"");
                }
                else if (entry.Kind == "command")
                    Console.WriteLine($"  command: trace=\"{entry.Text}\" summary=\"{summary}\"");
                else if (entry.Kind == "validation") Console.WriteLine("  validation: " + entry.Text);
                else if (entry.Kind == "result") Console.WriteLine("  result: " + entry.Text);
            }
        }
        CityState south = engine.State.FindCity("city-south");
        ArmyState staged = engine.State.FindArmy("army-shu-2");
        bool changed = south != null && string.Equals(south.OwnerSeatId, ShuSeat, StringComparison.Ordinal);
        string owner = south == null ? "missing"
            : (string.IsNullOrEmpty(south.OwnerSeatId) ? "neutral" : south.OwnerSeatId);
        Console.WriteLine($"city-south.owner={owner} changed_to_shu={(changed ? "yes" : "no")}");
        Console.WriteLine($"army-shu-2.final={(staged == null ? "missing" : "(" + staged.X + "," + staged.Z + ")")}");
    }

    private static string Token(long? value) => value.HasValue ? value.Value.ToString() : "unknown";

    // ---- staged two-seat demo game (no Unity/TBTK) -------------------------
    private static GameEngine BuildGame()
    {
        var engine = new GameEngine(Seed);
        engine.State.Map = FlatMap(12, 10);
        engine.State.Factions.Add(new FactionState
            { SeatId = WeiSeat, Name = "Wei", Controller = ControllerType.Human });
        engine.State.Factions.Add(new FactionState
            { SeatId = ShuSeat, Name = "Shu", Controller = ControllerType.Llm });
        AddOwnedCity(engine, "city-wei", "Wei Capital", WeiSeat, 2, 2);
        AddOwnedCity(engine, "city-shu", "Chengdu", ShuSeat, 9, 6);
        engine.State.Cities.Add(new CityState
            { Id = "city-south", Name = "Southern City", X = SouthX, Z = SouthZ,
              OwnerSeatId = null, Population = 3000, Food = 200, Gold = 100 });   // demo neutral
        engine.State.WatchTowers.Add(new WatchTowerState
            { Id = "watch-tower-1", Name = "Watch Tower", X = 6, Z = 2, OwnerSeatId = null });
        AddArmy(engine, "army-wei-1", "Wei Army", WeiSeat, 2, 3);
        AddArmy(engine, "army-shu-1", "Shu Army", ShuSeat, 9, 5);
        AddArmy(engine, "army-shu-2", "Shu Army", ShuSeat, StagedX, StagedZ);   // staged probe
        AddArmy(engine, "army-shu-3", "Shu Army", ShuSeat, 8, 6);
        AddArmy(engine, "army-shu-4", "Shu Army", ShuSeat, 8, 5);
        engine.BeginSeason();
        return engine;
    }
    private static void AddOwnedCity(GameEngine engine, string id, string name, string seat, int x, int z)
        => engine.State.Cities.Add(new CityState
        {
            Id = id, Name = name, X = x, Z = z, OwnerSeatId = seat,
            Population = BalanceConfig.StartPopulation, Food = BalanceConfig.StartFood,
            Gold = BalanceConfig.StartGold,
        });
    private static void AddArmy(GameEngine engine, string id, string name, string seat, int x, int z)
        => engine.State.Armies.Add(new ArmyState
        {
            Id = id, Name = name, OwnerSeatId = seat,
            Troops = BalanceConfig.StartFieldArmyTroops, X = x, Z = z,
        });

    // Flat all-walkable 4-neighbour map, the harness MovementGrid shape (not TBTK hex).
    private static MapData FlatMap(int width, int height)
    {
        var map = new MapData
        {
            Width = width, Height = height, Walkable = new bool[width * height],
            Cost = new int[width * height], Neighbors = new int[width * height][],
        };
        for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int i = map.Index(x, z);
                map.Walkable[i] = true;
                map.Cost[i] = 1;
                var list = new List<int>();
                if (x > 0) list.Add(map.Index(x - 1, z));
                if (x < width - 1) list.Add(map.Index(x + 1, z));
                if (z > 0) list.Add(map.Index(x, z - 1));
                if (z < height - 1) list.Add(map.Index(x, z + 1));
                map.Neighbors[i] = list.ToArray();
            }
        return map;
    }

    /// <summary>Same host binding as the demo AgentTurnHost: gate token, Observe, SubmitAs, EndTurn.</summary>
    private sealed class LiveHost : IPlanningHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;
        public readonly int Token;
        public readonly List<ActionCommand> Commands = new List<ActionCommand>();
        public int EndTurnCalls;

        public LiveHost(GameEngine engine, string seat)
        {
            _engine = engine;
            _seat = seat;
            Token = engine.BeginSeatTurn(seat);
        }

        public Observation Observe() => _engine.Observe(_seat);

        public ValidationResult Submit(ActionCommand cmd)
        {
            if (cmd == null) return ValidationResult.Fail("null command");
            Commands.Add(cmd);
            return _engine.SubmitAs(_seat, Token, cmd);
        }

        public void EndTurn()
        {
            EndTurnCalls++;
            _engine.EndSeatTurn(Token);
        }

        public MovementView PlanMovement() => MovementPlanner.Build(_engine, _seat);
        public bool IsGameOver => _engine.State.WinnerSeatId != null;
    }
}
