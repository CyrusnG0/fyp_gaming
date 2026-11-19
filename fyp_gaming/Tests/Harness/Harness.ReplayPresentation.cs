using System;
using System.IO;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunReplayPresentation()
    {
        Section("replay presentation (battle/rout/capture/explanation binding + reset lifecycle)");

        string csRunId = "run-20261119080859920-a52c3e69";
        string csRel = Path.Combine(".kimi-code", "worktrees", "prompt-size-integration",
            "runs", "controlled-scenarios", "controlled-20261119T080859Z-d6d9e6ca", "cs-route-seasons-v1");
        string csDir = null;
        string cur = Directory.GetCurrentDirectory();
        foreach (string probe in new[]
        {
            Path.Combine(cur, csRel),
            Path.Combine(cur, "..", csRel),
            Path.Combine(cur, "..", "..", csRel),
            Path.Combine(cur, "..", "..", "..", csRel),
            Path.Combine(cur, "..", "..", "..", "..", csRel),
        })
        {
            if (Directory.Exists(probe)) { csDir = Path.GetFullPath(probe); break; }
        }

        if (csDir == null || !ReplayLoader.TryResolveRunFiles(csRunId, csDir, out string css, out string csj, out string cse, out string csb, out _))
        {
            Check(false, $"[REPLAY-UI] could not locate {csRunId} under {csRel}");
            return;
        }

        ReplayBundle bundle = ReplayLoader.Load(css, csj, cse, csRunId, csb);
        var session = new ReplaySession(bundle, DemoMap()) { PauseOnDivergence = true };

        // Step -> Play -> Pause -> Reset -> Step -> RunToCompletion lifecycle
        Check(session.StepRequest(), "[REPLAY-UI] initial StepRequest() succeeds");
        session.Play();
        session.Tick(1.0f);
        session.Pause();
        int midIdx = session.CurrentRequestIndex;
        Check(midIdx >= 2 && session.EventFeed.Count == midIdx,
            $"[REPLAY-UI] Play+Tick+Pause advanced cleanly (idx={midIdx}, feed={session.EventFeed.Count})");

        session.Reset();
        Check(session.CurrentRequestIndex == 0 && session.EventFeed.Count == 0 && !session.IsCompleted,
            "[REPLAY-UI] Reset() clears EventFeed and resets CurrentRequestIndex to 0");

        session.RunToCompletion();
        Check(session.IsCompleted && !session.HasDivergence && session.EventFeed.Count == 31,
            $"[REPLAY-UI] RunToCompletion after Reset() completes 31/31 without divergence");

        bool sawAdvance = false, sawBattle1 = false, sawBattle2AndRout = false, sawCapture = false, sawInvalidWithRecordedExp = false;
        foreach (var ev in session.EventFeed)
        {
            if (ev.Faction == "Wei" && ev.Headline != null && ev.Headline.Contains("marched", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(ev.Explanation))
                sawAdvance = true;

            if (ev.OutcomeDetails != null && ev.OutcomeDetails.Contains("cs-army-own, 2000->1375 troops", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("cs-army-enemy, 2500->2095 troops", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("defender_holds", StringComparison.Ordinal)
                && ev.Faction == "Wei" && !string.IsNullOrEmpty(ev.Explanation))
                sawBattle1 = true;

            if (ev.OutcomeDetails != null && ev.OutcomeDetails.Contains("cs-army-enemy, 2095->1913 troops", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("cs-army-own, 1375->933 troops", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("defender_routed", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("Routed:", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("cs-army-own routed", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("Home", StringComparison.Ordinal)
                && ev.OutcomeDetails.Contains("with 654 troops", StringComparison.Ordinal)
                && ev.Faction == "Shu" && !string.IsNullOrEmpty(ev.Explanation))
                sawBattle2AndRout = true;

            if (ev.OutcomeDetails != null && ev.OutcomeDetails.Contains("Captured: Shu (seat-1) captured Ford Granary (cs-city-ford)", StringComparison.Ordinal)
                && ev.Faction == "Shu")
                sawCapture = true;

            if (ev.ActionType == "invalid" && ev.Sequence == 7
                && !string.IsNullOrEmpty(ev.Explanation)
                && ev.Explanation.Contains("Ford Granary", StringComparison.Ordinal))
                sawInvalidWithRecordedExp = true;

            Check(ev.PersonaId == "unknown",
                $"[REPLAY-UI] Req {ev.RequestIndex} PersonaId is 'unknown' without inference (got '{ev.PersonaId}')");
        }

        Check(sawAdvance, "[REPLAY-UI] feed binds Wei march headline + recorded explanation");
        Check(sawBattle1, "[REPLAY-UI] feed binds Battle 1 (Wei attacks Shu, defender_holds, troops & explanation)");
        Check(sawBattle2AndRout, "[REPLAY-UI] feed binds Battle 2 + Wei rout to Home with 654 troops & Shu explanation");
        Check(sawCapture, "[REPLAY-UI] feed binds Shu capture of Ford Granary (cs-city-ford)");
        Check(sawInvalidWithRecordedExp, "[REPLAY-UI] invalid reply seq=7 preserves its recorded explanation");

        // Missing explanation stays empty rather than being invented
        var noExpBundle = new ReplayBundle
        {
            Summary = bundle.Summary,
            ScenarioSetup = bundle.ScenarioSetup,
            SetupSource = bundle.SetupSource,
            MapSource = bundle.MapSource,
            Requests = new[]
            {
                new ReplayRequestRecord
                {
                    RunId = csRunId, SeatId = "seat-0", Season = 1, TurnToken = 1, RequestIndex = 1,
                    RawModelText = "not json", ParseOk = false, ResultOk = false, ResultRecorded = true,
                    RecordedExplanation = null
                }
            }
        };
        var noExpSession = new ReplaySession(noExpBundle, DemoMap()) { PauseOnDivergence = false };
        noExpSession.StepRequest();
        Check(noExpSession.EventFeed.Count == 1 && string.IsNullOrEmpty(noExpSession.EventFeed[0].Explanation),
            "[REPLAY-UI] missing recorded explanation remains empty rather than invented");
    }
}
