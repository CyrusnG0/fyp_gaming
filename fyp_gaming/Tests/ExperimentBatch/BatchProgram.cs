using System;
using System.Collections.Generic;
using System.IO;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// Offline experiment-batch tool.
    ///   validate &lt;manifest&gt;            check a manifest and print the planned matches
    ///   run &lt;manifest&gt; --out &lt;dir&gt;     run every arm with the scripted fake provider and write auditable evidence
    ///   test                          run the batch self-tests
    /// No network call is ever made and no credential is read.
    /// </summary>
    internal static class BatchProgram
    {
        private const string Usage =
            "LuanShi experiment batch (offline, fake provider only)\n" +
            "  validate <manifest.json>\n" +
            "  run <manifest.json> --out <dir>\n" +
            "  test\n";

        private static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "help";
            try
            {
                if (command == "test") return BatchSelfTests.Run();
                if (command == "validate" && args.Length == 2) return Validate(args[1]);
                if (command == "run" && args.Length == 4 && args[2] == "--out")
                {
                    // Test seam for the Python tool's regression tests: holds every fake reply back so wall-clock limits can be exercised.
                    if (int.TryParse(Environment.GetEnvironmentVariable("LUANSHI_BATCH_TEST_DELAY_MS"), out int delay) && delay > 0) ScriptedSeatTransport.DelayPerRequestMs = delay;
                    return Run(args[1], args[3]);
                }
            }
            catch (Exception e)
            {
                // Never print provider bodies, keys or stack traces.
                Console.Error.WriteLine("error: " + e.GetType().Name + ": " + e.Message);
                return 2;
            }
            Console.Error.WriteLine(Usage);
            return command == "help" || command == "--help" ? 0 : 2;
        }

        private static BatchManifest Load(string path, out string json)
        {
            json = File.ReadAllText(path);
            BatchManifest manifest = BatchManifest.Parse(json, out List<string> errors);
            if (manifest != null) errors.AddRange(BatchPlan.Validate(manifest));
            if (errors.Count == 0) return manifest;
            foreach (string error in errors) Console.Error.WriteLine("invalid: " + error);
            return null;
        }

        private static int Validate(string path)
        {
            BatchManifest manifest = Load(path, out _);
            if (manifest == null) return 2;
            foreach (MatchSpec spec in BatchPlan.Build(manifest))
                Console.WriteLine(spec.MatchId + "  pair=" + spec.PairId + "  settings_sha256=" + spec.SettingsHash);
            Console.WriteLine("manifest ok: " + manifest.BatchId + " @ " + manifest.PinnedCommit);
            return 0;
        }

        private static int Run(string path, string outDir)
        {
            BatchManifest manifest = Load(path, out string json);
            if (manifest == null) return 2;
            BatchResult result = BatchDriver.Run(manifest, json, outDir);
            Console.Write(BatchReport.Text(result));
            Console.WriteLine("evidence: " + Path.Combine(Path.GetFullPath(outDir), manifest.BatchId));
            return result.AllAuditsOk ? 0 : 1;
        }
    }
}
