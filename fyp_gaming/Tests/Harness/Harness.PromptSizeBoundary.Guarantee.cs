using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [new] Semantic movement guarantee: with movement enabled and a host-offered legal destination,
// the prompt lists at least one legal destination or is unfit (never zero listed and "fits").
internal static partial class Harness
{
    private static void PromptSizeBoundaryGuarantee()
    {
        var crowded = new PromptSizeProbeCase { ArmyCount = 8, OptionsPerArmy = 60, VisibleRowCount = 40, InquiryChars = 30000 };
        foreach (int cap in new[] { 1500, 400, 150 })
        {
            RichContextInputs input = ProbeInputs(crowded);
            input.SpatialOptions.MaxSectionChars = cap;
            RichContextResult r = ProbeAssembleWithInputs(input);
            List<ReserveMarch> shown = ReserveDisplayedMarches(r.User);
            bool listed = shown.Count > 0 && ReserveMarchesLegal(shown, input.Movement);
            bool unfit = !r.Report.Fits && r.Report.UnfitReason == "no_legal_destination_fits" && shown.Count == 0;
            Check(listed ? r.Report.Fits : unfit,
                "[new] guarantee: crowded section, MaxSectionChars " + cap + " (< reserve " + RichContextAssembler.MovementReserveChars
                + "): " + shown.Count + " legal destination(s) listed, fits=" + r.Report.Fits + " reason=" + r.Report.UnfitReason);
        }

        RichContextInputs tiny = ProbeInputs(crowded);
        tiny.SpatialOptions.MaxSectionChars = 20;
        RichContextResult rt = ProbeAssembleWithInputs(tiny);
        Check(!rt.Report.Fits && rt.Report.UnfitReason == "no_legal_destination_fits"
              && ReserveDisplayedMarches(rt.User).Count == 0 && rt.SizeReport.ToJson().Contains("no_legal_destination_fits"),
            "[new] guarantee: MaxSectionChars 20 cannot hold one destination, so the prompt is unfit (" + rt.Report.UnfitReason + ")");

        RichContextInputs noLegal = ProbeInputs(new PromptSizeProbeCase { OptionsPerArmy = 0, InquiryChars = 30000 });
        noLegal.SpatialOptions.MaxSectionChars = 20;
        RichContextResult rn = ProbeAssembleWithInputs(noLegal);
        Check(rn.Report.UnfitReason != "no_legal_destination_fits",
            "[new] guarantee: no offered destination means nothing is demanded (" + rn.Report.UnfitReason + ")");

        ReserveRun stopped = ReserveSession(200000, "{\"type\":\"end_turn\"}", 20);
        string json = stopped.Runner.LastPromptSizeJson ?? "";
        Check(stopped.Fake.CallCount == 0 && stopped.Runner.FailureReason == "prompt too large"
              && json.Contains("\"unfit_reason\":\"no_legal_destination_fits\"", StringComparison.Ordinal),
            "[new] guarantee: runner with MaxSectionChars 20 stops with prompt too large before any provider call (calls="
            + stopped.Fake.CallCount + ")");
    }
}
