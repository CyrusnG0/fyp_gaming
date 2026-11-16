using System;
using System.Collections.Generic;
using System.IO;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// LuanShi controlled-scenario runner (offline fake provider only).
    ///   run --manifest &lt;file&gt; [--out &lt;dir under repo&gt;/runs/controlled-scenarios]
    ///   validate --manifest &lt;file&gt;
    ///   test
    ///   provider-status
    ///   replay --run &lt;run-dir&gt; [--out &lt;dir under repo&gt;/runs/controlled-scenarios]
    /// No network call is made and no credential is read except a git provenance command.
    /// </summary>
    internal static class CsProgram
    {
        private const string Usage =
            "LuanShi controlled scenarios (offline fake provider only)\n" +
            "  run --manifest <manifest.json> [--out <dir>]\n" +
            "  validate --manifest <manifest.json>\n" +
            "  test\n" +
            "  provider-status\n" +
            "  replay --run <run-dir> [--out <dir>]\n";

        private static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "help";
            try
            {
                if (command == "test") return CsSelfTests.RunAll();
                if (command == "provider-status")
                {
                    Console.WriteLine(CsProviderFactory.StatusText);
                    return 0;
                }
                if (command == "validate")
                {
                    CsManifest manifest = LoadManifest(Required(args, "--manifest"), out int exit);
                    if (manifest == null) return exit;
                    Console.WriteLine("manifest ok: " + manifest.Scenarios.Count
                        + " scenario(s), provider.kind=" + manifest.Provider.Kind
                        + ", only the fake provider is available");
                    return 0;
                }
                if (command == "run")
                {
                    CsManifest manifest = LoadManifest(Required(args, "--manifest"), out int exit);
                    if (manifest == null) return exit;
                    string repo = CsPaths.RepoRoot();
                    CsRunResult result = CsRunner.Run(manifest, Optional(args, "--out"), repo);
                    Console.Write(CsReport.Text(result));
                    return result.Status == CsStatus.Completed ? 0 : 1;
                }
                if (command == "replay")
                {
                    string runDir = Required(args, "--run");
                    string repo = CsPaths.RepoRoot();
                    CsReplayResult replay = CsRunner.Replay(runDir, Optional(args, "--out"), repo);
                    foreach (string detail in replay.Details) Console.WriteLine("replay: " + detail);
                    Console.WriteLine("replay " + (replay.Matched ? "reproduced" : "MISMATCH")
                        + " -> " + replay.NewRunDirectory);
                    return replay.Matched ? 0 : 1;
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

        private static CsManifest LoadManifest(string path, out int exit)
        {
            exit = 2;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.Error.WriteLine("invalid: manifest not found");
                return null;
            }
            string json = File.ReadAllText(path);
            CsManifest manifest = CsManifest.Parse(json, out List<string> errors);
            if (manifest != null) return manifest;
            foreach (string error in errors) Console.Error.WriteLine("invalid: " + error);
            return null;
        }

        private static string Required(string[] args, string name)
        {
            string value = Optional(args, name);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("missing required " + name);
            return value;
        }

        private static string Optional(string[] args, string name)
        {
            for (int i = 1; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
