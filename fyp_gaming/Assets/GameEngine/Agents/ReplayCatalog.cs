using System;
using System.Collections.Generic;
using System.IO;

namespace LuanShi.Engine
{
    /// <summary>A named shortcut to a recorded run id, from the local preset file or the built-in list.</summary>
    public sealed class ReplayPreset
    {
        public string Name;
        public string RunId;
        public string Note;
    }

    /// <summary>The preset list the replay chooser shows, with a plain-language note about where it came from.</summary>
    public sealed class ReplayPresetSet
    {
        public List<ReplayPreset> Presets = new List<ReplayPreset>();
        /// <summary>Extra directories searched for run files, before the default locations.</summary>
        public List<string> SearchRoots = new List<string>();
        public string SourcePath;
        /// <summary>True when a preset file was found and read.</summary>
        public bool FromFile;
        /// <summary>Why the built-in list is shown (file missing, unreadable or empty); null when the file was used.</summary>
        public string Message;
    }

    public enum ReplayOpenStatus
    {
        Ready,
        /// <summary>No run files were found for the id.</summary>
        NotFound,
        /// <summary>Some but not all of the summary, request log, exchanges and bootstrap files exist.</summary>
        Incomplete,
        /// <summary>The files exist but failed validation, or the run id is not acceptable.</summary>
        Invalid,
        /// <summary>The files load but the recording is outside what replay supports (non-LLM controllers).</summary>
        Unsupported,
    }

    public sealed class ReplayOpenResult
    {
        public ReplayOpenStatus Status;
        public string RunId;
        public string Message;
        public ReplayBundle Bundle;
        public ReplaySession Session;
        /// <summary>Honest labels about what this replay is and is not, in display order.</summary>
        public List<string> Notes = new List<string>();
        public bool Ok => Status == ReplayOpenStatus.Ready && Session != null;
    }

    /// <summary>
    /// Run discovery, preset handling and open-time validation for the offline replay. Replay is LLM-only:
    /// a recording is accepted only when its files resolve, validate, and every request was made by an LLM
    /// controller. Mixed human/LLM replay is not supported, and a run is never reported as replayable
    /// merely because its id was typed. Nothing here makes a model or network call.
    /// </summary>
    public static class ReplayCatalog
    {
        public const string PresetFileName = "replay-presets.local.json";
        public const string PresetEnvVar = "LUANSHI_REPLAY_PRESETS";
        public const string RunsEnvVar = "LUANSHI_RUNS_DIR";
        public const string LlmOnlyLabel = "LLM-only replay: mixed human/LLM replay is not yet supported.";

        /// <summary>The run recorded on 23 Nov 2026 that ships as the demo's treaty-and-attack scene.</summary>
        public const string TreatyAttackRunId = "run-20261123090426264-1890d898";

        public static IReadOnlyList<ReplayPreset> BuiltInPresets => new List<ReplayPreset>
        {
            new ReplayPreset
            {
                Name = "Treaty & Attack",
                RunId = TreatyAttackRunId,
                Note = "Recorded real-model match: a non-aggression proposal, then a march and an attack. "
                       + "Needs its run files locally; they are not part of a clone.",
            },
        };

        // ---- presets -------------------------------------------------------------------------------

