using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        /// <summary>
        /// CS-01: the tracked seven-scenario default manifest runs fully offline through the
        /// production spectator session, and each scenario keeps its setup, script, settings, exact
        /// prompts, linked dispatches, production run log, exchange capture and report.
        /// </summary>
        private static void SevenScenariosEvidence(TestLog log)
        {
            string outRoot = NewRunDir("cs01");
            try
            {
                CsRunResult run = CsRunner.Run(DefaultManifest(), outRoot, CsPaths.RepoRoot());
                log.Check(run.Status == CsStatus.Completed && run.Scenarios.Count == 8,
                    "[CS-01] all eight fake scenarios complete", run.Status + " (" + run.Detail + ")");
                log.Check(File.Exists(Path.Combine(run.RunDirectory, "run-manifest.json"))
                        && File.Exists(Path.Combine(run.RunDirectory, "dispatches.ndjson")),
                    "[CS-01] the run manifest and aggregate dispatch file exist");

                foreach (CsScenarioResult scenario in run.Scenarios)
                {
                    bool ok = scenario.Status == CsStatus.Completed;
                    ok &= File.Exists(Path.Combine(scenario.Directory, "scenario.json"));
                    ok &= File.Exists(Path.Combine(scenario.Directory, "script.json"));
                    ok &= File.Exists(Path.Combine(scenario.Directory, "settings.json"));
                    ok &= File.Exists(Path.Combine(scenario.Directory, "result.json"));
                    ok &= File.Exists(Path.Combine(scenario.Directory, scenario.ScenarioId + "-report.txt"));
                    string runFile = RunFile(scenario.Directory);
                    string exchangeFile = ExchangeFile(scenario.Directory);
                    ok &= runFile != null && exchangeFile != null && SummaryFile(scenario.Directory) != null;
                    List<JsonElement> prompts = ReadJsonLines(Path.Combine(scenario.Directory, "prompts.ndjson"));
                    List<JsonElement> dispatches = ReadJsonLines(Path.Combine(scenario.Directory, "dispatches.ndjson"));
                    List<JsonElement> runners = ReadJsonLines(runFile);
                    List<JsonElement> exchanges = ReadJsonLines(exchangeFile);
                    ok &= dispatches.Count == scenario.Dispatches && dispatches.Count > 0;
                    ok &= prompts.Count == dispatches.Count;
                    ok &= runners.Count == scenario.Requests;
                    ok &= exchanges.Count == scenario.Requests * 2;
                    log.Check(ok, "[CS-01] " + scenario.ScenarioId
                            + " keeps setup, script, settings, prompts, dispatches, run log, exchanges and report",
                        "status=" + scenario.Status + " requests=" + scenario.Requests
                        + " run=" + runners.Count + " exchanges=" + exchanges.Count
                        + " prompts=" + prompts.Count + " dispatches=" + dispatches.Count);
                }

                bool labelled = true;
                foreach (CsScenarioResult scenario in run.Scenarios)
                    foreach (JsonElement dispatch in ReadJsonLines(
                        Path.Combine(scenario.Directory, "dispatches.ndjson")))
                        labelled &= CsEvidenceRecorder.Text(dispatch, "status") != null
                            && CsEvidenceRecorder.Text(Obj(dispatch, "reply"), "source") == "fake_scripted"
                            && CsEvidenceRecorder.Bool(Obj(dispatch, "provider"), "live") == false;
                log.Check(labelled, "[CS-01] every dispatch is labelled fake_scripted and not live, never model behaviour");
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>
        /// CS-05: a provider delay beyond the request timeout, and a scenario wall ceiling, both
        /// stop the run as failed/stopped with unknown usage; neither is reported as a pass.
        /// </summary>
        private static void TimeoutOrStoppedRun(TestLog log)
        {
            CsScenarioSpec timeout = Builtin("cs-visible-contact-v1", 4242);
            timeout.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("slow fixture"),
                    fault: "request_timeout", delayMs: 5000),
            };
            timeout.MaxRequests = 10;
            timeout.MaxWallSeconds = 30;
            timeout.RequestTimeoutMs = 100;
            CsScenarioResult timedOut = RunSpec(timeout, "cs05-timeout", out string timeoutRoot);
            try
            {
                log.Check(timedOut.Status == CsStatus.Failed || timedOut.Status == CsStatus.Stopped,
                    "[CS-05] a delay beyond request_timeout_ms stops the scenario as failed/stopped, not a pass",
                    timedOut.Status + " limit=" + timedOut.Limit + " detail=" + timedOut.Detail);
                log.Check(timedOut.InputTokens == null && timedOut.OutputTokens == null
                        && timedOut.UnknownUsageRequests >= 1,
                    "[CS-05] the timed-out request's usage is unknown, never zero",
                    "input=" + timedOut.InputTokens + " output=" + timedOut.OutputTokens
                    + " unknown=" + timedOut.UnknownUsageRequests);
                string summary = SummaryText(timedOut.Directory);
                log.Check(summary.IndexOf("RequestTimeout", StringComparison.Ordinal) >= 0
                        || summary.IndexOf("request timed out", StringComparison.Ordinal) >= 0,
                    "[CS-05] the bounded stop is named as a request timeout in the turn record", summary);
            }
            finally { Cleanup(timeoutRoot); }

            CsScenarioSpec wall = Builtin("cs-visible-contact-v1", 4243);
            wall.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("slow fixture"), delayMs: 3000),
            };
            wall.MaxRequests = 10;
            wall.MaxWallSeconds = 1;
            wall.RequestTimeoutMs = 60000;
            CsScenarioResult stopped = RunSpec(wall, "cs05-wall", out string wallRoot);
            try
            {
                log.Check(stopped.Status == CsStatus.Stopped
                        && stopped.Limit == "max_scenario_wall_seconds",
                    "[CS-05] a scenario wall ceiling stops the scenario and names the ceiling",
                    stopped.Status + " limit=" + stopped.Limit + " detail=" + stopped.Detail);
                log.Check(stopped.InputTokens == null && stopped.UnknownUsageRequests >= 1,
                    "[CS-05] a wall-stopped run reports its cut request as unknown usage",
                    "input=" + stopped.InputTokens + " unknown=" + stopped.UnknownUsageRequests);
            }
            finally { Cleanup(wallRoot); }

            string outRoot = NewRunDir("cs05-run-ceiling");
            try
            {
                CsManifest ceiling = Manifest(
                    new[] { "cs-visible-contact-v1", "cs-advantageous-attack-v1" }, 4250);
                ceiling.RunCeilings.MaxRunRequests = 2;
                CsRunResult capped = CsRunner.Run(ceiling, outRoot, CsPaths.RepoRoot());
                log.Check(capped.Status == CsStatus.Stopped && capped.Detail != null
                        && capped.Detail.Contains("max_run_requests"),
                    "[CS-05] a whole-run request ceiling stops the run and names the ceiling",
                    capped.Status + " detail=" + capped.Detail);
                log.Check(capped.Scenarios.Count == 2
                        && capped.Scenarios[1].Status == CsStatus.Skipped,
                    "[CS-05] a scenario not reached is retained as skipped, never reported as a pass",
                    "second=" + (capped.Scenarios.Count > 1 ? capped.Scenarios[1].Status : "missing"));
            }
            finally { Cleanup(outRoot); }
        }

        private static string SummaryText(string scenarioDirectory)
        {
            string path = SummaryFile(scenarioDirectory);
            return path != null && File.Exists(path) ? File.ReadAllText(path) : "";
        }
    }
}
