using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class BootstrapGateTransport : ILlmTransport
    {
        public string BootstrapPath;
        public bool SawBootstrapAtDispatch;
        public string Provider => "fake";
        public string Model => "bootstrap-check";
        public BootstrapGateTransport() { }
        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            SawBootstrapAtDispatch = File.Exists(BootstrapPath) && File.ReadAllText(BootstrapPath).Contains("initial_world_sha256");
            return Task.FromResult(LlmResponse.Success("{\"type\":\"end_turn\"}", 0, 1, 1, "fake"));
        }
    }

    private sealed class BootstrapHost : ITurnHost
    {
        private readonly GameEngine _engine;
        public BootstrapHost(GameEngine engine) { _engine = engine; }
        public Observation Observe() => _engine.Observe("seat-0");
        public ValidationResult Submit(ActionCommand cmd) => ValidationResult.Fail("not expected");
        public void EndTurn() { }
    }

    private static GameEngine BootstrapEngine(string gameId, bool withMap = true)
    {
        var engine = new GameEngine(712, gameId, BalanceProfile.FoodEasy);
        if (withMap)
            engine.State.Map = new MapData { Width = 2, Height = 2,
                Walkable = new[] { true, true, true, false }, Cost = new[] { 1, 2, 1, 1 },
                Neighbors = new[] { new[] { 1, 2 }, new[] { 0 }, new[] { 0 }, Array.Empty<int>() } };
        engine.State.ScenarioId = "compact-contact-v1";
        engine.State.Factions.Add(new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm });
        engine.State.Cities.Add(new CityState { Id = "city-home", Name = "Home", X = 0, Z = 0,
            OwnerSeatId = "seat-0", Population = 10000, Food = 3000, Gold = 900,
            Garrison = 500, GarrisonType = UnitTypes.Infantry, Morale = 60, Defense = 1, Training = 1 });
        engine.BeginSeason();
        return engine;
    }

    private static void RunReplayBootstrap()
    {
        Section("[REPLAY-BOOTSTRAP] canonical pre-request capture");
        string root = Path.Combine(Path.GetTempPath(), "luanshi-replay-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, "{\"api_key\":\"bootstrap-secret-marker\",\"enable_attack\":true,\"enable_research\":true,\"enable_diplomacy\":true}");
            var config = LlmConfig.LoadFromFile(configPath);
            var first = BootstrapEngine("bootstrap-game-a");
            var recorderA = new LlmRunRecorder(root);
            var transport = new BootstrapGateTransport();
            var runner = new LlmTurnRunner(transport, config, recorderA);
            string pathA = recorderA.WriteReplayBootstrap(first, 712, config, "test-revision", "spectator", 2);
            transport.BootstrapPath = pathA;
            runner.Begin(new BootstrapHost(first), 1, "seat-0", 712);
            Check(transport.SawBootstrapAtDispatch,
                "[REPLAY-BOOTSTRAP] snapshot is present before fake runner dispatches first request");

            var second = BootstrapEngine("bootstrap-game-b");
            var recorderB = new LlmRunRecorder(root);
            recorderB.UseRunId("run-bootstrap-b");
            string pathB = recorderB.WriteReplayBootstrap(second, 712, config, "test-revision", "spectator", 2);
            string jsonA = File.ReadAllText(pathA);
            string jsonB = File.ReadAllText(pathB);
            Check(pathA != pathB && jsonA.Contains("bootstrap-game-a") && !jsonA.Contains("bootstrap-game-b")
                && jsonB.Contains("bootstrap-game-b") && !jsonB.Contains("bootstrap-game-a"),
                "[REPLAY-BOOTSTRAP] each match gets an isolated snapshot and game identity");
            Check(jsonA.Contains("\"attack\":true") && jsonA.Contains("\"research\":true")
                && jsonA.Contains("\"diplomacy\":true") && jsonA.Contains("\"profile\":\"food-easy\""),
                "[REPLAY-BOOTSTRAP] profile and attack/research/diplomacy flags are captured");
            Check(!jsonA.Contains("bootstrap-secret-marker") && !jsonA.Contains("api_key"),
                "[REPLAY-BOOTSTRAP] bootstrap omits credentials and provider config");

            string sameA = ReplayBootstrap.BuildJson(BootstrapEngine("same-game"), 712,
                "run-a", config, "test-revision", "spectator", 2);
            string sameB = ReplayBootstrap.BuildJson(BootstrapEngine("same-game"), 712,
                "run-b", config, "test-revision", "spectator", 2);
            string worldHashA = ReadJsonString(sameA, "initial_world_sha256");
            string worldHashB = ReadJsonString(sameB, "initial_world_sha256");
            string mapHashA = ReadJsonString(sameA, "initial_map_sha256");
            string mapHashB = ReadJsonString(sameB, "initial_map_sha256");
            Check(worldHashA == worldHashB && mapHashA == mapHashB,
                "[REPLAY-BOOTSTRAP] same seed/setup/map produces stable initial world/map hashes");

            bool unavailable = false;
            try { ReplayBootstrap.BuildJson(BootstrapEngine("missing-map", false), 712,
                "run-missing", config, "test-revision", "spectator", 2); }
            catch (InvalidOperationException e) { unavailable = e.Message.Contains("map missing"); }
            Check(unavailable, "[REPLAY-BOOTSTRAP] missing map fails clearly instead of guessing");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    private static string ReadJsonString(string json, string key)
    {
        string marker = "\"" + key + "\":\"";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? null : json.Substring(start, end - start);
    }
}
