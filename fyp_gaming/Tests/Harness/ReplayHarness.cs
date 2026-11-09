using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

internal static partial class Harness
{
    public static void RunReplayPublic() => RunReplay();

    private static void RunReplay()
    {
        Section("offline match replay (zero model calls, 5-season verification)");

        // 1. Fail-closed resolution tests
        Check(!ReplayLoader.TryResolveRunFiles("", out _, out _, out _, out string errEmpty), "empty runId rejected");
        Check(errEmpty != null && errEmpty.Contains("required"), "error message mentions required");

        Check(!ReplayLoader.TryResolveRunFiles("run-other", out _, out _, out _, out string errOther), "unsupported runId rejected");
        Check(errOther != null && errOther.Contains("restricted"), "error message mentions restricted");

        string runId = ReplayLoader.VerifiedDemoRunId;
        bool resolved = ReplayLoader.TryResolveRunFiles(runId, out string summaryPath, out string jsonlPath, out string exchangesPath, out string error);
        Check(resolved, "resolve verified run files (" + (error ?? "found all 3 files") + ")");
        if (!resolved) return;

        Check(File.Exists(summaryPath), "summary path exists: " + Path.GetFileName(summaryPath));
        Check(File.Exists(jsonlPath), "jsonl path exists: " + Path.GetFileName(jsonlPath));
        Check(File.Exists(exchangesPath), "exchanges path exists: " + Path.GetFileName(exchangesPath));

        ReplayBundle bundle = ReplayLoader.Load(summaryPath, jsonlPath, exchangesPath);
        Check(bundle != null, "bundle loaded successfully");
        if (bundle == null) return;

        // Metadata assertions
        Check(bundle.Summary.Seed == 12345, $"seed == 12345 (got {bundle.Summary.Seed})");
        Check(bundle.Summary.BalanceProfileKey == "easy-research", $"balance_profile == 'easy-research' (got '{bundle.Summary.BalanceProfileKey}')");
        Check(bundle.Summary.SeasonsPlanned == 5, $"seasons_planned == 5 (got {bundle.Summary.SeasonsPlanned})");
        Check(bundle.Summary.SeasonsPlayed == 5, $"seasons_played == 5 (got {bundle.Summary.SeasonsPlayed})");
        Check(bundle.Summary.ManualLookupEnabled, "manual_lookup_enabled == true");
        Check(bundle.Requests.Count == 44, $"total requests in bundle == 44 (got {bundle.Requests.Count})");

        // Verify personas recorded in turn summaries
        bool hasWeiMild = false, hasShuAggressive = false;
        foreach (var t in bundle.Summary.Turns)
        {
            if (t.PersonaId == "wei-mild") hasWeiMild = true;
            if (t.PersonaId == "shu-aggressive") hasShuAggressive = true;
        }
        Check(hasWeiMild, "turn summaries contain persona 'wei-mild'");
        Check(hasShuAggressive, "turn summaries contain persona 'shu-aggressive'");

        // Verify joined exchange records
        bool exchangesJoined = true;
        for (int i = 0; i < bundle.Requests.Count; i++)
        {
            var req = bundle.Requests[i];
            if (string.IsNullOrEmpty(req.PromptExcerpt) || string.IsNullOrEmpty(req.ResponseContentText))
            {
                exchangesJoined = false;
                break;
            }
        }
        Check(exchangesJoined, "all 44 requests have joined PromptExcerpt and ResponseContentText");

        // 2. Playback controls & stepping tests
        var session = new ReplaySession(bundle);
        Check(session.State == ReplayState.NotStarted, "initial state == NotStarted");
        Check(session.CurrentRequestIndex == 0, "initial request index == 0");
        Check(session.CurrentSeason == 1, "initial season == 1");

        session.Play();
        Check(session.IsPlaying, "Play() sets IsPlaying == true");
        session.Pause();
        Check(!session.IsPlaying && session.State == ReplayState.Paused, "Pause() sets IsPlaying == false");

        session.SetSpeed(2.0f);
        Check(Math.Abs(session.Speed - 2.0f) < 0.001f, "SetSpeed(2.0f) updates speed");
        session.SetSpeed(1.0f);

        // Step one request manually
        bool step1 = session.StepRequest();
        Check(step1, "StepRequest() executed first request");
        Check(session.CurrentRequestIndex == 1, "CurrentRequestIndex == 1 after 1 step");
        Check(session.ObservationHashesMatched == 1, "1st observation hash matched");

        // Step through Season 1..5
        session.StepSeason(); // S1 -> S2
        Check(session.CurrentSeason == 2, $"Season 1 ended at request index 10 (got {session.CurrentRequestIndex})");
        session.StepSeason(); // S2 -> S3
        session.StepSeason(); // S3 -> S4
        session.StepSeason(); // S4 -> S5
        session.StepSeason(); // S5 -> finish
        Check(session.IsCompleted, "session is completed after Season 5");
        Check(session.CurrentRequestIndex == 44, $"total requests stepped == 44 (got {session.CurrentRequestIndex})");

        // 3. Strict verification metrics
        Check(session.AcceptedActionsReplayed == 42, $"AcceptedActionsReplayed == 42 (got {session.AcceptedActionsReplayed})");
        Check(session.RejectedActionsReplayed == 0, $"RejectedActionsReplayed == 0 (got {session.RejectedActionsReplayed})");
        Check(session.UnexpectedRejections == 0, $"UnexpectedRejections == 0 (got {session.UnexpectedRejections})");
        Check(session.UnexpectedAcceptances == 0, $"UnexpectedAcceptances == 0 (got {session.UnexpectedAcceptances})");
        Check(session.ActionOutcomeMismatches == 0, $"ActionOutcomeMismatches == 0 (got {session.ActionOutcomeMismatches})");
        Check(session.VoluntaryEndTurnsReplayed == 2, $"VoluntaryEndTurnsReplayed == 2 (got {session.VoluntaryEndTurnsReplayed})");

        Check(session.ObservationHashesMatched == 10, $"ObservationHashesMatched == 10 (got {session.ObservationHashesMatched})");
        Check(session.ObservationHashMismatches == 34, $"ObservationHashMismatches == 34 (got {session.ObservationHashMismatches})");

        // Raw final scores
        var rpt = session.FinalReport;
        Check(rpt != null, "final comparison report generated");
        Check(rpt.WeiFinalFood == 243 && rpt.WeiExpectedFood == 243, $"Wei Food == 243 (exp 243)");
        Check(rpt.WeiFinalGold == 1742 && rpt.WeiExpectedGold == 1741, $"Wei Gold == 1742 (exp 1741, raw diff +1)");
        Check(rpt.WeiFinalPop == 11291 && rpt.WeiExpectedPop == 11291, $"Wei Pop == 11291 (exp 11291)");
        Check(rpt.WeiFinalGarrison == 1000 && rpt.WeiExpectedGarrison == 1000, $"Wei Garrison == 1000 (exp 1000)");

        Check(rpt.ShuFinalFood == 71 && rpt.ShuExpectedFood == 71, $"Shu Food == 71 (exp 71)");
        Check(rpt.ShuFinalGold == 1249 && rpt.ShuExpectedGold == 1248, $"Shu Gold == 1249 (exp 1248, raw diff +1)");
        Check(rpt.ShuFinalPop == 10304 && rpt.ShuExpectedPop == 10304, $"Shu Pop == 10304 (exp 10304)");
        Check(rpt.ShuFinalGarrison == 2000 && rpt.ShuExpectedGarrison == 2000, $"Shu Garrison == 2000 (exp 2000)");

        Check(!rpt.FinalScoreboardExactMatch, "FinalScoreboardExactMatch == false (raw +1 gold reported honestly)");
        Check(!rpt.RawExactMatch, "RawExactMatch == false (under CoreCLR due to S1 float truncation)");

        // 4. Entity position checks
        Check(rpt.DirectObservationCount == 9 && rpt.DirectObservationMatched == 9, $"DirectObservationCount == 9 (got {rpt.DirectObservationMatched}/{rpt.DirectObservationCount})");
        Check(rpt.CommandInferredCount == 1 && rpt.CommandInferredMatched == 1, $"CommandInferredCount == 1 (got {rpt.CommandInferredMatched}/{rpt.CommandInferredCount})");
        Check(rpt.UnverifiedSceneSetupCount == 2, $"UnverifiedSceneSetupCount == 2 (got {rpt.UnverifiedSceneSetupCount})");

        bool unverifiedCheckPass = true;
        foreach (var pc in rpt.PositionChecks)
        {
            if (pc.EntityId == "city-south" || pc.EntityId == "watch-tower-1")
            {
                if (pc.VerifiedByLogs || pc.Matched || pc.ExpectedX.HasValue || pc.ExpectedZ.HasValue)
                    unverifiedCheckPass = false;
            }
        }
        Check(unverifiedCheckPass, "city-south and watch-tower-1 are strictly unverified in logs (fog of war)");

        // 5. Temp directory file resolution & cross-validation tests
        RunTempDirectoryValidationTests(summaryPath, jsonlPath, exchangesPath);

        // 6. Raw value tampering test
        RunTamperedScoreboardTest(summaryPath, jsonlPath, exchangesPath);

        // 7. Synthetic action rejection accounting tests (engine-level validator & parse failure)
        RunRejectedActionsAccountingTest();

        // 8. Snapshot verification & printout
        string snapshot = session.GetDisplayedSummarySnapshot();
        Check(!string.IsNullOrEmpty(snapshot) && snapshot.Contains("OFFLINE REPLAY") && snapshot.Contains("Zero API calls"),
            "GetDisplayedSummarySnapshot() returns non-empty formatted summary snapshot");

        Console.WriteLine();
        Console.WriteLine("=== DISPLAYED SUMMARY SNAPSHOT ===");
        Console.WriteLine(snapshot);
        Console.WriteLine();
        Console.WriteLine(rpt.FormatReport());
    }

