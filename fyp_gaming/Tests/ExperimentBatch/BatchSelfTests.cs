using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// Offline self-tests for the batch facility. Everything goes through the production runner,
    /// spectator session, recorder and summary writer with the scripted fake provider. Scripted replies
    /// are plumbing evidence, never model behaviour.
    /// </summary>
    internal static partial class BatchSelfTests
    {
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

        public static int Run()
        {
            var log = new TestLog();
            var tests = new List<KeyValuePair<string, Action<TestLog>>>
            {
                Entry("manifest: example parses, validates, carries no credential", ManifestExampleIsValid),
                Entry("manifest: credentials and unknown fields are refused", ManifestRefusesSecretsAndTypos),
                Entry("manifest: unset ceilings and inconsistent settings are refused", ManifestRefusesBadSettings),
                Entry("pairs: arms match except the declared variable", PairsDifferOnlyInTheVariable),
                Entry("pairs: a second difference or a wrong variable is caught", PairCheckCatchesExtraDifference),
                Entry("batch: deterministic matched pairs, evidence files and hashes", BatchRunsDeterministicPairs),
                Entry("audit: totals come from per-request records, not the summary's last command", AuditUsesPerRequestRecords),
                Entry("audit: a doctored summary is detected", AuditDetectsTamperedSummary),
                Entry("e2e: rich deliberation + research, per-seat isolation", EndToEndRichResearchIsolation),
                Entry("e2e: session request limit is retained, named and audited", EndToEndSessionLimit),
                Entry("e2e: failed provider response is retained and audited", EndToEndProviderFailure),
                Entry("e2e: missing usage and truncation are reported as unknown/truncated", EndToEndUnknownAndTruncated),
                Entry("batch: ceilings skip later matches but retain them", BatchCeilingsRetainSkipped),
                Entry("batch: evidence holds no credential and refuses overwrite", EvidenceIsSecretFreeAndNotOverwritten),
            Entry("review: skipped arm makes an incomplete pair, not a completed experiment", SkippedArmMakesAnIncompletePair),
            Entry("review: unknown usage is counted per match", UnknownUsageIsCountedPerMatch),
            Entry("review: system prompts compared by request identity", SystemPromptsAreComparedByRequestIdentity),
            Entry("review: wall-clock limits stop a running match", WallClockLimitsStopAMatch),
            Entry("review: input-token ceiling overshoots by one request then stops", InputTokenCeilingStopsAfterTheCrossingRequest),
            Entry("review: direct provider pause and invalid config are failed with auditable evidence", ProviderPauseAndInvalidConfigurationStatus),
            Entry("spatial: arms are configured through the production switch only", SpatialConfigurationIsTheProductionSwitch),
            Entry("spatial: the fake provider receives the production prompts, versions, hashes and truncation", SpatialPairSendsProductionPrompts),
            Entry("spatial: hidden-world equivalence and privacy across the pair", SpatialPairKeepsTheHiddenWorldAndPrivacy),
            Entry("spatial: a broken pair is caught", SpatialPairCheckCatchesABrokenPair),
            Entry("spatial: rich preset goes through production", SpatialRichLevelRunsThroughProduction),
            Entry("world digest: covers the whole canonical state, scope stated", WorldDigestCoversTheCanonicalState),
            Entry("privacy: judged per dispatch, hidden-visible-hidden and retained knowledge", PrivacyIsJudgedPerDispatch),
            Entry("privacy: the witness follows the real fog as an army appears and leaves", PrivacyWitnessFollowsTheRealFog),
            };
            foreach (KeyValuePair<string, Action<TestLog>> test in tests)
            {
                Console.WriteLine(test.Key);
                try { test.Value(log); }
                catch (Exception e)
                {
                    log.Failed++;
                    Console.WriteLine("  FAIL (exception) " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                }
            }
            Console.WriteLine();
            Console.WriteLine("experiment batch self-tests: " + log.Passed + " passed, " + log.Failed + " failed");
            return log.Failed == 0 ? 0 : 1;
        }

        private static KeyValuePair<string, Action<TestLog>> Entry(string name, Action<TestLog> test)
            => new KeyValuePair<string, Action<TestLog>>(name, test);

        // ---- fixtures ------------------------------------------------------------------

        private static string ExampleJson()
            => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "example-manifest.json"));

        private static BatchManifest Example(Action<BatchManifest> mutate = null)
        {
            BatchManifest m = BatchManifest.Parse(ExampleJson(), out List<string> errors);
            if (m == null) throw new InvalidOperationException("example manifest does not parse: " + string.Join("; ", errors));
            if (mutate != null) mutate(m);
            return m;
        }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "luanshi-batch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        private static BatchResult RunBatch(BatchManifest m, out string root)
        {
            root = TempDir();
            return BatchDriver.Run(m, m.ToJson(), root);
        }

        private static bool Has(List<string> errors, string fragment)
        {
            foreach (string e in errors) if (e.Contains(fragment)) return true;
            return false;
        }

        private static MatchOutcome Match(BatchResult r, string id)
        {
            foreach (MatchOutcome o in r.Matches) if (o.MatchId == id) return o;
            return null;
        }

        private static string SeatPrompts(MatchOutcome o, string seat, int? seatRequest = null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (ExchangeCapture c in o.Captures)
                if (c.Seat == seat && (!seatRequest.HasValue || c.SeatRequest == seatRequest.Value)) sb.Append(c.User).Append('\n');
            return sb.ToString();
        }
    }
}
