using System;

namespace LuanShi.ExperimentAnalysis.Tests
{
    /// <summary>
    /// Entry point for the standalone experiment-analysis evidence checks. Exit code 0 means every
    /// assertion passed. Deliberately separate from Tests/Harness/Program.cs and
    /// Tests/Playground/Program.cs so nothing shared has to be re-registered.
    /// </summary>
    internal static class AnalysisProgram
    {
        private static int Main(string[] args)
        {
            Console.WriteLine("LuanShi experiment-analysis evidence checks");
            Console.WriteLine("  project: Tests/ExperimentAnalysis/ExperimentAnalysis.csproj");
            Console.WriteLine("  provider: fake only (no network, no paid call)");
            var log = new TestLog();
            try
            {
                ResearchVisibilityTests.Run(log);
                NoteVisibilityTests.Run(log);
            }
            catch (Exception exception)
            {
                log.Check(false, "the evidence checks ran to completion",
                    exception.GetType().Name + ": " + exception.Message);
            }
            return log.Summarise();
        }
    }
}
