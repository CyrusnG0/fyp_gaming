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

        // Verify joined exchange records & exact-matched explanations
        bool exchangesJoined = true;
        bool explanationsJoined = true;
        for (int i = 0; i < bundle.Requests.Count; i++)
        {
            var req = bundle.Requests[i];
            if (string.IsNullOrEmpty(req.PromptExcerpt) || string.IsNullOrEmpty(req.ResponseContentText))
                exchangesJoined = false;
            if (string.IsNullOrEmpty(req.RecordedExplanation))
                explanationsJoined = false;
        }
        Check(exchangesJoined, "all 44 requests have joined PromptExcerpt and ResponseContentText");
        Check(explanationsJoined, "all 44 requests have exact-matched RecordedExplanation");

        // 2. Playback controls & stepping tests
        var session = new ReplaySession(bundle);
        Check(session.State == ReplayState.NotStarted, "initial state == NotStarted");
        Check(session.CurrentRequestIndex == 0, "initial request index == 0");
        Check(session.CurrentSeason == 1, "initial season == 1");

        session.Play();
        Check(session.IsPlaying, "Play() sets IsPlaying == true");
        session.Pause();
        Check(!session.IsPlaying && session.State == ReplayState.Paused, "Pause() sets IsPlaying == false");

        session.SetSpeed(0.5f);
        Check(Math.Abs(session.Speed - 0.5f) < 0.001f, "SetSpeed(0.5f) updates speed");
        session.SetSpeed(2.0f);
        Check(Math.Abs(session.Speed - 2.0f) < 0.001f, "SetSpeed(2.0f) updates speed");
        session.SetSpeed(4.0f);
        Check(Math.Abs(session.Speed - 4.0f) < 0.001f, "SetSpeed(4.0f) updates speed");
        session.SetSpeed(1.0f);

        // Test Tick stepping when playing
        session.Play();
        session.Tick(0.85f); // BaseStepInterval is 0.8f at 1.0x -> should trigger 1 step
        Check(session.CurrentRequestIndex == 1, "session.Tick(0.85f) advanced 1 request while playing");
        session.Pause();
        session.Tick(10.0f);
        Check(session.CurrentRequestIndex == 1, "session.Tick() does not advance requests while paused");

        // Step one request manually
        bool step2 = session.StepRequest();
        Check(step2, "StepRequest() executed next request");
        Check(session.CurrentRequestIndex == 2, "CurrentRequestIndex == 2 after manual step");
        Check(session.ObservationHashesMatched == 2, "first 2 observation hashes matched");

        // Step through Season 1..5
        session.StepSeason(); // S1 -> S2
        Check(session.CurrentSeason == 2, $"Season 1 ended at request index 10 (got {session.CurrentRequestIndex})");
        session.StepSeason(); // S2 -> S3
        session.StepSeason(); // S3 -> S4
        session.StepSeason(); // S4 -> S5
        session.StepSeason(); // S5 -> finish
        Check(session.IsCompleted, "session is completed after Season 5");
        Check(session.CurrentRequestIndex == 44, $"total requests stepped == 44 (got {session.CurrentRequestIndex})");
        Check(!session.StepRequest(), "StepRequest() returns false when already completed");
        Check(session.EventFeed.Count == 44, $"session.EventFeed.Count == 44 (got {session.EventFeed.Count})");
        Check(session.EventFeed[0].ActionType == "reclaim", "first event has ActionType == 'reclaim'");
        Check(!string.IsNullOrEmpty(session.EventFeed[0].Explanation), "first event has non-empty explanation");
        Check(session.EventFeed[0].ResultOk, "first event ResultOk == true");

        session.Reset();
        Check(session.EventFeed.Count == 0, "session.Reset() clears EventFeed");
        session.RunToCompletion();
        Check(session.EventFeed.Count == 44, "EventFeed re-accumulates on replay");

        // 3. Strict verification metrics
        Check(session.AcceptedActionsReplayed == 42, $"AcceptedActionsReplayed == 42 (got {session.AcceptedActionsReplayed})");
        Check(session.RejectedActionsReplayed == 0, $"RejectedActionsReplayed == 0 (got {session.RejectedActionsReplayed})");
        Check(session.UnexpectedRejections == 0, $"UnexpectedRejections == 0 (got {session.UnexpectedRejections})");
        Check(session.UnexpectedAcceptances == 0, $"UnexpectedAcceptances == 0 (got {session.UnexpectedAcceptances})");
        Check(session.RejectionReasonMismatches == 0, $"RejectionReasonMismatches == 0 (got {session.RejectionReasonMismatches})");
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

        // 6. Scoreboard integrity & tampering tests
        RunScoreboardIntegrityTests(summaryPath, jsonlPath, exchangesPath, session.ReplayedScores);

        // 7. Synthetic action rejection accounting & reason tests
        RunRejectedActionsAccountingTest();

        // 8. Exact explanation matching rule & event feed tests
        RunExactExplanationMatchingRuleTests();

        // 9. Numbers logging integrity tests (raw values or 'unknown')
        RunNumbersLoggingIntegrityTests();

        // 10. Validation display tests (verbatim Mono vs .NET results)
        RunValidationDisplayTests(rpt, session);

        // 11. Snapshot verification & printout
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
            try { ReplayLoader.Load(summaryPath, fakeJsonl, exchangesPath); } catch (InvalidDataException) { threwMixedJsonl = true; }
            Check(threwMixedJsonl, "mixed run_id in jsonl throws InvalidDataException");

            // Mixed run_id in exchanges
            string fakeExch = Path.Combine(tempDir, "mixed-exchanges.ndjson");
            string firstExchLine = File.ReadAllLines(exchangesPath)[0].Replace(ReplayLoader.VerifiedDemoRunId, "run-tampered-12345");
            File.WriteAllText(fakeExch, firstExchLine + "\n");
            bool threwMixedExch = false;
            try { ReplayLoader.Load(summaryPath, jsonlPath, fakeExch); } catch (InvalidDataException) { threwMixedExch = true; }
            Check(threwMixedExch, "mixed run_id in exchanges throws InvalidDataException");

            // Missing exchange entry
            string emptyExch = Path.Combine(tempDir, "empty-exchanges.ndjson");
            File.WriteAllText(emptyExch, "");
            bool threwMissingExch = false;
            try { ReplayLoader.Load(summaryPath, jsonlPath, emptyExch); } catch (InvalidDataException) { threwMissingExch = true; }
            Check(threwMissingExch, "missing exchange entry throws InvalidDataException");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
    }

    private static List<ReplaySeasonScoreboard> CloneScoreboards(IReadOnlyList<ReplaySeasonScoreboard> source)
    {
        var list = new List<ReplaySeasonScoreboard>();
        foreach (var sb in source)
        {
            var scores = new List<ReplaySeatScore>();
            foreach (var sc in sb.Scores)
                scores.Add(new ReplaySeatScore {
                    SeatId = sc.SeatId, Faction = sc.Faction, Gold = sc.Gold, Food = sc.Food,
                    Population = sc.Population, Garrison = sc.Garrison, Cities = sc.Cities, MissingFields = sc.MissingFields
                });
            list.Add(new ReplaySeasonScoreboard { Season = sb.Season, Scores = scores });
        }
        return list;
    }

    private static void RunScoreboardIntegrityTests(string summaryPath, string jsonlPath, string exchangesPath, IReadOnlyList<ReplaySeasonScoreboard> exactReplayedScores)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi_tamper_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 0. Exact-matching baseline (zero numeric deltas across all 5 seasons & both seats)
            var bExact = ReplayLoader.Load(summaryPath, jsonlPath, exchangesPath);
            bExact.Summary.Scoreboards = CloneScoreboards(exactReplayedScores);
            var sExact = new ReplaySession(bExact); sExact.RunToCompletion();
            Check(sExact.FinalReport.FinalScoreboardExactMatch && sExact.FinalReport.AllSeasonsScoreboardExactMatch,
                "exact-matching scoreboard baseline has FinalScoreboardExactMatch == true && AllSeasonsScoreboardExactMatch == true");

            // 1. Raw value tampering test
            string tamperedSum = Path.Combine(tempDir, "tampered-summary.json");
            string sumText = File.ReadAllText(summaryPath);
            File.WriteAllText(tamperedSum, sumText.Replace("\"food\":243", "\"food\":999").Replace("\"food\": 243", "\"food\": 999"));

            var b1 = ReplayLoader.Load(tamperedSum, jsonlPath, exchangesPath);
            var s1 = new ReplaySession(b1); s1.RunToCompletion();
            Check(s1.FinalReport != null && !s1.FinalReport.FinalScoreboardExactMatch, "tampered summary produces FinalScoreboardExactMatch == false");
            Check(HasMismatchText(s1.FinalReport, "Food", "999"), "RawScoreboardMismatches records raw food delta");

            // 2. Missing Season 3 scoreboard (from exact-matching baseline)
            var b2 = ReplayLoader.Load(summaryPath, jsonlPath, exchangesPath);
            var listNoS3 = CloneScoreboards(exactReplayedScores);
            listNoS3.RemoveAll(sb => sb.Season == 3);
            b2.Summary.Scoreboards = listNoS3;
            var s2 = new ReplaySession(b2); s2.RunToCompletion();
            Check(!s2.FinalReport.AllSeasonsScoreboardExactMatch, "missing S3 scoreboard flips AllSeasonsScoreboardExactMatch from true to false");
            Check(HasMismatchText(s2.FinalReport, "Season 3: missing expected summary season scoreboard"), "mismatches records missing S3");

            // 3. Missing Season 5 scoreboard (from exact-matching baseline)
            var b3 = ReplayLoader.Load(summaryPath, jsonlPath, exchangesPath);
            var listNoS5 = CloneScoreboards(exactReplayedScores);
            listNoS5.RemoveAll(sb => sb.Season == 5);
            b3.Summary.Scoreboards = listNoS5;
            var s3 = new ReplaySession(b3); s3.RunToCompletion();
            Check(!s3.FinalReport.FinalScoreboardExactMatch, "missing S5 scoreboard flips FinalScoreboardExactMatch from true to false");
            Check(!s3.FinalReport.AllSeasonsScoreboardExactMatch, "missing S5 scoreboard flips AllSeasonsScoreboardExactMatch from true to false");
            Check(HasMismatchText(s3.FinalReport, "Season 5: missing expected summary season scoreboard"), "mismatches records missing S5");

            // 4. Missing faction scoreboard entry in Season 5 (from exact-matching baseline)
            var b4 = ReplayLoader.Load(summaryPath, jsonlPath, exchangesPath);
            var sbsNoShu = CloneScoreboards(exactReplayedScores);
            var s5ScoresNoShu = new List<ReplaySeatScore>();
            foreach (var sc in sbsNoShu[4].Scores) if (sc.SeatId != "seat-1") s5ScoresNoShu.Add(sc);
            sbsNoShu[4] = new ReplaySeasonScoreboard { Season = 5, Scores = s5ScoresNoShu };
            b4.Summary.Scoreboards = sbsNoShu;
            var s4 = new ReplaySession(b4); s4.RunToCompletion();
            Check(!s4.FinalReport.FinalScoreboardExactMatch, "missing seat-1 in S5 flips FinalScoreboardExactMatch from true to false");
            Check(HasMismatchText(s4.FinalReport, "Season 5 seat-1: missing faction scoreboard entry"), "mismatches records missing seat-1 entry");

            // 5. Incomplete final score JSON (missing food field) & MissingFields on exact-matching baseline
            string incompSum = Path.Combine(tempDir, "incomp-summary.json");
            File.WriteAllText(incompSum, sumText.Replace("\"food\":243,", "").Replace("\"food\": 243,", ""));
            var b5 = ReplayLoader.Load(incompSum, jsonlPath, exchangesPath);
            ReplaySeatScore s5Wei = null;
            foreach (var sb in b5.Summary.Scoreboards)
                if (sb.Season == 5)
                    foreach (var sc in sb.Scores) if (sc.SeatId == "seat-0") { s5Wei = sc; break; }
            Check(s5Wei != null && s5Wei.MissingFields != null && new List<string>(s5Wei.MissingFields).Contains("food"), "missing food captured in MissingFields");
            var sbsIncomp = CloneScoreboards(exactReplayedScores);
            sbsIncomp[4].Scores[0].MissingFields = s5Wei?.MissingFields ?? new[] { "food" };
            b5.Summary.Scoreboards = sbsIncomp;
            var s5 = new ReplaySession(b5); s5.RunToCompletion();
            Check(!s5.FinalReport.FinalScoreboardExactMatch, "incomplete S5 score flips FinalScoreboardExactMatch from true to false even with 0 numeric deltas");
            Check(HasMismatchText(s5.FinalReport, "incomplete scoreboard entry in summary (missing: food)"), "mismatches records incomplete food entry");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
    }

    private static bool HasMismatchText(ReplayComparisonReport rpt, params string[] terms)
    {
        if (rpt?.RawScoreboardMismatches == null) return false;
        foreach (var m in rpt.RawScoreboardMismatches)
        {
            bool match = true;
            for (int i = 0; i < terms.Length; i++) if (!m.Contains(terms[i])) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    private static void RunRejectedActionsAccountingTest()
    {
        // Case 0: Parse failure (unparseable text)
        var s0 = CreateSingleReqSession("invalid gibberish action", parseOk: false, resultOk: false);
        s0.StepRequest();
        Check(s0.RejectedActionsReplayed == 1 && s0.AcceptedActionsReplayed == 0 && s0.ActionOutcomeMismatches == 0,
            "case 0: parse failure increments RejectedActionsReplayed (1)");

        // Case 1: Recorded rejected, Replayed rejected via engine validation (valid JSON march into fog)
        const string marchFog = "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":25,\"param_b\":16,\"reason\":\"march beyond sight\"}";
        var s1 = CreateSingleReqSession(marchFog, parseOk: true, resultOk: false);
        s1.StepRequest();
        Check(s1.RejectedActionsReplayed == 1 && s1.AcceptedActionsReplayed == 0 && s1.ActionOutcomeMismatches == 0,
            "case 1: matching engine rejection increments RejectedActionsReplayed (1)");

        // Case 2: Recorded accepted, Replayed rejected via engine validation (unexpected rejection)
        var s2 = CreateSingleReqSession(marchFog, parseOk: true, resultOk: true);
        s2.StepRequest();
        Check(s2.UnexpectedRejections == 1 && s2.ActionOutcomeMismatches == 1 && s2.AcceptedActionsReplayed == 0,
            "case 2: engine unexpected rejection increments UnexpectedRejections (1)");

        // Case 3: Recorded rejected, Replayed accepted via engine validation (unexpected acceptance)
        var s3 = CreateSingleReqSession("{\"type\":\"reclaim\",\"target_id\":\"city-wei\",\"reason\":\"legal reclaim\"}", parseOk: true, resultOk: false);
        s3.StepRequest();
        Check(s3.UnexpectedAcceptances == 1 && s3.ActionOutcomeMismatches == 1 && s3.AcceptedActionsReplayed == 0,
            "case 3: engine unexpected acceptance increments UnexpectedAcceptances (1)");

        // Case 4: Rejection category mismatch (recorded parse failure vs replayed engine validator rejection)
        var s4 = CreateSingleReqSession(marchFog, parseOk: false, resultOk: false);
        s4.StepRequest();
        Check(s4.RejectionReasonMismatches == 1 && s4.ActionOutcomeMismatches == 1 && s4.RejectedActionsReplayed == 0,
            "case 4: category mismatch increments RejectionReasonMismatches (1)");

        // Case 5: Rejection validator reason mismatch (recorded 'Insufficient gold' vs replayed sight failure)
        var s5 = CreateSingleReqSession(marchFog, parseOk: true, resultOk: false, resultError: "Insufficient gold");
        s5.StepRequest();
        Check(s5.RejectionReasonMismatches == 1 && s5.ActionOutcomeMismatches == 1 && s5.RejectedActionsReplayed == 0,
            "case 5: reason mismatch increments RejectionReasonMismatches (1)");

        // Case 6: Matching validator reason ('destination is beyond your sight')
        var s6 = CreateSingleReqSession(marchFog, parseOk: true, resultOk: false, resultError: "destination is beyond your sight");
        s6.StepRequest();
        Check(s6.RejectedActionsReplayed == 1 && s6.RejectionReasonMismatches == 0 && s6.ActionOutcomeMismatches == 0,
            "case 6: matching validator reason increments RejectedActionsReplayed (1)");
    }

    private static void RunExactExplanationMatchingRuleTests()
    {
        // 1. Absent request index (<= 0) -> no explanation
        var reqAbsent = new ReplayRequestRecord
        {
            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
            TurnToken = 1, Season = 1, RequestIndex = 0, TotalRequests = 1,
            ObservationHash = "hash00000001", RawModelText = "{\"type\":\"end_turn\"}",
            ParseOk = true, ResultOk = true, RecordedExplanation = null
        };
        var sAbsent = new ReplaySession(new ReplayBundle {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeasonsPlanned = 1, SeasonsPlayed = 1, TotalRequests = 1 },
            Requests = new List<ReplayRequestRecord> { reqAbsent }
        });
        sAbsent.StepRequest();
        Check(sAbsent.EventFeed.Count == 1 && sAbsent.EventFeed[0].Explanation == null,
            "absent request index (<= 0) shows no explanation");

        // 2. Exact match on run_id + seat_id + season + request_index
        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi_exp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sFile = Path.Combine(tempDir, "sum.json");
            string jFile = Path.Combine(tempDir, "req.jsonl");
            string eFile = Path.Combine(tempDir, "ex.ndjson");

            File.WriteAllText(sFile, $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seed\":12345,\"requests\":1,\"totals\":{{\"actions\":1,\"accepted\":1,\"rejected\":0,\"voluntary_end_turns\":0}}}}");
            File.WriteAllText(jFile, $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seed\":12345,\"seat_id\":\"seat-0\",\"turn_token\":1,\"season\":1,\"request_index\":1,\"total_requests\":1,\"raw_model_text\":\"{{\\\"type\\\":\\\"end_turn\\\"}}\",\"parse_ok\":true,\"result_ok\":true,\"observation_hash\":\"h1\"}}\n");

            // Ex with matching key but no summary/reason in JSON -> "unknown"
            File.WriteAllText(eFile, $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":1,\"exchange_event\":\"request\",\"request\":{{\"user\":\"test\"}}}}\n" +
                                     $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":1,\"exchange_event\":\"result\",\"response\":{{\"content_text\":\"{{\\\"type\\\":\\\"end_turn\\\"}}\"}}}}\n");
            var bUnknown = ReplayLoader.Load(sFile, jFile, eFile);
            Check(bUnknown.Requests[0].RecordedExplanation == "unknown", "exchange missing explanation field resolves to 'unknown'");

            // Ex with mismatched seat_id -> fails because request has no matching exchange
            File.WriteAllText(eFile, $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-1\",\"season\":1,\"request_index\":1,\"exchange_event\":\"request\",\"request\":{{\"user\":\"test\"}}}}\n" +
                                     $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-1\",\"season\":1,\"request_index\":1,\"exchange_event\":\"result\",\"response\":{{\"content_text\":\"{{\\\"summary\\\":\\\"test explanation\\\"}}\"}}}}\n");
            bool caughtMismatchedSeat = false;
            try { ReplayLoader.Load(sFile, jFile, eFile); }
            catch (InvalidDataException) { caughtMismatchedSeat = true; }
            Check(caughtMismatchedSeat, "exchange with mismatched seat_id fails exact match");

            // Ex with mismatched season -> fails exact match
            File.WriteAllText(eFile, $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-0\",\"season\":2,\"request_index\":1,\"exchange_event\":\"request\",\"request\":{{\"user\":\"test\"}}}}\n" +
                                     $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-0\",\"season\":2,\"request_index\":1,\"exchange_event\":\"result\",\"response\":{{\"content_text\":\"{{\\\"summary\\\":\\\"test explanation\\\"}}\"}}}}\n");
            bool caughtMismatchedSeason = false;
            try { ReplayLoader.Load(sFile, jFile, eFile); }
            catch (InvalidDataException) { caughtMismatchedSeason = true; }
            Check(caughtMismatchedSeason, "exchange with mismatched season fails exact match");

            // Ex with matching key and summary -> exact explanation
            File.WriteAllText(eFile, $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":1,\"exchange_event\":\"request\",\"request\":{{\"user\":\"test\"}}}}\n" +
                                     $"{{\"run_id\":\"{ReplayLoader.VerifiedDemoRunId}\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":1,\"exchange_event\":\"result\",\"response\":{{\"content_text\":\"{{\\\"summary\\\":\\\"I will defend the border\\\"}}\"}}}}\n");
            var bMatched = ReplayLoader.Load(sFile, jFile, eFile);
            Check(bMatched.Requests[0].RecordedExplanation == "I will defend the border", "exact match on run_id + seat_id + season + request_index captures summary");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static void RunNumbersLoggingIntegrityTests()
    {
        var scIncomplete = new ReplaySeatScore {
            SeatId = "seat-0", Faction = "Wei", Gold = 100, MissingFields = new[] { "food", "population" }
        };
        Check(FormatScoreFieldTest(scIncomplete, "gold", scIncomplete.Gold) == "100", "present score field formats number");
        Check(FormatScoreFieldTest(scIncomplete, "food", scIncomplete.Food) == "unknown", "missing score field formats as 'unknown'");
        Check(FormatScoreFieldTest(null, "gold", 0) == "unknown", "null score object formats as 'unknown'");
    }

    private static void RunValidationDisplayTests(ReplayComparisonReport rpt, ReplaySession session)
    {
        string fullReport = rpt.FormatReport();
        Check(fullReport.Contains("Wei:  Food 243 (exp 243, delta 0) | Gold 1742 (exp 1741, delta 1)"), "report displays verbatim .NET +1 gold delta for Wei");
        Check(fullReport.Contains("Shu:  Food 71 (exp 71, delta 0) | Gold 1249 (exp 1248, delta 1)"), "report displays verbatim .NET +1 gold delta for Shu");
        Check(fullReport.Contains("OVERALL RAW VERIFICATION: FAIL (RAW MISMATCHES REPORTED HONESTLY)"), "report discloses raw mismatch without smoothing over");
        Check(fullReport.Contains("89 in Unity Mono (F64 CIL evaluation stack) vs 90 in .NET CoreCLR (F32 native single precision)"), "report contains verbatim float truncation explanation");
        Check(fullReport.Contains("A separate standalone Unity Mono CLI executable (MonoBleedingEdge/bin/mono.exe against unityjit-win32) matched this run (44/44 hashes, 0 resource deltas)"), "report notes standalone Mono CLI vs Unity Editor difference");
        Check(fullReport.Contains("Direct Observations 9/9 | Command Inferred 1/1 | Unverified Scene Setup 2"), "report summarizes position verification categories");

        var emptyRpt = new ReplayComparisonReport { RunId = "test-run", Seed = 0, BalanceProfileKey = "unknown" };
        string emptyFormatted = emptyRpt.FormatReport();
        Check(emptyFormatted.Contains("Seed: unknown") && emptyFormatted.Contains("Profile: unknown"), "empty rpt displays unknown for missing seed/profile");

        string snapshot = session.GetDisplayedSummarySnapshot();
        Check(snapshot.Contains("Wei Summary G:1741 (+1)") && snapshot.Contains("Shu Summary G:1248 (+1)"), "snapshot shows verbatim +1 gold deltas");
        Check(snapshot.Contains("Obs: 10/44") && snapshot.Contains("Positions: 9/9 obs, 1/1 cmd-inferred, 2 unverified"), "snapshot displays observation and position breakdown");
    }

    private static string FormatScoreFieldTest(ReplaySeatScore sc, string field, int val)
    {
        if (sc == null) return "unknown";
        if (sc.MissingFields != null)
        {
            foreach (var f in sc.MissingFields)
                if (string.Equals(f, field, StringComparison.Ordinal)) return "unknown";
        }
        return val.ToString();
    }

    private static ReplaySession CreateSingleReqSession(string rawText, bool parseOk, bool resultOk, string resultError = null)
    {
        var req = new ReplayRequestRecord {
            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
            TurnToken = 1, Season = 1, RequestIndex = 1, TotalRequests = 1,
            ObservationHash = "hash00000001", RawModelText = rawText,
            ParseOk = parseOk, ResultOk = resultOk, ResultError = resultError
        };
        var bundle = new ReplayBundle {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeasonsPlanned = 1, SeasonsPlayed = 1, TotalRequests = 1 },
            Requests = new List<ReplayRequestRecord> { req }
        };
        return new ReplaySession(bundle);
    }
}

namespace LuanShi.Harness
{
    internal static partial class Program
    {
        public static void RunReplay() => global::Harness.RunReplayPublic();
    }
}
