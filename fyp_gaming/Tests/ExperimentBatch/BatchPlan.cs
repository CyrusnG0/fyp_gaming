using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>One arm of one matched pair: a fully resolved single-seed manifest plus its identity.</summary>
    public sealed class MatchSpec
    {
        public string MatchId;
        public string PairId;
        public string Arm;                // "control" | "treatment"
        public int Seed;
        public BatchManifest Resolved;    // single seed, variable applied
        public string SettingsHash;       // sha256 of the canonical flat settings
        public SortedDictionary<string, string> Flat;
    }

    public sealed class PairCheck
    {
        public string PairId;
        public string Variable;
        public bool Ok;
        public List<string> Differences = new List<string>();
        public List<string> Problems = new List<string>();
    }

    /// <summary>Validation, arm resolution and the matched-pair settings check.</summary>
    public static class BatchPlan
    {
        public static readonly string[] FaultKinds = { "provider_error", "missing_usage", "length_truncated", "empty_reply" };
        private static readonly Regex SafeName = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$");
        private static readonly Regex CommitHash = new Regex("^[0-9a-f]{7,40}$");

        public static List<string> Validate(BatchManifest m)
        {
            var e = new List<string>();
            if (m == null) { e.Add("no manifest"); return e; }
            if (m.Schema != BatchManifest.SchemaId) e.Add("schema must be " + BatchManifest.SchemaId);
            if (m.BatchId == null || !SafeName.IsMatch(m.BatchId)) e.Add("batch_id must be 1-64 of [A-Za-z0-9._-]");
            if (m.PinnedCommit == null || !CommitHash.IsMatch(m.PinnedCommit)) e.Add("pinned_commit must be 7-40 lowercase hex characters");
            if (m.Seasons < 1 || m.Seasons > 40) e.Add("seasons must be 1..40");

            CompactScenario scenario = null;
            if (!CompactScenario.TryGet(m.Scenario ?? "", out scenario)) e.Add("unknown scenario '" + m.Scenario + "'");
            if (!LuanShi.Engine.BalanceProfile.TryGet(m.BalanceProfile, out _) || string.IsNullOrEmpty(m.BalanceProfile))
                e.Add("unknown or missing balance_profile '" + m.BalanceProfile + "'");
            if (scenario != null) CheckFactions(m, scenario, e);

            ProviderSettings p = m.Provider;
            if (p == null || p.Kind != "fake") e.Add("provider.kind must be 'fake' (this offline tool never calls a provider)");
            if (p == null || string.IsNullOrWhiteSpace(p.Model)) e.Add("provider.model is required");
            if (p != null && p.Faults != null)
                foreach (FaultSpec f in p.Faults)
                {
                    if (f == null || f.Seat == null || !m.Factions.ContainsKey(f.Seat)) e.Add("fault names a seat outside factions");
                    else if (f.SeatRequest < 1) e.Add("fault seat_request must be >= 1");
                    if (f != null && Array.IndexOf(FaultKinds, f.Kind) < 0) e.Add("unknown fault kind '" + f.Kind + "'");
                }

            FeatureFlags fl = m.Features;
            if (fl == null) e.Add("features are required");
            else
            {
                if (m.Experiment != null && m.Experiment.Variable == "spatial_context" && !fl.VisibleMarchRoutes)
                    e.Add("a spatial_context pair needs features.visible_march_routes=true in both arms (spatial context would otherwise enforce it in one arm only)");
                if (m.Spatial == null || (m.Spatial.Level != "standard" && m.Spatial.Level != "rich")) e.Add("spatial.level must be 'standard' or 'rich'");
                if (fl.RichProfile && !fl.Deliberation) e.Add("features.rich_profile requires features.deliberation");
                if (fl.ManualLookup && fl.Deliberation) e.Add("features.manual_lookup is the legacy path and conflicts with deliberation");
            }
            if (m.Personas == null) e.Add("personas settings are required");
            else
            {
                if (!m.Personas.Enabled && m.Personas.BySeat != null && m.Personas.BySeat.Count > 0) e.Add("personas.by_seat set while personas are disabled");
                if (m.Personas.BySeat != null)
                    foreach (KeyValuePair<string, string> kv in m.Personas.BySeat)
                        if (!m.Factions.ContainsKey(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) e.Add("personas.by_seat has an unknown seat or empty persona");
            }

            CheckCeilings(m, e);
            if (m.BatchCeilings != null && m.BatchCeilings.UnknownUsagePolicy != "stop_batch" && m.BatchCeilings.UnknownUsagePolicy != "continue_on_unknown")
                e.Add("batch_ceilings.unknown_usage_policy must be 'stop_batch' or 'continue_on_unknown'");
            if (e.Count == 0)
                foreach (string code in BatchWorld.Config(m).ValidateExperiment()) e.Add("the production runner would refuse this configuration: " + code);
            if (e.Count == 0 && m.Experiment != null && FeatureProperty(m.Experiment.Variable) != null)
                foreach (bool value in new[] { m.Experiment.Control, m.Experiment.Treatment })
                {
                    BatchManifest arm = Clone(m);
                    FeatureProperty(m.Experiment.Variable).SetValue(arm.Features, value);
                    foreach (string code in BatchWorld.Config(arm).ValidateExperiment())
                        e.Add("the production runner would refuse the " + (value == m.Experiment.Control ? "control" : "treatment") + " arm: " + code);
                }
            if (m.Seeds == null || m.Seeds.Count == 0) e.Add("seeds must list at least one seed");
            else if (new HashSet<int>(m.Seeds).Count != m.Seeds.Count) e.Add("seeds must be distinct");
            CheckExperiment(m, e);
            if (m.Seeds != null && m.BatchCeilings != null && m.BatchCeilings.MaxMatches.HasValue
                && m.BatchCeilings.MaxMatches.Value < m.Seeds.Count * 2)
                e.Add("batch_ceilings.max_matches is below the planned " + m.Seeds.Count * 2 + " matches");
            return e;
        }

        private static void CheckFactions(BatchManifest m, CompactScenario scenario, List<string> e)
        {
            if (scenario.Factions.Count == 0 || m.FirstMover != scenario.Factions[0].SeatId)
                e.Add("first_mover '" + m.FirstMover + "' is not the first seat of scenario " + scenario.ScenarioId);
            if (m.Factions == null || m.Factions.Count != scenario.Factions.Count)
            {
                e.Add("factions must list every scenario seat");
                return;
            }
            foreach (ScenarioFaction f in scenario.Factions)
                if (!m.Factions.TryGetValue(f.SeatId, out string name) || name != f.Name)
                    e.Add("faction assignment for " + f.SeatId + " does not match the scenario (" + f.Name + ")");
        }

        private static void CheckCeilings(BatchManifest m, List<string> e)
        {
            MatchCeilings c = m.Ceilings ?? new MatchCeilings();
            Need(e, "ceilings.max_requests_per_turn", c.MaxRequestsPerTurn);
            Need(e, "ceilings.max_total_requests", c.MaxTotalRequests);
            Need(e, "ceilings.max_actions_per_turn", c.MaxActionsPerTurn);
            Need(e, "ceilings.max_accepted_actions_per_session", c.MaxAcceptedActionsPerSession);
            Need(e, "ceilings.max_inquiries_per_turn", c.MaxInquiriesPerTurn);
            Need(e, "ceilings.max_inquiries_per_session", c.MaxInquiriesPerSession);
            Need(e, "ceilings.max_turn_seconds", c.MaxTurnSeconds);
            Need(e, "ceilings.max_session_seconds", c.MaxSessionSeconds);
            Need(e, "ceilings.request_timeout_ms", c.RequestTimeoutMs);
            Need(e, "ceilings.max_output_tokens", c.MaxOutputTokens);
            Need(e, "ceilings.max_session_input_tokens", c.MaxSessionInputTokens);
            Need(e, "ceilings.max_session_output_tokens", c.MaxSessionOutputTokens);
            Need(e, "ceilings.max_prompt_chars", c.MaxPromptChars);
            BatchCeilings b = m.BatchCeilings ?? new BatchCeilings();
            Need(e, "batch_ceilings.max_matches", b.MaxMatches);
            Need(e, "batch_ceilings.max_total_requests", b.MaxTotalRequests);
            Need(e, "batch_ceilings.max_total_input_tokens", b.MaxTotalInputTokens);
            Need(e, "batch_ceilings.max_total_output_tokens", b.MaxTotalOutputTokens);
            Need(e, "batch_ceilings.max_batch_wall_seconds", b.MaxBatchWallSeconds);
            Need(e, "batch_ceilings.max_match_wall_seconds", b.MaxMatchWallSeconds);
        }

        private static void Need(List<string> e, string name, long? value)
        {
            if (!value.HasValue || value.Value <= 0) e.Add(name + " must be set to a positive value");
        }

        private static void CheckExperiment(BatchManifest m, List<string> e)
        {
            ExperimentSpec x = m.Experiment;
            if (x == null || string.IsNullOrEmpty(x.Variable) || FeatureProperty(x.Variable) == null)
            {
                e.Add("experiment.variable must name a features.* flag");
                return;
            }
            if (x.Control == x.Treatment) e.Add("experiment.control and experiment.treatment must differ");
        }

        private static System.Reflection.PropertyInfo FeatureProperty(string jsonName)
        {
            foreach (System.Reflection.PropertyInfo p in typeof(FeatureFlags).GetProperties())
            {
                var attr = (System.Text.Json.Serialization.JsonPropertyNameAttribute)Attribute.GetCustomAttribute(
                    p, typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute));
                if (attr != null && attr.Name == jsonName) return p;
            }
            return null;
        }

        /// <summary>Two arms per seed: control then treatment. Each is a complete, single-seed manifest.</summary>
        public static List<MatchSpec> Build(BatchManifest manifest)
        {
            var specs = new List<MatchSpec>();
            foreach (int seed in manifest.Seeds)
                foreach (string arm in new[] { "control", "treatment" })
                {
                    BatchManifest resolved = Clone(manifest);
                    resolved.Seeds = new List<int> { seed };
                    FeatureProperty(manifest.Experiment.Variable).SetValue(resolved.Features,
                        arm == "control" ? manifest.Experiment.Control : manifest.Experiment.Treatment);
                    var spec = new MatchSpec
                    {
                        MatchId = "seed" + seed + "-" + arm, PairId = "seed" + seed, Arm = arm, Seed = seed,
                        Resolved = resolved, Flat = FlatSettings(resolved),
                    };
                    spec.SettingsHash = Sha256(Canonical(spec.Flat));
                    specs.Add(spec);
                }
            return specs;
        }

        public static BatchManifest Clone(BatchManifest m)
            => JsonSerializer.Deserialize<BatchManifest>(JsonSerializer.Serialize(m));

        /// <summary>
        /// Every per-arm setting as path -> value, read from the serialized manifest so a newly added
        /// field cannot be forgotten. Batch-scoped sections (experiment, batch ceilings) are excluded.
        /// </summary>
        public static SortedDictionary<string, string> FlatSettings(BatchManifest resolved)
        {
            var flat = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(resolved)))
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    if (p.Name != "experiment" && p.Name != "batch_ceilings") Flatten(p.Value, p.Name, flat);
            return flat;
        }

        private static void Flatten(JsonElement e, string path, SortedDictionary<string, string> into)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    int n = 0;
                    foreach (JsonProperty p in e.EnumerateObject()) { Flatten(p.Value, path + "." + p.Name, into); n++; }
                    if (n == 0) into[path] = "{}";
                    break;
                case JsonValueKind.Array:
                    int i = 0;
                    foreach (JsonElement item in e.EnumerateArray()) Flatten(item, path + "[" + i++ + "]", into);
                    if (i == 0) into[path] = "[]";
                    break;
                default:
                    into[path] = e.GetRawText();
                    break;
            }
        }

        public static PairCheck CheckPair(MatchSpec control, MatchSpec treatment, ExperimentSpec declared)
        {
            var check = new PairCheck { PairId = control.PairId, Variable = "features." + declared.Variable };
            if (control.PairId != treatment.PairId || control.Seed != treatment.Seed)
                check.Problems.Add("arms belong to different pairs or seeds");
            var keys = new SortedSet<string>(control.Flat.Keys, StringComparer.Ordinal);
            keys.UnionWith(treatment.Flat.Keys);
            foreach (string key in keys)
            {
                control.Flat.TryGetValue(key, out string a);
                treatment.Flat.TryGetValue(key, out string b);
                if (a != b) check.Differences.Add(key + ": " + (a ?? "<absent>") + " -> " + (b ?? "<absent>"));
            }
            bool onlyDeclared = check.Differences.Count == 1 && check.Differences[0].StartsWith(check.Variable + ":", StringComparison.Ordinal);
            if (!onlyDeclared) check.Problems.Add("arms differ in " + check.Differences.Count + " setting(s); only " + check.Variable + " may differ");
            check.Ok = check.Problems.Count == 0;
            return check;
        }

        public static string Canonical(SortedDictionary<string, string> flat)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in flat) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text ?? ""));

        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
