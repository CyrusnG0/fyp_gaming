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
        private static void EndToEndRichResearchIsolation(TestLog log)
        {
            BatchManifest m = Example();
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                List<JsonElement> records = Records(root, m, o);
                var kinds = new List<string>();
                var cpSpent = new List<string>();
                foreach (JsonElement rec in records.Where(x => MatchAuditor.Str(x, "seat_id") == "seat-0" && MatchAuditor.Num(x, "season") == 1))
                {
                    JsonElement ev = rec.GetProperty("deliberation_event");
                    kinds.Add(MatchAuditor.Str(ev, "kind"));
                    cpSpent.Add(MatchAuditor.Num(ev, "command_points_before") + ">" + MatchAuditor.Num(ev, "command_points_after"));
                }
                log.Check(kinds.SequenceEqual(new[] { "inquiry", "note", "action", "action", "end_turn" }),
                    "seat-0 season 1: inquiry, private note, research purchase, march, end turn (from the run records)", string.Join(",", kinds));
                string[] cp = cpSpent.ToArray();
                log.Check(cp[0].Split('>')[0] == cp[0].Split('>')[1] && cp[1].Split('>')[0] == cp[1].Split('>')[1]
                          && cp[2].Split('>')[0] != cp[2].Split('>')[1],
                    "the inquiry and the note spend no command point; the research order does", string.Join(" ", cp));

                GameEngine start = BatchWorld.Create(m, "probe", 4242);
                ArmyState before = start.State.FindArmy("army-wei-1");
                string startPosition = "army-wei-1@" + before.X + "," + before.Z;
                log.Check(o.FinalState[0].Contains("research_agri=1") && o.FinalState[1].Contains("research_agri=1"),
                    "both seats bought agriculture research through the engine", o.FinalState[0]);
                log.Check(!o.FinalState[0].Contains(startPosition),
                    "the march moved the army off its starting cell", o.FinalState[0] + " vs " + startPosition);

                string a1 = SeatPrompts(o, "seat-0", 1), b1 = SeatPrompts(o, "seat-1", 1);
                log.Check(!b1.Contains("NOTE-seat-0") && !b1.Contains("level_costs") && !b1.Contains("army-wei"),
                    "seat-1's first prompt holds no seat-0 note, research lookup result or seat-0 army");
                string a2 = SeatPrompts(o, "seat-0", 2);
                log.Check(a2.Contains("level_costs"), "seat-0's own research lookup result reaches seat-0's next prompt");
                string seat0All = SeatPrompts(o, "seat-0"), seat1All = SeatPrompts(o, "seat-1");
                log.Check(!seat0All.Contains("NOTE-seat-1") && !seat1All.Contains("NOTE-seat-0"),
                    "no prompt of either seat ever contains the other seat's private note");
                ExchangeCapture a6 = o.Captures.First(c => c.Seat == "seat-0" && c.SeatRequest == 6);
                ExchangeCapture b6 = o.Captures.First(c => c.Seat == "seat-1" && c.SeatRequest == 6);
                log.Check(a6.User.Contains(BatchScripts.NoteFor("seat-0")) && b6.User.Contains(BatchScripts.NoteFor("seat-1")),
                    "each seat reads its own note on its next turn");
                log.Check(o.Captures.All(c => c.System.Contains("RESEARCH ORDERS") && c.System.Contains("RICH DELIBERATION")),
                    "every dispatched request carries both the research and the rich-deliberation material");
                log.Check(o.TurnEndings.Count == 4 && o.TurnEndings.All(t => t.Contains("ModelEndedTurn")) && o.SeasonsPlayed == 2,
                    "two seasons, four turns, all ended by the model");
            }
            finally { Cleanup(root); }
        }

        private static void EndToEndSessionLimit(TestLog log)
        {
            BatchManifest m = Example(x => { x.Ceilings.MaxRequestsPerTurn = 6; x.Ceilings.MaxTotalRequests = 7; });
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                log.Check(o.Requests == 7 && o.Captures.Count == 7 && o.Audit.Ok,
                    "a session request ceiling of 7 stops the match at exactly 7 requests and the audit stays clean",
                    "requests=" + o.Requests + " " + string.Join("; ", o.Audit.Mismatches));
                log.Check(o.Status != "completed" && o.TurnEndings.Any(t => t.Contains("SessionRequestLimit")),
                    "the stop is retained as an outcome, not reported as a clean completion", o.Status + " " + o.StopReason + " " + string.Join(" / ", o.TurnEndings));
                log.Check(o.TurnEndings.Any(t => t.Contains("SessionRequestLimit") && t.Contains("[ceiling: session_requests_exhausted]")),
                    "the turn ending names the ceiling that stopped it", string.Join(" / ", o.TurnEndings));
                log.Check(File.Exists(Path.Combine(MatchDir(root, m, o), "match.json")) && Match(r, "seed4242-treatment").Requests == 7,
                    "both arms stop at the same ceiling and both are retained");
            }
            finally { Cleanup(root); }

            m = Example(x => x.Ceilings.MaxRequestsPerTurn = 3);
            r = RunBatch(m, out root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                log.Check(o.TurnEndings[0].Contains("RequestLimit") && o.TurnEndings[0].Contains("[ceiling: turn_requests_exhausted]") && o.Audit.Ok,
                    "a per-turn request ceiling ends the turn after 3 requests and names itself", o.TurnEndings[0]);
            }
            finally { Cleanup(root); }
        }

        private static void EndToEndProviderFailure(TestLog log)
        {
            BatchManifest m = Example(x =>
            {
                x.Provider.Faults.Add(new FaultSpec { Seat = "seat-0", SeatRequest = 3, Kind = "provider_error" });
                x.BatchCeilings.UnknownUsagePolicy = "continue_on_unknown";   // keep the second arm of the pair running
            });
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                ExchangeCapture failed = o.Captures[2];
                log.Check(failed.Fault == "provider_error" && !failed.Ok && failed.Error.Contains("Server"),
                    "the provider answered request 3 of seat-0 with an injected server failure", failed.Error);
                List<JsonElement> records = Records(root, m, o);
                JsonElement third = records[2];
                log.Check(MatchAuditor.Str(third, "error_kind") == "Server" && MatchAuditor.Num(third, "http_status") == 500 && MatchAuditor.Bool(third, "parse_ok") == false,
                    "the run record keeps the failed request with its error kind and http status");
                log.Check(o.TurnEndings[0].Contains("ProviderError"), "the turn ends as a provider error, not a model decision", o.TurnEndings[0]);
                log.Check(o.Status == "completed_with_stops" && Match(r, "seed4242-treatment").Status == "completed_with_stops",
                    "both matches are retained as completed-with-stops, not as clean completions", o.Status);
                log.Check(r.BatchCeilingsHit.Count == 0 && r.InputTokensTotal == null && BatchReport.Text(r).Contains("input tokens unknown"),
                    "under continue_on_unknown both arms run and the batch token total is reported as unknown");
                log.Check(o.Audit.Ok && Match(r, "seed4242-treatment").Audit.Ok && r.AllAuditsOk,
                    "the failed matches still audit clean: records, exchanges and summary agree", string.Join("; ", o.Audit.Mismatches));
                log.Check(r.Pairs[0].SettingsCheck.Ok, "the fault is a setting present in both arms, so the pair stays matched");
                log.Check(File.ReadAllText(Path.Combine(MatchDir(root, m, o), o.Files["prompts"].Path)).Contains("\"fault\":\"provider_error\""),
                    "the prompt capture marks the faulted request");
            }
            finally { Cleanup(root); }
        }

        private static void EndToEndUnknownAndTruncated(TestLog log)
        {
            BatchManifest m = Example(x => { x.Seeds = new List<int> { 4242, 4243 }; x.BatchCeilings.MaxMatches = 4;
                x.Provider.Faults.Add(new FaultSpec { Seat = "seat-0", SeatRequest = 1, Kind = "missing_usage" }); });
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                log.Check(o.InputTokens == null && o.OutputTokens == null && o.Audit.Unknown.Any(u => u.StartsWith("token usage unknown for 1 of")),
                    "missing usage makes the match totals unknown (not zero) and the audit says so");
                log.Check(o.Audit.Ok, "unknown usage is consistent across records, runner and summary", string.Join("; ", o.Audit.Mismatches));
                log.Check(o.TurnEndings[0].Contains("[ceiling: session_usage_unknown]"),
                    "with token ceilings set the runner stops on unknown usage rather than assuming zero", o.TurnEndings[0]);
                log.Check(r.InputTokensTotal == null && r.MatchesWithUnknownUsage >= 1 && BatchReport.Text(r).Contains("input tokens unknown"),
                    "the batch total is unknown, and the report prints 'unknown'");
                log.Check(r.BatchCeilingsHit.Contains("usage_unknown") && r.StatusCounts.TryGetValue("skipped", out int skipped) && skipped >= 1,
                    "later matches are skipped, and retained, because the running token total is unknown", string.Join(",", r.BatchCeilingsHit));
            }
            finally { Cleanup(root); }

            m = Example(x => x.Provider.Faults.Add(new FaultSpec { Seat = "seat-1", SeatRequest = 3, Kind = "length_truncated" }));
            r = RunBatch(m, out root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                log.Check(o.Audit.Truncation.Any(t => t.Contains("finish_reason=length")) && o.Audit.Counts["finish_reason_length"] == 1
                          && o.Audit.Counts["replies_at_output_bound"] >= 1,
                    "a reply cut by the output limit is reported as truncated, with the count of replies at the output bound");
                log.Check(o.Audit.Ok, "a truncated reply still reconciles", string.Join("; ", o.Audit.Mismatches));
            }
            finally { Cleanup(root); }

            m = Example(x => x.Provider.Faults.Add(new FaultSpec { Seat = "seat-0", SeatRequest = 2, Kind = "empty_reply" }));
            r = RunBatch(m, out root);
            try
            {
                MatchAudit a = Match(r, "seed4242-control").Audit;
                log.Check(a.Truncation.Any(t => t.Contains("empty reply")) && a.Counts["empty_replies"] == 1 && a.Ok,
                    "an empty reply is counted and reported", string.Join("; ", a.Mismatches));
            }
            finally { Cleanup(root); }
        }

        private static void BatchCeilingsRetainSkipped(TestLog log)
        {
            BatchManifest m = Example(x => { x.Seeds = new List<int> { 1, 2 }; x.BatchCeilings.MaxMatches = 4; x.BatchCeilings.MaxTotalRequests = 100; });
            BatchResult r = RunBatch(m, out string root);
            try
            {
                int completed = r.StatusCounts.TryGetValue("completed", out int c) ? c : 0;
                int skipped = r.StatusCounts.TryGetValue("skipped", out int s) ? s : 0;
                log.Check(completed == 2 && skipped == 2 && r.BatchCeilingsHit.SequenceEqual(new[] { "max_total_requests" }),
                    "a batch request ceiling lets two matches run and skips the other two, naming the ceiling",
                    string.Join(",", r.StatusCounts.Select(kv => kv.Key + "=" + kv.Value)) + " " + string.Join(",", r.BatchCeilingsHit));
                MatchOutcome skippedMatch = Match(r, "seed2-treatment");
                log.Check(skippedMatch.Status == "skipped" && skippedMatch.Detail.Contains("max_total_requests")
                          && File.Exists(Path.Combine(MatchDir(root, m, skippedMatch), "match.json")),
                    "a skipped match is retained with its reason");
                log.Check(r.Pairs[1].SystemPromptsIdentical == null && r.Pairs[1].FinalStateDiffers == null && r.AllAuditsOk,
                    "a pair that never ran reports no comparison instead of inventing one");
            }
            finally { Cleanup(root); }

            m = Example(x => x.BatchCeilings.MaxTotalInputTokens = 100000);   // below one match's own 2,000,000 ceiling
            r = RunBatch(m, out root);
            try
            {
                log.Check(r.StatusCounts.TryGetValue("skipped", out int all) && all == 2 && r.RequestsTotal == 0
                          && r.BatchCeilingsHit.Contains("max_total_input_tokens"),
                    "a batch token ceiling below one match's own ceiling refuses to start any match");
            }
            finally { Cleanup(root); }
        }

        private static void EvidenceIsSecretFreeAndNotOverwritten(TestLog log)
        {
            const string canary = "sk-CANARYCANARYCANARYCANARY00";
            string previous = Environment.GetEnvironmentVariable("LUANSHI_LLM_API_KEY");
            Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", canary);
            BatchManifest m = Example();
            BatchResult r = RunBatch(m, out string root);
            try
            {
                int files = 0;
                bool clean = true;
                var shape = new Regex("sk-[A-Za-z0-9_\\-]{12,}|Bearer\\s+[A-Za-z0-9._\\-]{12,}");
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    files++;
                    string text = File.ReadAllText(file);
                    clean &= !text.Contains(canary) && !shape.IsMatch(text);
                }
                log.Check(files >= 14 && clean, "no evidence file holds a key, even with a key in the environment (" + files + " files scanned)");
                bool refused = false;
                try { BatchDriver.Run(m, m.ToJson(), root); }
                catch (InvalidOperationException e) { refused = e.Message.Contains("already holds evidence"); }
                log.Check(refused && File.Exists(Path.Combine(root, m.BatchId, "batch-result.json")),
                    "a second run into the same directory is refused and the first evidence is untouched");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", previous);
                Cleanup(root);
            }
        }
    }
}
