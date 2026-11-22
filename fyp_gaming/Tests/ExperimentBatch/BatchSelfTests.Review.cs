using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LuanShi.Engine;

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
                log.Check(o.TurnEndings.Any(t => t.Contains("[ceiling: session_input_tokens]"))
                          && o.Status == "stopped" && Match(r, "seed4242-treatment").Status == "stopped"
                          && o.SessionState == "Paused" && o.SeasonsPlayed < m.Seasons,
                    "later turns end on the named session_input_tokens ceiling and the interrupted match is stopped", o.Status + " / " + string.Join(" | ", o.TurnEndings));
                log.Check(!r.Pairs[0].Complete && !r.MatchedBatchComplete && r.IncompletePairs.Count == 1
                          && r.StatusCounts.TryGetValue("stopped", out int stoppedCount) && stoppedCount == 2
                          && BatchReport.Text(r).Contains("not a completed experiment"),
                    "a session_input_tokens stop keeps the pair incomplete and marks the batch incomplete", string.Join("; ", r.IncompletePairs));
                log.Check(o.Audit.Ok && o.InputTokens == total, "records, runner and summary agree on the overshoot total", string.Join("; ", o.Audit.Mismatches));
                log.Check(BatchReport.Text(r).Contains("one request can overshoot"), "the report describes the overshoot");
                log.Check(new[] { "settings", "prompts", "run_jsonl", "exchanges", "summary", "match" }
                          .All(k => o.Files.TryGetValue(k, out FileRef refFile)
                                    && File.Exists(Path.Combine(MatchDir(root, m, o), refFile.Path))
                                    && BatchPlan.Sha256(File.ReadAllBytes(Path.Combine(MatchDir(root, m, o), refFile.Path))) == refFile.Sha256),
                    "retained evidence files for the input-token stopped match exist and match their recorded hashes");
            }
            finally { Cleanup(root); }
        }

        private static void ProviderPauseAndInvalidConfigurationStatus(TestLog log)
        {
            // 1. Direct provider pause (rate limit / auth failure -> Paused without session_usage_unknown) must be "failed".
            string root = TempDir();
            try
            {
                BatchManifest m = Example();
                GameEngine engine = BatchWorld.Create(m, "direct-provider-pause", 4242);
                var transport = new DirectFailureTransport(LlmErrorKind.RateLimited, "rate limited 429");
                var recorder = new LlmRunRecorder(root);
                var config = new LlmConfig
                {
                    Provider = "fake",
                    Model = "fake-direct-failure",
                    EnableDeliberation = true,
                    EnableAttack = true,
                    EnableResearch = true,
                    MaxRequestsPerTurn = 6,
                    MaxTotalRequests = 20,
                    MaxActionsPerTurn = 3,
                    MaxAcceptedActionsPerSession = 20,
                };
                var runner = new LlmTurnRunner(transport, config, recorder, null, new MemoryLlmStrategicNoteStore());
                var session = new SpectatorSession(engine, runner, m.Seasons, 4242, recorder);
                session.Start();
                while (session.State == SpectatorState.Running) session.Tick();

                string status = BatchRunner.ClassifyStatus(false, session, runner, config, null, out string detail);
                log.Check(session.State == SpectatorState.Paused && runner.EndReason == EndReason.ProviderError
                          && status == "failed" && !string.IsNullOrEmpty(detail),
                    "a provider rate-limit pause is classified as failed, never stopped or completed_with_stops",
                    status + " state=" + session.State + " detail=" + detail);
            }
            finally { Cleanup(root); }

            // 2. BatchDriver refuses an invalid configuration cleanly before creating partial batch evidence.
            BatchManifest badManifest = Example(x => x.Ceilings.MaxOutputTokens = 0);
            string refusedDir = TempDir();
            bool driverRefused = false;
            try { BatchDriver.Run(badManifest, badManifest.ToJson(), refusedDir); }
            catch (InvalidOperationException ex) { driverRefused = ex.Message.Contains("manifest invalid") && ex.Message.Contains("max_output_tokens"); }
            finally { Cleanup(refusedDir); }
            log.Check(driverRefused, "BatchDriver refuses an invalid configuration manifest before starting any match");

            // 3. If BatchRunner.RunMatch is invoked directly with an invalid LlmConfig (1-season or 2-season),
            //    it sends zero provider requests, classifies as "failed" (never completed/completed_with_stops),
            //    and retains auditable evidence (settings, summary, prompts, match.json).
            foreach (int seasons in new[] { 1, 2 })
            {
                BatchManifest directBad = Example(x => { x.Seasons = seasons; x.Ceilings.MaxOutputTokens = 0; });
                List<MatchSpec> specs = BatchPlan.Build(directBad);
                root = TempDir();
                try
                {
                    string matchDir = Path.Combine(root, specs[0].MatchId);
                    MatchOutcome o = BatchRunner.RunMatch(specs[0], matchDir, "invalid-cfg-check");
                    log.Check(o.Status == "failed" && o.Requests == 0 && o.Captures.Count == 0
                              && o.Detail != null && o.Detail.Contains("experiment configuration invalid"),
                        $"invalid configuration (seasons={seasons}) sends 0 requests and is classified as failed",
                        $"status={o.Status} req={o.Requests} detail={o.Detail}");
                    log.Check(o.Audit != null && o.Audit.Ok
                              && new[] { "settings", "prompts", "summary", "match" }
                                  .All(k => o.Files.TryGetValue(k, out FileRef f) && File.Exists(Path.Combine(matchDir, f.Path))),
                        $"invalid configuration (seasons={seasons}) retains auditable evidence and audits clean",
                        o.Audit == null ? "null audit" : string.Join("; ", o.Audit.Mismatches));
                }
                finally { Cleanup(root); }
            }
        }

        private sealed class DirectFailureTransport : ILlmTransport
        {
            private readonly LlmErrorKind _kind;
            private readonly string _detail;
            public DirectFailureTransport(LlmErrorKind kind, string detail) { _kind = kind; _detail = detail; }
            public string Provider => "fake";
            public string Model => "fake-direct-failure";
            public System.Threading.Tasks.Task<LlmResponse> SendAsync(LlmRequest req, System.Threading.CancellationToken ct)
                => System.Threading.Tasks.Task.FromResult(LlmResponse.Failure(_kind, _detail, 5, null, 429));
        }
    }
}
