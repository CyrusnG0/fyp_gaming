using System;
using System.Collections.Generic;
using LuanShi.Engine;

// Headless assertion harness for the LuanShi engine. Compiles the real engine sources
// (Assets/GameEngine/*.cs) into a console app — no UnityEngine involved.
//
// The assertions were recovered verbatim from the historical headless harness (P6–P15; its only
// surviving copy lived in a temp directory) and split by feature across Harness.*.cs. Sections marked
// [new] were added during recovery and review. See ../README.md.

internal static partial class Harness
{
    private static int passed, failed;

    private static void Check(bool ok, string name)
    {
        if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
        else { failed++; Console.WriteLine("  FAIL  " + name); }
    }

    private static void Section(string title) => Console.WriteLine("\n== " + title + " ==");

    /// <summary>
    /// Harness-only A/B switch: --prompt-version demo-slice-1 or LUANSHI_PROMPT_VERSION.
    /// The normal suite always starts from the current default unless the caller opts in.
    /// </summary>
    private static bool TryApplyPromptVersionSwitch(string[] args, out int exitCode)
    {
        exitCode = 0;
        string version = null;
        for (int i = 0; args != null && i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--prompt-version", StringComparison.Ordinal)) continue;
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("usage: --prompt-version <demo-slice-4|demo-slice-3|demo-slice-1>");
                exitCode = 2;
                return false;
            }
            version = args[i + 1];
        }
        if (string.IsNullOrEmpty(version))
            version = Environment.GetEnvironmentVariable("LUANSHI_PROMPT_VERSION");
        if (string.IsNullOrEmpty(version)) return true;

        if (PromptBuilder.TryUsePromptVersion(version, out string error))
        {
            Console.WriteLine("PROMPT version=" + PromptBuilder.ActivePromptVersion);
            return true;
        }
        Console.Error.WriteLine("PROMPT error: " + error + " ('" + version + "')");
        exitCode = 2;
        return false;
    }

    private static int Main(string[] args)
    {
        if (!TryApplyPromptVersionSwitch(args, out int promptExit)) return promptExit;

        // The live smoke is opt-in and must never run as part of the normal suite.
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--live-pilot", StringComparison.Ordinal))
                    return RunLivePilotFromArgs(args, i);
                if (!string.Equals(args[i], "--live", StringComparison.Ordinal)) continue;
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("usage: EngineHarness --live <configPath>");
                    return 2;
                }
                return RunLiveSmoke(args[i + 1]);
            }
        }

        RunRevolt();
        RunSupplyDesertion();
        RunSeasonalModifiers();
        RunVictory();
        RunObservation();
        RunScriptedController();
        RunResearch();
        RunEspionage();
        RunGarrisonTransfers();
        RunResearchPrivacy();
        RunUnitTypes();
        RunUnitSpecials();
        RunEconomySmoke();
        RunDiplomacySmoke();
        RunEventPrivacy();
        RunObservationDetachment();
        RunReportText();
        RunHistoryExport();
        RunTurnGate();
        RunTurnFlowPlanner();
        RunLlmParse();
        RunLlmRunner();
        RunLlmActivity();
        RunLlmSessionChat();
        RunMovementOptions();
        RunLlmMovement();
        RunLlmMovementPrompts();
        RunFeedbackPolicy();
        RunLlmTerminal();
        RunDemoS4();

        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
