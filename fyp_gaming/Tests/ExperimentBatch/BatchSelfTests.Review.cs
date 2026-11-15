using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LuanShi.ExperimentBatch
{
    internal static partial class BatchSelfTests
    {
        private static ExchangeCapture Cap(string seat, int seatRequest, string system)
            => new ExchangeCapture { Seat = seat, SeatRequest = seatRequest, System = system, User = "u" };

        private static void SkippedArmMakesAnIncompletePair(TestLog log)
        {
            BatchManifest m = Example(x => x.BatchCeilings.MaxTotalRequests = 90);   // the treatment arm cannot start after 14 requests: 14 + 80 > 90
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome treatment = Match(r, "seed4242-treatment");
                PairComparison pair = r.Pairs[0];
                log.Check(Match(r, "seed4242-control").Status == "completed" && treatment.Status == "skipped",
                    "the control arm completes and the treatment arm is skipped by the batch ceiling");
                log.Check(r.AllAuditsOk, "the records that exist are internally consistent");
                log.Check(!r.MatchedBatchComplete && !pair.Complete && pair.IncompleteReason.Contains("treatment skipped")
                          && r.IncompletePairs.Count == 1,
                    "an incomplete pair is not a completed matched batch", pair.IncompleteReason);
                log.Check(pair.SystemPromptsIdentical == null && pair.FinalStateDiffers == null,
                    "no prompt or outcome comparison is invented for a pair with a skipped arm");
                string report = BatchReport.Text(r);
                log.Check(report.Contains("records internally consistent: yes") && report.Contains("matched batch complete: NO - not a completed experiment"),
                    "the report separates 'records consistent' from 'matched batch complete'");
                log.Check(File.Exists(Path.Combine(MatchDir(root, m, treatment), "settings.json")) && treatment.Files.ContainsKey("settings"),
                    "the skipped arm's planned settings are retained");
            }
            finally { Cleanup(root); }

            m = Example();
            r = RunBatch(m, out root);
            try { log.Check(r.MatchedBatchComplete && r.Pairs[0].Complete && r.IncompletePairs.Count == 0, "a pair whose two arms both ran is complete"); }
            finally { Cleanup(root); }
        }

        private static void UnknownUsageIsCountedPerMatch(TestLog log)
        {
            MatchOutcome Known(string id) => new MatchOutcome { MatchId = id, Status = "completed", Requests = 5, InputTokens = 100, OutputTokens = 10 };
            MatchOutcome Unknown(string id) => new MatchOutcome { MatchId = id, Status = "completed_with_stops", Requests = 3 };
            var r = new BatchResult();
            r.Matches.AddRange(new[] { Known("a"), Unknown("b"), Known("c"), new MatchOutcome { MatchId = "d", Status = "skipped" } });
            BatchDriver.Tally(r);
            log.Check(r.MatchesWithUnknownUsage == 1 && r.RequestsTotal == 13 && r.InputTokensTotal == null && r.OutputTokensTotal == null,
                "one unknown match among known and skipped ones counts once; the total is unknown, not zero",
                r.MatchesWithUnknownUsage + " unknown, requests " + r.RequestsTotal);
            r = new BatchResult();
            r.Matches.AddRange(new[] { Known("a"), Known("b"), new MatchOutcome { MatchId = "c", Status = "skipped" } });
            BatchDriver.Tally(r);
            log.Check(r.MatchesWithUnknownUsage == 0 && r.InputTokensTotal == 200 && r.OutputTokensTotal == 20 && r.RequestsTotal == 10,
                "known matches sum exactly and a skipped match adds nothing");
            r = new BatchResult();
            r.Matches.AddRange(new[] { Unknown("a"), Unknown("b") });
            BatchDriver.Tally(r);
            log.Check(r.MatchesWithUnknownUsage == 2, "two unknown matches count twice");

            BatchManifest m = Example(x =>
            {
                x.Provider.Faults.Add(new FaultSpec { Seat = "seat-0", SeatRequest = 3, Kind = "provider_error" });
                x.BatchCeilings.UnknownUsagePolicy = "continue_on_unknown";
            });
            BatchResult run = RunBatch(m, out string root);
            try
            {
                log.Check(run.MatchesWithUnknownUsage == run.Matches.Count(o => o.Status != "skipped" && (o.InputTokens == null || o.OutputTokens == null)),
                    "an actual run reports the same per-match count (" + run.MatchesWithUnknownUsage + ")");
            }
            finally { Cleanup(root); }
        }

        private static void SystemPromptsAreComparedByRequestIdentity(TestLog log)
        {
            var a = new List<ExchangeCapture> { Cap("seat-0", 1, "X"), Cap("seat-0", 2, "Y"), Cap("seat-1", 1, "Z") };
            var same = new List<ExchangeCapture> { Cap("seat-1", 1, "Z"), Cap("seat-0", 1, "X"), Cap("seat-0", 2, "Y") };
            log.Check(BatchDriver.CompareSystemPrompts(a, same, out string how) && how.Contains("3 aligned"),
                "the same requests in a different arrival order are aligned by seat and request number", how);
            var swapped = new List<ExchangeCapture> { Cap("seat-0", 1, "Y"), Cap("seat-0", 2, "X"), Cap("seat-1", 1, "Z") };
            log.Check(!BatchDriver.CompareSystemPrompts(a, swapped, out how) && how.Contains("2 differ"),
                "prompts that swap places are NOT identical (a set-membership check would have passed them)", how);
            var shorter = new List<ExchangeCapture> { Cap("seat-0", 1, "X"), Cap("seat-0", 2, "Y") };
            log.Check(!BatchDriver.CompareSystemPrompts(a, shorter, out how) && how.Contains("1 control-only"),
                "a missing request makes the arms not identical and says how many could not be aligned", how);
            var extra = new List<ExchangeCapture>(a) { Cap("seat-1", 2, "Z") };
            log.Check(!BatchDriver.CompareSystemPrompts(a, extra, out how) && how.Contains("1 treatment-only"),
                "an extra request in one arm is reported as treatment-only", how);
            var repeated = new List<ExchangeCapture> { Cap("seat-0", 1, "X"), Cap("seat-0", 2, "X"), Cap("seat-1", 1, "X") };
            log.Check(!BatchDriver.CompareSystemPrompts(repeated, a, out how), "one repeated prompt standing in for different ones is not identical", how);
        }

        private static void WallClockLimitsStopAMatch(TestLog log)
        {
            ScriptedSeatTransport.DelayPerRequestMs = 150;   // 14 requests need about 2 s
            try
            {
                BatchManifest m = Example(x => x.BatchCeilings.MaxMatchWallSeconds = 1);
                BatchResult r = RunBatch(m, out string root);
                try
                {
                    MatchOutcome o = Match(r, "seed4242-control");
                    log.Check(o.Status == "stopped" && o.WallLimit == "max_match_wall_seconds" && o.Requests < 14 && o.Requests > 0,
                        "the match wall limit stops a running match part-way and names itself", o.Status + " " + o.WallLimit + " " + o.Requests + " requests");
                    log.Check(o.Audit != null && o.Audit.Ok, "the partial match still audits clean", o.Audit == null ? "no audit" : string.Join("; ", o.Audit.Mismatches));
                    log.Check(!r.MatchedBatchComplete && !r.Pairs[0].Complete, "a stopped arm leaves the pair incomplete");
                }
                finally { Cleanup(root); }

                m = Example(x => { x.BatchCeilings.MaxBatchWallSeconds = 1; x.BatchCeilings.MaxMatchWallSeconds = 60; });
                r = RunBatch(m, out root);
                try
                {
                    MatchOutcome first = Match(r, "seed4242-control");
                    log.Check(first.Status == "stopped" && first.WallLimit == "max_batch_wall_seconds" && first.Requests < 14,
                        "the batch wall limit is enforced DURING a match, not only before the next one", first.Status + " " + first.WallLimit);
                    log.Check(Match(r, "seed4242-treatment").Status == "skipped" && r.BatchCeilingsHit.Contains("max_batch_wall_seconds") && !r.MatchedBatchComplete,
                        "the later arm is skipped and retained, the ceiling is named, and the batch is not complete");
                    log.Check(BatchReport.Text(r).Contains("up to roughly request_timeout_ms"), "the report states the limit is checked between ticks and may overrun");
                }
                finally { Cleanup(root); }
            }
            finally { ScriptedSeatTransport.DelayPerRequestMs = 0; }
        }

        private static void InputTokenCeilingStopsAfterTheCrossingRequest(TestLog log)
        {
            BatchManifest m = Example(x => x.Ceilings.MaxSessionInputTokens = 6000);
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome o = Match(r, "seed4242-control");
                List<ExchangeCapture> caps = o.Captures;
                long total = caps.Sum(c => (long)(c.InputTokens ?? 0));
                long beforeLast = total - (caps[caps.Count - 1].InputTokens ?? 0);
                log.Check(caps.Count >= 1 && caps.Count < 14 && total >= 6000 && beforeLast < 6000,
                    "the request that crosses the input ceiling completes, then nothing more is sent (one-request overshoot)",
                    caps.Count + " requests, input " + total + ", before the last " + beforeLast);
                log.Check(total - 6000 < (caps[caps.Count - 1].InputTokens ?? 0),
                    "the overshoot is smaller than the final request's own input (" + (total - 6000) + " tokens over 6000)");
                log.Check(o.TurnEndings.Any(t => t.Contains("[ceiling: session_input_tokens]")) && o.Status == "completed_with_stops",
                    "later turns end on the named session_input_tokens ceiling and the match is completed-with-stops", string.Join(" | ", o.TurnEndings));
                log.Check(o.Audit.Ok && o.InputTokens == total, "records, runner and summary agree on the overshoot total", string.Join("; ", o.Audit.Mismatches));
                log.Check(BatchReport.Text(r).Contains("one request can overshoot"), "the report describes the overshoot");
            }
            finally { Cleanup(root); }
        }
    }
}
