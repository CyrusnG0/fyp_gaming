using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // A failed LlmResponse carries no text. The evidence writer must name the missing reply text as
    // unknown (null), while a successful response with an empty string stays an observed empty reply.
    public static partial class CsSelfTests
    {
        private sealed class CannedProvider : IScenarioProvider
        {
            private readonly Func<LlmResponse> next;
            public CannedProvider(Func<LlmResponse> next) { this.next = next; }
            public string Kind => "fake";
            public string Model => FakeModel;
            public bool IsLive => false;
            public ILlmTransport CreateTransport(CsProviderContext context) => new CannedTransport(next);
        }

        private sealed class CannedTransport : ILlmTransport
        {
            private readonly Func<LlmResponse> next;
            public CannedTransport(Func<LlmResponse> next) { this.next = next; }
            public string Provider => "fake";
            public string Model => FakeModel;
            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
                => Task.FromResult(next());
        }

        private static JsonElement FirstDispatchWithReply(CsRunResult run)
        {
            List<JsonElement> all = ReadJsonLines(Path.Combine(run.Scenarios[0].Directory, "dispatches.ndjson"));
            return Find(all, d => Obj(d, "reply").ValueKind == JsonValueKind.Object
                && CsEvidenceRecorder.Text(d, "dispatch_state") != "refused_before_dispatch");
        }

        private static CsRunResult RunCanned(Func<LlmResponse> next, string label, out string outRoot)
        {
            outRoot = NewRunDir(label);
            var options = new CsRunOptions { ProviderOverride = new CannedProvider(next) };
            return CsRunner.Run(Manifest(new[] { "cs-visible-contact-v1" }, 4242), outRoot,
                CsPaths.RepoRoot(), options);
        }

        private static void NullReplyTextIsUnknown(TestLog log)
        {
            // 1. Provider failure with no text: raw_text is null and response_text is named unknown.
            CsRunResult failed = RunCanned(
                () => LlmResponse.Failure(LlmErrorKind.Server, "injected failure", 7), "csn1", out string root1);
            try
            {
                JsonElement d = FirstDispatchWithReply(failed);
                JsonElement reply = Obj(d, "reply");
                log.Check(d.ValueKind != JsonValueKind.Undefined && IsNull(reply, "raw_text"),
                    "[CS-N1] a failed response with no text records reply.raw_text as null");
                log.Check(Names(reply, "unknown_fields", "response_text")
                        && Names(d, "unknown_fields", "reply.response_text"),
                    "[CS-N1] the null reply text is named in reply.unknown_fields and the flat list");
                log.Check(CsEvidenceRecorder.Text(d, "status") != "invalid_response"
                        && CsEvidenceRecorder.Bool(Obj(d, "action"), "accepted") != true,
                    "[CS-N1] provider failure stays distinct from an invalid response or accepted action",
                    "status=" + CsEvidenceRecorder.Text(d, "status"));
                AuditHasNoUnknownFieldMismatch(log, failed.RunDirectory);
            }
            finally { Cleanup(root1); }

            // 2. Successful response: text is kept and response_text is not unknown.
            CsRunResult ok = RunCanned(
                () => LlmResponse.Success(CsScenarioFactory.EndTurn("fine"), 5, 10, 3, "req-ok", "stop", 0, 13),
                "csn2", out string root2);
            try
            {
                JsonElement reply = Obj(FirstDispatchWithReply(ok), "reply");
                string text = CsEvidenceRecorder.Text(reply, "raw_text");
                log.Check(!string.IsNullOrEmpty(text) && !Names(reply, "unknown_fields", "response_text"),
                    "[CS-N2] a successful response keeps its text and does not name response_text unknown");
            }
            finally { Cleanup(root2); }

            // 3. Observed empty string: raw_text is "", not null, and response_text is not unknown.
            CsRunResult empty = RunCanned(
                () => LlmResponse.Success("", 5, 10, 0, "req-empty", "stop", 0, 10), "csn3", out string root3);
            try
            {
                JsonElement d = FirstDispatchWithReply(empty);
                JsonElement reply = Obj(d, "reply");
                bool emptyString = reply.TryGetProperty("raw_text", out JsonElement raw)
                    && raw.ValueKind == JsonValueKind.String && raw.GetString() == "";
                log.Check(emptyString && !Names(reply, "unknown_fields", "response_text")
                        && !Names(d, "unknown_fields", "reply.response_text"),
                    "[CS-N3] an observed empty reply stays an empty string, distinct from null");
            }
            finally { Cleanup(root3); }
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from))
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        // The independent Python audit must report the failed run as incomplete (exit 3), with no
        // MISMATCH. The run is audited from a short temp path: Python's isfile fails past Windows'
        // 260-character limit, which the worktree path plus the nested run folders can exceed.
        private static void AuditHasNoUnknownFieldMismatch(TestLog log, string runDirectory)
        {
            string script = Path.Combine(CsPaths.RepoRoot(), "tools", "luanshi_controlled_audit.py");
            string shortCopy = Path.Combine(Path.GetTempPath(), "csn-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                CopyTree(runDirectory, shortCopy);
                var start = new ProcessStartInfo("python", "\"" + script + "\" audit \"" + shortCopy + "\"")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                };
                using (Process p = Process.Start(start))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    log.Check(p.ExitCode == 3 && output.IndexOf("MISMATCH", StringComparison.Ordinal) < 0,
                        "[CS-N1] the Python audit reports the failed run incomplete with no mismatch",
                        "exit=" + p.ExitCode + " " + output.Trim());
                }
            }
            catch (Win32Exception)
            {
                Console.WriteLine("  SKIP [CS-N1] python is not on PATH; the Python audit was not run here");
            }
            finally { Cleanup(shortCopy); }
        }
    }
}
