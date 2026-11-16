using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // Status classification and the per-scenario / per-run JSON artefacts.
    public static partial class CsRunner
    {
        private static void Classify(CsScenarioResult result, SpectatorSession session,
            CsScenarioSpec spec, List<CsDispatchCapture> captures, List<CsRunLine> runLines,
            string wallLimit, string requestLimitName, bool wallCutByTransport = false)
        {
            bool clean = true, failedTurn = false;
            foreach (SpectatorTurnRecord record in session.TurnRecords)
            {
                if (record.EndReason != EndReason.ModelEndedTurn && record.EndReason != EndReason.GameOver)
                    clean = false;
                if (record.EndReason == EndReason.ProviderError || record.EndReason == EndReason.RequestTimeout
                    || record.EndReason == EndReason.TransportError || record.EndReason == EndReason.HostError
                    || record.EndReason == EndReason.PromptTooLarge || record.EndReason == EndReason.UsdBudgetUnknown)
                    failedTurn = true;
            }
            string limit = wallLimit;
            if (limit == null && session.PauseReason != null
                && session.PauseReason.IndexOf("session request limit", StringComparison.Ordinal) >= 0)
                limit = string.IsNullOrEmpty(requestLimitName) ? "max_scenario_requests" : requestLimitName;
            result.Limit = limit;
            result.Detail = session.PauseReason ?? session.StopReason;
            // A request abandoned by the transport exactly at the wall boundary makes the runner
            // report a failure; the real cause is the named wall ceiling, so keep it a stop.
            result.Status = failedTurn && !wallCutByTransport ? CsStatus.Failed
                : limit != null ? CsStatus.Stopped
                : session.State == SpectatorState.Completed
                    ? (clean ? CsStatus.Completed : CsStatus.CompletedWithStops)
                    : session.State == SpectatorState.Failed ? CsStatus.Failed : CsStatus.Stopped;

            int accepted = 0, rejected = 0, inquiries = 0, notes = 0, unknown = 0;
            long input = 0, output = 0;
            foreach (CsRunLine line in runLines)
            {
                bool? ok = CsEvidenceRecorder.Bool(line.Root, "result_ok");
                if (ok == true) accepted++;
                else if (ok == false) rejected++;
                if (!string.IsNullOrEmpty(CsEvidenceRecorder.Text(line.Root, "inquiry_tool"))) inquiries++;
                if (IsNote(line)) notes++;
                int? inTokens = CsEvidenceRecorder.Int(line.Root, "input_tokens");
                int? outTokens = CsEvidenceRecorder.Int(line.Root, "output_tokens");
                if (inTokens.HasValue && outTokens.HasValue) { input += inTokens.Value; output += outTokens.Value; }
                else unknown++;
            }
            // An in-flight request cut by a wall stop has no run line; its usage is unknown.
            if (captures.Count > runLines.Count) unknown += captures.Count - runLines.Count;
            result.Accepted = accepted;
            result.Rejected = rejected;
            result.Inquiries = inquiries;
            result.Notes = notes;
            result.UnknownUsageRequests = unknown;
            if (runLines.Count == 0 && captures.Count == 0)
            {
                result.InputTokens = 0;
                result.OutputTokens = 0;
            }
            else if (unknown == 0)
            {
                result.InputTokens = input;
                result.OutputTokens = output;
            }
        }

        private static bool IsNote(CsRunLine line)
        {
            if (!string.IsNullOrEmpty(CsEvidenceRecorder.Text(line.Root, "result_error"))) return false;
            string raw = CsEvidenceRecorder.Text(line.Root, "raw_model_text");
            if (string.IsNullOrEmpty(raw)) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(raw))
                    return doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("note", out JsonElement note)
                        && note.ValueKind == JsonValueKind.String;
            }
            catch (JsonException) { return false; }
        }

        private static int PrivacyLeaks(CsScenarioSpec spec, List<CsDispatchCapture> captures)
        {
            var ids = new List<string>();
            foreach (CsCitySetup city in spec.Cities) ids.Add(city.Id);
            foreach (CsArmySetup army in spec.Armies) ids.Add(army.Id);
            foreach (CsTowerSetup tower in spec.Towers) ids.Add(tower.Id);
            int leaks = 0;
            foreach (CsDispatchCapture cap in captures)
            {
                CsPrivacyReport report = CsPrivacy.ScanSetup(cap.System, cap.User, cap.Seat,
                    cap.Before?.ObservationJson, ids);
                leaks += report.Leaks;
            }
            return leaks;
        }

        private static Dictionary<string, object> ResultObject(CsScenarioResult result,
            List<CsDispatchCapture> captures)
        {
            var hashes = new List<Dictionary<string, object>>();
            foreach (CsDispatchCapture cap in captures)
                hashes.Add(new Dictionary<string, object>
                {
                    { "index", cap.Index }, { "seat", cap.Seat },
                    { "system_sha256", CsWorld.Sha256(cap.System) },
                    { "user_sha256", CsWorld.Sha256(cap.User) },
                });
            return new Dictionary<string, object>
            {
                { "schema", "luanshi-controlled-scenario-result/1" },
                { "run_id", result.RunId }, { "scenario_id", result.ScenarioId },
                { "seed", result.Seed }, { "settings_hash", result.SettingsHash },
                { "setup_hash", result.SetupHash }, { "script_sha256", result.ScriptSha256 },
                { "start_world_digest", result.StartWorldDigest },
                { "final_world_digest", result.FinalWorldDigest },
                { "status", result.Status }, { "limit", result.Limit },
                { "requests", result.Requests }, { "dispatches", result.Dispatches },
                { "late_responses_discarded", result.LateResponsesDiscarded },
                { "prompt_hashes", hashes },
            };
        }

        // The audit treats run-root dispatches.ndjson as the whole linked evidence set.
        private static void WriteAggregateDispatches(string runDir, CsRunResult result)
        {
            var sb = new StringBuilder();
            foreach (CsScenarioResult scenario in result.Scenarios)
            {
                if (string.IsNullOrEmpty(scenario.Directory)) continue;
                string path = Path.Combine(scenario.Directory, "dispatches.ndjson");
                if (File.Exists(path)) sb.Append(File.ReadAllText(path));
            }
            File.WriteAllText(Path.Combine(runDir, "dispatches.ndjson"), sb.ToString(),
                new UTF8Encoding(false));
        }

        /// <summary>
        /// A manifest scenario that was not executed. The manifest keeps the staged setup and
        /// script hashes derived from the built-in definition, but nothing is written to disk and
        /// no result, run log or dispatch record exists, so its status stays skipped.
        /// </summary>
        private static CsScenarioResult Skipped(CsManifestScenario entry, string reason,
            CsManifest manifest = null)
        {
            var skipped = new CsScenarioResult
            {
                ScenarioId = entry.ScenarioId, Seed = entry.Seed ?? 0, StudySeat = "seat-0",
                Status = CsStatus.Skipped, Limit = reason, Detail = "not run: " + reason,
            };
            if (manifest == null || !CsScenarios.TryGet(entry.ScenarioId, entry.Seed, out CsScenarioSpec spec))
                return skipped;
            spec.MaxRequests = manifest.ScenarioRequests(entry);
            spec.MaxWallSeconds = manifest.ScenarioWallSeconds(entry);
            spec.RequestTimeoutMs = manifest.ScenarioRequestTimeoutMs(entry);
            skipped.SeasonsPlanned = spec.Seasons;
            CsScenarioFile file = CsScenarioFile.FromSpec(spec);
            skipped.SetupHash = CsWorld.Sha256(JsonSerializer.Serialize(file.Setup, CsJson.Compact));
            skipped.ScriptSha256 = CsWorld.Sha256(StagedScriptJson(spec));
            return skipped;
        }

        /// <summary>Exact bytes of script.json, shared by executed and skipped scenarios.</summary>
        internal static string StagedScriptJson(CsScenarioSpec spec)
        {
            var scriptFile = new Dictionary<string, object>
            {
                { "source", "fake_scripted" }, { "fixture", true }, { "scripts", spec.Scripts },
            };
            return JsonSerializer.Serialize(scriptFile, CsJson.Indented);
        }

        private static void WriteRunManifest(string runDir, CsManifest manifest, CsRunResult result,
            CsGitInfo git)
        {
            var scenarios = new List<Dictionary<string, object>>();
            foreach (CsScenarioResult s in result.Scenarios)
            {
                scenarios.Add(new Dictionary<string, object>
                {
                    { "scenario_id", s.ScenarioId }, { "seed", s.Seed },
                    { "study_seat", s.StudySeat }, { "seats", new[] { "seat-0", "seat-1" } },
                    { "seasons", s.SeasonsPlanned }, { "setup_hash", s.SetupHash },
                    { "script_sha256", s.ScriptSha256 }, { "status", s.Status },
                    { "detail", s.Detail }, { "limit", s.Limit }, { "run_id", s.RunId },
                    { "requests", s.Requests }, { "dispatches", s.Dispatches },
                    { "late_responses_discarded", s.LateResponsesDiscarded },
                    { "settings_hash", s.SettingsHash },
                    { "files", s.Files },
                    { "provider", ScenarioProviderBlock(s, result, git) },
                });
            }
            var manifestOut = new Dictionary<string, object>
            {
                { "schema", "luanshi-controlled-scenarios/1" },
                { "run_id", result.RunId },
                { "branch", git.Branch }, { "base_commit", CsGitInfo.BaseCommit },
                { "head_commit", git.HeadCommit },
                { "dirty_files", result.DirtyFiles },
                { "untracked_build_inputs", result.UntrackedBuildInputs },
                { "provider", new Dictionary<string, object>
                    {
                        { "kind", result.ProviderKind ?? manifest.Provider.Kind },
                        { "model", result.ProviderModel ?? manifest.Provider.Model },
                        { "live", result.ProviderLive },
                        { "base_url_display", result.ProviderBaseUrlDisplay },
                        { "env_var_names", result.ProviderEnvVarNames },
                        { "max_output_tokens", result.ProviderMaxOutputTokens },
                        { "request_timeout_ms", manifest.RunCeilings?.RequestTimeoutMs },
                        { "max_requests", manifest.RunCeilings?.MaxRunRequests },
                        { "max_wall_seconds", manifest.RunCeilings?.MaxRunWallSeconds },
                        { "effective_max_requests", manifest.RunCeilings?.MaxRunRequests },
                        { "effective_max_wall_seconds", manifest.RunCeilings?.MaxRunWallSeconds },
                        { "commit", git.HeadCommit ?? CsGitInfo.BaseCommit },
                        { "dirty", result.DirtyFiles.Count > 0 },
                        // Scenario-specific identity lives in each scenario entry below.
                        { "seed", null }, { "scenario_id", null }, { "settings_hash", null },
                        { "limits_note", CsLimits.LimitsNote },
                        { "status_note", result.ProviderLive
                            ? "live provider prepared; model-written output, NOT fixtures"
                            : "no network; fake scripts are plumbing evidence, not model behaviour" },
                    } },
                { "scenarios", scenarios },
                { "run_ceilings", new Dictionary<string, object>
                    {
                        { "max_scenarios", manifest.RunCeilings.MaxScenarios },
                        { "max_run_requests", manifest.RunCeilings.MaxRunRequests },
                        { "max_run_wall_seconds", manifest.RunCeilings.MaxRunWallSeconds },
                        { "max_scenario_requests", manifest.RunCeilings.MaxScenarioRequests },
                        { "max_scenario_wall_seconds", manifest.RunCeilings.MaxScenarioWallSeconds },
                        { "request_timeout_ms", manifest.RunCeilings.RequestTimeoutMs },
                        { "unknown_usage_policy", manifest.RunCeilings.UnknownUsagePolicy },
                    } },
                { "usage", new Dictionary<string, object>
                    {
                        { "input_tokens", result.InputTokens },
                        { "output_tokens", result.OutputTokens },
                        { "unknown_requests", result.UnknownUsageRequests },
                        { "source", result.ProviderLive ? "provider_reported" : "fake_deterministic" },
                    } },
                { "status", result.Status }, { "detail", result.Detail },
                { "late_responses_discarded", result.LateResponsesDiscarded },
                { "limitations", result.Limitations },
            };
            File.WriteAllText(Path.Combine(runDir, "run-manifest.json"),
                JsonSerializer.Serialize(manifestOut, CsJson.Indented), new UTF8Encoding(false));
        }

        /// <summary>Per-scenario provider identity, repeated so every manifest entry is self-contained.</summary>
        private static Dictionary<string, object> ScenarioProviderBlock(CsScenarioResult scenario,
            CsRunResult result, CsGitInfo git)
        {
            return new Dictionary<string, object>
            {
                { "kind", scenario.ProviderKind ?? result.ProviderKind },
                { "model", scenario.ProviderModel ?? result.ProviderModel },
                { "live", scenario.ProviderKind == null ? result.ProviderLive : scenario.ProviderLive },
                { "base_url_display", result.ProviderBaseUrlDisplay },
                { "env_var_names", result.ProviderEnvVarNames },
                { "max_output_tokens", scenario.MaxOutputTokens > 0
                    ? scenario.MaxOutputTokens : result.ProviderMaxOutputTokens },
                { "request_timeout_ms", scenario.RequestTimeoutMs > 0
                    ? scenario.RequestTimeoutMs : (int?)null },
                { "max_requests", scenario.EffectiveMaxRequests > 0
                    ? scenario.EffectiveMaxRequests : (int?)null },
                { "max_wall_seconds", scenario.EffectiveWallSeconds > 0
                    ? scenario.EffectiveWallSeconds : (int?)null },
                { "effective_max_requests", scenario.EffectiveMaxRequests > 0
                    ? scenario.EffectiveMaxRequests : (int?)null },
                { "effective_max_wall_seconds", scenario.EffectiveWallSeconds > 0
                    ? scenario.EffectiveWallSeconds : (int?)null },
                { "commit", git.HeadCommit ?? CsGitInfo.BaseCommit },
                { "dirty", result.DirtyFiles.Count > 0 },
                { "seed", scenario.Seed }, { "scenario_id", scenario.ScenarioId },
                { "settings_hash", scenario.SettingsHash },
                { "limits_note", CsLimits.LimitsNote },
            };
        }

        private static void WriteJson(string directory, string name, object value)
        {
            File.WriteAllText(Path.Combine(directory, name),
                JsonSerializer.Serialize(value, CsJson.Indented), new UTF8Encoding(false));
        }

        private static string Str(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
    }
}
