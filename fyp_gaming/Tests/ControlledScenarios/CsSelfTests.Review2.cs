using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // CS-36..CS-38: persistence-store redaction, too-short configured keys, and percent-encoded
    // credentials in a base URL. No network call and no real credential: every test sets a
    // synthetic value in-process and restores it through LiveEnvLease.
    public static partial class CsSelfTests
    {
        private static void Review2CredentialGaps(TestLog log)
        {
            Guard(log, PersistedStoresAreRedacted);
            Guard(log, ShortConfiguredKeyIsRejected);
            Guard(log, PercentEncodedKeyForms);
        }

        // Built at runtime from parts so the literal never appears in source or output.
        private static string Review2Sentinel()
            => string.Concat("SENTINEL", "KEY-", "ab12", "cd34", "ef56", "gh78");

        // A key with characters that change under percent-encoding and JSON escaping.
        private static string Review2EncodedKey()
            => string.Concat("Ab", "+", "Cd", "/", "Ef", "=", "Gh", ":", "12345678");

        private static string PercentHexLower(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '%' && i + 2 < value.Length)
                {
                    sb.Append('%').Append(char.ToLowerInvariant(value[i + 1]))
                        .Append(char.ToLowerInvariant(value[i + 2]));
                    i += 2;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string JsonStringForm(string value)
        {
            string json = JsonSerializer.Serialize(value);
            return json.Length >= 2 ? json.Substring(1, json.Length - 2) : value;
        }

        private static bool HasAny(string text, params string[] needles)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrEmpty(needle) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static bool TreeHasAny(string directory, params string[] needles)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
            foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                if (HasAny(File.ReadAllText(path), needles)) return true;
            return false;
        }

        // ---- CS-36: notes.jsonl and memory/ are redacted at write time and by the sweep ----

        private static void PersistedStoresAreRedacted(TestLog log)
        {
            using (new LiveEnvLease())
            {
                string sentinel = Review2Sentinel();
                SetLiveEnv(key: sentinel);
                string root = NewRunDir("cs36");
                try
                {
                    int calls = 0;
                    var provider = new TestLiveProvider(ctx => new CountingTransport(_ =>
                    {
                        int n = Interlocked.Increment(ref calls);
                        if ((n & 1) == 1)
                            return LlmResponse.Success(
                                "{\"note\":\"private note echoing " + sentinel + "\"}",
                                1, 12, 6, "live-test-request", "stop", null, 18, null, null, 200);
                        return EndTurnReply("memory echoing " + sentinel);
                    }));
                    CsManifest manifest = LiveManifest(
                        new[] { "cs-memory-note-followup-v1" }, 4700);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsRunResult run;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        run = CsRunner.Run(manifest, root, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }

                    string scenarioDir = Path.Combine(run.RunDirectory,
                        "cs-memory-note-followup-v1");
                    string notesPath = Path.Combine(scenarioDir, "notes.jsonl");
                    bool noteSaved = false, noteMarker = false;
                    if (File.Exists(notesPath))
                    {
                        string notes = File.ReadAllText(notesPath);
                        noteSaved = notes.Trim().Length > 0;
                        noteMarker = notes.Contains(CsEvidence.RedactedKeyMarker)
                            && !HasAny(notes, sentinel);
                    }
                    string memoryDir = Path.Combine(scenarioDir, "memory");
                    bool memorySaved = false, memoryMarker = false;
                    if (Directory.Exists(memoryDir))
                    {
                        foreach (string path in Directory.GetFiles(memoryDir, "*",
                            SearchOption.AllDirectories))
                        {
                            string text = File.ReadAllText(path);
                            memorySaved = true;
                            if (text.Contains(CsEvidence.RedactedKeyMarker) && !HasAny(text, sentinel))
                                memoryMarker = true;
                        }
                    }
                    log.Check(noteSaved && noteMarker,
                        "[CS-36] a model-authored strategic note is persisted and redacted "
                        + "at write time", "saved=" + noteSaved + " marker=" + noteMarker);

                    log.Check(memorySaved && memoryMarker,
                        "[CS-36] a model-authored memory record is persisted and redacted "
                        + "at write time", "saved=" + memorySaved + " marker=" + memoryMarker);

                    bool leaked = HasAny(captured.ToString(), sentinel)
                        || TreeHasAny(run.RunDirectory, sentinel);
                    log.Check(run.RunDirectory != null && !leaked,
                        "[CS-36] the configured sentinel is absent from every run file and "
                        + "console capture", "leaked=" + leaked);
                }
                finally { Cleanup(root); }
            }
        }

        // ---- CS-37: configured keys shorter than 8 are rejected, never dispatched ----

        private static void ShortConfiguredKeyIsRejected(TestLog log)
        {
            using (new LiveEnvLease())
            {
                string shortKey = string.Concat("Sh", "ort", "K7"); // 7 characters
                SetLiveEnv(key: shortKey);
                CsLiveEnvironmentResult read = CsLiveEnvironment.Read();
                string error = read.Errors.Count == 0 ? null : string.Join("; ", read.Errors);
                bool namesOnly = error != null
                    && error.Contains("LUANSHI_LLM_API_KEY")
                    && error.Contains("too short")
                    && error.IndexOf(shortKey, StringComparison.Ordinal) < 0
                    && !HasDigit(error);
                log.Check(!read.Ok && namesOnly,
                    "[CS-37] a 7-character configured key is rejected by Read with a "
                    + "name-only message", error ?? "accepted");

                string absent = Path.Combine(CsPaths.AllowedRoot(CsPaths.RepoRoot()),
                    "selftest-cs37-" + Guid.NewGuid().ToString("N"));
                string message = null;
                try
                {
                    CsRunner.Run(LiveManifest(new[] { "cs-visible-contact-v1" }, 4710),
                        absent, CsPaths.RepoRoot(), new CsRunOptions { AllowLive = true });
                }
                catch (CsLiveConfigException e) { message = e.Message; }
                log.Check(message != null && message.Contains("too short")
                        && message.IndexOf(shortKey, StringComparison.Ordinal) < 0
                        && !Directory.Exists(absent),
                    "[CS-37] a live Run with the short key throws before any run directory exists",
                    message ?? "no exception");

                string scenarioRoot = NewTempDir("cs37-scenario");
                try
                {
                    CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4711);
                    spec.MaxRequests = 10; spec.MaxWallSeconds = 10; spec.RequestTimeoutMs = 5000;
                    var provider = new TestLiveProvider(
                        ctx => new CountingTransport(_ => EndTurnReply()));
                    Exception rejected = ScenarioException(spec, scenarioRoot, provider,
                        new CsRunBudget(10, 60000), 64, allowLive: true);
                    log.Check(rejected is CsLiveConfigException
                            && Directory.GetFileSystemEntries(scenarioRoot).Length == 0
                            && provider.Created == 0,
                        "[CS-37] RunScenario rejects the short key before any directory or "
                        + "transport", rejected == null ? "no exception" : rejected.GetType().Name);
                }
                finally { Cleanup(scenarioRoot); }

                // Exactly the minimum length is accepted.
                SetLiveEnv(key: string.Concat("Ab", "cd", "1234")); // 8 characters
                CsLiveEnvironmentResult eight = CsLiveEnvironment.Read();
                log.Check(eight.Ok,
                    "[CS-37] a key of exactly 8 characters is accepted by Read",
                    eight.Ok ? "accepted" : string.Join("; ", eight.Errors));
            }
        }

        private static bool HasDigit(string value)
        {
            foreach (char c in value) if (char.IsDigit(c)) return true;
            return false;
        }

        // ---- CS-38: percent-encoded key in the URL path and in replies/errors ----

        private static void PercentEncodedKeyForms(TestLog log)
        {
            using (new LiveEnvLease())
            {
                string key = Review2EncodedKey();
                string encoded = Uri.EscapeDataString(key);
                string lower = PercentHexLower(encoded);
                string jsonForm = JsonStringForm(key);
                string malicious = "https://example.invalid/v1/" + encoded + "/";

                SetLiveEnv(key: key, baseUrl: malicious);
                CsLiveEnvironmentResult read = CsLiveEnvironment.Read();
                string error = read.Errors.Count == 0 ? null : string.Join("; ", read.Errors);
                bool noRaw = error != null
                    && error.IndexOf(key, StringComparison.Ordinal) < 0
                    && error.IndexOf(encoded, StringComparison.OrdinalIgnoreCase) < 0;
                log.Check(!read.Ok && noRaw && error.Contains("LUANSHI_LLM_BASE_URL"),
                    "[CS-38] Read rejects a percent-encoded key in the path without echoing "
                    + "the raw or encoded form", error ?? "accepted");

                string display = CsLiveEnvironment.DisplayBaseUrl(malicious);
                log.Check(display != null
                        && display.IndexOf(key, StringComparison.Ordinal) < 0
                        && display.IndexOf(encoded, StringComparison.OrdinalIgnoreCase) < 0
                        && display.IndexOf('%') < 0,
                    "[CS-38] DisplayBaseUrl hides a percent-encoded key segment",
                    display ?? "null");

                string clean = CsEvidence.Redact(
                    "raw=" + key + " enc=" + encoded + " lower=" + lower + " json=" + jsonForm);
                log.Check(clean.IndexOf(key, StringComparison.Ordinal) < 0
                        && clean.IndexOf(encoded, StringComparison.OrdinalIgnoreCase) < 0
                        && clean.IndexOf(lower, StringComparison.OrdinalIgnoreCase) < 0
                        && clean.IndexOf(jsonForm, StringComparison.Ordinal) < 0
                        && clean.Contains(CsEvidence.RedactedKeyMarker),
                    "[CS-38] Redact removes the raw, percent-encoded and JSON-escaped forms");

                // Live reply echoes both hex cases; every file and the console must stay clean.
                SetLiveEnv(key: key, baseUrl: "https://example.invalid/v1");
                string root = NewRunDir("cs38-reply");
                try
                {
                    var provider = new TestLiveProvider(ctx => new CountingTransport(
                        _ => EndTurnReply("echo " + encoded + " plus " + lower + " plus " + key)));
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4720);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsRunResult run;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        run = CsRunner.Run(manifest, root, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }
                    bool leaked = HasAny(captured.ToString(), key, encoded, lower)
                        || TreeHasAny(run.RunDirectory, key, encoded, lower);
                    log.Check(run.RunDirectory != null && !leaked,
                        "[CS-38] a reply echoing the encoded key leaves no raw or encoded form "
                        + "in any run file or console capture", "leaked=" + leaked);
                }
                finally { Cleanup(root); }

                // Exception path: an encoded credential in the message must not reach disk/console.
                string throwRoot = NewRunDir("cs38-throw");
                try
                {
                    var transport = new ReviewThrowingTransport(
                        "provider failure containing " + encoded);
                    var provider = new TestLiveProvider(ctx => transport);
                    CsManifest manifest = LiveManifest(new[] { "cs-visible-contact-v1" }, 4725);
                    TextWriter stdout = Console.Out, stderr = Console.Error;
                    var captured = new StringWriter();
                    CsRunResult run;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        run = CsRunner.Run(manifest, throwRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }
                    bool leaked = transport.Calls > 0
                        && (HasAny(captured.ToString(), key, encoded, lower)
                            || TreeHasAny(run.RunDirectory, key, encoded, lower));
                    log.Check(transport.Calls > 0 && !leaked,
                        "[CS-38] an exception echoing the encoded key leaves no raw or encoded "
                        + "form in any run file or console capture",
                        "calls=" + transport.Calls + " leaked=" + leaked);
                }
                finally { Cleanup(throwRoot); }
            }
        }
    }
}
