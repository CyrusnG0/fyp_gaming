using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuanShi.ExperimentBatch
{
    public sealed class PairComparison
    {
        [JsonPropertyName("pair_id")] public string PairId { get; set; }
        [JsonPropertyName("settings_check")] public PairCheck SettingsCheck { get; set; }
        /// <summary>True when every system prompt both arms sent is byte-identical. Expected while spatial context is a fake switch.</summary>
        [JsonPropertyName("system_prompts_identical")] public bool? SystemPromptsIdentical { get; set; }
        [JsonPropertyName("final_state_differs")] public bool? FinalStateDiffers { get; set; }
        [JsonPropertyName("both_audits_ok")] public bool BothAuditsOk { get; set; }
    }

    public sealed class BatchResult
    {
        [JsonPropertyName("batch_id")] public string BatchId { get; set; }
        [JsonPropertyName("pinned_commit")] public string PinnedCommit { get; set; }
        [JsonPropertyName("manifest_sha256")] public string ManifestSha256 { get; set; }
        [JsonPropertyName("planned_matches")] public int PlannedMatches { get; set; }
        [JsonPropertyName("matches")] public List<MatchOutcome> Matches { get; set; } = new List<MatchOutcome>();
        [JsonPropertyName("pairs")] public List<PairComparison> Pairs { get; set; } = new List<PairComparison>();
        [JsonPropertyName("requests_total")] public int RequestsTotal { get; set; }
        [JsonPropertyName("input_tokens_total")] public long? InputTokensTotal { get; set; }
        [JsonPropertyName("output_tokens_total")] public long? OutputTokensTotal { get; set; }
        [JsonPropertyName("matches_with_unknown_usage")] public int MatchesWithUnknownUsage { get; set; }
        [JsonPropertyName("batch_ceilings_hit")] public List<string> BatchCeilingsHit { get; set; } = new List<string>();
        [JsonPropertyName("status_counts")] public SortedDictionary<string, int> StatusCounts { get; set; } = new SortedDictionary<string, int>();
        [JsonPropertyName("all_audits_ok")] public bool AllAuditsOk { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } =
            "Offline scripted fake provider. Outcomes are plumbing evidence, never model behaviour. Spatial context is a fake switch, not integrated in production.";
    }

    /// <summary>
    /// Runs every arm of every pair, enforces the batch ceilings BEFORE each match (conservatively, using
    /// the per-match ceilings so a batch can never exceed its bound), keeps every outcome including
    /// failures and skips, and writes one auditable result file.
    /// </summary>
    public static class BatchDriver
    {
        public static BatchResult Run(BatchManifest manifest, string manifestJson, string outRoot)
        {
            List<string> errors = BatchPlan.Validate(manifest);
            if (errors.Count > 0) throw new InvalidOperationException("manifest invalid: " + string.Join("; ", errors));
            string batchDir = Path.Combine(outRoot, manifest.BatchId);
            if (Directory.Exists(batchDir) && Directory.GetFileSystemEntries(batchDir).Length > 0)
                throw new InvalidOperationException("output directory already holds evidence: " + batchDir);
            Directory.CreateDirectory(batchDir);
            File.WriteAllText(Path.Combine(batchDir, "manifest.json"), manifestJson, new UTF8Encoding(false));

            List<MatchSpec> specs = BatchPlan.Build(manifest);
            var result = new BatchResult
            {
                BatchId = manifest.BatchId, PinnedCommit = manifest.PinnedCommit, PlannedMatches = specs.Count,
                ManifestSha256 = BatchPlan.Sha256(manifestJson),
            };
            BatchCeilings ceiling = manifest.BatchCeilings;
            long? inputUsed = 0, outputUsed = 0;
            long knownInput = 0, knownOutput = 0;   // usage the matches DID report; under continue_on_unknown the ceilings use these
            int started = 0;
            var wall = Stopwatch.StartNew();
            string stopped = null;
            foreach (MatchSpec spec in specs)
            {
                string dir = Path.Combine(batchDir, spec.MatchId);
                string reason = stopped ?? BeforeMatch(manifest, ceiling, started, result.RequestsTotal, inputUsed, outputUsed, knownInput, knownOutput, wall);
                if (reason != null)
                {
                    stopped = reason;   // once a ceiling stops the batch, every later match is skipped for that reason
                    if (!result.BatchCeilingsHit.Contains(reason)) result.BatchCeilingsHit.Add(reason);
                    result.Matches.Add(BatchRunner.Skipped(spec, dir, "batch ceiling: " + reason));
                    continue;
                }
                started++;
                MatchOutcome outcome = BatchRunner.RunMatch(spec, dir, manifest.BatchId);
                result.Matches.Add(outcome);
                result.RequestsTotal += outcome.Requests;
                knownInput += outcome.KnownInputTokens; knownOutput += outcome.KnownOutputTokens;
                if (inputUsed.HasValue && outputUsed.HasValue && outcome.InputTokens.HasValue && outcome.OutputTokens.HasValue)
                { inputUsed += outcome.InputTokens; outputUsed += outcome.OutputTokens; }
                else { inputUsed = null; outputUsed = null; result.MatchesWithUnknownUsage++; }
            }
            result.InputTokensTotal = inputUsed;
            result.OutputTokensTotal = outputUsed;
            Finish(result, specs, manifest);
            File.WriteAllText(Path.Combine(batchDir, "batch-result.json"),
                JsonSerializer.Serialize(result, BatchManifest.WriteOptions), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(batchDir, "batch-report.txt"), BatchReport.Text(result), new UTF8Encoding(false));
            return result;
        }

        /// <summary>Returns the ceiling that forbids starting another match, or null.</summary>
        private static string BeforeMatch(BatchManifest m, BatchCeilings c, int started, int requests,
            long? inputUsed, long? outputUsed, long knownInput, long knownOutput, Stopwatch wall)
        {
            if (started >= c.MaxMatches.Value) return "max_matches";
            if (wall.Elapsed.TotalSeconds >= c.MaxBatchWallSeconds.Value) return "max_batch_wall_seconds";
            if (requests + m.Ceilings.MaxTotalRequests.Value > c.MaxTotalRequests.Value) return "max_total_requests";
            // An earlier match with unknown usage makes the running total unknown: stop rather than assume zero.
            if ((!inputUsed.HasValue || !outputUsed.HasValue) && c.UnknownUsagePolicy == "stop_batch") return "usage_unknown";
            if (knownInput + m.Ceilings.MaxSessionInputTokens.Value > c.MaxTotalInputTokens.Value) return "max_total_input_tokens";
            if (knownOutput + m.Ceilings.MaxSessionOutputTokens.Value > c.MaxTotalOutputTokens.Value) return "max_total_output_tokens";
            return null;
        }

        private static void Finish(BatchResult result, List<MatchSpec> specs, BatchManifest manifest)
        {
            bool allOk = true;
            foreach (MatchOutcome o in result.Matches)
            {
                result.StatusCounts.TryGetValue(o.Status, out int n);
                result.StatusCounts[o.Status] = n + 1;
                if (o.Status != "skipped" && (o.Audit == null || !o.Audit.Ok)) allOk = false;
            }
            for (int i = 0; i + 1 < specs.Count; i += 2)
            {
                MatchOutcome c = result.Matches[i], t = result.Matches[i + 1];
                var pair = new PairComparison
                {
                    PairId = specs[i].PairId, SettingsCheck = BatchPlan.CheckPair(specs[i], specs[i + 1], manifest.Experiment),
                };
                bool bothRan = c.Captures.Count > 0 && t.Captures.Count > 0;
                pair.BothAuditsOk = c.Audit != null && c.Audit.Ok && t.Audit != null && t.Audit.Ok;
                if (bothRan) pair.SystemPromptsIdentical = SystemPromptsMatch(c.Captures, t.Captures);
                if (bothRan) pair.FinalStateDiffers = string.Join("|", c.FinalState) != string.Join("|", t.FinalState);
                result.Pairs.Add(pair);
                if (!pair.SettingsCheck.Ok) allOk = false;
            }
            result.AllAuditsOk = allOk;
        }

        private static bool SystemPromptsMatch(List<ExchangeCapture> a, List<ExchangeCapture> b)
        {
            if (a.Count == 0 || b.Count == 0) return false;
            var first = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExchangeCapture x in a) first.Add(x.System);
            foreach (ExchangeCapture x in b) if (!first.Contains(x.System)) return false;
            return true;
        }
    }
}
