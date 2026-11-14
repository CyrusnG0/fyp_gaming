using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal enum PlaygroundActionMode
    {
        /// <summary>Validate on an isolated replay, then execute through the live turn gate.</summary>
        Execute = 0,

        /// <summary>Validate on an isolated replay and stop; the authoritative state never advances.</summary>
        CheckOnly = 1,
    }

    /// <summary>
    /// Everything that can change comparison results for the same scenario and seed. Defaults are the
    /// documented fake run; <see cref="ParseArgs"/> lets a caller override one setting at a time.
    /// Provider defaults to "fake" and never reaches a network unless a local operator asks for it.
    /// </summary>
    internal sealed class PlaygroundConfig
    {
        public const string SchemaVersion = "playground-v1";
        public const string PromptAddendumVersion = "playground-deliberation-1";
        public const int DefaultSeed = 4242;
        public const string DefaultGameId = "playground-match-001";
        public const string ScenarioId = "two-faction-corridor-v1";

        public int Seed = DefaultSeed;
        public string GameId = DefaultGameId;
        public int Seasons = 2;

        // ---- provider -------------------------------------------------------
        public string Provider = "fake";
        public string Model = "fake-model";
        public string BaseUrl = "";
        public bool AllowRealProvider;

        // ---- action mode ----------------------------------------------------
        public PlaygroundActionMode ActionMode = PlaygroundActionMode.Execute;
        public bool CheckBeforeExecute = true;
        public bool EnableAttack;

        // ---- runner/budget limits (mirrors LlmConfig names) -----------------
        public int MaxRequestsPerTurn = 6;
        public int MaxActionsPerTurn = 5;
        public int MaxInvalidPerTurn = 2;
        public int MaxInquiryPerTurn = 4;
        public int MaxRequestsPerSession = 60;
        public int MaxActionsPerSession = 30;
        public int MaxInquiryPerSession = 20;
        public int MaxWallMillisPerTurn = 60000;
        public int MaxWallMillisPerSession = 600000;
        public int RequestTimeoutMs = 25000;
        public int MaxOutputTokens = 400;
        public int InputTokenBound = 24000;

        // ---- playground bounds ---------------------------------------------
        public int MaxRetainedInquiryResults = 4;
        public int InquiryMaxResponseChars = 4000;
        public int MaxNoteChars = 400;
        public int MaxTraceResponseChars = 20000;
        public int SimulatedRequestMs = 100;
        public int SimulatedInquiryMs = 20;

        // ---- run layout -----------------------------------------------------
        public string OutputDir;
        public string RunLabel = "fake-session";
        public bool ResetStores = true;
        public bool SeedFixtureMemories = true;
        public bool UseBuiltInFakePlan = true;

        /// <summary>The default deterministic fake configuration.</summary>
        public static PlaygroundConfig Defaults() => new PlaygroundConfig();

        /// <summary>
        /// Stable run id used in results: no timestamps, so two runs can be compared byte-for-byte.
        /// The settings hash keeps different comparison configurations from overwriting one output
        /// directory while leaving identical settings byte-identical.
        /// </summary>
        public string RunId => RunLabel + "-" + Seed.ToString(CultureInfo.InvariantCulture)
            + "-" + ScenarioId + "-" + Seasons.ToString(CultureInfo.InvariantCulture)
            + "-" + (ActionMode == PlaygroundActionMode.CheckOnly ? "check" : "execute")
            + "-" + SettingsHash();

        private string SettingsHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(ToJson()));
                var sb = new StringBuilder(8);
                for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Relevant configuration, secret-free and stable for the results header.</summary>
        public string ToJson()
        {
            return PlaygroundJson.Obj(
                "schema_version", PlaygroundJson.Str(SchemaVersion),
                "seed", PlaygroundJson.Num(Seed),
                "game_id", PlaygroundJson.Str(GameId),
                "scenario", PlaygroundJson.Str(ScenarioId),
                "seasons", PlaygroundJson.Num(Seasons),
                "provider", PlaygroundJson.Str(Provider),
                "fake_provider", PlaygroundJson.Bool(IsFakeProvider),
                "model", PlaygroundJson.Str(Model),
                "action_mode", PlaygroundJson.Str(ActionMode == PlaygroundActionMode.CheckOnly ? "check" : "execute"),
                "check_before_execute", PlaygroundJson.Bool(CheckBeforeExecute),
                "enable_attack", PlaygroundJson.Bool(EnableAttack),
                "enable_four_inquiry_tools", "true",
                "max_requests_per_turn", PlaygroundJson.Num(MaxRequestsPerTurn),
                "max_actions_per_turn", PlaygroundJson.Num(MaxActionsPerTurn),
                "max_invalid_per_turn", PlaygroundJson.Num(MaxInvalidPerTurn),
                "max_inquiry_per_turn", PlaygroundJson.Num(MaxInquiryPerTurn),
                "max_requests_per_session", PlaygroundJson.Num(MaxRequestsPerSession),
                "max_actions_per_session", PlaygroundJson.Num(MaxActionsPerSession),
                "max_inquiry_per_session", PlaygroundJson.Num(MaxInquiryPerSession),
                "max_wall_ms_per_turn", PlaygroundJson.Num(MaxWallMillisPerTurn),
                "max_wall_ms_per_session", PlaygroundJson.Num(MaxWallMillisPerSession),
                "request_timeout_ms", PlaygroundJson.Num(RequestTimeoutMs),
                "max_output_tokens", PlaygroundJson.Num(MaxOutputTokens),
                "input_token_bound", PlaygroundJson.Num(InputTokenBound),
                "max_retained_inquiry_results", PlaygroundJson.Num(MaxRetainedInquiryResults),
                "inquiry_max_response_chars", PlaygroundJson.Num(InquiryMaxResponseChars),
                "max_note_chars", PlaygroundJson.Num(MaxNoteChars),
                "max_trace_response_chars", PlaygroundJson.Num(MaxTraceResponseChars),
                "simulated_request_ms", PlaygroundJson.Num(SimulatedRequestMs),
                "simulated_inquiry_ms", PlaygroundJson.Num(SimulatedInquiryMs),
                "seed_fixture_memories", PlaygroundJson.Bool(SeedFixtureMemories));
        }

        public bool IsFakeProvider
            => string.Equals(Provider, "fake", StringComparison.OrdinalIgnoreCase);

        public void Validate()
        {
            if (Seed < 0) throw new ArgumentOutOfRangeException(nameof(Seed), "seed must be non-negative");
            if (Seasons < 0) throw new ArgumentOutOfRangeException(nameof(Seasons), "seasons must be non-negative");
            if (string.IsNullOrWhiteSpace(GameId))
                throw new ArgumentException("a game id is required", nameof(GameId));
            if (MaxRequestsPerTurn < 1) throw new ArgumentOutOfRangeException(nameof(MaxRequestsPerTurn));
            if (MaxActionsPerTurn < 0) throw new ArgumentOutOfRangeException(nameof(MaxActionsPerTurn));
            if (MaxInvalidPerTurn < 0) throw new ArgumentOutOfRangeException(nameof(MaxInvalidPerTurn));
            if (MaxInquiryPerTurn < 0) throw new ArgumentOutOfRangeException(nameof(MaxInquiryPerTurn));
            if (MaxRetainedInquiryResults < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxRetainedInquiryResults));
            if (InquiryMaxResponseChars < InvestigationOptions.MinimumResponseChars)
                throw new ArgumentOutOfRangeException(nameof(InquiryMaxResponseChars),
                    "the inquiry response cap must be at least " + InvestigationOptions.MinimumResponseChars);
            if (MaxNoteChars < 1) throw new ArgumentOutOfRangeException(nameof(MaxNoteChars));
            if (MaxOutputTokens < 1) throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens));
            if (InputTokenBound < 1) throw new ArgumentOutOfRangeException(nameof(InputTokenBound));
            if (RequestTimeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(RequestTimeoutMs));
            if (SimulatedRequestMs < 0 || SimulatedInquiryMs < 0)
                throw new ArgumentException("simulated elapsed time cannot be negative");
        }

        /// <summary>
        /// Builds the provider boundary. The real provider is deliberately opt-in twice: a local
        /// operator must pass <c>--allow-real-provider</c> and have a key in the environment. The
        /// default path constructs only the scripted fake transport.
        /// </summary>
        public ILlmTransport CreateTransport()
        {
            if (IsFakeProvider) return new FakeLlmTransport();
            if (!AllowRealProvider)
                throw new PlaygroundRefusal(
                    "real provider is disabled; pass --allow-real-provider and use a local key to opt in");
            if (string.IsNullOrWhiteSpace(BaseUrl))
                throw new PlaygroundRefusal("real provider requires a local base URL");

            // Load() applies the documented environment overrides (including the key); it never
            // prints the key. Explicit CLI values win afterwards.
            LlmConfig config = LlmConfig.Load();
            config.Provider = Provider;
            config.BaseUrl = BaseUrl;
            config.Model = Model;
            config.MaxOutputTokens = MaxOutputTokens;
            config.RequestTimeoutMs = RequestTimeoutMs;
            config.MaxRequestsPerTurn = MaxRequestsPerTurn;
            config.MaxActionsPerTurn = MaxActionsPerTurn;
            config.MaxInvalidPerTurn = MaxInvalidPerTurn;
            if (!config.HasApiKey)
                throw new PlaygroundRefusal(
                    "real provider requires LUANSHI_LLM_API_KEY in the local environment");
            return new OpenAiChatTransport(config);
        }

        /// <summary>
        /// Parses <c>run</c> options. Unknown flags are errors rather than silently ignored, so a
        /// typo cannot change a comparison without telling the operator.
        /// </summary>
        public static PlaygroundConfig ParseArgs(string[] args, out string error)
        {
            error = null;
            var config = new PlaygroundConfig();
            for (int i = 0; args != null && i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--seed") { if (!TakeInt(args, ref i, out config.Seed)) { error = "--seed needs an integer"; return null; } }
                else if (arg == "--seasons") { if (!TakeInt(args, ref i, out config.Seasons)) { error = "--seasons needs an integer"; return null; } }
                else if (arg == "--out") { if (!TakeString(args, ref i, out config.OutputDir)) { error = "--out needs a path"; return null; } }
                else if (arg == "--run-label") { if (!TakeString(args, ref i, out config.RunLabel)) { error = "--run-label needs text"; return null; } }
                else if (arg == "--game-id") { if (!TakeString(args, ref i, out config.GameId)) { error = "--game-id needs text"; return null; } }
                else if (arg == "--mode")
                {
                    if (!TakeString(args, ref i, out string mode)) { error = "--mode needs execute|check"; return null; }
                    if (mode == "check") config.ActionMode = PlaygroundActionMode.CheckOnly;
                    else if (mode == "execute") config.ActionMode = PlaygroundActionMode.Execute;
                    else { error = "--mode must be execute or check"; return null; }
                }
                else if (arg == "--no-preflight-check") config.CheckBeforeExecute = false;
                else if (arg == "--enable-attack") config.EnableAttack = true;
                else if (arg == "--provider") { if (!TakeString(args, ref i, out config.Provider)) { error = "--provider needs a name"; return null; } }
                else if (arg == "--model") { if (!TakeString(args, ref i, out config.Model)) { error = "--model needs text"; return null; } }
                else if (arg == "--base-url") { if (!TakeString(args, ref i, out config.BaseUrl)) { error = "--base-url needs text"; return null; } }
                else if (arg == "--allow-real-provider") config.AllowRealProvider = true;
                else if (arg == "--no-fixture-memories") config.SeedFixtureMemories = false;
                else if (arg == "--keep-stores") config.ResetStores = false;
                else { error = "unknown option '" + arg + "'"; return null; }
            }

            try { config.Validate(); }
            catch (Exception ex) { error = ex.Message; return null; }
            return config;
        }

        private static bool TakeInt(string[] args, ref int i, out int value)
        {
            value = 0;
            if (i + 1 >= args.Length) return false;
            if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
            i++;
            return true;
        }

        private static bool TakeString(string[] args, ref int i, out string value)
        {
            value = null;
            if (i + 1 >= args.Length || string.IsNullOrEmpty(args[i + 1])) return false;
            value = args[i + 1];
            i++;
            return true;
        }
    }

    /// <summary>A bounded, user-facing refusal. Never carries provider credentials or bodies.</summary>
    internal sealed class PlaygroundRefusal : Exception
    {
        public PlaygroundRefusal(string message) : base(message) { }
    }
}
