using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LuanShi.Engine;

// [new] Movement reserve + inquiry wording. A large inquiry result must give up space before the
// march destinations do; the destinations shown must be legal; if mandatory text plus the reserve
// cannot fit, the prompt is reported unfit (no silent removal of the reserve).
internal static partial class Harness
{
    private static void PromptSizeBoundaryReserve()
    {
        PromptSizeBoundaryReserveAssembler();
        PromptSizeBoundaryReserveCannotFit();
        PromptSizeBoundaryReserveRunner();
    }

    private static void PromptSizeBoundaryReserveAssembler()
    {
        RichContextInputs input = ProbeInputs(new PromptSizeProbeCase { InquiryChars = 30000 });
        RichContextResult r = ProbeAssembleWithInputs(input);
        string section = PromptSizeProbeMovementSection(r.User);
        List<ReserveMarch> shown = ReserveDisplayedMarches(r.User);
        Check(r.Report.Fits && r.Report.TrimLevel == 5 && r.Report.InquiryKeptChars < r.Report.InquiryOriginalChars
              && shown.Count > 0 && ReserveMarchesLegal(shown, input.Movement),
            "[new] reserve: a 30000-char inquiry is cut while " + shown.Count + " legal march destinations stay ("
            + SectionCounts(r.Report, "march destinations") + ", section " + section.Length + " chars)");

        // Wording: advice to repeat is only given when an inquiry is still possible.
        Check(r.User.Contains("you may repeat a narrower inquiry for the rest", StringComparison.Ordinal),
            "[new] reserve: with inquiries left the marker says the inquiry may be repeated narrower");
        RichContextInputs none = ProbeInputs(new PromptSizeProbeCase { InquiryChars = 30000 });
        none.Allowances.InquiriesLeftTurn = 0;
        RichContextResult rn = ProbeAssembleWithInputs(none);
        Check(rn.Report.InquiryKeptChars < rn.Report.InquiryOriginalChars
              && rn.User.Contains("no further inquiry is available this turn", StringComparison.Ordinal)
              && !rn.User.Contains("repeat a narrower inquiry", StringComparison.Ordinal),
            "[new] reserve: with no inquiry left the marker and note say so and do not advise repeating");
        RichContextInputs lastRequest = ProbeInputs(new PromptSizeProbeCase { InquiryChars = 30000 });
        lastRequest.Allowances.RequestsLeftTurn = 1;
        RichContextResult rl = ProbeAssembleWithInputs(lastRequest);
        Check(rl.User.Contains("no further inquiry is available this turn", StringComparison.Ordinal)
              && !rl.User.Contains("repeat a narrower inquiry", StringComparison.Ordinal),
            "[new] reserve: with one request left (this reply) the marker does not advise repeating either");
    }

    /// <summary>Mandatory text fits only if the reserve is dropped: the prompt must be reported unfit.</summary>
    private static void PromptSizeBoundaryReserveCannotFit()
    {
        MovementView movement = ProbeMovement(new PromptSizeProbeCase());
        var opts = new SpatialContextOptions { Level = SpatialDetailLevel.Rich, ProviderCharLimit = PromptSizeProbeLimit };
        int floorLen = PromptBuilder.RenderSpatialMovementSectionForBudget(movement, opts, 1, out _).Length;
        for (int chars = 1500; chars <= 2600; chars += 10)
        {
            RichContextInputs input = ProbeInputs(new PromptSizeProbeCase { HistoryLineChars = chars });
            RichContextResult r = ProbeAssembleWithInputs(input);
            if (r.Report.Fits) continue;
            string section = PromptSizeProbeMovementSection(r.User);
            int withoutReserve = r.Report.PromptChars - (section.Length - floorLen);
            Check(r.Report.UnfitReason == "mandatory_context_exceeds_limit" && section.Length > floorLen
                  && withoutReserve <= r.Report.MaxPromptChars && ReserveDisplayedMarches(r.User).Count > 0,
                "[new] reserve: at history " + chars + " chars the prompt would fit only without the reserve ("
                + withoutReserve + " vs " + r.Report.MaxPromptChars + "); it is reported unfit ("
                + r.Report.UnfitReason + "), reserve kept (" + section.Length + " > floor " + floorLen + ")");
            return;
        }
        Check(false, "[new] reserve: found a size where only the reserve makes the prompt overflow");
    }

    private sealed class ReserveRun
    {
        public FakeLlmTransport Fake = new FakeLlmTransport();
        public LlmTurnRunner Runner;
        public GameEngine Engine;
        public SpectatorSession Session;
        public int Limit;
        public int Total(int i) => Fake.Requests[i].System.Length + Fake.Requests[i].User.Length;
    }

