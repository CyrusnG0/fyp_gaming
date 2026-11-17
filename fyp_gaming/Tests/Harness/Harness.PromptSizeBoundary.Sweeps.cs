using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LuanShi.Engine;

// [new] Deterministic sweeps and the fixed-width result table for the prompt-size probe.
internal static partial class Harness
{
    /// <summary>
    /// H-a sweep: keep every other input at the baseline and lengthen the 40 history lines.
    /// Returns the smallest line length that still overflows after trim level 4, or -1.
    /// </summary>
    private static int PromptSizeProbeSweepHistoryLines(StringBuilder rows)
    {
        int firstFail = -1;
        for (int lineChars = 0; lineChars <= 2000; lineChars++)
        {
            RichContextResult result = ProbeAssemble(new PromptSizeProbeCase { HistoryLineChars = lineChars });
            if (lineChars % 5 == 0 || (firstFail < 0 && !result.Report.Fits))
                ProbeWriteRow(rows, "history_line_chars", lineChars, result);
            if (firstFail < 0 && !result.Report.Fits) firstFail = lineChars;
        }
        return firstFail;
    }

    /// <summary>
    /// H-b sweep: inflate the observation (not the movement section) until the non-movement text
    /// alone reaches the 40000 ceiling. Reports the first failing count and the first count where the
    /// assembler's budget is forced to its 1-char floor.
    /// </summary>
    private static int PromptSizeProbeSweepObservationCities(StringBuilder rows,
        out RichContextResult firstFail, out int firstZeroCities, out RichContextResult firstZeroResult)
    {
        firstFail = null;
        firstZeroCities = -1;
        firstZeroResult = null;
        int firstFailCities = -1;
        for (int cities = 0; cities <= 200; cities++)
        {
            RichContextResult result = ProbeAssemble(new PromptSizeProbeCase { ExtraObservationCities = cities });
            bool budgetZero = PromptSizeProbeNonMovementReachesLimit(result);
            if (cities % 5 == 0 || (firstFailCities < 0 && !result.Report.Fits)
                || (firstZeroCities < 0 && budgetZero))
                ProbeWriteRow(rows, "extra_observation_cities", cities, result);
            if (firstFailCities < 0 && !result.Report.Fits)
            {
                firstFailCities = cities;
                firstFail = result;
            }
            if (firstZeroCities < 0 && budgetZero)
            {
                firstZeroCities = cities;
                firstZeroResult = result;
            }
        }
        return firstFailCities;
    }

    /// <summary>
    /// H-c/level-5 sweep: the manual_lookup result from the previous request in the same turn,
    /// 0..40000 in steps of 500. Level 5 bounds it, so this normally returns -1 (no overflow).
    /// </summary>
    private static int PromptSizeProbeSweepInquiry(StringBuilder rows)
    {
        int firstFail = -1;
        for (int len = 0; len <= 40000; len += 500)
        {
            RichContextResult result = ProbeAssemble(new PromptSizeProbeCase { InquiryChars = len });
            ProbeWriteRow(rows, "inquiry_chars", len, result);
            if (firstFail < 0 && !result.Report.Fits) firstFail = len;
        }
        return firstFail;
    }

    // ---- measurement helpers ---------------------------------------------------------------

    /// <summary>The exact spatial movement section inside a rendered rich prompt.</summary>
    private static string PromptSizeProbeMovementSection(string user)
    {
        if (string.IsNullOrEmpty(user)) return "";
        // The spatial movement section begins with SpatialHeader (the header itself starts with the
        // newline the assembler supplied) and ends just before the observation block.
        int start = user.IndexOf(PromptBuilder.SpatialHeader, StringComparison.Ordinal);
        if (start < 0) return "";
        int end = user.IndexOf("\nCURRENT SEAT-FILTERED OBSERVATION", start, StringComparison.Ordinal);
        return end < 0 ? user.Substring(start) : user.Substring(start, end - start);
    }

    /// <summary>
    /// True when the prompt without its movement section already equals/exceeds the ceiling, which is
    /// exactly the condition that forces the assembler's movement budget down to its floor.
    /// </summary>
    private static bool PromptSizeProbeNonMovementReachesLimit(RichContextResult result)
    {
        string movement = PromptSizeProbeMovementSection(result.User);
        // PromptChars = SystemChars + User.Length, so subtracting the movement section leaves the
        // system prompt plus every user-prompt section except movement.
        int nonMovement = result.Report.PromptChars - movement.Length;
        return nonMovement >= result.Report.MaxPromptChars;
    }

    // ---- output ----------------------------------------------------------------------------

    private static void ProbeWriteRow(StringBuilder rows, string variable, int value,
        RichContextResult result)
    {
        ProbeWriteRawRow(rows, variable, value, result.Report.TrimLevel, result.Report.PromptChars,
            result.Report.MaxPromptChars, result.Report.Fits,
            result.Report.SpatialTruncation?.MoveOptionsDropped ?? -1);
    }

    private static void ProbeWriteRawRow(StringBuilder rows, string variable, int value, int trimLevel,
        int promptChars, int limit, bool fits, int moveOptionsDropped)
    {
        rows.Append(variable).Append(',').Append(value.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(trimLevel < 0 ? "-" : trimLevel.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(promptChars.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(limit.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(fits ? "true" : "false")
            .Append(',').Append(moveOptionsDropped.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static string PromptSizeProbeOutputPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets"))
                && Directory.Exists(Path.Combine(dir.FullName, "Tests")))
                return Path.Combine(dir.FullName, "Tests", "PromptSizeProbe.out.txt");
            dir = dir.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "Tests", "PromptSizeProbe.out.txt");
    }

    private static bool PromptSizeProbeWrite(string path, StringBuilder rows)
    {
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, rows.ToString());
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
