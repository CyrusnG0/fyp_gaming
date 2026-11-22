using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static void GarrisonChoiceChecks(TestLog log)
        {
            CsScenarioSpec spec = Builtin(CsScenarios.GarrisonChoiceId, 5151);
            spec.MaxRequests = 20;
            CsScenarioResult result = RunSpec(spec, "cs-garrison", out string root);
            try
            {
                log.Check(result.Status == CsStatus.Completed && result.SeasonsPlayed == 2,
                    "[GARRISON] cs-garrison-choice-v1 completes two seasons",
                    "status=" + result.Status + " seasons=" + result.SeasonsPlayed + " detail=" + result.Detail);
                log.Check(result.Accepted == 3 && result.Rejected == 0,
                    "[GARRISON] cs-garrison-choice-v1 records three accepted actions",
                    "accepted=" + result.Accepted + " rejected=" + result.Rejected);

                var runLines = new List<JsonElement>();
                foreach (string f in Directory.GetFiles(result.Directory, "run-*.jsonl"))
                    if (!f.EndsWith("-exchanges.ndjson")) runLines.AddRange(ReadJsonLines(f));

                bool hasConvertedEvent = false;
                bool allPromptVersionsSuffixed = runLines.Count > 0;
                foreach (JsonElement line in runLines)
                {
                    string version = CsEvidenceRecorder.Text(line, "prompt_version");
                    if (string.IsNullOrEmpty(version)
                        || !version.EndsWith(PromptBuilder.GarrisonConversionVersionSuffix, System.StringComparison.Ordinal)
                        || !PromptBuilder.IsGarrisonConversionPromptVersion(version))
                    {
                        allPromptVersionsSuffixed = false;
                    }

                    string obsJson = CsEvidenceRecorder.Text(line, "observation_json");
                    if (string.IsNullOrEmpty(obsJson)) continue;
                    using (JsonDocument obs = JsonDocument.Parse(obsJson))
                    {
                        if (!obs.RootElement.TryGetProperty("events", out JsonElement events)
                            || events.ValueKind != JsonValueKind.Array) continue;
                        foreach (JsonElement ev in events.EnumerateArray())
                        {
                            if (CsEvidenceRecorder.Text(ev, "event_type") == "garrison_converted")
                                hasConvertedEvent = true;
                        }
                    }
                }

                string summaryPath = SummaryFile(result.Directory);
                string summaryPromptVersion = null;
                if (!string.IsNullOrEmpty(summaryPath))
                {
                    using (JsonDocument sum = JsonDocument.Parse(File.ReadAllText(summaryPath)))
                        summaryPromptVersion = CsEvidenceRecorder.Text(sum.RootElement, "prompt_version");
                }
                bool summarySuffixed = !string.IsNullOrEmpty(summaryPromptVersion)
                    && summaryPromptVersion.EndsWith(PromptBuilder.GarrisonConversionVersionSuffix, System.StringComparison.Ordinal)
                    && PromptBuilder.IsGarrisonConversionPromptVersion(summaryPromptVersion);

                log.Check(hasConvertedEvent,
                    "[GARRISON] recorded run observations include a garrison_converted event");
                log.Check(allPromptVersionsSuffixed && summarySuffixed,
                    "[GARRISON] run log and summary record the garrison-conversion prompt-version suffix",
                    "summary=" + (summaryPromptVersion ?? "null"));
            }
            finally { Cleanup(root); }
        }
    }
}
