using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static JsonElement ReadJsonFile(string path)
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                return doc.RootElement.Clone();
        }

        private static string JsonProp(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object
                || !parent.TryGetProperty(name, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }

        /// <summary>
        /// CS-02: a saved scenario re-runs from its recorded setup with the same settings hash,
        /// prompt hashes and final world digest, into a new run id and directory.
        /// </summary>
        private static void ReproduceFromRecordedSetup(TestLog log)
        {
            string outRoot = NewRunDir("cs02");
            try
            {
                CsRunResult first = CsRunner.Run(
                    Manifest(new[] { "cs-visible-contact-v1" }, 4242), outRoot, CsPaths.RepoRoot());
                log.Check(first.Status == CsStatus.Completed && first.Scenarios.Count == 1
                        && first.Scenarios[0].Status == CsStatus.Completed,
                    "[CS-02] the recorded setup completes a first run", first.Status + " " + first.Detail);
                if (first.Scenarios.Count != 1) return;

                CsReplayResult replay = CsRunner.Replay(first.RunDirectory, outRoot, CsPaths.RepoRoot());
                log.Check(replay.Matched, "[CS-02] replay from the saved scenario.json reproduces the run",
                    string.Join("; ", replay.Details));
                log.Check(!string.IsNullOrEmpty(replay.NewRunDirectory)
                        && replay.NewRunDirectory != first.RunDirectory,
                    "[CS-02] replay writes a distinct run directory and run id");

                string oldDir = first.Scenarios[0].Directory;
                string newDir = Path.Combine(replay.NewRunDirectory, "cs-visible-contact-v1");
                JsonElement oldResult = ReadJsonFile(Path.Combine(oldDir, "result.json"));
                JsonElement newResult = ReadJsonFile(Path.Combine(newDir, "result.json"));
                bool settingsSame = JsonProp(oldResult, "settings_hash") == JsonProp(newResult, "settings_hash");
                bool promptsSame = JsonProp(oldResult, "prompt_hashes") == JsonProp(newResult, "prompt_hashes");
                bool worldSame = JsonProp(oldResult, "final_world_digest") == JsonProp(newResult, "final_world_digest")
                              && JsonProp(oldResult, "start_world_digest") == JsonProp(newResult, "start_world_digest");
                bool setupSame = JsonProp(oldResult, "setup_hash") == JsonProp(newResult, "setup_hash");
                log.Check(settingsSame && promptsSame && worldSame && setupSame,
                    "[CS-02] settings hash, prompt hashes, setup hash and final world digest match",
                    "settings=" + settingsSame + " prompts=" + promptsSame + " world=" + worldSame + " setup=" + setupSame);
                log.Check(JsonProp(oldResult, "run_id") != JsonProp(newResult, "run_id"),
                    "[CS-02] the reproduced run keeps a different runner run id");
            }
            finally { Cleanup(outRoot); }
        }

        /// <summary>
        /// CS-03: every dispatched observation is the acting seat's, and an id is shown only when
        /// that seat's own observation contains it. The hidden enemy city/army ids never reach the
        /// non-owning seat's prompt, and the scenario privacy witness reports zero leaks.
        /// </summary>
        private static void FogSeatIsolation(TestLog log)
        {
            string outRoot = NewRunDir("cs03");
            try
            {
                CsRunResult run = CsRunner.Run(DefaultManifest(), outRoot, CsPaths.RepoRoot());
                log.Check(run.Status == CsStatus.Completed && run.Scenarios.Count == 8,
                    "[CS-03] all eight scenarios provide dispatches to check", run.Status + " " + run.Detail);

                var hidden = new[] { CsScenarioFactory.EnemyCityId, CsScenarioFactory.HiddenArmyId };
                bool actingSeat = true, fogBoundary = true, ownVisible = false, leaksOk = true;
                string detail = "";
                foreach (CsScenarioResult scenario in run.Scenarios)
                {
                    leaksOk &= scenario.PrivacyLeaks == 0;
                    List<JsonElement> dispatches = ReadJsonLines(
                        Path.Combine(scenario.Directory, "dispatches.ndjson"));
                    foreach (JsonElement dispatch in dispatches)
                    {
                        string seat = CsEvidenceRecorder.Text(dispatch, "seat_id");
                        JsonElement before = Obj(Obj(dispatch, "state"), "before");
                        string observation = CsEvidenceRecorder.Text(before, "observation_json");
                        if (string.IsNullOrEmpty(observation)
                            || observation.IndexOf("\"seat_id\":" + Quote(seat), StringComparison.Ordinal) < 0)
                        {
                            actingSeat = false;
                            detail += scenario.ScenarioId + " dispatch "
                                + CsEvidenceRecorder.Text(dispatch, "dispatch_id")
                                + " is not the acting seat's observation; ";
                            continue;
                        }
                        string prompt = PromptText(dispatch);
                        foreach (string id in hidden)
                            if (observation.Contains(id) != prompt.Contains(id))
                            {
                                fogBoundary = false;
                                detail += scenario.ScenarioId + " seat=" + seat + " id=" + id
                                    + " observation=" + observation.Contains(id) + " prompt=" + prompt.Contains(id) + "; ";
                            }
                        if (scenario.ScenarioId == "cs-visible-contact-v1" && seat == "seat-0")
                        {
                            if (prompt.Contains(CsScenarioFactory.EnemyCityId)
                                || prompt.Contains(CsScenarioFactory.HiddenArmyId))
                            {
                                fogBoundary = false;
                                detail += "hidden enemy id reached seat-0's prompt; ";
                            }
                            ownVisible |= prompt.Contains(CsScenarioFactory.HomeCityId);
                        }
                    }
                }

                log.Check(actingSeat,
                    "[CS-03] each dispatch prompt is built from the acting seat's own observation", detail);
                log.Check(fogBoundary,
                    "[CS-03] an entity id appears in a prompt only when that seat's observation shows it", detail);
                log.Check(ownVisible,
                    "[CS-03] the study seat's visible home city is present, so the fog filter is not empty");
                log.Check(leaksOk,
                    "[CS-03] the per-scenario CsPrivacy witness reports zero leaks");
            }
            finally { Cleanup(outRoot); }
        }

        private static string Quote(string value)
            => value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
