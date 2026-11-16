using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LuanShi.ControlledScenarios
{
    /// <summary>Run-directory rules. The CLI refuses any --out outside &lt;repo root&gt;/runs/controlled-scenarios/.</summary>
    public static class CsPaths
    {
        public static string RepoRoot()
        {
            string found = Walk(Directory.GetCurrentDirectory());
            if (found != null) return found;
            return Walk(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
        }

        public static string AllowedRoot(string repoRoot)
            => Path.GetFullPath(Path.Combine(repoRoot, "runs", "controlled-scenarios"));

        public static string ResolveOut(string outArgument, string repoRoot)
        {
            string allowed = AllowedRoot(repoRoot);
            string full = Path.GetFullPath(string.IsNullOrWhiteSpace(outArgument)
                ? allowed : outArgument);
            if (!IsUnder(full, allowed))
                throw new InvalidOperationException("--out must be inside " + allowed
                    + " (got " + full + ")");
            return full;
        }

        public static bool IsUnder(string path, string root)
        {
            string p = Trim(path);
            string r = Trim(root);
            return string.Equals(p, r, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static string NewRunId()
            => "controlled-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "-"
               + Guid.NewGuid().ToString("N").Substring(0, 8);

        private static string Trim(string value)
            => Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string Walk(string start)
        {
            try
            {
                var dir = new DirectoryInfo(Path.GetFullPath(start));
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                        || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>Best-effort provenance. A git failure leaves fields null rather than aborting a run.</summary>
    public sealed class CsGitInfo
    {
        public const string BaseCommit = "19b1685";
        public string Branch { get; set; }
        public string HeadCommit { get; set; }
        public List<string> DirtyFiles { get; } = new List<string>();
        public List<string> UntrackedBuildInputs { get; } = new List<string>();
    }

    public static class CsGit
    {
        public static CsGitInfo Collect(string repoRoot)
        {
            var info = new CsGitInfo
            {
                Branch = Run(repoRoot, "rev-parse --abbrev-ref HEAD"),
                HeadCommit = Short(Run(repoRoot, "rev-parse HEAD")),
            };
            string status = Run(repoRoot, "status --porcelain");
            if (status == null) return info;
            foreach (string line in status.Split('\n'))
            {
                string entry = line.TrimEnd('\r');
                if (entry.Length == 0) continue;
                info.DirtyFiles.Add(entry);
                if (entry.StartsWith("?? ", StringComparison.Ordinal))
                {
                    string path = entry.Substring(3).Trim();
                    if (path.IndexOf("Tests/ControlledScenarios/", StringComparison.Ordinal) >= 0
                        || path.IndexOf("ControlledScenarios/", StringComparison.Ordinal) >= 0)
                        info.UntrackedBuildInputs.Add(path);
                }
            }
            return info;
        }

        private static string Short(string commit)
        {
            if (string.IsNullOrEmpty(commit)) return null;
            return commit.Length > 7 ? commit.Substring(0, 7) : commit;
        }

        private static string Run(string repoRoot, string arguments)
        {
            try
            {
                var start = new ProcessStartInfo("git", "-C \"" + repoRoot + "\" " + arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (Process process = Process.Start(start))
                {
                    if (process == null) return null;
                    string text = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    return process.ExitCode == 0 ? text.Trim() : null;
                }
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Status vocabulary shared by the report and the run manifest.</summary>
    public static class CsStatus
    {
        public const string Completed = "completed";
        public const string CompletedWithStops = "completed_with_stops";
        public const string Stopped = "stopped";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        public const string Incomplete = "incomplete";
    }

    /// <summary>Human/machine summary. An incomplete or unknown-usage run is never printed as a pass.</summary>
    public static class CsReport
    {
        /// <summary>
        /// First line of every text report. Live output is model-written and must never be read as
        /// a fixture; fake output is plumbing evidence only.
        /// </summary>
        public static string ProviderBanner(string kind, string model, bool live)
            => live
                ? "PROVIDER: LIVE " + kind + " " + model
                    + " -- model-written output, NOT fixtures\n"
                : "PROVIDER: FAKE (scripted fixtures, plumbing only)\n";

        public static string Text(CsRunResult result)
        {
            var sb = new StringBuilder();
            sb.Append(ProviderBanner(result.ProviderKind ?? "unknown",
                result.ProviderModel ?? "unknown", result.ProviderLive));
            if (result.ProviderLive)
                sb.Append("limits: ").Append(CsLimits.LimitsNote).Append('\n');
            sb.Append("run ").Append(result.RunId).Append("  status=").Append(result.Status);
            sb.Append("  scenarios=").Append(result.Scenarios.Count);
            sb.Append("  requests=").Append(result.Requests);
            sb.Append("  input_tokens=").Append(Token(result.InputTokens));
            sb.Append("  output_tokens=").Append(Token(result.OutputTokens));
            sb.Append("  unknown_usage_requests=").Append(result.UnknownUsageRequests);
            sb.Append("  elapsed_ms=").Append(result.ElapsedMs).Append('\n');
            if (!string.IsNullOrEmpty(result.Detail)) sb.Append("  detail: ").Append(result.Detail).Append('\n');
            foreach (CsScenarioResult scenario in result.Scenarios)
                sb.Append(ScenarioText(scenario));
            foreach (string limitation in result.Limitations)
                sb.Append("  limitation: ").Append(limitation).Append('\n');
            sb.Append("evidence: ").Append(result.RunDirectory).Append('\n');
            return CsEvidence.Redact(sb.ToString());
        }

        public static string ScenarioText(CsScenarioResult scenario)
        {
            var sb = new StringBuilder();
            sb.Append("  ").Append(scenario.ScenarioId).Append("  seed=").Append(scenario.Seed);
            sb.Append("  status=").Append(scenario.Status);
            if (!string.IsNullOrEmpty(scenario.Limit)) sb.Append("  limit=").Append(scenario.Limit);
            sb.Append("  requests=").Append(scenario.Requests);
            sb.Append("  dispatches=").Append(scenario.Dispatches);
            sb.Append("  accepted=").Append(scenario.Accepted);
            sb.Append("  rejected=").Append(scenario.Rejected);
            sb.Append("  inquiries=").Append(scenario.Inquiries);
            sb.Append("  notes=").Append(scenario.Notes);
            sb.Append("  privacy_leaks=").Append(scenario.PrivacyLeaks);
            sb.Append("  input_tokens=").Append(Token(scenario.InputTokens));
            sb.Append("  output_tokens=").Append(Token(scenario.OutputTokens));
            sb.Append("  unknown_usage_requests=").Append(scenario.UnknownUsageRequests);
            sb.Append("  elapsed_ms=").Append(scenario.ElapsedMs).Append('\n');
            if (!string.IsNullOrEmpty(scenario.Detail))
                sb.Append("    detail: ").Append(scenario.Detail).Append('\n');
            return CsEvidence.Redact(sb.ToString());
        }

        private static string Token(long? value) => value.HasValue ? value.Value.ToString() : "unknown";
    }
}