    private static ReserveRun ReserveSession(int limit, string reply2)
    {
        var run = new ReserveRun { Limit = limit };
        run.Engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31011);
        LlmConfig config = LlmConfig.RichDeliberation();
        config.EnableManualLookup = true;
        config.MaxPromptChars = limit;
        config.SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Rich };
        run.Runner = new LlmTurnRunner(run.Fake, config, null);
        run.Session = new SpectatorSession(run.Engine, run.Runner, 1, 31011);
        run.Fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}", 0, 100, 20);
        run.Fake.EnqueueSuccess(reply2, 0, 100, 20);
        for (int i = 0; i < 3; i++) run.Fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 20);
        run.Session.Start();
        for (int i = 0; i < 64 && run.Session.State == SpectatorState.Running; i++) run.Session.Tick();
        return run;
    }

    private static void PromptSizeBoundaryReserveRunner()
    {
        const string endTurn = "{\"type\":\"end_turn\"}";
        ReserveRun open = ReserveSession(200000, endTurn);
        if (open.Fake.CallCount < 2) { Check(false, "[new] reserve runner: baseline made 2 requests"); return; }
        int first = open.Total(0), second = open.Total(1);

        // Lower the limit until the post-inquiry prompt must cut the inquiry result (movement is
        // squeezed to its reserve first, so the cut only starts once that is not enough).
        ReserveRun probe = null;
        for (int limit = second - 1000; limit > 12000 && probe == null; limit -= 500)
        {
            ReserveRun candidate = ReserveSession(limit, endTurn);
            if (candidate.Fake.CallCount >= 2 && candidate.Fake.Requests[1].User.Contains(
                    "[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal))
                probe = candidate;
        }
        if (probe == null) { Check(false, "[new] reserve runner: a limit exists that forces the inquiry cut"); return; }
        string user1 = probe.Fake.Requests[1].User;
        List<ReserveMarch> shown = ReserveDisplayedMarches(user1);
        MovementView legalNow = MovementPlanner.BuildDiversified(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31011), "seat-0",
            MovementPlanner.DefaultMaxOptionsPerArmy, null,
            new SpatialContextOptions { Level = SpatialDetailLevel.Rich });
        Check(shown.Count > 0 && ReserveMarchesLegal(shown, legalNow)
              && user1.Contains("march destinations across your armies", StringComparison.Ordinal)
              && user1.Contains("(INCOMPLETE)", StringComparison.Ordinal)
              && user1.Contains("you may repeat a narrower inquiry", StringComparison.Ordinal),
            "[new] reserve runner: after the inquiry the next prompt (limit " + probe.Limit + ", " + probe.Total(1)
            + " chars of " + second + " uncut) labels the cut and lists " + shown.Count + " destinations, all legal");
        bool within = true;
        for (int i = 0; i < probe.Fake.CallCount; i++) within &= probe.Total(i) <= probe.Limit;
        Check(within, "[new] reserve runner: every prompt of the turn stays within the " + probe.Limit + "-char limit");

        // Same limit, now march to a destination that the cut prompt displayed.
        ReserveMarch pick = shown.First(m => m.Army == "army-wei-1");
        string march = "{\"type\":\"march\",\"target_id\":\"" + pick.Army + "\",\"param_a\":" + pick.X
            + ",\"param_b\":" + pick.Z + ",\"summary\":\"RESERVE march\"}";
        ReserveRun go = ReserveSession(probe.Limit, march);
        ArmyState after = go.Engine.State.FindArmy("army-wei-1");
        Check(go.Fake.CallCount >= 3 && go.Fake.Requests[1].User == user1 && after != null
              && after.X == pick.X && after.Z == pick.Z
              && go.Fake.Requests[2].User.Contains("march army-wei-1 -> ok", StringComparison.Ordinal),
            "[new] reserve runner: marching to displayed (" + pick.X + "," + pick.Z + ") is accepted by the engine "
            + "and the next prompt records march army-wei-1 -> ok");

        // Mandatory content cannot fit: fail before any provider call with the explicit outcome.
        ReserveRun tiny = ReserveSession(first - 6000, endTurn);
        string json = tiny.Runner.LastPromptSizeJson ?? "";
        Check(tiny.Fake.CallCount == 0 && tiny.Runner.FailureReason == "prompt too large"
              && json.Contains("\"unfit_reason\":\"mandatory_context_exceeds_limit\"", StringComparison.Ordinal),
            "[new] reserve runner: limit " + (first - 6000) + " (mandatory text + reserve cannot fit) stops with "
            + "prompt too large before any provider call (calls=" + tiny.Fake.CallCount + ")");
    }

    private struct ReserveMarch { public string Army; public int X, Z; }

    /// <summary>Destinations listed in the "- army_id=... / options:" block of a rendered prompt.</summary>
    private static List<ReserveMarch> ReserveDisplayedMarches(string user)
    {
        var result = new List<ReserveMarch>();
        string army = null;
        foreach (string raw in user.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("- army_id=", StringComparison.Ordinal))
            {
                army = line.Substring(10).Split(' ')[0];
                continue;
            }
            if (army == null || !line.StartsWith("  options:", StringComparison.Ordinal)) continue;
            foreach (string part in line.Substring(10).Split(';'))
            {
                string[] xz = part.Trim().Split(' ')[0].Split(',');
                if (xz.Length == 2 && int.TryParse(xz[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    && int.TryParse(xz[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
                    result.Add(new ReserveMarch { Army = army, X = x, Z = z });
            }
            army = null;
        }
        return result;
    }

    private static bool ReserveMarchesLegal(List<ReserveMarch> shown, MovementView view)
        => shown.All(m => view.Armies.Any(a => a.ArmyId == m.Army && a.Options != null
                                               && a.Options.Any(o => o.X == m.X && o.Z == m.Z)));
}
