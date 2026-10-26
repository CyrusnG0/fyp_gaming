using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Local LLM configuration. The API key is kept in a private field and is never part of
    /// ToString/Redacted, warnings or exceptions. <see cref="LoadFromFile"/> uses the B1
    /// StrictJson parser; a missing or malformed file degrades to defaults + environment values.
    /// </summary>
    public sealed class LlmConfig
    {
        public string Provider = "deepseek";
        public string BaseUrl = "https://api.deepseek.com";
        public string Model = "deepseek-flash";
        public string ApiKeyEnv = "LUANSHI_LLM_API_KEY";
        public bool JsonMode = true;

        public int MaxRequestsPerTurn = 6;
        public int MaxActionsPerTurn = 5;
        public int MaxInvalidPerTurn = 2;
        public int MaxTurnSeconds = 60;
        public int RequestTimeoutMs = 25000;
        public int MaxOutputTokens = 400;

        /// <summary>
        /// Whole-session request cap (default 60, the user-approved demo budget). This is the only
        /// spend bound when prices are not configured.
        /// </summary>
        public int MaxTotalRequests = 60;

        public double? UsdPerMTokIn;
        public double? UsdPerMTokOut;

        /// <summary>
        /// Optional hard dollar cap. When set, both per-million-token prices are required and a
        /// response with uncounted usage fails the turn closed with "usd budget unknown" instead of
        /// skipping the check. Without prices <see cref="MaxTotalRequests"/> alone bounds spend.
        /// </summary>
        public double? MaxUsd;

        private string _apiKey;

        /// <summary>True when a key came from the file or an environment variable; the key is not exposed.</summary>
        public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);

        /// <summary>Human-readable, secret-free diagnostic from the last load (never file contents).</summary>
        public string LoadWarning { get; private set; }

        public static LlmConfig Load()
        {
            return LoadFromFile(null);
        }

        /// <summary>
        /// File first, then environment override. Recognised snake_case keys are provider, base_url,
        /// model, api_key, api_key_env, json_mode, max_requests_per_turn, max_actions_per_turn,
        /// max_invalid_per_turn, max_turn_seconds, request_timeout_ms, max_output_tokens,
        /// max_total_requests, usd_per_mtok_in, usd_per_mtok_out, max_usd.
        /// </summary>
        public static LlmConfig LoadFromFile(string path)
        {
            var cfg = new LlmConfig();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    string text = File.ReadAllText(path);
                    if (StrictJson.TryParse(text, out JsonNode root, out _) && root is JsonObject obj)
                        cfg.ApplyFile(obj);
                    else
                        cfg.LoadWarning = "config file contains invalid JSON; using defaults";
                }
                catch (Exception)
                {
                    // Do not include exception text: it may contain the path or file contents.
                    cfg.LoadWarning = "config file could not be read; using defaults";
                }
            }

            cfg.ApplyEnvironment();
            return cfg;
        }

        /// <summary>Description safe for logs/UI. It contains only whether a key is present.</summary>
        public string Redacted()
        {
            var sb = new StringBuilder(256);
            sb.Append("provider=").Append(Provider);
            sb.Append("; base_url=").Append(BaseUrl);
            sb.Append("; model=").Append(Model);
            sb.Append("; api_key=").Append(HasApiKey ? "<set>" : "<missing>");
            sb.Append("; api_key_env=").Append(ApiKeyEnv);
            sb.Append("; json_mode=").Append(JsonMode ? "true" : "false");
            sb.Append("; requests/turn=").Append(MaxRequestsPerTurn.ToString(CultureInfo.InvariantCulture));
            sb.Append("; actions/turn=").Append(MaxActionsPerTurn.ToString(CultureInfo.InvariantCulture));
            sb.Append("; invalid/turn=").Append(MaxInvalidPerTurn.ToString(CultureInfo.InvariantCulture));
            sb.Append("; max_turn_seconds=").Append(MaxTurnSeconds.ToString(CultureInfo.InvariantCulture));
            sb.Append("; request_timeout_ms=").Append(RequestTimeoutMs.ToString(CultureInfo.InvariantCulture));
            sb.Append("; max_output_tokens=").Append(MaxOutputTokens.ToString(CultureInfo.InvariantCulture));
            sb.Append("; max_total_requests=").Append(MaxTotalRequests.ToString(CultureInfo.InvariantCulture));
            sb.Append("; usd_per_mtok_in=").Append(Number(UsdPerMTokIn));
            sb.Append("; usd_per_mtok_out=").Append(Number(UsdPerMTokOut));
            sb.Append("; max_usd=").Append(Number(MaxUsd));
            return sb.ToString();
        }

        public override string ToString() => Redacted();

        internal string ApiKeyValue => _apiKey;

        private void ApplyFile(JsonObject obj)
        {
            foreach (var field in obj.Items)
            {
                switch (field.Key)
                {
                    case "provider": Provider = Text(field.Value, Provider); break;
                    case "base_url": BaseUrl = Text(field.Value, BaseUrl); break;
                    case "model": Model = Text(field.Value, Model); break;
                    case "api_key": _apiKey = CleanKey(Text(field.Value, _apiKey)); break;
                    case "api_key_env": ApiKeyEnv = Text(field.Value, ApiKeyEnv); break;
                    case "json_mode": JsonMode = Bool(field.Value, JsonMode); break;
                    case "max_requests_per_turn": MaxRequestsPerTurn = Int(field.Value, MaxRequestsPerTurn); break;
                    case "max_actions_per_turn": MaxActionsPerTurn = Int(field.Value, MaxActionsPerTurn); break;
                    case "max_invalid_per_turn": MaxInvalidPerTurn = Int(field.Value, MaxInvalidPerTurn); break;
                    case "max_turn_seconds": MaxTurnSeconds = Int(field.Value, MaxTurnSeconds); break;
                    case "request_timeout_ms": RequestTimeoutMs = Int(field.Value, RequestTimeoutMs); break;
                    case "max_output_tokens": MaxOutputTokens = Int(field.Value, MaxOutputTokens); break;
                    case "max_total_requests": MaxTotalRequests = Int(field.Value, MaxTotalRequests); break;
                    case "usd_per_mtok_in": UsdPerMTokIn = Number(field.Value, UsdPerMTokIn); break;
                    case "usd_per_mtok_out": UsdPerMTokOut = Number(field.Value, UsdPerMTokOut); break;
                    case "max_usd": MaxUsd = Number(field.Value, MaxUsd); break;
                }
            }
        }

        private void ApplyEnvironment()
        {
            string model = Environment.GetEnvironmentVariable("LUANSHI_LLM_MODEL");
            if (!string.IsNullOrEmpty(model)) Model = model.Trim();

            string baseUrl = Environment.GetEnvironmentVariable("LUANSHI_LLM_BASE_URL");
            if (!string.IsNullOrEmpty(baseUrl)) BaseUrl = baseUrl.Trim();

            string direct = Environment.GetEnvironmentVariable("LUANSHI_LLM_API_KEY");
            string named = string.IsNullOrEmpty(ApiKeyEnv)
                ? null
                : Environment.GetEnvironmentVariable(ApiKeyEnv);
            if (!string.IsNullOrEmpty(direct)) _apiKey = direct.Trim();
            else if (!string.IsNullOrEmpty(named)) _apiKey = named.Trim();
        }

        private static string CleanKey(string value)
            => string.IsNullOrEmpty(value) ? null : value.Trim();

        private static string Text(JsonNode node, string fallback)
        {
            var text = node as JsonString;
            return text == null ? fallback : text.Value;
        }

        private static bool Bool(JsonNode node, bool fallback)
        {
            var value = node as JsonBool;
            return value == null ? fallback : value.Value;
        }

        private static int Int(JsonNode node, int fallback)
        {
            var value = node as JsonInt;
            if (value == null || value.Value < 0 || value.Value > int.MaxValue) return fallback;
            return (int)value.Value;
        }

        /// <summary>
        /// StrictJson intentionally accepts integers only. Prices may therefore be written as an
        /// integer or a decimal string (for example "0.27"); absent/invalid values stay null.
        /// </summary>
        private static double? Number(JsonNode node, double? fallback)
        {
            var integer = node as JsonInt;
            if (integer != null) return integer.Value < 0 ? fallback : (double)integer.Value;
            var text = node as JsonString;
            if (text != null
                && double.TryParse(text.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && value >= 0)
                return value;
            return fallback;
        }

        private static string Number(double? value)
            => value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : "unknown";
    }
}