    private static void RunTempDirectoryValidationTests(string summaryPath, string jsonlPath, string exchangesPath)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi_replay_temp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(summaryPath, Path.Combine(tempDir, Path.GetFileName(summaryPath)));
            File.Copy(jsonlPath, Path.Combine(tempDir, Path.GetFileName(jsonlPath)));
            File.WriteAllText(Path.Combine(tempDir, "run-other-exchanges.ndjson"), "{}");

            bool resolved = ReplayLoader.TryResolveRunFiles(ReplayLoader.VerifiedDemoRunId, tempDir, out _, out _, out _, out string missingErr);
            Check(!resolved, "missing exchanges fails closed in temp dir");
            Check(missingErr != null && missingErr.Contains("missing exchanges"), "error mentions missing exchanges");

            // Mixed run_id in jsonl
            string fakeJsonl = Path.Combine(tempDir, "mixed.jsonl");
            string firstJsonlLine = File.ReadAllLines(jsonlPath)[0].Replace(ReplayLoader.VerifiedDemoRunId, "run-tampered-12345");
            File.WriteAllText(fakeJsonl, firstJsonlLine + "\n");
            bool threwMixedJsonl = false;
            try { ReplayLoader.Load(summaryPath, fakeJsonl, exchangesPath); }
            catch (InvalidDataException) { threwMixedJsonl = true; }
            Check(threwMixedJsonl, "mixed run_id in jsonl throws InvalidDataException");

