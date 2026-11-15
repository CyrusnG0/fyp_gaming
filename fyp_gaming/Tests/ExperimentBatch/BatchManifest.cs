using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LuanShi.ExperimentBatch
{
    /// <summary>Scripted provider settings. Only the offline fake exists in this tool.</summary>
    public sealed class ProviderSettings
    {
        [JsonPropertyName("kind")] public string Kind { get; set; }
        [JsonPropertyName("model")] public string Model { get; set; }
        /// <summary>Injected faults, identical in both arms of a pair (they are settings, not outcomes).</summary>
        [JsonPropertyName("faults")] public List<FaultSpec> Faults { get; set; } = new List<FaultSpec>();
    }

    public sealed class FaultSpec
    {
        [JsonPropertyName("seat")] public string Seat { get; set; }
        /// <summary>1-based index among the requests this seat makes in the match.</summary>
        [JsonPropertyName("seat_request")] public int SeatRequest { get; set; }
        /// <summary>provider_error | missing_usage | length_truncated | empty_reply</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; }
    }

    public sealed class FeatureFlags
    {
        [JsonPropertyName("deliberation")] public bool Deliberation { get; set; }
        [JsonPropertyName("rich_profile")] public bool RichProfile { get; set; }
        [JsonPropertyName("research")] public bool Research { get; set; }
        /// <summary>Production spatial map context: off in the arm that has it false, <c>spatial.level</c> in the other.</summary>
        [JsonPropertyName("spatial_context")] public bool SpatialContext { get; set; }
        [JsonPropertyName("visible_march_routes")] public bool VisibleMarchRoutes { get; set; }
        [JsonPropertyName("attack")] public bool Attack { get; set; }
        [JsonPropertyName("manual_lookup")] public bool ManualLookup { get; set; }
    }

    /// <summary>How the production spatial context is configured when it is on. Identical in both arms of a pair.</summary>
    public sealed class SpatialSettings
    {
        /// <summary>standard | rich (the production presets). The off arm never reads it.</summary>
        [JsonPropertyName("level")] public string Level { get; set; } = "standard";
    }

    public sealed class PersonaSettings
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        /// <summary>seat id -> persona id; applied through LlmConfig.SetPersonaOverride.</summary>
        [JsonPropertyName("by_seat")] public Dictionary<string, string> BySeat { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Per-match ceilings. Every one is explicit: a missing or non-positive value is refused.</summary>
    public sealed class MatchCeilings
    {
        [JsonPropertyName("max_requests_per_turn")] public int? MaxRequestsPerTurn { get; set; }
        [JsonPropertyName("max_total_requests")] public int? MaxTotalRequests { get; set; }
        [JsonPropertyName("max_actions_per_turn")] public int? MaxActionsPerTurn { get; set; }
        [JsonPropertyName("max_accepted_actions_per_session")] public int? MaxAcceptedActionsPerSession { get; set; }
        [JsonPropertyName("max_inquiries_per_turn")] public int? MaxInquiriesPerTurn { get; set; }
        [JsonPropertyName("max_inquiries_per_session")] public int? MaxInquiriesPerSession { get; set; }
        [JsonPropertyName("max_turn_seconds")] public int? MaxTurnSeconds { get; set; }
        [JsonPropertyName("max_session_seconds")] public int? MaxSessionSeconds { get; set; }
        [JsonPropertyName("request_timeout_ms")] public int? RequestTimeoutMs { get; set; }
        [JsonPropertyName("max_output_tokens")] public int? MaxOutputTokens { get; set; }
        [JsonPropertyName("max_session_input_tokens")] public int? MaxSessionInputTokens { get; set; }
        [JsonPropertyName("max_session_output_tokens")] public int? MaxSessionOutputTokens { get; set; }
        [JsonPropertyName("max_prompt_chars")] public int? MaxPromptChars { get; set; }
    }

    /// <summary>Whole-batch ceilings enforced by the batch runner between matches.</summary>
    public sealed class BatchCeilings
    {
        [JsonPropertyName("max_matches")] public int? MaxMatches { get; set; }
        [JsonPropertyName("max_total_requests")] public int? MaxTotalRequests { get; set; }
        [JsonPropertyName("max_total_input_tokens")] public long? MaxTotalInputTokens { get; set; }
        [JsonPropertyName("max_total_output_tokens")] public long? MaxTotalOutputTokens { get; set; }
        [JsonPropertyName("max_batch_wall_seconds")] public int? MaxBatchWallSeconds { get; set; }
        [JsonPropertyName("max_match_wall_seconds")] public int? MaxMatchWallSeconds { get; set; }
        /// <summary>
        /// What to do when a finished match reported no usage (for example after a provider error):
        /// "stop_batch" skips every later match (the token total is unknown, so the ceiling cannot be
        /// proven); "continue_on_unknown" keeps going and enforces the token ceilings against KNOWN usage only.
        /// </summary>
        [JsonPropertyName("unknown_usage_policy")] public string UnknownUsagePolicy { get; set; }
    }

    public sealed class ScenarioSetup
    {
        /// <summary>Opening research points per seat, so a purchase is possible in a two-season match.</summary>
        [JsonPropertyName("research_points")] public int ResearchPoints { get; set; }
    }

    /// <summary>The single declared difference between the two arms of a pair.</summary>
    public sealed class ExperimentSpec
    {
        /// <summary>A FeatureFlags property name as written in the manifest, for example "spatial_context".</summary>
        [JsonPropertyName("variable")] public string Variable { get; set; }
        [JsonPropertyName("control")] public bool Control { get; set; }
        [JsonPropertyName("treatment")] public bool Treatment { get; set; }
    }

    /// <summary>
    /// Machine-readable batch manifest. It carries no credential: the loader refuses any key or value
    /// that looks like one, and refuses unknown fields so a typo cannot silently change a run.
    /// </summary>
    public sealed class BatchManifest
    {
        public const string SchemaId = "luanshi-experiment-batch/1";

        [JsonPropertyName("schema")] public string Schema { get; set; }
        [JsonPropertyName("batch_id")] public string BatchId { get; set; }
        [JsonPropertyName("pinned_commit")] public string PinnedCommit { get; set; }
        [JsonPropertyName("scenario")] public string Scenario { get; set; }
        [JsonPropertyName("balance_profile")] public string BalanceProfile { get; set; }
        [JsonPropertyName("seasons")] public int Seasons { get; set; }
        [JsonPropertyName("first_mover")] public string FirstMover { get; set; }
        /// <summary>seat id -> faction name; checked against the scenario.</summary>
        [JsonPropertyName("factions")] public Dictionary<string, string> Factions { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("personas")] public PersonaSettings Personas { get; set; } = new PersonaSettings();
        [JsonPropertyName("provider")] public ProviderSettings Provider { get; set; } = new ProviderSettings();
        [JsonPropertyName("features")] public FeatureFlags Features { get; set; } = new FeatureFlags();
        [JsonPropertyName("spatial")] public SpatialSettings Spatial { get; set; } = new SpatialSettings();
        [JsonPropertyName("setup")] public ScenarioSetup Setup { get; set; } = new ScenarioSetup();
        [JsonPropertyName("ceilings")] public MatchCeilings Ceilings { get; set; } = new MatchCeilings();
        [JsonPropertyName("batch_ceilings")] public BatchCeilings BatchCeilings { get; set; } = new BatchCeilings();
        [JsonPropertyName("seeds")] public List<int> Seeds { get; set; } = new List<int>();
        [JsonPropertyName("experiment")] public ExperimentSpec Experiment { get; set; } = new ExperimentSpec();

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };

        public static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        // Names that would hold a credential, and value shapes that look like one.
        private static readonly Regex SecretKeyName = new Regex(
            "^(api_?key|apikey|secret|password|passwd|authorization|bearer|credentials?|access_?token|auth_?token)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SecretValue = new Regex(
            @"(sk-[A-Za-z0-9_\-]{12,}|Bearer\s+[A-Za-z0-9._\-]{12,})", RegexOptions.CultureInvariant);

        public static BatchManifest Parse(string json, out List<string> errors)
        {
            errors = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                    ScanForCredentials(doc.RootElement, "$", errors);
            }
            catch (JsonException e)
            {
                errors.Add("manifest is not valid JSON: " + e.Message);
                return null;
            }
            if (errors.Count > 0) return null;   // never deserialize (or echo) a manifest holding a secret
            try
            {
                BatchManifest manifest = JsonSerializer.Deserialize<BatchManifest>(json, ReadOptions);
                if (manifest == null) errors.Add("manifest is empty");
                return manifest;
            }
            catch (JsonException e)
            {
                errors.Add("manifest rejected: " + e.Message);
                return null;
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

        private static void ScanForCredentials(JsonElement element, string path, List<string> errors)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (SecretKeyName.IsMatch(property.Name))
                            errors.Add("credential-like key at " + path + "." + property.Name + " (a manifest must hold no credential)");
                        ScanForCredentials(property.Value, path + "." + property.Name, errors);
                    }
                    break;
                case JsonValueKind.Array:
                    int i = 0;
                    foreach (JsonElement item in element.EnumerateArray()) ScanForCredentials(item, path + "[" + i++ + "]", errors);
                    break;
                case JsonValueKind.String:
                    if (SecretValue.IsMatch(element.GetString() ?? ""))
                        errors.Add("credential-like value at " + path + " (value not shown)");
                    break;
            }
        }
    }
}
