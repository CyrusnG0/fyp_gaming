using System;
using System.Collections.Generic;
using System.Globalization;
using LuanShi.Engine;

// [new] Long-match regression: one seat over 16 consecutive turns while history, events, memories,
// movement options and the latest manual_lookup result all keep growing. Every turn must either fit
// the 40,000-char ceiling or fail loudly with a reason; any trimmed section or inquiry must say so.
internal static partial class Harness
{
    private static void PromptSizeBoundaryLongTurn()
    {
        int turns = 16, continued = 0, trimmedTurns = 0, silentLoss = 0, maxChars = 0, lostMoves = 0;
        for (int t = 1; t <= turns; t++)
        {
            var c = new PromptSizeProbeCase
            {
                HistoryLines = 6 * t, HistoryLineChars = 60 + 8 * t, EventCount = 30 * t,
                MemoryCount = 4 * t, ArmyCount = 5, OptionsPerArmy = 12 + 2 * t,
                PriorResultCount = Math.Min(6, t), InquiryChars = t >= 3 ? 2500 * t : 0,
            };
            RichContextInputs input = ProbeInputs(c);
            RichContextResult r = ProbeAssembleWithInputs(input);
            RichContextReport rep = r.Report;
            bool candidates = r.User.Contains(ActionCatalogue.ToPromptText(input.Options),
                StringComparison.Ordinal);
            bool privacy = r.User.Contains("other factions' private information", StringComparison.Ordinal);
            bool budget = r.User.Contains("CONTEXT COMPLETENESS", StringComparison.Ordinal);
            bool anyTrim = false;
            foreach (RichContextReport.Section s in rep.Sections)
                if (s.Available >= 0 && s.Included < s.Available) anyTrim = true;
            bool inquiryCut = rep.InquiryKeptChars >= 0 && rep.InquiryKeptChars < rep.InquiryOriginalChars;
            bool labelled = !inquiryCut
                || (r.User.Contains("[INCOMPLETE: inquiry result cut from", StringComparison.Ordinal)
                    && r.User.Contains("(INCOMPLETE)", StringComparison.Ordinal));
            bool trimNoted = !(anyTrim || rep.TrimLevel > 0)
                || r.User.Contains("(INCOMPLETE)", StringComparison.Ordinal)
                || r.User.Contains("trim level", StringComparison.Ordinal);
            List<ReserveMarch> shown = ReserveDisplayedMarches(r.User);
            bool movesKept = shown.Count > 0 && ReserveMarchesLegal(shown, input.Movement);
            if (!movesKept) lostMoves++;
            if (!labelled || !trimNoted) silentLoss++;
            if (rep.Fits) continued++; else if (string.IsNullOrEmpty(rep.UnfitReason)) silentLoss++;
            if (rep.TrimLevel > 0) trimmedTurns++;
            maxChars = Math.Max(maxChars, rep.PromptChars);
            Console.WriteLine("  LONGTURN t=" + t + " level=" + rep.TrimLevel + " chars=" + rep.PromptChars
                + "/" + rep.MaxPromptChars + " fits=" + rep.Fits + " inquiry="
                + rep.InquiryKeptChars + "/" + rep.InquiryOriginalChars + " hist="
                + SectionCounts(rep, "own action feedback") + " mem=" + SectionCounts(rep, "long-term")
                + " events=" + SectionCounts(rep, "observation events") + " moves="
                + SectionCounts(rep, "march destinations") + " listed=" + shown.Count);
            Check(candidates && privacy && budget,
                "[new] long turn " + t + ": candidate orders, privacy line and completeness block kept");
        }
        Check(lostMoves == 0, "[new] long turns: every turn still lists legal march destinations (" + lostMoves + " without)");
        Check(continued == turns && silentLoss == 0 && trimmedTurns > 0 && maxChars <= PromptSizeProbeLimit,
            "[new] long turns: all " + turns + " turns fit " + PromptSizeProbeLimit + " chars (max "
            + maxChars + "), " + trimmedTurns + " trimmed, 0 unlabelled losses");
    }

    private static string SectionCounts(RichContextReport rep, string prefix)
    {
        foreach (RichContextReport.Section s in rep.Sections)
            if (s.Name.StartsWith(prefix, StringComparison.Ordinal))
                return s.Included.ToString(CultureInfo.InvariantCulture) + "/"
                    + s.Available.ToString(CultureInfo.InvariantCulture);
        return "-";
    }
}
