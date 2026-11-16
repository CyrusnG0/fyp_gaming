using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>Shared serializer settings. Nulls are always written: "unknown" is a null value,
    /// never an omitted field and never zero.</summary>
    internal static class CsJson
    {
        public static readonly JsonSerializerOptions Indented = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static readonly JsonSerializerOptions Compact = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private static readonly JsonSerializerOptions Read = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };

        public static T Parse<T>(string json, out string error) where T : class
        {
            error = null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, Read);
            }
            catch (JsonException e)
            {
                error = "JSON rejected: " + e.Message;
                return null;
            }
        }
    }

    /// <summary>Whole-run ceilings. Every field is required and must be positive.</summary>
    public sealed class CsRunCeilings
    {
        [JsonPropertyName("max_scenarios")] public int? MaxScenarios { get; set; }
        [JsonPropertyName("max_run_requests")] public int? MaxRunRequests { get; set; }
        [JsonPropertyName("max_run_wall_seconds")] public int? MaxRunWallSeconds { get; set; }
        [JsonPropertyName("max_scenario_requests")] public int? MaxScenarioRequests { get; set; }
        [JsonPropertyName("max_scenario_wall_seconds")] public int? MaxScenarioWallSeconds { get; set; }
        [JsonPropertyName("request_timeout_ms")] public int? RequestTimeoutMs { get; set; }
        /// <summary>Only "report" is implemented: an unknown total is reported, never treated as zero.</summary>
        [JsonPropertyName("unknown_usage_policy")] public string UnknownUsagePolicy { get; set; }
    }

    /// <summary>Optional per-scenario overrides. A missing field falls back to the run ceiling.</summary>
    public sealed class CsScenarioCeilings
    {
        [JsonPropertyName("max_requests")] public int? MaxRequests { get; set; }
        [JsonPropertyName("max_wall_seconds")] public int? MaxWallSeconds { get; set; }
        [JsonPropertyName("request_timeout_ms")] public int? RequestTimeoutMs { get; set; }
    }

    public sealed class CsProviderManifest
    {
        /// <summary>"fake" is the only accepted kind in this task; live kinds are refused.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; }
        [JsonPropertyName("model")] public string Model { get; set; }
    }

    public sealed class CsManifestScenario
    {
        [JsonPropertyName("scenario_id")] public string ScenarioId { get; set; }
        [JsonPropertyName("seed")] public int? Seed { get; set; }
        [JsonPropertyName("ceilings")] public CsScenarioCeilings Ceilings { get; set; }
    }

    /// <summary>
    /// Machine-readable controlled-scenario manifest. It carries no credential (the loader scans
    /// keys and values before deserialization), rejects unknown fields so a typo cannot silently
    /// change a run, and refuses every ceiling that is missing or non-positive.
    /// </summary>
    public sealed class CsManifest
    {
        public const string SchemaId = "luanshi-controlled-scenarios/1";

        [JsonPropertyName("schema")] public string Schema { get; set; }
        [JsonPropertyName("provider")] public CsProviderManifest Provider { get; set; }
        [JsonPropertyName("run_ceilings")] public CsRunCeilings RunCeilings { get; set; }
        [JsonPropertyName("scenarios")] public List<CsManifestScenario> Scenarios { get; set; }
            = new List<CsManifestScenario>();

        private static readonly Regex SecretKeyName = new Regex(
            "^(api_?key|apikey|secret|password|passwd|authorization|bearer|credentials?|access_?token|auth_?token)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SecretValue = new Regex(
            @"(sk-[A-Za-z0-9_\-]{12,}|Bearer\s+[A-Za-z0-9._\-]{12,})", RegexOptions.CultureInvariant);

        public static CsManifest Parse(string json, out List<string> errors)
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
            if (errors.Count > 0) return null;   // never deserialize or echo a manifest holding a secret
            CsManifest manifest = CsJson.Parse<CsManifest>(json, out string error);
            if (manifest == null) { errors.Add(error ?? "manifest is empty"); return null; }
            errors.AddRange(Validate(manifest));
            return errors.Count == 0 ? manifest : null;
        }

        /// <summary>Returns every reason the manifest cannot bound and reproduce a run.</summary>
        public static List<string> Validate(CsManifest manifest)
        {
            var errors = new List<string>();
            if (manifest == null) { errors.Add("manifest is empty"); return errors; }
            if (manifest.Schema != SchemaId)
                errors.Add("schema must be " + SchemaId);
            if (manifest.Provider == null) errors.Add("provider block is required");
            else
            {
                if (string.IsNullOrWhiteSpace(manifest.Provider.Kind))
                    errors.Add("provider.kind is required");
                else if (manifest.Provider.Kind != "fake")
                    errors.Add("provider.kind '" + manifest.Provider.Kind
                        + "' is refused: this build accepts only 'fake' and makes no live call");
                if (string.IsNullOrWhiteSpace(manifest.Provider.Model))
                    errors.Add("provider.model is required");
            }

            CsRunCeilings c = manifest.RunCeilings;
            if (c == null) errors.Add("run_ceilings are required");
            else
            {
                Positive(errors, "run_ceilings.max_scenarios", c.MaxScenarios);
                Positive(errors, "run_ceilings.max_run_requests", c.MaxRunRequests);
                Positive(errors, "run_ceilings.max_run_wall_seconds", c.MaxRunWallSeconds);
                Positive(errors, "run_ceilings.max_scenario_requests", c.MaxScenarioRequests);
                Positive(errors, "run_ceilings.max_scenario_wall_seconds", c.MaxScenarioWallSeconds);
                Positive(errors, "run_ceilings.request_timeout_ms", c.RequestTimeoutMs);
                if (c.UnknownUsagePolicy != "report")
                    errors.Add("run_ceilings.unknown_usage_policy must be 'report' (unknown usage is never counted as zero)");
            }

            if (manifest.Scenarios == null || manifest.Scenarios.Count == 0)
                errors.Add("at least one scenario is required");
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < manifest.Scenarios.Count; i++)
                {
                    CsManifestScenario s = manifest.Scenarios[i];
                    string where = "scenarios[" + i + "]";
                    if (s == null) { errors.Add(where + " is empty"); continue; }
                    if (string.IsNullOrWhiteSpace(s.ScenarioId)) { errors.Add(where + ".scenario_id is required"); continue; }
                    if (!seen.Add(s.ScenarioId)) errors.Add("duplicate scenario " + s.ScenarioId);
                    if (!CsScenarios.TryGet(s.ScenarioId, null, out _))
                        errors.Add("unknown scenario " + s.ScenarioId);
                    if (!s.Seed.HasValue) errors.Add(where + ".seed is required");
                    if (s.Ceilings != null)
                    {
                        OptionalPositive(errors, where + ".ceilings.max_requests", s.Ceilings.MaxRequests);
                        OptionalPositive(errors, where + ".ceilings.max_wall_seconds", s.Ceilings.MaxWallSeconds);
                        OptionalPositive(errors, where + ".ceilings.request_timeout_ms", s.Ceilings.RequestTimeoutMs);
                    }
                }
            }
            return errors;
        }

        /// <summary>Effective per-scenario request ceiling, or 0 when the manifest is invalid.</summary>
        public int ScenarioRequests(CsManifestScenario s)
            => s?.Ceilings?.MaxRequests ?? RunCeilings?.MaxScenarioRequests ?? 0;

        /// <summary>Effective per-scenario wall ceiling in seconds, or 0 when invalid.</summary>
        public int ScenarioWallSeconds(CsManifestScenario s)
            => s?.Ceilings?.MaxWallSeconds ?? RunCeilings?.MaxScenarioWallSeconds ?? 0;

        /// <summary>Effective per-request timeout in milliseconds, or 0 when invalid.</summary>
        public int ScenarioRequestTimeoutMs(CsManifestScenario s)
            => s?.Ceilings?.RequestTimeoutMs ?? RunCeilings?.RequestTimeoutMs ?? 0;

        private static void Positive(List<string> errors, string name, int? value)
        {
            if (!value.HasValue || value.Value <= 0) errors.Add(name + " must be a positive integer");
        }

        private static void OptionalPositive(List<string> errors, string name, int? value)
        {
            if (value.HasValue && value.Value <= 0) errors.Add(name + " must be positive when present");
        }

        private static void ScanForCredentials(JsonElement element, string path, List<string> errors)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (SecretKeyName.IsMatch(property.Name))
                            errors.Add("credential-like key at " + path + "." + property.Name
                                + " (a manifest must hold no credential)");
                        ScanForCredentials(property.Value, path + "." + property.Name, errors);
                    }
                    break;
                case JsonValueKind.Array:
                    int i = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                        ScanForCredentials(item, path + "[" + i++ + "]", errors);
                    break;
                case JsonValueKind.String:
                    if (SecretValue.IsMatch(element.GetString() ?? ""))
                        errors.Add("credential-like value at " + path + " (value not shown)");
                    break;
            }
        }
    }

    /// <summary>
    /// Effective production configuration for one scenario and its reproducibility identity. Only
    /// explicit ceilings are set; the API key is never read (a new LlmConfig, not LlmConfig.Load).
    /// </summary>
    internal static class CsSettings
    {
        public static LlmConfig Build(CsScenarioSpec spec, string model)
            => Build(spec, model, null, null);

        /// <summary>
        /// Builds the config with EFFECTIVE ceilings (already clamped to the whole-run budget) so
        /// settings.json and the production runner agree with the wrapper's backstop. Passing null
        /// keeps the scenario's declared ceiling, which is what Replay and direct test calls use.
        /// </summary>
        public static LlmConfig Build(CsScenarioSpec spec, string model, int? effectiveMaxRequests,
            int? effectiveWallSeconds)
        {
            return new LlmConfig
            {
                Provider = "fake",
                Model = string.IsNullOrWhiteSpace(model) ? "fake-model" : model,
                EnableAttack = spec.EnableAttack,
                EnableResearch = spec.EnableResearch,
                EnableManualLookup = spec.EnableManualLookup,
                EnableDeliberation = spec.EnableDeliberation,
                MaxRequestsPerTurn = 6,
                MaxActionsPerTurn = 5,
                MaxInvalidPerTurn = 2,
                MaxTurnSeconds = effectiveWallSeconds ?? spec.MaxWallSeconds,
                RequestTimeoutMs = spec.RequestTimeoutMs,
                MaxOutputTokens = 400,
                MaxTotalRequests = effectiveMaxRequests ?? spec.MaxRequests,
                MaxInquiriesPerTurn = 4,
                MaxInquiriesPerSession = 20,
            };
        }

        public static string Hash(CsScenarioSpec spec, LlmConfig config)
        {
            var canonical = new Dictionary<string, object>
            {
                { "scenario_id", spec.ScenarioId },
                { "provider", "fake" },
                { "model", config.Model },
                { "attack", config.EnableAttack },
                { "research", config.EnableResearch },
                { "manual_lookup", config.EnableManualLookup },
                { "deliberation", config.EnableDeliberation },
                { "max_requests_per_turn", config.MaxRequestsPerTurn },
                { "max_actions_per_turn", config.MaxActionsPerTurn },
                { "max_invalid_per_turn", config.MaxInvalidPerTurn },
                { "max_turn_seconds", config.MaxTurnSeconds },
                { "request_timeout_ms", config.RequestTimeoutMs },
                { "max_output_tokens", config.MaxOutputTokens },
                { "max_total_requests", config.MaxTotalRequests },
                { "max_inquiries_per_turn", config.MaxInquiriesPerTurn },
                { "max_inquiries_per_session", config.MaxInquiriesPerSession },
                { "scenario_max_requests", spec.MaxRequests },
                { "scenario_wall_seconds", spec.MaxWallSeconds },
                { "prompt_limit", PromptLimit(config) },
            };
            return CsWorld.Sha256(JsonSerializer.Serialize(canonical, CsJson.Compact));
        }

        public static int PromptLimit(LlmConfig config)
            => config != null && config.ExperimentActive ? config.EffectiveMaxPromptChars : PromptBuilder.MaxPromptChars;
    }
}
