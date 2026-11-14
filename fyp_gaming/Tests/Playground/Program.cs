using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Entry point. Usage:
    ///   dotnet run --project Tests/Playground/Playground.csproj -c Release -- run
    ///   dotnet run --project Tests/Playground/Playground.csproj -c Release -- test
    /// The default provider is the scripted fake; no network call is made.
    /// </summary>
    internal static class Program
    {
        private const string Usage =
            "LuanShi headless playground\n" +
            "\n" +
            "Commands:\n" +
            "  run   Run one deterministic fake session and write trace.txt + results.json.\n" +
            "  test  Run focused in-process checks (seat isolation, Check, budget faults).\n" +
            "\n" +
            "Run options:\n" +
            "  --seed <n>              deterministic seed (default 4242)\n" +
            "  --seasons <n>           seasons to play (default 2)\n" +
            "  --out <dir>             output directory (default Tests/Playground/out/<run-id>)\n" +
            "  --run-label <text>      run label used in the run id\n" +
            "  --mode execute|check    execute normally, or validate only and stop (default execute)\n" +
            "  --no-preflight-check    skip the isolated Check before each executed order\n" +
            "  --no-fixture-memories   do not seed deterministic fixture memories\n" +
            "  --keep-stores           keep the local memory/notes stores instead of resetting them\n" +
            "  --provider <name>       fake (default) or a locally configured provider\n" +
            "  --allow-real-provider   required for any non-fake provider; needs a local API key\n" +
            "\n" +
            "The fake provider is the default and makes no network calls. Real-provider execution is\n" +
            "not exercised by this worker and requires an explicit local opt-in.\n";

        private static int Main(string[] args)
        {
            string command = args != null && args.Length > 0 ? args[0] : "run";
            if (command == "--help" || command == "-h" || command == "help")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            try
            {
                if (command == "run") return RunFromArgs(TrimFirst(args));
                if (command == "test") return PlaygroundSelfTests.Run();
                Console.Error.WriteLine("unknown command '" + command + "'\n");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            catch (PlaygroundRefusal refusal)
            {
                Console.Error.WriteLine("refused: " + refusal.Message);
                return 2;
            }
            catch (Exception exception)
            {
                // Never print provider bodies, keys or full stack traces from this tool.
                Console.Error.WriteLine("error: " + exception.GetType().Name + ": " + exception.Message);
                return 1;
            }
        }

        private static int RunFromArgs(string[] args)
        {
            PlaygroundConfig config = PlaygroundConfig.ParseArgs(args, out string error);
            if (config == null)
            {
                Console.Error.WriteLine("error: " + error + "\n");
                Console.Error.WriteLine(Usage);
                return 2;
            }

            string outputDirectory = string.IsNullOrWhiteSpace(config.OutputDir)
                ? Path.Combine(Environment.CurrentDirectory, "Tests", "Playground", "out", config.RunId)
                : Path.GetFullPath(config.OutputDir);
            Directory.CreateDirectory(outputDirectory);
            string memoryDirectory = Path.Combine(outputDirectory, "memory");
            string notePath = Path.Combine(outputDirectory, "notes.jsonl");
            if (config.ResetStores)
            {
                TryDeleteDirectory(memoryDirectory);
                TryDeleteFile(notePath);
            }

            var memoryStore = new LocalLlmMemoryStore(memoryDirectory);
            var noteStore = new FilePlaygroundNoteStore(notePath);
            var session = new PlaygroundSession(config, null, null, null, memoryStore, noteStore);
            PlaygroundRunRecord record = session.Run();
            PlaygroundOutput.Write(record, outputDirectory);

            Console.WriteLine("LuanShi playground: " + record.RunId);
            Console.WriteLine("  provider: " + record.Provider + (record.Fake ? " (FAKE)" : " (REAL)"));
            Console.WriteLine("  seed: " + record.Seed + "; scenario: " + record.ScenarioId);
            Console.WriteLine("  prompt: " + record.PromptBaseVersion + "+" + record.PromptAddendumVersion);
            Console.WriteLine("  requests: " + record.Requests.Count + "; inquiries: " + record.Inquiries.Count
                + "; actions accepted/rejected: " + record.AcceptedActions + "/" + record.RejectedActions);
            Console.WriteLine("  start hash: " + record.StartStateHash);
            Console.WriteLine("  end hash:   " + record.EndStateHash);
            int failed = 0;
            foreach (PlaygroundAssertion assertion in record.Assertions) if (!assertion.Ok) failed++;
            Console.WriteLine("  assertions: " + (record.Assertions.Count - failed) + " passed, " + failed + " failed");
            Console.WriteLine("  trace:   " + Path.Combine(outputDirectory, "trace.txt"));
            Console.WriteLine("  results: " + Path.Combine(outputDirectory, "results.json"));
            return failed == 0 ? 0 : 1;
        }

        private static string[] TrimFirst(string[] args)
        {
            if (args == null || args.Length <= 1) return new string[0];
            var result = new string[args.Length - 1];
            Array.Copy(args, 1, result, 0, result.Length);
            return result;
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception) { }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }
    }
}
