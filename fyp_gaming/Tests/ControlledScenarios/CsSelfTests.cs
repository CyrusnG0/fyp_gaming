using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Offline self-tests CS-01..CS-12. Every test drives the real controlled runner, evidence
    /// writer and production recorder with the fake provider. Scripted replies are labelled
    /// fixtures (source=fake_scripted); no test treats a particular choice as a pass condition.
    /// Output goes to the OS temp dir or a temporary directory under runs/controlled-scenarios/,
    /// and is removed afterwards.
    /// </summary>
    public static partial class CsSelfTests
    {
        private const string FakeModel = "fake-model";

        private sealed class TestLog
        {
            public int Passed, Failed;

            public void Check(bool ok, string name, string detail = null)
            {
                if (ok) { Passed++; Console.WriteLine("  PASS " + name); return; }
                Failed++;
                Console.WriteLine("  FAIL " + name + (detail == null ? "" : "\n       " + detail));
            }
        }

        public static int RunAll()
        {
            var log = new TestLog();
            var tests = new List<KeyValuePair<string, Action<TestLog>>>
            {
                Entry("[CS-01] EightScenariosEvidence", SevenScenariosEvidence),
                Entry("[CS-02] ReproduceFromRecordedSetup", ReproduceFromRecordedSetup),
                Entry("[CS-03] FogSeatIsolation", FogSeatIsolation),
                Entry("[CS-04] MalformedAndEmptyResponse", MalformedAndEmptyResponse),
                Entry("[CS-05] TimeoutOrStoppedRun", TimeoutOrStoppedRun),
                Entry("[CS-06] MissingUsageIsUnknown", MissingUsageIsUnknown),
                Entry("[CS-07] EvidenceLinksJoin", EvidenceLinksJoin),
                Entry("[CS-08] AcceptedAndRejectedAction", AcceptedAndRejectedAction),
                Entry("[CS-09] InquiryResultLinked", InquiryResultLinked),
                Entry("[CS-10] NoteAndMemoryFollowUp", NoteAndMemoryFollowUp),
                Entry("[CS-11] ManifestRefusesBadInput", ManifestRefusesBadInput),
                Entry("[CS-12] ProviderStatusHonesty", ProviderStatusHonesty),
                Entry("[CS-13] CeilingsAndTransportRegression", CeilingsAndTransportRegression),
                Entry("[CS-22] LiveProviderPlumbing", LiveProviderPlumbing),
                Entry("[CS-27] LiveTransportConfigAndBoundedRun", LiveTransportConfigAndBoundedRun),
                Entry("[CS-31] LiveGateRedactionAndBaseUrl", ReviewLiveSafety),
                Entry("[CS-34] StrongThreatLegalActions", StrongThreatLegalActions),
                Entry("[CS-40] DiplomacyVoluntaryResponse", DiplomacyVoluntaryResponse),
                Entry("[CS-35] WallCutAttribution", WallCutAttribution),
                Entry("[CS-36] CredentialGapRegression", Review2CredentialGaps),
                Entry("[CS-39] Review3Fixes", Review3Fixes),
                Entry("[CS-R1] RouteChoiceEngineChecks", RouteChoiceEngineChecks),
                Entry("[CS-R2] RouteChoiceLivePlumbing", RouteChoiceLivePlumbing),
                Entry("[CS-S1] RouteSeasonsEngineChecks", RouteSeasonsEngineChecks),
                Entry("[CS-S2] RouteSeasonsFakeRehearsal", RouteSeasonsFakeRehearsal),
                Entry("[CS-N1] NullReplyTextIsUnknown", NullReplyTextIsUnknown),
                Entry("[CS-T1] TacticalChoiceChecks", TacticalChoiceChecks),
                Entry("[LONG] LongDemoChecks", LongDemoChecks),
            };
            foreach (KeyValuePair<string, Action<TestLog>> test in tests)
            {
                Console.WriteLine(test.Key);
                try { test.Value(log); }
                catch (Exception e)
                {
                    log.Failed++;
                    Console.WriteLine("  FAIL (exception) " + e.GetType().Name + ": " + e.Message);
                }
            }
            Console.WriteLine();
            Console.WriteLine("controlled scenario self-tests: " + log.Passed + " passed, " + log.Failed + " failed");
            return log.Failed == 0 ? 0 : 1;
        }

        private static KeyValuePair<string, Action<TestLog>> Entry(string name, Action<TestLog> test)
            => new KeyValuePair<string, Action<TestLog>>(name, test);

        // ---- fixtures and evidence helpers ---------------------------------------------

        private static string NewTempDir(string label)
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "luanshi-cs-" + label + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string NewRunDir(string label)
        {
            string dir = Path.Combine(CsPaths.AllowedRoot(CsPaths.RepoRoot()),
                "selftest-" + label + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception) { }
        }

        private static CsGitInfo TestGit()
            => new CsGitInfo { Branch = "codex/controlled-scenarios", HeadCommit = CsGitInfo.BaseCommit };

        private static string DefaultManifestJson()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "CsDefaultManifest.json");
            if (!File.Exists(path))
                path = Path.Combine(CsPaths.RepoRoot(), "fyp_gaming", "Tests",
                    "ControlledScenarios", "CsDefaultManifest.json");
            return File.ReadAllText(path);
        }

        private static CsManifest DefaultManifest()
        {
            CsManifest manifest = CsManifest.Parse(DefaultManifestJson(), out List<string> errors);
            if (manifest == null) throw new InvalidOperationException(string.Join("; ", errors));
            return manifest;
        }

        /// <summary>Builds a valid manifest over registered scenarios, then lets a test change ceilings.</summary>
        private static CsManifest Manifest(string[] ids, int firstSeed, Action<CsRunCeilings> ceilings = null)
        {
            var manifest = new CsManifest
            {
                Schema = CsManifest.SchemaId,
                Provider = new CsProviderManifest { Kind = "fake", Model = FakeModel },
                RunCeilings = new CsRunCeilings
                {
                    MaxScenarios = ids.Length,
                    MaxRunRequests = 200,
                    MaxRunWallSeconds = 120,
                    MaxScenarioRequests = 40,
                    MaxScenarioWallSeconds = 30,
                    RequestTimeoutMs = 5000,
                    UnknownUsagePolicy = "report",
                },
            };
            for (int i = 0; i < ids.Length; i++)
                manifest.Scenarios.Add(new CsManifestScenario { ScenarioId = ids[i], Seed = firstSeed + i });
            ceilings?.Invoke(manifest.RunCeilings);
            List<string> errors = CsManifest.Validate(manifest);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            return manifest;
        }

        private static CsRunResult RunManifest(CsManifest manifest, string label, out string outRoot)
        {
            outRoot = NewRunDir(label);
            return CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot());
        }

        private static CsScenarioSpec Builtin(string scenarioId, int seed = 4242)
        {
            if (!CsScenarios.TryGet(scenarioId, seed, out CsScenarioSpec spec))
                throw new InvalidOperationException("unknown built-in scenario " + scenarioId);
            return spec;
        }

        /// <summary>Direct evidence run of one in-memory spec, into a caller-cleaned temp root.</summary>
        private static CsScenarioResult RunSpec(CsScenarioSpec spec, string label, out string root)
        {
            root = NewTempDir(label);
            if (spec.MaxRequests <= 0) spec.MaxRequests = 20;
            if (spec.MaxWallSeconds <= 0) spec.MaxWallSeconds = 30;
            if (spec.RequestTimeoutMs <= 0) spec.RequestTimeoutMs = 5000;
            return CsRunner.RunScenario(spec, root, FakeModel, TestGit(),
                "controlled-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        private static List<JsonElement> ReadJsonLines(string path)
        {
            var list = new List<JsonElement>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using (JsonDocument doc = JsonDocument.Parse(line)) list.Add(doc.RootElement.Clone());
            }
            return list;
        }

        private static string RunFile(string directory)
        {
            string[] files = Directory.GetFiles(directory, "run-*.jsonl");
            return files.Length == 0 ? null : files[0];
        }

        private static string ExchangeFile(string directory)
        {
            string[] files = Directory.GetFiles(directory, "run-*-exchanges.ndjson");
            return files.Length == 0 ? null : files[0];
        }

        private static string SummaryFile(string directory)
        {
            string[] files = Directory.GetFiles(directory, "pilot-*-summary.json");
            return files.Length == 0 ? null : files[0];
        }

        private static JsonElement Obj(JsonElement parent, string name)
        {
            if (parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Object) return value;
            return default;
        }

        private static JsonElement Prompt(JsonElement dispatch) => Obj(dispatch, "prompt");

        private static string PromptText(JsonElement dispatch)
        {
            JsonElement prompt = Prompt(dispatch);
            return (CsEvidenceRecorder.Text(prompt, "system") ?? "") + "\n"
                 + (CsEvidenceRecorder.Text(prompt, "user") ?? "");
        }

        private static string UserText(JsonElement dispatch)
            => CsEvidenceRecorder.Text(Prompt(dispatch), "user") ?? "";

        private static bool IsNull(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object
                || !parent.TryGetProperty(name, out JsonElement value)) return false;
            return value.ValueKind == JsonValueKind.Null;
        }

        private static bool Contains(JsonElement array, string value)
        {
            if (array.ValueKind != JsonValueKind.Array) return false;
            foreach (JsonElement item in array.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() == value) return true;
            return false;
        }

        private static JsonElement Find(IEnumerable<JsonElement> items, Func<JsonElement, bool> match)
        {
            foreach (JsonElement item in items) if (match(item)) return item;
            return default;
        }

        private static string BetweenMarkers(string text, string begin, string end)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf(begin, StringComparison.Ordinal);
            if (start < 0) return null;
            start += begin.Length;
            int finish = text.IndexOf(end, start, StringComparison.Ordinal);
            return finish < 0 ? null : text.Substring(start, finish - start).Trim();
        }

        private static bool Names(JsonElement parent, string arrayName, string value)
        {
            if (parent.ValueKind != JsonValueKind.Object
                || !parent.TryGetProperty(arrayName, out JsonElement array)) return false;
            return Contains(array, value);
        }

        private static string JsonStringField(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                    return doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty(name, out JsonElement value)
                        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            catch (JsonException) { return null; }
        }

        private static int IndexOf(IEnumerable<JsonElement> items, Func<JsonElement, bool> match)
        {
            int i = 0;
            foreach (JsonElement item in items) { if (match(item)) return i; i++; }
            return -1;
        }

        private static string NodeText(JsonElement element)
            => element.ValueKind == JsonValueKind.Undefined ? "{}"
             : element.ValueKind == JsonValueKind.String ? element.GetString()
             : element.GetRawText();
    }
}