        /// <summary>
        /// Reads the preset file at <paramref name="path"/> (or the environment variable, or the default
        /// file next to the project). A missing, unreadable or empty file is reported in
        /// <see cref="ReplayPresetSet.Message"/> and the built-in list is returned, never an exception.
        /// </summary>
        public static ReplayPresetSet LoadPresets(string path = null)
        {
            var set = new ReplayPresetSet();
            set.SearchRoots.AddRange(EnvironmentRoots());
            bool explicitPath = !string.IsNullOrWhiteSpace(path);
            if (!explicitPath) path = Environment.GetEnvironmentVariable(PresetEnvVar);
            bool configured = !string.IsNullOrWhiteSpace(path);
            if (!configured) path = Path.Combine(Directory.GetCurrentDirectory(), PresetFileName);
            set.SourcePath = path;

            set.Presets.AddRange(BuiltInPresets);
            if (!File.Exists(path))
            {
                set.Message = configured || explicitPath
                    ? "Preset file not found: " + path + ". Showing the built-in presets."
                    : "No local preset file (" + PresetFileName + "). Showing the built-in presets; copy "
                      + "replay-presets.example.json to " + PresetFileName + " to add your own.";
                return set;
            }

            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex)
            {
                set.Message = "Preset file could not be read (" + ex.Message + "). Showing the built-in presets.";
                return set;
            }
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode root, out string error) || !(root is JsonObject obj))
            {
                set.Message = "Preset file is not valid JSON (" + (error ?? "not an object") + "): " + path
                    + ". Showing the built-in presets.";
                return set;
            }

            var fromFile = new List<ReplayPreset>();
            if (obj.TryGet("presets", out JsonNode list) && list is JsonArray array)
            {
                foreach (JsonNode item in array.Items)
                {
                    var entry = item as JsonObject;
                    string name = Text(entry, "name"), id = Text(entry, "run_id");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    fromFile.Add(new ReplayPreset { Name = name.Trim(), RunId = id.Trim(), Note = Text(entry, "note") });
                }
            }
            if (obj.TryGet("search_roots", out JsonNode roots) && roots is JsonArray rootArray)
                foreach (JsonNode item in rootArray.Items)
                    if (item is JsonString s && !string.IsNullOrWhiteSpace(s.Value)) set.SearchRoots.Insert(0, s.Value.Trim());

            if (fromFile.Count == 0)
            {
                set.Message = "Preset file has no usable presets (each needs a name and a run_id): " + path
                    + ". Showing the built-in presets.";
                return set;
            }
            set.Presets = fromFile;
            set.FromFile = true;
            return set;
        }

        private static string Text(JsonObject obj, string key)
            => obj != null && obj.TryGet(key, out JsonNode node) && node is JsonString s ? s.Value : null;

        private static IEnumerable<string> EnvironmentRoots()
        {
            string value = Environment.GetEnvironmentVariable(RunsEnvVar);
            if (string.IsNullOrWhiteSpace(value)) yield break;
            foreach (string part in value.Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(part)) yield return part.Trim();
        }

        // ---- discovery -------------------------------------------------------------------------------

        /// <summary>
        /// Whether run files for <paramref name="runId"/> exist, without loading them: Ready means all required
        /// files were found, not that the recording is valid or replayable (use <see cref="Open"/> for that).
        /// </summary>
        public static ReplayOpenStatus Probe(string runId, IEnumerable<string> searchRoots, out string message)
        {
            ReplayOpenStatus status = Resolve(runId, searchRoots, out _, out _, out _, out _, out message);
            return status;
        }

        private static ReplayOpenStatus Resolve(string runId, IEnumerable<string> searchRoots,
            out string summary, out string jsonl, out string exchanges, out string bootstrap, out string message)
        {
            summary = jsonl = exchanges = bootstrap = null;
            message = null;
            if (string.IsNullOrWhiteSpace(runId)) { message = "Enter a run ID."; return ReplayOpenStatus.NotFound; }

            string incomplete = null, other = null;
            var roots = new List<string>();
            if (searchRoots != null) foreach (string r in searchRoots) if (!string.IsNullOrWhiteSpace(r)) roots.Add(r);
            roots.Add(null);   // the loader's own default locations come last
            foreach (string root in roots)
            {
                string error;
                string rootError;
                try
                {
                    if (ReplayLoader.TryResolveRunFiles(runId, root, out summary, out jsonl, out exchanges, out bootstrap, out error))
                        return ReplayOpenStatus.Ready;
                }
                catch (Exception ex) { error = ex.Message; }
                rootError = error;
                if (rootError != null && rootError.Contains("unsupported filename"))
                {
                    message = "That run ID is not acceptable: it may use letters, digits, '-', '_' and '.' only.";
                    return ReplayOpenStatus.Invalid;
                }
                if (rootError != null && rootError.Contains("incomplete files")) incomplete = incomplete ?? rootError;
                else other = other ?? rootError;
            }
            summary = jsonl = exchanges = bootstrap = null;
            if (incomplete != null) { message = incomplete; return ReplayOpenStatus.Incomplete; }
            message = "No run files found for '" + runId.Trim() + "'. Run files are local-only and are not part of a "
                + "clone; place them in a runs folder or set " + RunsEnvVar + ".";
            return ReplayOpenStatus.NotFound;
        }

        // ---- open and validate -----------------------------------------------------------------------

        /// <summary>Requests recorded under a controller other than the LLM; replay accepts none.</summary>
        public static int CountNonLlmRequests(ReplayBundle bundle)
        {
            int count = 0;
            if (bundle == null || bundle.Requests == null) return count;
            foreach (ReplayRequestRecord request in bundle.Requests)
                if (!string.IsNullOrEmpty(request.ControllerType)
                    && !string.Equals(request.ControllerType, ControllerType.Llm, StringComparison.Ordinal))
                    count++;
            return count;
        }

        /// <summary>
        /// Resolves, loads and validates a recorded run and creates its replay session. Every failure is a
        /// status with a plain message; nothing throws. The notes say what the replay is: LLM-only, recorded
        /// material, and the provenance of the rules it replays under.
        /// </summary>
        public static ReplayOpenResult Open(string runId, IEnumerable<string> searchRoots = null, MapData hostMap = null)
        {
            var result = new ReplayOpenResult { RunId = runId == null ? null : runId.Trim() };
            ReplayOpenStatus found = Resolve(runId, searchRoots, out string s, out string j, out string e, out string b,
                out string message);
            if (found != ReplayOpenStatus.Ready)
            {
                result.Status = found;
                result.Message = message;
                return result;
            }

            ReplayBundle bundle;
            try { bundle = ReplayLoader.Load(s, j, e, result.RunId, b); }
            catch (Exception ex)
            {
                result.Status = ReplayOpenStatus.Invalid;
                result.Message = "The run files were found but failed validation: " + ex.Message;
                return result;
            }

            int nonLlm = CountNonLlmRequests(bundle);
            if (nonLlm > 0)
            {
                result.Status = ReplayOpenStatus.Unsupported;
                result.Bundle = bundle;
                result.Message = "This recording contains " + nonLlm + " request(s) from a non-LLM controller. "
                    + "Mixed human/LLM replay is not supported, so it was not opened.";
                return result;
            }

            ReplaySession session;
            try { session = new ReplaySession(bundle, hostMap); }
            catch (Exception ex)
            {
                result.Status = ReplayOpenStatus.Invalid;
                result.Bundle = bundle;
                result.Message = "The recording could not be prepared for replay: " + ex.Message;
                return result;
            }

            result.Status = ReplayOpenStatus.Ready;
            result.Bundle = bundle;
            result.Session = session;
            result.Message = "Ready: " + bundle.Requests.Count + " recorded request(s).";
            result.Notes.Add(LlmOnlyLabel);
            result.Notes.Add("Recorded material only: the replay re-executes recorded actions through the engine and makes no model call.");
            if (bundle.Summary != null && !string.IsNullOrEmpty(bundle.Summary.Provider))
                result.Notes.Add("Recorded with " + bundle.Summary.Provider
                    + (string.IsNullOrEmpty(bundle.Summary.Model) ? "" : " / " + bundle.Summary.Model) + ".");
            if (session.IsIncompleteRun)
                result.Notes.Add("This run did not finish: " + (session.StopReason ?? "stopped early") + ".");
            if (!string.IsNullOrEmpty(session.AssaultTroopRatioRuleNote))
                result.Notes.Add("City-assault rule: " + session.AssaultTroopRatioRuleNote);
            if (!string.IsNullOrEmpty(session.VisibleMarchRuleNote))
                result.Notes.Add("March visibility: " + session.VisibleMarchRuleNote);
            return result;
        }
    }
}
