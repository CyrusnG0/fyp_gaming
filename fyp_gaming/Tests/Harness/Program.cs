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

    private static int Main(string[] args)
    {
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

        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
