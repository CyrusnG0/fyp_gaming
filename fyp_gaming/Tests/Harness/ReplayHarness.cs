using System;
using System.IO;
using LuanShi.Engine;

internal static partial class Harness
{
    public static void RunReplayPublic() => RunReplay();

    private static void RunReplay()
    {
        Section("offline match replay (zero model calls, 5-season verification)");

        string runId = "run-20261109082434809-51b7ebd5";
        bool resolved = ReplayLoader.TryResolveRunFiles(runId, out string summaryPath, out string jsonlPath, out string exchangesPath, out string error);
        Check(resolved, "resolve run files (" + (error ?? "found all 3 files") + ")");
        if (!resolved) return;

        Check(File.Exists(summaryPath), "summary path exists: " + Path.GetFileName(summaryPath));
        Check(File.Exists(jsonlPath), "jsonl path exists: " + Path.GetFileName(jsonlPath));
        Check(File.Exists(exchangesPath), "exchanges path exists: " + Path.GetFileName(exchangesPath));

        ReplayBundle bundle = ReplayLoader.Load(summaryPath, jsonlPath, exchangesPath);
        Check(bundle != null, "bundle loaded successfully");
        if (bundle == null) return;

        // 1. Summary metadata assertions
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

        // Step remaining requests of Season 1
        session.StepSeason();
        Check(session.CurrentSeason == 2, $"StepSeason() advanced to Season 2 (current season {session.CurrentSeason})");
        Check(session.CurrentRequestIndex == 10, $"Season 1 ended at request index 10 (got {session.CurrentRequestIndex})");

        // Step through Season 2, 3, 4, 5
        session.StepSeason(); // S2 -> S3
        Check(session.CurrentSeason == 3, $"advanced to Season 3 (index {session.CurrentRequestIndex})");
        session.StepSeason(); // S3 -> S4
        Check(session.CurrentSeason == 4, $"advanced to Season 4 (index {session.CurrentRequestIndex})");
        session.StepSeason(); // S4 -> S5
        Check(session.CurrentSeason == 5, $"advanced to Season 5 (index {session.CurrentRequestIndex})");
        session.StepSeason(); // S5 -> finish
        Check(session.IsCompleted, "session is completed after Season 5");
        Check(session.CurrentRequestIndex == 44, $"total requests stepped == 44 (got {session.CurrentRequestIndex})");

        // 3. Verification metrics
        Check(session.ObservationHashesMatched == 10, $"Season 1 raw observation hashes matched: {session.ObservationHashesMatched}/10");
        Check(session.NormalizedObservationsMatched == 44, $"all 44 observations matched (Mono F64 normalized): {session.NormalizedObservationsMatched}/44");
        Check(session.AcceptedActionsMatched == 42, $"accepted actions matched: {session.AcceptedActionsMatched}/42");
        Check(session.VoluntaryEndTurnsMatched == 2, $"voluntary end_turns matched: {session.VoluntaryEndTurnsMatched}/2");

        // 4. Scoreboard verification for S1..S5
        Check(session.ReplayedScores.Count == 5, $"5 seasons of scores recorded (got {session.ReplayedScores.Count})");
        int s1MonoTaxDelta = ((int)(9000 * BalanceConfig.TaxGoldPerPop * 1f * 1f)) - ((int)(9000.0 * (double)BalanceConfig.TaxGoldPerPop * 1.0 * 1.0));
        int[][] expectedSummaryScores = new int[][]
        {
            // S1: Wei G=939,F=1112,P=9294,Gar=1000; Shu G=939,F=1080,P=9295,Gar=1000
            new[] { 939, 1112, 9294, 1000, 939, 1080, 9295, 1000 },
            // S2: Wei G=1031,F=867,P=9860,Gar=1000; Shu G=971,F=715,P=8713,Gar=2000
            new[] { 1031, 867, 9860, 1000, 971, 715, 8713, 2000 },
            // S3: Wei G=1329,F=1090,P=10470,Gar=1000; Shu G=1058,F=731,P=9262,Gar=2000
            new[] { 1329, 1090, 10470, 1000, 1058, 731, 9262, 2000 },
            // S4: Wei G=1433,F=575,P=10853,Gar=1000; Shu G=1150,F=163,P=9812,Gar=2000
            new[] { 1433, 575, 10853, 1000, 1150, 163, 9812, 2000 },
            // S5: Wei G=1741,F=243,P=11291,Gar=1000; Shu G=1248,F=71,P=10304,Gar=2000
            new[] { 1741, 243, 11291, 1000, 1248, 71, 10304, 2000 }
        };

        bool allPopAndGarMatch = true;
        for (int s = 0; s < 5; s++)
        {
            var sb = session.ReplayedScores[s];
            var wei = sb.Scores[0];
            var shu = sb.Scores[1];
            int[] exp = expectedSummaryScores[s];
            if (wei.Gold - s1MonoTaxDelta != exp[0] || wei.Population != exp[2] || wei.Garrison != exp[3] ||
                shu.Gold - s1MonoTaxDelta != exp[4] || shu.Food != exp[5] || shu.Population != exp[6] || shu.Garrison != exp[7])
            {
                allPopAndGarMatch = false;
                Console.WriteLine($"  Scoreboard mismatch at Season {s + 1}: Wei G={wei.Gold} F={wei.Food} P={wei.Population} Gar={wei.Garrison} | Shu G={shu.Gold} F={shu.Food} P={shu.Population} Gar={shu.Garrison}");
            }
        }
        Check(allPopAndGarMatch, "all seasons S1..S5 populations, garrisons, Shu food, and Mono-F64 gold match recorded summary");

        // Specific Final S5 Scoreboard checks
        Check(session.FinalReport != null, "final comparison report generated");
        Check(session.FinalReport.WeiFinalFood == 243, $"Wei final food == 243 (got {session.FinalReport.WeiFinalFood})");
        Check(session.FinalReport.WeiMonoF64Gold == 1741, $"Wei final gold (Mono F64) == 1741, CoreCLR F32 == {session.FinalReport.WeiFinalGold}");
        Check(session.FinalReport.WeiFinalPop == 11291, $"Wei final pop == 11291 (got {session.FinalReport.WeiFinalPop})");
        Check(session.FinalReport.WeiFinalGarrison == 1000, $"Wei final garrison == 1000 (got {session.FinalReport.WeiFinalGarrison})");
        Check(session.FinalReport.ShuFinalFood == 71, $"Shu final food == 71 (got {session.FinalReport.ShuFinalFood})");
        Check(session.FinalReport.ShuMonoF64Gold == 1248, $"Shu final gold (Mono F64) == 1248, CoreCLR F32 == {session.FinalReport.ShuFinalGold}");
        Check(session.FinalReport.ShuFinalPop == 10304, $"Shu final pop == 10304 (got {session.FinalReport.ShuFinalPop})");
        Check(session.FinalReport.ShuFinalGarrison == 2000, $"Shu final garrison == 2000 (got {session.FinalReport.ShuFinalGarrison})");

        // 5. Entity Position checks
        Check(session.FinalReport.PositionChecks != null && session.FinalReport.PositionChecks.Count == 12, "all 12 position checks present");
        bool positionsMatched = true;
        foreach (var pc in session.FinalReport.PositionChecks)
        {
            if (!pc.Matched)
            {
                positionsMatched = false;
                Console.WriteLine($"  Position check failed: {pc.EntityId} exp=({pc.ExpectedX},{pc.ExpectedZ}) act=({pc.ActualX},{pc.ActualZ})");
            }
        }
        Check(positionsMatched, "all 12 entity positions matched (armies, capitals, neutral city & tower)");
        Check(session.FinalReport.AllVerifiedMatched, "FinalReport.AllVerifiedMatched == true (100% bit-exact)");

        // 6. Print FormatReport()
        Console.WriteLine();
        Console.WriteLine(session.FinalReport.FormatReport());
    }
}

namespace LuanShi.Harness
{
    internal static partial class Program
    {
        public static void RunReplay() => global::Harness.RunReplayPublic();
    }
}
