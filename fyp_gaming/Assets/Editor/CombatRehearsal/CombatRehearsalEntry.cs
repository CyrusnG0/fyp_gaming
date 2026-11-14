using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using LuanShi.Engine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LuanShi.EditorTools
{
    /// <summary>
    /// Scripted Play Mode rehearsal for the combat milestone. Launched from the command line:
    ///
    ///   Unity.exe -projectPath &lt;worktree&gt;/fyp_gaming \
    ///             -executeMethod LuanShi.EditorTools.CombatRehearsalEntry.Run
    ///
    /// It opens the shipped demo scene, enters Play Mode, drives battle and assault commands through the
    /// demo's own submit path, records what the engine and the presentation layer did, writes the
    /// evidence beside the screenshots, and exits. It never saves the scene and never changes a
    /// serialized field: every fixture change below is in-memory game state for this play session only.
    ///
    /// Entering Play Mode reloads the domain, which clears static delegates. The state machine therefore
    /// persists its position in <see cref="SessionState"/> (which survives the reload) and is re-hooked
    /// by the <see cref="InitializeOnLoadAttribute"/> constructor.
    /// </summary>
    [InitializeOnLoad]
    public static class CombatRehearsalEntry
    {
        private const string DemoScene = "Assets/GameUI/Scenes/LuanShi_Demo.unity";
        private const string ActiveKey = "LuanShi.CombatRehearsal.Active";
        private const string DirKey = "LuanShi.CombatRehearsal.Dir";
        private const string StepKey = "LuanShi.CombatRehearsal.Step";
        private const string StartedKey = "LuanShi.CombatRehearsal.Started";
        private const string PhaseKey = "LuanShi.CombatRehearsal.Phase";
        private const string IdleKey = "LuanShi.CombatRehearsal.Idle";

        private static bool _hooked;
        private static int _waitFrames;
        private static readonly List<string> _log = new List<string>();

        static CombatRehearsalEntry()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("LUANSHI_REHEARSAL_OUT");
            if (string.IsNullOrEmpty(outDir))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                outDir = Path.Combine(projectRoot, "..", "runs",
                    "unity-combat-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);
            SessionState.SetString(DirKey, outDir.Replace('\\', '/'));
            SessionState.SetInt(StepKey, 0);
            SessionState.SetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(PhaseKey, "wait");
            SessionState.SetInt(IdleKey, 0);
            File.WriteAllText(StepsPath(), "");

            Hook();
            Debug.Log("[rehearsal] waiting for the editor to finish importing/compiling before Play Mode");
        }

        private static string OutDir => SessionState.GetString(DirKey, "");
        private static string StepsPath() => Path.Combine(OutDir, "rehearsal-steps.jsonl").Replace('\\', '/');
        private static int Step { get => SessionState.GetInt(StepKey, 0); set => SessionState.SetInt(StepKey, value); }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            EditorApplication.update += Tick;
        }

        // ------------------------------------------------------------------ state machine

        private static void Tick()
        {
            try
            {
                if (!SessionState.GetBool(ActiveKey, false)) { Unhook(); return; }

                // Phase 1: a startup recompile during Play Mode reloads the domain and silently
                // drops the demo's non-serialized engine reference, so wait for a calm editor first.
                if (SessionState.GetString(PhaseKey, "wait") == "wait")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating
                        || EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        SessionState.SetInt(IdleKey, 0);
                        return;
                    }
                    int idle = SessionState.GetInt(IdleKey, 0) + 1;
                    SessionState.SetInt(IdleKey, idle);
                    if (idle < 120) return;
                    Debug.Log("[rehearsal] editor idle for " + idle + " frames; opening the demo scene and entering Play Mode");
                    EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Single);
                    SessionState.SetString(PhaseKey, "play");
                    EditorApplication.EnterPlaymode();
                    return;
                }

                if (!EditorApplication.isPlaying)
                {
                    if (Step > 0) Finish(1, "play mode ended unexpectedly");
                    return;
                }
                if (_waitFrames > 0) { _waitFrames--; return; }

                var demo = ResolveDemo(out int instances);
                if (demo == null) return;
                if (!AsBool(FieldOrNull(demo, "ready"))) return;
                if (EngineOrNull(demo) == null)
                {
                    Finish(3, "the demo lost its engine reference: a domain reload happened during Play Mode "
                        + "(non-serialized state is not restored)");
                    return;
                }
                if (SessionState.GetInt("LuanShi.CombatRehearsal.DiagStep", -1) != Step)
                {
                    SessionState.SetInt("LuanShi.CombatRehearsal.DiagStep", Step);
                    Debug.Log("[rehearsal] step " + Step + ": " + instances + " KingdomDemo instance(s); using id="
                        + demo.GetEntityId().ToString() + " engine=" + (EngineOrNull(demo) != null)
                        + " season=" + (EngineOrNull(demo) == null ? -1 : EngineOrNull(demo).State.Season));
                }

                switch (Step)
                {
                    case 0: StepBaseline(demo); break;
                    case 1: StepAgentTurn(demo); break;
                    case 11: StepAgentTurnWait(demo); break;
                    case 2: StepFieldBattle(demo); break;
                    case 3: StepAssaultRejected(demo); break;
                    case 4: StepAssaultCaptured(demo); break;
                    case 5: StepUnification(demo); break;
                    case 9: Finish(0, "rehearsal complete"); break;
                    default: Finish(0, "rehearsal complete"); break;
                }
            }
            catch (Exception e)
            {
                Finish(2, "rehearsal threw: " + e);
            }
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            EditorApplication.update -= Tick;
        }

        // ---- step 0: what the match looks like before any order -----------------

        private static void StepBaseline(KingdomDemo demo)
        {
            var engine = Engine(demo);
            var roster = new List<string>();
            foreach (var f in engine.State.Factions) roster.Add(f.SeatId + "=" + f.Name + "/" + f.Controller);
            _log.Add("[baseline] roster " + string.Join(", ", roster.ToArray()));
            _log.Add("[baseline] currentSeat=" + (string)Field(demo, "currentSeat")
                + " season=" + engine.State.Season
                + " cities=" + engine.State.Cities.Count + " armies=" + engine.State.Armies.Count
                + " agentRunner=" + (Field(demo, "agentRunner") != null));
            _log.Add("[baseline] fog overlays=" + Count(Field(demo, "fogOverlays"))
                + " visible=" + VisibleFogCount(demo));
            _log.Add("[baseline] Wei city tint=" + TintOf(demo, "city-wei")
                + " Shu city tint=" + TintOf(demo, "city-shu")
                + " neutral city tint=" + TintOf(demo, "city-south"));
            foreach (var army in engine.State.Armies)
                _log.Add("[baseline] banner " + army.Id + " " + BannerActive(demo, army.Id)
                    + " pos=" + BannerPos(demo, army.Id) + " troops=" + army.Troops);
            LogLines();
            Record(demo, "baseline", true);
            Capture("01-baseline");
            Next(1, 5);
        }

        // ---- step 1: the model seat's own turn (fake provider) ------------------

        private static void StepAgentTurn(KingdomDemo demo)
        {
            // The shipped scene has useFakeLlm off and no key, so the model seat would be skipped.
            // Switching the flags in memory (never saved) lets the fake provider play the seat once,
            // which exercises the agent turn, the runner activity and the session chat.
            SetField(demo, "useFakeLlm", true);
            CallByName(demo, "RebuildAgentRunner");
            var runner = Field(demo, "agentRunner");
            _log.Add("[agent] fake transport installed; runner=" + (runner != null)
                + " configAttack=" + AsBool(FieldOrNull(Field(demo, "agentConfig"), "EnableAttack")));
            CallByName(demo, "DebugEndTurn");   // the End-turn button's own path
            _log.Add("[agent] human seat ended; the fake model seat now plays the season");
            LogLines();
            SessionState.SetInt("LuanShi.CombatRehearsal.Wait", 0);
            Next(11, 30);   // wait for the fake request and the season rollover
        }

        /// <summary>Waits for the fake model seat to finish, then records the agent/chat evidence.</summary>
        private static void StepAgentTurnWait(KingdomDemo demo)
        {
            var engine = EngineOrNull(demo);
            bool active = AsBool(FieldOrNull(demo, "agentTurnActive"));
            int waited = SessionState.GetInt("LuanShi.CombatRehearsal.Wait", 0) + 1;
            SessionState.SetInt("LuanShi.CombatRehearsal.Wait", waited);

            if (active && waited < 900) { _waitFrames = 2; return; }

            var runner = FieldOrNull(demo, "agentRunner");
            var activity = runner == null ? null : FieldOrNull(runner, "Activity") as System.Collections.IList;
            _log.Add("[agent] turn finished: active=" + active + " waited_frames=" + waited
                + " season=" + (engine == null ? -1 : engine.State.Season)
                + " runner=" + (runner != null) + " fake=" + AsBool(FieldOrNull(demo, "agentFakeMode"))
                + " chat_lines=" + (activity == null ? 0 : activity.Count));
            if (activity != null)
            {
                int first = Math.Max(0, activity.Count - 4);
                for (int i = first; i < activity.Count; i++)
                {
                    var entry = activity[i];
                    string kind = Convert.ToString(FieldOrNull(entry, "Kind"));
                    string text = Convert.ToString(FieldOrNull(entry, "Text"));
                    _log.Add("[agent] chat[" + i + "] " + kind + ": " + text);
                }
            }
            _log.Add("[agent] chat panel visible=" + (runner != null && AsBool(FieldOrNull(demo, "agentHasSeat"))
                && AsBool(FieldOrNull(demo, "showAgentDetails"))));
            _log.Add("[agent] feed=" + string.Join(" | ", FeedLines(demo)));
            LogLines();
            Record(demo, "agent-turn", true);
            Capture("02-agent-turn");
            Next(2, 10);
        }

        // ---- step 2: an accepted field battle through the human submit path ------

        private static void StepFieldBattle(KingdomDemo demo)
        {
            var engine = Engine(demo);
            DismissReport(demo);
            var wei = engine.State.FindArmy("army-wei-1");
            var shu = engine.State.FindArmy("army-shu-1");
            if (wei == null || shu == null)
            {
                _log.Add("[field] FAILED: a vanguard is missing after the agent turn (armies="
                    + engine.State.Armies.Count + ")");
                Finish(1, "vanguard missing");
                return;
            }

            // Fixture: two walkable neighbouring hexes on the demo map.
            wei.X = 12; wei.Z = 8;
            shu.X = 13; shu.Z = 8;
            CallByName(demo, "SyncArmyVisual", wei.Id);
            CallByName(demo, "SyncArmyVisual", shu.Id);
            CallByName(demo, "RefreshObservation");
            _log.Add("[field] fixture: Wei at (12,8), Shu at (13,8); banners synced");

            string json = "{\"type\":\"attack_army\",\"target_id\":\"army-wei-1\",\"param_a\":13,\"param_b\":8,"
                        + "\"summary\":\"Rehearsal: engage the Shu vanguard.\"}";
            SubmitThroughDemoJson(demo, json, out bool accepted, out string error);
            _log.Add("[field] accepted=" + accepted + " error=" + (error ?? "-"));
            var battle = LatestEvent(engine, "battle_field");
            if (battle != null)
                _log.Add("[field] outcome=" + battle.Payload["outcome"]
                    + " rounds=" + battle.Payload["rounds"]
                    + " attacker " + battle.Payload["attacker_troops_before"] + "->" + battle.Payload["attacker_troops_after"]
                    + " defender " + battle.Payload["defender_troops_before"] + "->" + battle.Payload["defender_troops_after"]);
            _log.Add("[field] battleResultShowing=" + AsBool(Field(demo, "battleResultShowing"))
                + " status=\"" + (string)Field(demo, "statusMsg") + "\"");
            _log.Add("[field] banner army-wei-1 " + BannerActive(demo, "army-wei-1")
                + " pos=" + BannerPos(demo, "army-wei-1")
                + " / army-shu-1 " + BannerActive(demo, "army-shu-1")
                + " pos=" + BannerPos(demo, "army-shu-1"));
            _log.Add("[field] fog visible=" + VisibleFogCount(demo));
            LogLines();
            Record(demo, "field-battle", accepted && battle != null);
            Capture("03-field-battle");
            Next(3, 6);
        }

        // ---- step 3: an assault the 3:1 rule must refuse ------------------------

        private static void StepAssaultRejected(KingdomDemo demo)
        {
            DismissModals(demo);
            var engine = Engine(demo);
            var city = engine.State.FindCity("city-shu");
            city.Garrison = 1000;   // 3:1 needs 3,000; the host has 2,000
            var host = EnsureRehearsalArmy(demo, "army-wei-9", "Wei Siege Host", "seat-0", 21, 8, 2000);
            CallByName(demo, "RefreshObservation");
            CallByName(demo, "TintCity", city.Id);
            _log.Add("[assault-reject] fixture: Chengdu garrison 1000 at (" + city.X + "," + city.Z
                + "), host " + host.Id + " at (" + host.X + "," + host.Z + ") troops " + host.Troops
                + " adjacent=" + engine.State.Map.AreAdjacent(host.X, host.Z, city.X, city.Z));

            string json = "{\"type\":\"attack_city\",\"target_id\":\"army-wei-9\",\"param_a\":" + city.X
                 + ",\"param_b\":" + city.Z + "}";
            SubmitThroughDemoJson(demo, json, out bool accepted, out string error);
            var siege = LatestEvent(engine, "battle_siege");
            _log.Add("[assault-reject] accepted=" + accepted + " error=" + (error ?? "-")
                + " battleEvent=" + (siege == null ? "none" : siege.Payload["outcome"]));
            _log.Add("[assault-reject] status=\"" + (string)Field(demo, "statusMsg") + "\"");
            _log.Add("[assault-reject] battleResultShowing=" + AsBool(Field(demo, "battleResultShowing")));
            LogLines();
            Record(demo, "assault-rejected", !accepted && siege == null);
            Capture("04-assault-rejected");
            Next(4, 6);
        }

        // ---- step 4: a legal assault that takes the city -----------------------

        private static void StepAssaultCaptured(KingdomDemo demo)
        {
            DismissModals(demo);
            var engine = Engine(demo);
            var city = engine.State.FindCity("city-shu");
            city.Garrison = 500;    // 3:1 needs 1,500; the host qualifies
            CallByName(demo, "RefreshObservation");
            string beforeTint = TintOf(demo, "city-shu");

            string json = "{\"type\":\"attack_city\",\"target_id\":\"army-wei-9\",\"param_a\":" + city.X
                 + ",\"param_b\":" + city.Z + ",\"summary\":\"Rehearsal: storm Chengdu.\"}";
            SubmitThroughDemoJson(demo, json, out bool accepted, out string error);
            var siege = LatestEvent(engine, "battle_siege");
            _log.Add("[assault] accepted=" + accepted + " error=" + (error ?? "-"));
            if (siege != null)
                _log.Add("[assault] outcome=" + siege.Payload["outcome"]
                    + " rounds=" + siege.Payload["rounds"]
                    + " garrison " + siege.Payload["garrison_before"] + "->" + siege.Payload["garrison_after"]
                    + " troops " + siege.Payload["troops_before"] + "->" + siege.Payload["troops_after"]);
            _log.Add("[assault] owner now " + engine.State.FindCity("city-shu").OwnerSeatId
                + " / seat-0 cities=" + engine.State.CitiesOf("seat-0").Count);
            _log.Add("[assault] city tint before=" + beforeTint + " after=" + TintOf(demo, "city-shu")
                + " banner army-wei-9 " + BannerActive(demo, "army-wei-9")
                + " pos=" + BannerPos(demo, "army-wei-9"));
            _log.Add("[assault] status=\"" + (string)Field(demo, "statusMsg") + "\"");
            LogLines();
            Record(demo, "assault-captured", accepted && engine.State.FindCity("city-shu").OwnerSeatId == "seat-0");
            Capture("05-assault-captured");
            Next(5, 6);
        }

        // ---- step 5: unification through the existing engine rule --------------

        private static void StepUnification(KingdomDemo demo)
        {
            DismissModals(demo);
            var engine = Engine(demo);
            var south = engine.State.FindCity("city-south");
            var marcher = EnsureRehearsalArmy(demo, "army-wei-8", "Wei Reserve", "seat-0",
                south.X - 1, south.Z, 1200);
            _log.Add("[unification] fixture: reserve at (" + marcher.X + "," + marcher.Z
                + ") beside the neutral city at (" + south.X + "," + south.Z + ")");

            string json = "{\"type\":\"march\",\"target_id\":\"army-wei-8\",\"param_a\":" + south.X
                 + ",\"param_b\":" + south.Z + ",\"summary\":\"Rehearsal: take the southern city.\"}";
            SubmitThroughDemoJson(demo, json, out bool accepted, out string error);
            _log.Add("[unification] march accepted=" + accepted + " error=" + (error ?? "-"));

            var owners = new List<string>();
            foreach (var c in engine.State.Cities) owners.Add(c.Id + "=" + (c.OwnerSeatId ?? "neutral"));
            _log.Add("[unification] owners " + string.Join(", ", owners.ToArray()));
            _log.Add("[unification] WinnerSeatId=" + (engine.State.WinnerSeatId ?? "none"));
            var over = LatestEvent(engine, "game_over");
            _log.Add("[unification] game_over=" + (over == null ? "none" : over.Payload["reason"]));
            _log.Add("[unification] southern city tint=" + TintOf(demo, "city-south")
                + " status=\"" + (string)Field(demo, "statusMsg") + "\"");
            LogLines();
            Record(demo, "unification", accepted && engine.State.WinnerSeatId != null);
            Capture("06-unification");
            Next(9, 20);   // let the last screenshot flush before the process exits
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Parses the same JSON a model would send, then submits it through the demo's own
        /// human order path (the method the attack button calls). The engine still validates.</summary>
        private static void SubmitThroughDemoJson(KingdomDemo demo, string json, out bool accepted, out string error)
        {
            accepted = false;
            error = null;
            ParseResult parsed = LlmActionParser.Parse(json, true);
            if (!parsed.Ok) { error = "parser: " + parsed.Error; return; }
            var command = LlmActionParser.ToCommand(parsed.Action);
            accepted = (bool)CallByName(demo, "SubmitHuman", command);
            error = Convert.ToString(FieldOrNull(demo, "statusMsg"));
            _log.Add("[submit] json=" + json);
            _log.Add("[submit] parsed type=" + command.Type + " target=" + command.TargetId
                + " (" + command.ParamA + "," + command.ParamB + ")");
        }

        private static ArmyState EnsureRehearsalArmy(KingdomDemo demo, string id, string name, string seat,
            int x, int z, int troops)
        {
            var engine = Engine(demo);
            var army = engine.State.FindArmy(id);
            if (army == null)
            {
                army = new ArmyState
                {
                    Id = id, Name = name, OwnerSeatId = seat, X = x, Z = z, Troops = troops,
                    UnitType = UnitTypes.Infantry,
                };
                engine.State.Armies.Add(army);
            }
            army.X = x; army.Z = z;
            CallByName(demo, "EnsureArmyVisual", army);
            _log.Add("[fixture] army " + id + " at (" + x + "," + z + ") troops " + army.Troops
                + " banner " + BannerActive(demo, id));
            return army;
        }

        private static void DismissModals(KingdomDemo demo)
        {
            DismissReport(demo);
            if (AsBool(Field(demo, "battleResultShowing"))) SetField(demo, "battleResultShowing", false);
        }

        private static void DismissReport(KingdomDemo demo)
        {
            if (!AsBool(Field(demo, "reportShowing"))) return;
            SetField(demo, "reportShowing", false);
            SetField(demo, "pendingReport", false);
            _log.Add("[field] dismissed the season court report so the battle modal is visible");
        }

        private static void LogLines()
        {
            foreach (string line in _log) Debug.Log("[rehearsal] " + line);
        }

        private static void Next(int step, int waitFrames)
        {
            _log.Clear();
            Step = step;
            _waitFrames = waitFrames;
        }

        private static void Record(KingdomDemo demo, string stepName, bool ok)
        {
            var engine = EngineOrNull(demo);
            var sb = new StringBuilder();
            sb.Append("{\"step\":").Append(Json(stepName));
            sb.Append(",\"ok\":").Append(ok ? "true" : "false");
            sb.Append(",\"season\":").Append(engine == null ? -1 : engine.State.Season);
            sb.Append(",\"current_seat\":").Append(Json((string)Field(demo, "currentSeat")));
            sb.Append(",\"status\":").Append(Json((string)Field(demo, "statusMsg")));
            sb.Append(",\"battle_modal\":").Append(AsBool(Field(demo, "battleResultShowing")) ? "true" : "false");
            sb.Append(",\"report_showing\":").Append(AsBool(Field(demo, "reportShowing")) ? "true" : "false");
            sb.Append(",\"winner\":").Append(Json(engine == null ? null : engine.State.WinnerSeatId));
            sb.Append(",\"fog_visible\":").Append(VisibleFogCount(demo));
            sb.Append(",\"cities\":[");
            var cities = new List<string>();
            if (engine != null) foreach (var c in engine.State.Cities)
                cities.Add("{\"id\":" + Json(c.Id) + ",\"owner\":" + Json(c.OwnerSeatId)
                    + ",\"tint\":" + Json(TintOf(demo, c.Id)) + ",\"garrison\":" + c.Garrison + "}");
            sb.Append(string.Join(",", cities.ToArray())).Append("]");
            sb.Append(",\"armies\":[");
            var armies = new List<string>();
            if (engine != null) foreach (var a in engine.State.Armies)
                armies.Add("{\"id\":" + Json(a.Id) + ",\"owner\":" + Json(a.OwnerSeatId)
                    + ",\"x\":" + a.X + ",\"z\":" + a.Z + ",\"troops\":" + a.Troops
                    + ",\"banner\":" + Json(BannerActive(demo, a.Id))
                    + ",\"banner_pos\":" + Json(BannerPos(demo, a.Id)) + "}");
            sb.Append(string.Join(",", armies.ToArray())).Append("]");
            sb.Append(",\"feed\":[");
            var feedLines = new List<string>();
            foreach (var line in (System.Collections.IEnumerable)Field(demo, "jsonFeed"))
                feedLines.Add(Json(line as string));
            sb.Append(string.Join(",", feedLines.ToArray())).Append("]");
            sb.Append(",\"log\":[");
            var logs = new List<string>();
            foreach (string line in _log) logs.Add(Json(line));
            sb.Append(string.Join(",", logs.ToArray())).Append("]}");
            File.AppendAllText(StepsPath(), sb.ToString() + "\n");
        }

        private static void Capture(string name)
        {
            string path = Path.Combine(OutDir, name + ".png").Replace('\\', '/');
            ScreenCapture.CaptureScreenshot(path, 1);
            Debug.Log("[rehearsal] screenshot -> " + path);
        }

        private static void Finish(int code, string reason)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            SessionState.SetBool(ActiveKey, false);
            Unhook();

            var steps = new List<string>();
            if (File.Exists(StepsPath()))
                foreach (string line in File.ReadAllLines(StepsPath()))
                    if (!string.IsNullOrWhiteSpace(line)) steps.Add(line);

            var envelope = new StringBuilder();
            envelope.Append("{\n");
            envelope.Append("  \"scene\": ").Append(Json(DemoScene)).Append(",\n");
            envelope.Append("  \"project\": ").Append(Json(Path.GetFullPath(Path.Combine(Application.dataPath, "..")))).Append(",\n");
            envelope.Append("  \"started\": ").Append(Json(DateTime.Now.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
            envelope.Append("  \"result\": ").Append(Json(reason)).Append(",\n");
            envelope.Append("  \"exit_code\": ").Append(code).Append(",\n");
            envelope.Append("  \"wall_seconds\": ").Append(((float)EditorApplication.timeSinceStartup
                - SessionState.GetFloat(StartedKey, 0f)).ToString("0.0", CultureInfo.InvariantCulture)).Append(",\n");
            envelope.Append("  \"steps\": [\n    ").Append(string.Join(",\n    ", steps.ToArray())).Append("\n  ]\n}\n");
            string path = Path.Combine(OutDir, "rehearsal-evidence.json");
            File.WriteAllText(path, envelope.ToString());
            Debug.Log("[rehearsal] evidence -> " + path + " (" + reason + ", exit " + code + ")");
            Debug.Log("[rehearsal] scene not saved; play-mode changes discarded");

            // The process must end even though play mode would otherwise keep it alive.
            EditorApplication.Exit(0);
        }

        private static GameEngine Engine(KingdomDemo demo) => (GameEngine)Field(demo, "engine");

        /// <summary>
        /// The live demo component. During Play Mode a stale or duplicate component can be returned
        /// first, so prefer the instance that actually owns an engine; report how many were found.
        /// </summary>
        private static KingdomDemo ResolveDemo(out int instances)
        {
            var all = UnityEngine.Object.FindObjectsByType<KingdomDemo>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            instances = all.Length;
            KingdomDemo fallback = null;
            foreach (var candidate in all)
            {
                if (candidate == null) continue;
                if (fallback == null) fallback = candidate;
                if (EngineOrNull(candidate) != null) return candidate;
            }
            return fallback;
        }

        private static GameEngine EngineOrNull(KingdomDemo demo)
        {
            try { return demo == null ? null : FieldOrNull(demo, "engine") as GameEngine; }
            catch (Exception) { return null; }
        }

        private static IEnumerable<string> FeedLines(KingdomDemo demo)
        {
            var feed = FieldOrNull(demo, "jsonFeed") as System.Collections.IEnumerable;
            if (feed == null) yield break;
            foreach (var line in feed) yield return line as string;
        }

        private static object Field(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }
            throw new InvalidOperationException("no field " + name + " on " + target.GetType().Name);
        }

        private static object FieldOrNull(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null) return field.GetValue(target);
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null && property.CanRead) return property.GetValue(target);
                type = type.BaseType;
            }
            return null;
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new InvalidOperationException("no field " + name);
            field.SetValue(target, value);
        }

        private static object CallByName(object target, string name, params object[] args)
        {
            foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (method.Name != name) continue;
                if (method.GetParameters().Length != args.Length) continue;
                return method.Invoke(target, args);
            }
            throw new InvalidOperationException("no method " + name + "/" + args.Length + " on " + target.GetType().Name);
        }

        private static bool AsBool(object value) => value is bool b && b;

        private static int Count(object enumerable)
        {
            int n = 0;
            foreach (var unused in (System.Collections.IEnumerable)enumerable) n++;
            return n;
        }

        private static string BannerActive(KingdomDemo demo, string armyId)
        {
            var map = (System.Collections.IDictionary)Field(demo, "armyObjs");
            if (!map.Contains(armyId)) return "no-banner";
            var go = map[armyId] as GameObject;
            return go == null ? "destroyed" : (go.activeInHierarchy ? "active" : "hidden");
        }

        private static string BannerPos(KingdomDemo demo, string armyId)
        {
            var map = (System.Collections.IDictionary)Field(demo, "armyObjs");
            if (!map.Contains(armyId)) return null;
            var go = map[armyId] as GameObject;
            if (go == null) return null;
            var p = go.transform.position;
            return p.x.ToString("0.00", CultureInfo.InvariantCulture) + ","
                 + p.z.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string TintOf(KingdomDemo demo, string cityId)
        {
            var map = (System.Collections.IDictionary)Field(demo, "cityObjs");
            if (!map.Contains(cityId)) return "no-marker";
            var go = map[cityId] as GameObject;
            if (go == null) return "destroyed";
            var sprite = go.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) return "#" + ColorUtility.ToHtmlStringRGB(sprite.color);
            var mesh = go.GetComponentInChildren<MeshRenderer>();
            if (mesh != null && mesh.sharedMaterial != null && mesh.sharedMaterial.HasProperty("_BaseColor"))
                return "#" + ColorUtility.ToHtmlStringRGB(mesh.sharedMaterial.GetColor("_BaseColor"));
            return "unknown";
        }

        private static int VisibleFogCount(KingdomDemo demo)
        {
            int visible = 0;
            foreach (var item in (System.Collections.IEnumerable)Field(demo, "fogOverlays"))
            {
                var sprite = item as SpriteRenderer;
                if (sprite != null && sprite.enabled && sprite.gameObject.activeInHierarchy) visible++;
            }
            return visible;
        }

        private static EventRecord LatestEvent(GameEngine engine, string type)
        {
            for (int i = engine.Log.Records.Count - 1; i >= 0; i--)
                if (engine.Log.Records[i].EventType == type) return engine.Log.Records[i];
            return null;
        }

        private static string Json(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