            // Mixed run_id in exchanges
            string fakeExch = Path.Combine(tempDir, "mixed-exchanges.ndjson");
            string firstExchLine = File.ReadAllLines(exchangesPath)[0].Replace(ReplayLoader.VerifiedDemoRunId, "run-tampered-12345");
            File.WriteAllText(fakeExch, firstExchLine + "\n");
            bool threwMixedExch = false;
            try { ReplayLoader.Load(summaryPath, jsonlPath, fakeExch); }
            catch (InvalidDataException) { threwMixedExch = true; }
            Check(threwMixedExch, "mixed run_id in exchanges throws InvalidDataException");

            // Missing exchange entry
            string emptyExch = Path.Combine(tempDir, "empty-exchanges.ndjson");
            File.WriteAllText(emptyExch, "");
            bool threwMissingExch = false;
            try { ReplayLoader.Load(summaryPath, jsonlPath, emptyExch); }
            catch (InvalidDataException) { threwMissingExch = true; }
            Check(threwMissingExch, "missing exchange entry throws InvalidDataException");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static void RunTamperedScoreboardTest(string summaryPath, string jsonlPath, string exchangesPath)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi_tamper_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string tamperedSum = Path.Combine(tempDir, Path.GetFileName(summaryPath));
            string sumText = File.ReadAllText(summaryPath);
            string modified = sumText.Replace("\"food\":243", "\"food\":999").Replace("\"food\": 243", "\"food\": 999");
            File.WriteAllText(tamperedSum, modified);

            var tamperedBundle = ReplayLoader.Load(tamperedSum, jsonlPath, exchangesPath);
            var tamperedSession = new ReplaySession(tamperedBundle);
            tamperedSession.RunToCompletion();
            var rpt = tamperedSession.FinalReport;

