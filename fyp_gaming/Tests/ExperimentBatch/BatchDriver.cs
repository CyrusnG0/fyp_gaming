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
        /// <summary>True only when both arms sent the same requests and every aligned system prompt is byte-identical.</summary>
        [JsonPropertyName("system_prompts_identical")] public bool? SystemPromptsIdentical { get; set; }
        /// <summary>How the comparison was made: alignment key, counts, and any request that could not be aligned.</summary>
        [JsonPropertyName("system_prompt_comparison")] public string SystemPromptComparison { get; set; }
        /// <summary>Both arms ran their seasons (completed or completed_with_stops) and the settings check passed.</summary>
        [JsonPropertyName("complete")] public bool Complete { get; set; }
        [JsonPropertyName("incomplete_reason")] public string IncompleteReason { get; set; }
        [JsonPropertyName("final_state_differs")] public bool? FinalStateDiffers { get; set; }
        [JsonPropertyName("both_audits_ok")] public bool BothAuditsOk { get; set; }
        /// <summary>For a spatial_context pair: problems with the claim "same game, only the production spatial context differs". Null for any other variable.</summary>
        [JsonPropertyName("spatial_problems")] public List<string> SpatialProblems { get; set; }
        /// <summary>The arms' final canonical worlds are identical. Expected with the scripted fake (same orders); a real model would diverge.</summary>
        [JsonPropertyName("final_world_identical")] public bool? FinalWorldIdentical { get; set; }
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
        /// <summary>Records are internally consistent (audits, settings checks). Says nothing about whether the matched batch finished.</summary>
        [JsonPropertyName("all_audits_ok")] public bool AllAuditsOk { get; set; }
        /// <summary>Every planned pair ran both arms to the end of its seasons. False for any skipped, errored, failed or stopped arm.</summary>
        [JsonPropertyName("matched_batch_complete")] public bool MatchedBatchComplete { get; set; }
        [JsonPropertyName("incomplete_pairs")] public List<string> IncompletePairs { get; set; } = new List<string>();
        [JsonPropertyName("wall_clock_note")] public string WallClockNote { get; set; } =
            "Batch and match wall-clock limits are checked between session ticks, so one in-flight request can overrun a limit by up to roughly request_timeout_ms; not measured against a real provider.";
        [JsonPropertyName("token_ceiling_note")] public string TokenCeilingNote { get; set; } =
            "Input-token ceilings are checked against usage already reported before each request, so one request can overshoot the ceiling; batch totals can exceed a batch ceiling by up to one request per started match. Output ceilings are strict.";
        [JsonPropertyName("note")] public string Note { get; set; } =
            "Offline scripted fake provider. Outcomes are plumbing evidence, never model behaviour. The scripted replies are identical in both arms and ignore the spatial setting; only the production prompt differs.";
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
                double remaining = Math.Max(0.0, ceiling.MaxBatchWallSeconds.Value - wall.Elapsed.TotalSeconds);
                MatchOutcome outcome = BatchRunner.RunMatch(spec, dir, manifest.BatchId, remaining);
                result.Matches.Add(outcome);
                if (outcome.WallLimit == "max_batch_wall_seconds")
                {
                    stopped = "max_batch_wall_seconds";   // the batch limit expired mid-match: nothing later may start
                    if (!result.BatchCeilingsHit.Contains(stopped)) result.BatchCeilingsHit.Add(stopped);
                }
                result.RequestsTotal += outcome.Requests;
                knownInput += outcome.KnownInputTokens; knownOutput += outcome.KnownOutputTokens;
                if (inputUsed.HasValue && outputUsed.HasValue && outcome.InputTokens.HasValue && outcome.OutputTokens.HasValue)
                { inputUsed += outcome.InputTokens; outputUsed += outcome.OutputTokens; }
                else { inputUsed = null; outputUsed = null; }   // the running total is unknown from here on
            }
            Tally(result);
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

        /// <summary>
        /// Token totals and the count of matches with unknown usage, each judged on the match's OWN usage.
        /// A match that never ran (skipped) has no usage to be unknown about and is not counted.
        /// </summary>
        public static void Tally(BatchResult result)
        {
            long input = 0, output = 0;
            bool known = true;
            result.MatchesWithUnknownUsage = 0;
            result.RequestsTotal = 0;
            foreach (MatchOutcome o in result.Matches)
            {
                if (o.Status == "skipped") continue;
                result.RequestsTotal += o.Requests;
                if (o.InputTokens.HasValue && o.OutputTokens.HasValue) { input += o.InputTokens.Value; output += o.OutputTokens.Value; }
                else { known = false; result.MatchesWithUnknownUsage++; }
            }
            result.InputTokensTotal = known ? input : (long?)null;
            result.OutputTokensTotal = known ? output : (long?)null;
        }

        private static bool Ran(MatchOutcome o) => o.Status == "completed" || o.Status == "completed_with_stops";

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
                if (bothRan)
                {
                    pair.SystemPromptsIdentical = CompareSystemPrompts(c.Captures, t.Captures, out string how);
                    pair.SystemPromptComparison = how;
                    pair.FinalStateDiffers = string.Join("|", c.FinalState) != string.Join("|", t.FinalState);
                    if (c.Spatial != null && t.Spatial != null) pair.FinalWorldIdentical = c.Spatial.WorldFinalDigest == t.Spatial.WorldFinalDigest;
                    // Only a pair whose arms both reached the end of their seasons is judged: a stopped or failed arm makes the pair incomplete, not inconsistent.
                    if (manifest.Experiment.Variable == "spatial_context" && Ran(c) && Ran(t)) pair.SpatialProblems = BatchSpatial.CheckPair(c, t);
                }
                if (pair.SpatialProblems != null && pair.SpatialProblems.Count > 0) allOk = false;
                pair.Complete = Ran(c) && Ran(t) && pair.SettingsCheck.Ok;
                if (!pair.Complete)
                {
                    pair.IncompleteReason = "control " + c.Status + ", treatment " + t.Status + (pair.SettingsCheck.Ok ? "" : ", settings check failed");
                    result.IncompletePairs.Add(pair.PairId + ": " + pair.IncompleteReason);
                }
                result.Pairs.Add(pair);
                if (!pair.SettingsCheck.Ok) allOk = false;
            }
            result.AllAuditsOk = allOk;
            result.MatchedBatchComplete = result.IncompletePairs.Count == 0 && result.Matches.Count == specs.Count && specs.Count > 0;
        }

        /// <summary>
        /// Aligns requests by identity (seat, nth request of that seat) and compares system prompts. Identical only
        /// when both arms made the same requests and every aligned system prompt matches byte for byte; otherwise the
        /// description says how many aligned and how many could not be.
        /// </summary>
        public static bool CompareSystemPrompts(List<ExchangeCapture> a, List<ExchangeCapture> b, out string how)
        {
            var left = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ExchangeCapture x in a) left[x.Seat + "#" + x.SeatRequest] = x.System;
            int aligned = 0, differing = 0, onlyRight = 0;
            foreach (ExchangeCapture x in b)
            {
                if (!left.TryGetValue(x.Seat + "#" + x.SeatRequest, out string other)) { onlyRight++; continue; }
                aligned++;
                if (!string.Equals(other, x.System, StringComparison.Ordinal)) differing++;
            }
            int onlyLeft = left.Count - aligned;
            bool identical = differing == 0 && onlyLeft == 0 && onlyRight == 0;
            how = "aligned by seat and per-seat request number: " + aligned + " aligned (" + a.Count + " control, " + b.Count + " treatment requests); "
                + differing + " differ, " + onlyLeft + " control-only, " + onlyRight + " treatment-only" + (identical ? "" : "; NOT identical");
            return identical;
        }
    }
}
