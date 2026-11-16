using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // CS-39: third-review fixes. (1) The key matcher accepts raw, freely mixed-case percent
    // encodings (including unreserved characters) and JSON-escaped forms everywhere evidence and
    // URL paths are checked. (2) The provider-injection RunScenario overload is internal and every
    // live dispatch validates the complete environment before any directory or transport.
    // (3) The directory sweep leaves binaries untouched and reports bounded, secret-free failures
    // that force a scenario/run to fail rather than report success.
    //
    // No network, no real credential: synthetic values are set in-process and restored by lease.
    public static partial class CsSelfTests
    {
        private static void Review3Fixes(TestLog log)
        {
            Guard(log, Review3MixedPercentRedaction);
            Guard(log, Review3DirectEntryIsInternal);
            Guard(log, Review3LiveRequiresFullEnvironment);
            Guard(log, Review3SweepFailureFold);
            Guard(log, Review3BinaryAndTextSweep);
        }

        // ---- form builders (never printed) ------------------------------------------------

        private static string Review3Key()
            => string.Concat("Ab", "+", "Cd", "/", "Ef", "=", "Gh", ":", "12345678");

        /// <summary>Escapes reserved characters and alternates hex case per escape.</summary>
        private static string Review3MixedCase(string key)
        {
            string encoded = Uri.EscapeDataString(key);
            var sb = new StringBuilder(encoded.Length);
            int escape = 0;
            for (int i = 0; i < encoded.Length; i++)
            {
                char c = encoded[i];
                if (c == '%' && i + 2 < encoded.Length)
                {
                    bool upper = (escape++ & 1) == 0;
                    sb.Append('%').Append(Hex(encoded[i + 1], upper)).Append(Hex(encoded[i + 2], upper));
                    i += 2;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Encodes only unreserved characters; reserved characters stay literal.</summary>
        private static string Review3UnreservedEncoded(string key)
        {
            var sb = new StringBuilder();
            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '~')
                    foreach (byte b in Encoding.UTF8.GetBytes(c.ToString()))
                        sb.Append('%').Append(b.ToString("x2"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Review3EncodeAll(string key, bool upper)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(key))
                sb.Append('%').Append(b.ToString(upper ? "X2" : "x2"));
            return sb.ToString();
        }

        private static char Hex(char c, bool upper)
            => upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);

        private static bool Review3HasAny(string text, params string[] needles)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrEmpty(needle)
                    && text.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ---- CS-39a: all supported percent forms are redacted at every boundary ----

        private static void Review3MixedPercentRedaction(TestLog log)
        {
            using (new LiveEnvLease())
            {
                string key = Review3Key();
                string mixed = Review3MixedCase(key);
                string unreserved = Review3UnreservedEncoded(key);
                string upper = Review3EncodeAll(key, true);
                string lower = Review3EncodeAll(key, false);
                string[] forms = { key, mixed, unreserved, upper, lower };
                SetLiveEnv(key: key);

                string clean = CsEvidence.Redact("raw=" + key + " mixed=" + mixed
                    + " unreserved=" + unreserved + " upper=" + upper + " lower=" + lower);
                log.Check(!Review3HasAny(clean, forms)
                        && clean.Contains(CsEvidence.RedactedKeyMarker),
                    "[CS-39] Redact removes raw, mixed-case, unreserved, upper and lower forms");
                string negative = "unrelated%2Bvalue%2fkept";
                log.Check(CsEvidence.Redact(negative) == negative,
                    "[CS-39] an unrelated percent string is left untouched");

                string root = NewRunDir("cs39-forms");
                try
                {
                    var provider = new TestLiveProvider(ctx => new CountingTransport(_ =>
                        EndTurnReply("echo " + string.Join(" | ", forms))));
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4800);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsRunResult run;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        run = CsRunner.Run(manifest, root, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }

                    bool leaked = Review3HasAny(captured.ToString(), forms);
                    bool marker = false;
                    foreach (string path in Directory.GetFiles(run.RunDirectory, "*",
                        SearchOption.AllDirectories))
                    {
                        string text = File.ReadAllText(path);
                        if (Review3HasAny(text, forms)) leaked = true;
                        if (text.Contains(CsEvidence.RedactedKeyMarker)) marker = true;
                    }
                    log.Check(run.RunDirectory != null && !leaked && marker,
                        "[CS-39] no key form reaches any run file or the console, marker present",
                        "leaked=" + leaked + " marker=" + marker);
                }
                finally { Cleanup(root); }
            }
        }

        // ---- CS-39b: only the string-kind overload is public ----

        private static void Review3DirectEntryIsInternal(TestLog log)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo injected = typeof(CsRunner).GetMethod("RunScenario", flags, null, new[]
            {
                typeof(CsScenarioSpec), typeof(string), typeof(IScenarioProvider),
                typeof(CsGitInfo), typeof(string), typeof(CsRunBudget), typeof(CsRunCeilings),
                typeof(int?), typeof(bool),
            }, null);
            MethodInfo byKind = typeof(CsRunner).GetMethod("RunScenario", new[]
            {
                typeof(CsScenarioSpec), typeof(string), typeof(string), typeof(CsGitInfo),
                typeof(string), typeof(CsRunBudget), typeof(string), typeof(CsRunCeilings),
            });
            log.Check(injected != null && !injected.IsPublic && injected.IsAssembly,
                "[CS-39] the IScenarioProvider RunScenario overload is internal, not public");
            log.Check(byKind != null && byKind.IsPublic,
                "[CS-39] the public string-kind RunScenario overload is preserved");
        }

        // ---- CS-39c: a live dispatch needs the full environment before anything exists ----

        private static void Review3LiveRequiresFullEnvironment(TestLog log)
        {
            using (new LiveEnvLease())
            {
                CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4810);
                spec.MaxRequests = 10; spec.MaxWallSeconds = 10; spec.RequestTimeoutMs = 5000;

                ClearLiveEnv();
                string missingRoot = NewTempDir("cs39-env-missing");
                try
                {
                    var provider = new TestLiveProvider(
                        ctx => new CountingTransport(_ => EndTurnReply()));
                    Exception rejected = ScenarioException(spec, missingRoot, provider,
                        new CsRunBudget(10, 60000), 64, allowLive: true);
                    string message = rejected == null ? null : rejected.Message;
                    bool named = message != null;
                    foreach (string name in CsLiveEnvironment.EnvVarNames)
                        named &= message != null && message.Contains(name);
                    log.Check(rejected is CsLiveConfigException && named
                            && provider.Created == 0
                            && Directory.GetFileSystemEntries(missingRoot).Length == 0,
                        "[CS-39] missing live env throws before any directory or CreateTransport",
                        rejected == null ? "no exception" : rejected.GetType().Name
                            + " created=" + provider.Created);
                }
                finally { Cleanup(missingRoot); }

                string invalidRoot = NewTempDir("cs39-env-invalid");
                try
                {
                    SetLiveEnv(baseUrl: "http://example.invalid/v1");
                    var provider = new TestLiveProvider(
                        ctx => new CountingTransport(_ => EndTurnReply()));
                    Exception rejected = ScenarioException(spec, invalidRoot, provider,
                        new CsRunBudget(10, 60000), 64, allowLive: true);
                    string message = rejected == null ? null : rejected.Message;
                    log.Check(rejected is CsLiveConfigException
                            && message != null && message.Contains("LUANSHI_LLM_BASE_URL")
                            && message.IndexOf("example.invalid", StringComparison.Ordinal) < 0
                            && provider.Created == 0
                            && Directory.GetFileSystemEntries(invalidRoot).Length == 0,
                        "[CS-39] an invalid base URL is rejected without echoing it",
                        message ?? "no exception");
                }
                finally { Cleanup(invalidRoot); }

                ClearLiveEnv();
                string fakeRoot = NewTempDir("cs39-env-fake");
                try
                {
                    CsScenarioResult fake = CsRunner.RunScenario(spec, fakeRoot,
                        new CsFakeScenarioProvider(FakeModel), TestGit(), "review3-fake");
                    log.Check(fake != null && !fake.ProviderLive
                            && Directory.Exists(Path.Combine(fakeRoot, spec.ScenarioId)),
                        "[CS-39] the fake provider still runs without any live environment");
                }
                finally { Cleanup(fakeRoot); }
            }
        }

        // ---- CS-39d: sweep failures are bounded, secret-free and never a success ----

        private static void Review3SweepFailureFold(TestLog log)
        {
            string key = Review3Key();
            using (new LiveEnvLease())
            {
                SetLiveEnv(key: key);
                string dir = NewTempDir("cs39-sweep");
                try
                {
                    string locked = Path.Combine(dir, "artifact.txt");
                    File.WriteAllText(locked, "leaking " + key, new UTF8Encoding(false));
                    using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        CsRedactionSweepResult sweep = CsEvidence.RedactDirectory(dir);
                        bool named = sweep.Failed && sweep.Total == 1
                            && sweep.Paths.Count == 1 && sweep.Paths[0] == "artifact.txt";
                        bool secretFree = sweep.Summary != null
                            && sweep.Summary.IndexOf(key, StringComparison.Ordinal) < 0;
                        log.Check(named && secretFree,
                            "[CS-39] a locked text artifact is named by a secret-free sweep summary",
                            sweep.Summary ?? "no failure");

                        var scenario = new CsScenarioResult { Status = CsStatus.Completed };
                        CsEvidence.ApplySweepFailure(scenario, sweep);
                        log.Check(scenario.Status == CsStatus.Failed
                                && scenario.Detail != null
                                && scenario.Detail.StartsWith("redaction sweep failed", StringComparison.Ordinal)
                                && scenario.Detail.IndexOf(key, StringComparison.Ordinal) < 0,
                            "[CS-39] the scenario folding helper marks Failed with bounded detail",
                            scenario.Detail ?? "null");

                        var run = new CsRunResult { Status = CsStatus.Completed };
                        CsEvidence.ApplyRunSweepFailure(run, sweep);
                        log.Check(run.Status == CsStatus.Failed && run.Detail != null
                                && run.Detail.StartsWith("redaction sweep failed", StringComparison.Ordinal)
                                && run.Detail.IndexOf(key, StringComparison.Ordinal) < 0,
                            "[CS-39] the run folding helper marks Failed with bounded detail",
                            run.Detail ?? "null");
                    }
                }
                finally { Cleanup(dir); }

                string manyRoot = NewTempDir("cs39-sweep-many");
                var locks = new List<FileStream>();
                try
                {
                    for (int i = 0; i < 12; i++)
                    {
                        string path = Path.Combine(manyRoot, "part-" + i.ToString("D2") + ".txt");
                        File.WriteAllText(path, "x", new UTF8Encoding(false));
                        locks.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
                    }
                    CsRedactionSweepResult sweep = CsEvidence.RedactDirectory(manyRoot);
                    log.Check(sweep.Failed && sweep.Total == 12
                            && sweep.Paths.Count == CsRedactionSweepResult.MaxListed
                            && sweep.Summary != null && sweep.Summary.Contains("..."),
                        "[CS-39] the failure summary lists at most 10 paths plus the total count",
                        "total=" + sweep.Total + " listed=" + sweep.Paths.Count);
                }
                finally
                {
                    foreach (FileStream stream in locks) stream.Dispose();
                    Cleanup(manyRoot);
                }
            }
        }

        // ---- CS-39e: binaries are byte-identical; text is redacted without a BOM ----

        private static void Review3BinaryAndTextSweep(TestLog log)
        {
            string key = Review3Key();
            using (new LiveEnvLease())
            {
                SetLiveEnv(key: key);
                string dir = NewTempDir("cs39-binary");
                try
                {
                    byte[] binary = { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xC3, 0x28, 0xFE };
                    var bytes = new List<byte>(binary);
                    bytes.AddRange(Encoding.UTF8.GetBytes(key));
                    bytes.Add(0x00);
                    byte[] payload = bytes.ToArray();
                    string png = Path.Combine(dir, "image.png");
                    string bin = Path.Combine(dir, "blob.bin");
                    File.WriteAllBytes(png, payload);
                    File.WriteAllBytes(bin, payload);

                    CsRedactionSweepResult sweep = CsEvidence.RedactDirectory(dir);
                    bool same = !sweep.Failed
                        && SameBytes(payload, File.ReadAllBytes(png))
                        && SameBytes(payload, File.ReadAllBytes(bin));
                    log.Check(same,
                        "[CS-39] binary files with invalid UTF-8 and key bytes stay byte-identical");
                }
                finally { Cleanup(dir); }

                string textDir = NewTempDir("cs39-text");
                try
                {
                    string path = Path.Combine(textDir, "report.txt");
                    File.WriteAllText(path, "token=" + key, new UTF8Encoding(false));
                    CsRedactionSweepResult sweep = CsEvidence.RedactDirectory(textDir);
                    byte[] written = File.ReadAllBytes(path);
                    string text = new UTF8Encoding(false, true).GetString(written);
                    bool noBom = written.Length < 3 || !(written[0] == 0xEF
                        && written[1] == 0xBB && written[2] == 0xBF);
                    log.Check(!sweep.Failed && noBom
                            && text.Contains(CsEvidence.RedactedKeyMarker)
                            && !Review3HasAny(text, key),
                        "[CS-39] a text artifact with the key is redacted and written without a BOM",
                        "bom=" + !noBom);

                    // Documented decision: a text-extension file that cannot be strictly decoded
                    // (NUL or invalid UTF-8) cannot be proven key-free, so the sweep reports it and
                    // leaves its bytes untouched rather than silently succeeding.
                    byte[] opaque = { 0x41, 0xFF, 0x00, 0x42 };
                    string opaquePath = Path.Combine(textDir, "opaque.txt");
                    File.WriteAllBytes(opaquePath, opaque);
                    CsRedactionSweepResult opaqueSweep = CsEvidence.RedactDirectory(textDir);
                    log.Check(opaqueSweep.Failed && opaqueSweep.Paths.Contains("opaque.txt")
                            && SameBytes(opaque, File.ReadAllBytes(opaquePath)),
                        "[CS-39] an undecodable text artifact is reported and left byte-identical");
                }
                finally { Cleanup(textDir); }
            }
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