            Check(rpt != null && !rpt.FinalScoreboardExactMatch, "tampered summary produces FinalScoreboardExactMatch == false");
            bool recordsRawMismatch = false;
            if (rpt != null && rpt.RawScoreboardMismatches != null)
            {
                foreach (var m in rpt.RawScoreboardMismatches)
                    if (m.Contains("Food") && (m.Contains("999") || m.Contains("-756"))) recordsRawMismatch = true;
            }
            Check(recordsRawMismatch, "RawScoreboardMismatches records raw food delta (243 vs 999, delta -756)");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static void RunRejectedActionsAccountingTest()
    {
        // Case 0: Parse failure (unparseable text)
        var fakeReqs0 = new List<ReplayRequestRecord>
        {
            new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                TurnToken = 1, Season = 1, RequestIndex = 1, TotalRequests = 1,
                ObservationHash = "hash00000000",
                RawModelText = "invalid gibberish action", ParseOk = false, ResultOk = false
            }
        };
        var bundle0 = new ReplayBundle
        {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeasonsPlanned = 1, SeasonsPlayed = 1, TotalRequests = 1 },
            Requests = fakeReqs0
        };
        var sess0 = new ReplaySession(bundle0);
        sess0.StepRequest();
        Check(sess0.RejectedActionsReplayed == 1, "case 0: parse failure increments RejectedActionsReplayed (1)");
        Check(sess0.AcceptedActionsReplayed == 0, "case 0: parse failure does NOT increment AcceptedActionsReplayed (0)");
        Check(sess0.ActionOutcomeMismatches == 0, "case 0: parse failure has ActionOutcomeMismatches == 0");

        // Case 1: Recorded rejected, Replayed rejected via engine validation (valid JSON march into fog)
        var fakeReqs1 = new List<ReplayRequestRecord>
        {
            new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                TurnToken = 1, Season = 1, RequestIndex = 1, TotalRequests = 1,
                ObservationHash = "hash00000001",
                RawModelText = "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":25,\"param_b\":16,\"reason\":\"march beyond sight\"}",
                ParseOk = true, ResultOk = false
            }
        };
        var bundle1 = new ReplayBundle
        {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeasonsPlanned = 1, SeasonsPlayed = 1, TotalRequests = 1 },
            Requests = fakeReqs1
        };
        var sess1 = new ReplaySession(bundle1);
        sess1.StepRequest();
        Check(sess1.RejectedActionsReplayed == 1, "case 1: engine rejected action increments RejectedActionsReplayed (1)");
        Check(sess1.AcceptedActionsReplayed == 0, "case 1: engine rejected action does NOT increment AcceptedActionsReplayed (0)");
        Check(sess1.ActionOutcomeMismatches == 0, "case 1: matching engine rejection has ActionOutcomeMismatches == 0");

        // Case 2: Recorded accepted, Replayed rejected via engine validation (unexpected rejection)
        var fakeReqs2 = new List<ReplayRequestRecord>
        {
            new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                TurnToken = 1, Season = 1, RequestIndex = 1, TotalRequests = 1,
                ObservationHash = "hash00000002",
                RawModelText = "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":25,\"param_b\":16,\"reason\":\"march beyond sight\"}",
                ParseOk = true, ResultOk = true
            }
        };
        var bundle2 = new ReplayBundle
        {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeasonsPlanned = 1, SeasonsPlayed = 1, TotalRequests = 1 },
            Requests = fakeReqs2
        };
        var sess2 = new ReplaySession(bundle2);
        sess2.StepRequest();
        Check(sess2.UnexpectedRejections == 1, "case 2: engine unexpected rejection increments UnexpectedRejections (1)");
        Check(sess2.ActionOutcomeMismatches == 1, "case 2: engine unexpected rejection increments ActionOutcomeMismatches (1)");
        Check(sess2.AcceptedActionsReplayed == 0, "case 2: engine unexpected rejection does NOT increment AcceptedActionsReplayed (0)");

        // Case 3: Recorded rejected, Replayed accepted via engine validation (unexpected acceptance)
        var fakeReqs3 = new List<ReplayRequestRecord>
        {
            new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
                TurnToken = 1, Season = 1, RequestIndex = 1, TotalRequests = 1,
                ObservationHash = "hash00000003",
                RawModelText = "{\"type\":\"reclaim\",\"target_id\":\"city-wei\",\"reason\":\"legal reclaim\"}",
                ParseOk = true, ResultOk = false
            }
        };
        var bundle3 = new ReplayBundle
        {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeasonsPlanned = 1, SeasonsPlayed = 1, TotalRequests = 1 },
            Requests = fakeReqs3
        };
        var sess3 = new ReplaySession(bundle3);
        sess3.StepRequest();
        Check(sess3.UnexpectedAcceptances == 1, "case 3: engine unexpected acceptance increments UnexpectedAcceptances (1)");
        Check(sess3.ActionOutcomeMismatches == 1, "case 3: engine unexpected acceptance increments ActionOutcomeMismatches (1)");
        Check(sess3.AcceptedActionsReplayed == 0, "case 3: engine unexpected acceptance does NOT increment AcceptedActionsReplayed (0)");
    }
}

namespace LuanShi.Harness
{
    internal static partial class Program
    {
        public static void RunReplay() => global::Harness.RunReplayPublic();
    }
}
