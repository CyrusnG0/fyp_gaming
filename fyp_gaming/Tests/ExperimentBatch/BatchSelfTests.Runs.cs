using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    internal static partial class BatchSelfTests
    {
        private static string MatchDir(string root, BatchManifest m, MatchOutcome o) => Path.Combine(root, m.BatchId, o.MatchId);

        private static List<JsonElement> Records(string root, BatchManifest m, MatchOutcome o)
        {
            var list = new List<JsonElement>();
            foreach (string line in File.ReadAllLines(Path.Combine(MatchDir(root, m, o), o.Files["run_jsonl"].Path)))
                if (line.Trim().Length > 0) list.Add(JsonDocument.Parse(line).RootElement.Clone());
            return list;
        }

        private static string Sequence(MatchOutcome o)
            => string.Join("|", o.Captures.Select(c => c.Seat + ":" + BatchPlan.Sha256(c.User).Substring(0, 12)));

        private static void BatchRunsDeterministicPairs(TestLog log)
        {
            BatchManifest m = Example(x => { x.Seeds = new List<int> { 4242, 4243 }; x.BatchCeilings.MaxMatches = 4; });
            BatchResult r = RunBatch(m, out string root);
            string rootAgain = null;
            try
            {
                int completed = 0;
                r.StatusCounts.TryGetValue("completed", out completed);
                log.Check(r.Matches.Count == 4 && completed == 4 && r.AllAuditsOk,
                    "four matches (two seeds x two arms) complete and every audit is clean",
                    "status counts: " + string.Join(",", r.StatusCounts.Select(kv => kv.Key + "=" + kv.Value)));
                log.Check(r.Pairs.Count == 2 && r.Pairs.All(p => p.SettingsCheck.Ok && p.BothAuditsOk),
                    "both pairs pass the settings check and their audits");
                log.Check(r.Pairs.All(p => p.SystemPromptsIdentical == false && p.SystemPromptComparison.Contains("NOT identical")),
                    "the arms' system prompts differ: the treatment carries the production spatial addendum");
                log.Check(r.Pairs.All(p => p.FinalStateDiffers == false && p.FinalWorldIdentical == true),
                    "the scripted replies ignore the spatial setting, so both arms end in the same world");
                MatchOutcome treatment = Match(r, "seed4242-treatment");
                log.Check(treatment.SpatialContext == "standard" && Match(r, "seed4242-control").SpatialContext == "off",
                    "each result states the production spatial level it dispatched with");

                bool filesOk = true;
                foreach (MatchOutcome o in r.Matches)
                {
                    foreach (string key in new[] { "settings", "run_jsonl", "exchanges", "prompts", "summary" })
                    {
                        if (!o.Files.TryGetValue(key, out FileRef f)) { filesOk = false; continue; }
                        string path = Path.Combine(MatchDir(root, m, o), f.Path);
                        filesOk &= File.Exists(path) && BatchPlan.Sha256(File.ReadAllBytes(path)) == f.Sha256 && new FileInfo(path).Length == f.Bytes;
                    }
                    filesOk &= File.ReadAllLines(Path.Combine(MatchDir(root, m, o), o.Files["prompts"].Path)).Length == o.Requests;
                }
                log.Check(filesOk, "every match keeps settings, run records, exchanges, prompts and summary; hashes and sizes match the files, one prompt line per request");
                string manifestCopy = File.ReadAllText(Path.Combine(root, m.BatchId, "manifest.json"));
                log.Check(BatchPlan.Sha256(manifestCopy) == r.ManifestSha256
                          && File.Exists(Path.Combine(root, m.BatchId, "batch-result.json")) && File.Exists(Path.Combine(root, m.BatchId, "batch-report.txt")),
                    "the manifest copy, batch result and report are written and the manifest hash matches");

                BatchResult again = RunBatch(m, out rootAgain);
                bool same = again.Matches.Count == r.Matches.Count;
                for (int i = 0; same && i < r.Matches.Count; i++)
                    same = r.Matches[i].SettingsHash == again.Matches[i].SettingsHash
                           && string.Join("|", r.Matches[i].FinalState) == string.Join("|", again.Matches[i].FinalState)
                           && Sequence(r.Matches[i]) == Sequence(again.Matches[i]);
                log.Check(same, "a second run reproduces every settings hash, final state and prompt sequence");
            }
            finally { Cleanup(root); if (rootAgain != null) Cleanup(rootAgain); }
        }

        private static void AuditUsesPerRequestRecords(TestLog log)
        {
            BatchManifest m = Example();
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                MatchAudit a = o.Audit;
                log.Check(a.Ok && a.Counts["run_records"] == 14 && a.Counts["summary_requests"] == 14 && a.Counts["prompts_captured"] == 14
                          && a.Counts["exchange_requests"] == 14 && a.Counts["runner_total_requests"] == 14,
                    "requests agree across run records, exchanges, prompt capture, runner and summary", string.Join("; ", a.Mismatches));
                log.Check(a.Counts["accepted_from_records"] == 4 && a.Counts["accepted_summary"] == 4 && a.Counts["rejected_from_records"] == 0,
                    "accepted and rejected orders are rebuilt from the records and agree with the summary");
                TurnAudit first = a.Turns[0];
                log.Check(first.Seat == "seat-0" && first.OrdersInRecords.SequenceEqual(new[] { "research:accepted", "march:accepted" })
                          && first.SummaryLastCommand != null && first.SummaryLastCommand.Contains("end_turn")
                          && first.OrdersNotVisibleInSummaryCommands == 2,
                    "a turn's summary shows only its last command (end_turn) while the records hold two accepted orders",
                    string.Join(",", first.OrdersInRecords) + " last=" + first.SummaryLastCommand);
                log.Check(a.Unknown.Any(u => u.Contains("not visible in the summary")),
                    "the audit reports the orders the summary's command list cannot show");
                log.Check(a.Truncation.Any(t => t.Contains("march destinations across your armies")),
                    "prompt truncation (destination list cut) is reported explicitly");
                log.Check(a.Unknown.Any(u => u.StartsWith("cost_usd unknown")) && a.Unknown.Any(u => u.StartsWith("reasoning_tokens not reported")),
                    "unknown cost and reasoning counts are reported, not reported as zero");
            }
            finally { Cleanup(root); }
        }

        private static void AuditDetectsTamperedSummary(TestLog log)
        {
            BatchManifest m = Example();
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                string dir = MatchDir(root, m, o);
                string path = Path.Combine(dir, o.Files["summary"].Path);
                string original = File.ReadAllText(path);

                File.WriteAllText(path, new Regex("\"accepted\":4").Replace(original, "\"accepted\":5", 1));
                MatchAudit a = MatchAuditor.Audit(dir, o, m);
                log.Check(!a.Ok && a.Mismatches.Any(x => x.StartsWith("accepted orders")), "an inflated accepted total in the summary is caught", string.Join("; ", a.Mismatches));

                File.WriteAllText(path, new Regex("\"input_tokens\":\\d+").Replace(original, "\"input_tokens\":1", 1));
                a = MatchAuditor.Audit(dir, o, m);
                log.Check(!a.Ok && a.Mismatches.Any(x => x.Contains("tokens")), "a wrong token total in the summary is caught", string.Join("; ", a.Mismatches));

                File.WriteAllText(path, original);
                string runPath = Path.Combine(dir, o.Files["run_jsonl"].Path);
                string[] lines = File.ReadAllLines(runPath);
                File.WriteAllLines(runPath, lines.Take(lines.Length - 1));
                a = MatchAuditor.Audit(dir, o, m);
                log.Check(!a.Ok && a.Mismatches.Any(x => x.StartsWith("request count")), "a dropped run record is caught", string.Join("; ", a.Mismatches));
                File.WriteAllLines(runPath, lines);
                log.Check(MatchAuditor.Audit(dir, o, m).Ok, "the untouched evidence audits clean again");
            }
            finally { Cleanup(root); }
        }
    }
}
