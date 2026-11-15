using System.Text;

namespace LuanShi.ExperimentBatch
{
    /// <summary>Human-readable batch report. Unknown values are printed as "unknown", never as zero.</summary>
    public static class BatchReport
    {
        public static string Text(BatchResult r)
        {
            var sb = new StringBuilder();
            sb.Append("batch ").Append(r.BatchId).Append(" @ ").Append(r.PinnedCommit)
              .Append("  manifest sha256 ").Append(r.ManifestSha256).Append('\n');
            sb.Append(r.Note).Append('\n');
            sb.Append("planned ").Append(r.PlannedMatches).Append(" matches; status:");
            foreach (var kv in r.StatusCounts) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            sb.Append('\n');
            sb.Append("requests ").Append(r.RequestsTotal).Append("; input tokens ").Append(r.InputTokensTotal.HasValue ? r.InputTokensTotal.Value.ToString() : "unknown")
              .Append("; output tokens ").Append(r.OutputTokensTotal.HasValue ? r.OutputTokensTotal.Value.ToString() : "unknown")
              .Append(r.MatchesWithUnknownUsage > 0 ? " (" + r.MatchesWithUnknownUsage + " match(es) without full usage)" : "").Append('\n');
            if (r.BatchCeilingsHit.Count > 0) sb.Append("batch ceilings hit: ").Append(string.Join(", ", r.BatchCeilingsHit)).Append('\n');
            sb.Append("all audits ok: ").Append(r.AllAuditsOk ? "yes" : "NO").Append("\n\n");
            foreach (MatchOutcome o in r.Matches)
            {
                sb.Append(o.MatchId).Append(": ").Append(o.Status);
                if (o.StopReason != null) sb.Append(" (").Append(o.StopReason).Append(')');
                if (!string.IsNullOrEmpty(o.Detail)) sb.Append(" - ").Append(o.Detail);
                sb.Append('\n');
                foreach (string t in o.TurnEndings) sb.Append("    ").Append(t).Append('\n');
                if (o.Audit == null) continue;
                sb.Append("    audit: ").Append(o.Audit.Ok ? "ok" : "MISMATCH").Append('\n');
                foreach (string s in o.Audit.Mismatches) sb.Append("      mismatch: ").Append(s).Append('\n');
                foreach (string s in o.Audit.Truncation) sb.Append("      truncated: ").Append(s).Append('\n');
                foreach (string s in o.Audit.Unknown) sb.Append("      unknown: ").Append(s).Append('\n');
            }
            foreach (PairComparison p in r.Pairs)
            {
                sb.Append('\n').Append(p.PairId).Append(" settings differ only in ").Append(p.SettingsCheck.Variable).Append(": ")
                  .Append(p.SettingsCheck.Ok ? "yes" : "NO").Append('\n');
                foreach (string d in p.SettingsCheck.Differences) sb.Append("    ").Append(d).Append('\n');
                foreach (string d in p.SettingsCheck.Problems) sb.Append("    problem: ").Append(d).Append('\n');
            }
            return sb.ToString();
        }
    }
}
