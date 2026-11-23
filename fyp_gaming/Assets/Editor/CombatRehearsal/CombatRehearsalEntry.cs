using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LuanShi.Engine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LuanShi.EditorTools
{
    /// <summary>
    /// Scripted Play Mode rehearsal for the opt-in compact scenario. Launched from the command line:
    ///
    ///   set LUANSHI_REHEARSAL_OUT=&lt;evidence dir&gt;
    ///   set LUANSHI_REHEARSAL_MODE=agent        (default) or "spectator"
    ///   Unity.exe -projectPath &lt;worktree&gt;/fyp_gaming ^
    ///             -executeMethod LuanShi.EditorTools.CombatRehearsalEntry.Run
    ///
    /// It opens the shipped demo scene, selects the compact scenario and a balance profile in memory
    /// (never saved), enters Play Mode, drives battle and assault commands through the demo's own submit
    /// path, records what the engine and the presentation layer did, writes evidence beside the
    /// screenshots, and exits. Every fixture change is in-memory game state for this play session.
    ///
    /// Entering Play Mode reloads the domain, which clears static delegates. The state machine therefore
    /// persists its position in <see cref="SessionState"/> and is re-hooked by the
    /// <see cref="InitializeOnLoadAttribute"/> constructor.
    /// </summary>
    [InitializeOnLoad]
    public static class CombatRehearsalEntry
    {
        private const string DemoScene = "Assets/GameUI/Scenes/LuanShi_Demo.unity";
        private const string ScenarioKey = "compact-contact-v1";
        private const string ProfileKey = "food-easy";
        private const string ActiveKey = "LuanShi.CombatRehearsal.Active";
        private const string DirKey = "LuanShi.CombatRehearsal.Dir";
        private const string StepKey = "LuanShi.CombatRehearsal.Step";
        private const string StartedKey = "LuanShi.CombatRehearsal.Started";
        private const string PhaseKey = "LuanShi.CombatRehearsal.Phase";
        private const string IdleKey = "LuanShi.CombatRehearsal.Idle";
        private const string ModeKey = "LuanShi.CombatRehearsal.Mode";
        private const string KeepOpenKey = "LuanShi.CombatRehearsal.KeepOpen";

        private static bool _hooked;
        private static int _waitFrames;
        private static readonly List<string> _log = new List<string>();

        static CombatRehearsalEntry()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        public static void Run()
        {
            Start(Environment.GetEnvironmentVariable("LUANSHI_REHEARSAL_KEEP_OPEN") == "1");
        }

        /// <summary>
        /// Same rehearsal from inside an open editor: on finish it leaves Play Mode instead of exiting
        /// the editor, and it never reopens (or saves) the scene. Mode comes from LUANSHI_REHEARSAL_MODE.
        /// </summary>
        [MenuItem("LuanShi/Combat rehearsal (keep editor open)")]
        public static void RunKeepOpen() => Start(true);

        private static void Start(bool keepOpen)
        {
            SessionState.SetBool(KeepOpenKey, keepOpen);
            string outDir = Environment.GetEnvironmentVariable("LUANSHI_REHEARSAL_OUT");
            if (string.IsNullOrEmpty(outDir))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                outDir = Path.Combine(projectRoot, "..", "runs",
                    "unity-scenario-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);
            string mode = Environment.GetEnvironmentVariable("LUANSHI_REHEARSAL_MODE");
            if (string.IsNullOrEmpty(mode)) mode = "agent";

            SessionState.SetString(DirKey, outDir.Replace('\\', '/'));
            SessionState.SetString(ModeKey, mode);
            SessionState.SetInt(StepKey, 0);
            SessionState.SetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(PhaseKey, "select");
            SessionState.SetInt(IdleKey, 0);
            File.WriteAllText(StepsPath(), "");

            Hook();
            Debug.Log("[rehearsal] mode=" + mode + " scenario=" + ScenarioKey + " profile=" + ProfileKey
                + "; waiting for the editor to settle before selecting them");
        }

        private static string OutDir => SessionState.GetString(DirKey, "");
        private static string Mode => SessionState.GetString(ModeKey, "agent");
        private static string StepsPath() => Path.Combine(OutDir, "rehearsal-steps.jsonl").Replace('\\', '/');
        private static int Step { get => SessionState.GetInt(StepKey, 0); set => SessionState.SetInt(StepKey, value); }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            EditorApplication.update += Tick;
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            EditorApplication.update -= Tick;
        }

        // ------------------------------------------------------------------ state machine

        private static void Tick()
        {
            try
            {
                if (!SessionState.GetBool(ActiveKey, false)) { Unhook(); return; }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

                string phase = SessionState.GetString(PhaseKey, "select");

                if (phase == "select")
                {
                    int idle = SessionState.GetInt(IdleKey, 0) + 1;
                    SessionState.SetInt(IdleKey, idle);
                    if (idle < 90) return;

                    // Keep-open runs reuse the already open demo scene: a reopen would prompt to save it.
                    if (!SessionState.GetBool(KeepOpenKey, false)
                        || EditorSceneManager.GetActiveScene().path != DemoScene)
                        EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Single);
                    var demo = UnityEngine.Object.FindAnyObjectByType<KingdomDemo>();
                    if (demo == null) { Finish(4, "the demo scene has no KingdomDemo component"); return; }
                    // Opt-in selection, in memory only. The scene is never saved.
                    SetField(demo, "scenario", ScenarioKey);
                    SetField(demo, "balanceProfile", ProfileKey);
                    if (Mode == "spectator")
                    {
                        SetField(demo, "spectatorMode", true);
                        SetField(demo, "spectatorSeasons", 1);
                        SetField(demo, "useFakeLlm", true);
                    }
                    Debug.Log("[rehearsal] selected scenario=" + ScenarioKey + " profile=" + ProfileKey
                        + " spectator=" + (Mode == "spectator") + "; entering Play Mode");
                    SessionState.SetString(PhaseKey, "play");
                    EditorApplication.EnterPlaymode();
                    return;
                }

                if (!EditorApplication.isPlaying) return;
                if (_waitFrames > 0) { _waitFrames--; return; }

                var live = ResolveDemo();
                if (live == null) return;
                if (!AsBool(FieldOrNull(live, "ready"))) return;
                if (EngineOrNull(live) == null)
                {
                    Finish(3, "the demo lost its engine reference: a domain reload happened during Play Mode");
                    return;
                }

                switch (Step)
                {
                    case 0: StepSelection(live); break;
                    case 1: if (Mode == "spectator") StepSpectator(live); else StepAgentTurn(live); break;
                    case 11: StepAgentTurnWait(live); break;
                    case 2: StepFieldBattle(live); break;
                    case 4: StepAssaultCaptured(live); break;
                    case 9: Finish(0, "rehearsal complete"); break;
                    default: Finish(0, "rehearsal complete"); break;
                }
            }
            catch (Exception e)
            {
                Finish(2, "rehearsal threw: " + e);
            }
        }

        // ---- step 0: the selected scenario and profile are live ------------------

        private static void StepSelection(KingdomDemo demo)
        {
            var engine = Engine(demo);
            var scenario = CompactScenario.CompactContact;
            _log.Add("[select] scenario=" + (engine.State.ScenarioId ?? "none")
                + " profile=" + engine.Balance.Key + " season=" + engine.State.Season);
            _log.Add("[select] cities=" + engine.State.Cities.Count + " armies=" + engine.State.Armies.Count
                + " towers=" + engine.State.WatchTowers.Count
                + " roster=" + Roster(engine));
            _log.Add("[select] expected scenario=" + scenario.ScenarioId + " keys="
                + string.Join(",", CompactScenario.Keys.ToArray()));

            foreach (var city in engine.State.Cities)
                _log.Add("[select] city " + city.Id + " owner=" + (city.OwnerSeatId ?? "neutral")
                    + " at (" + city.X + "," + city.Z + ") food=" + city.Food + " gold=" + city.Gold
                    + " garrison=" + city.Garrison
                    + " tint=" + TintOf(demo, city.Id));
            foreach (var army in engine.State.Armies)
                _log.Add("[select] army " + army.Id + " owner=" + army.OwnerSeatId + " at ("
                    + army.X + "," + army.Z + ") troops=" + army.Troops + " banner=" + BannerActive(demo, army.Id));

            var wei = engine.Observe("seat-0");
            var shu = engine.Observe("seat-1");
            _log.Add("[select] seat-0 sees " + Names(wei));
            _log.Add("[select] seat-1 sees " + Names(shu));
            _log.Add("[select] fog overlays=" + Count(Field(demo, "fogOverlays")) + " visible=" + VisibleFogCount(demo)
                + " marker counts cities=" + Count(Field(demo, "cityObjs"))
                + " armies=" + Count(Field(demo, "armyObjs"))
                + " towers=" + Count(Field(demo, "watchTowerObjs")));

            bool ok = engine.State.ScenarioId == CompactScenario.CompactContactId
                      && engine.Balance.Key == ProfileKey
                      && engine.State.Cities.Count == scenario.Cities.Count
                      && engine.State.Armies.Count == scenario.Armies.Count
                      && engine.State.CitiesOf("seat-0").TrueForAll(c => c.Food == BalanceConfig.FoodEasyStartFood);
            _log.Add("[select] PASS=" + ok);
            foreach (string line in _log) Debug.Log("[rehearsal] " + line);
            Record(demo, "selection", ok);
            Capture("01-selection");
            Next(1, 5);
        }

        // ---- step 1: the LLM seat's own turn, or the spectator pilot -------------

        private static void StepAgentTurn(KingdomDemo demo)
        {
            SetField(demo, "useFakeLlm", true);
            CallByName(demo, "RebuildAgentRunner");
            var runner = Field(demo, "agentRunner");
            _log.Add("[agent] fake transport installed; runner=" + (runner != null)
                + " configAttack=" + AsBool(FieldOrNull(Field(demo, "agentConfig"), "EnableAttack")));
            CallByName(demo, "DebugEndTurn");   // the End-turn button's own path
            _log.Add("[agent] human seat ended; the fake model seat now plays the season");
            LogLines();
            SessionState.SetInt("LuanShi.CombatRehearsal.Wait", 0);
            Next(11, 20);
        }

        private static void StepAgentTurnWait(KingdomDemo demo)
        {
            var engine = EngineOrNull(demo);
            bool active = AsBool(FieldOrNull(demo, "agentTurnActive"));
            int waited = SessionState.GetInt("LuanShi.CombatRehearsal.Wait", 0) + 1;
            SessionState.SetInt("LuanShi.CombatRehearsal.Wait", waited);
            if (active && waited < 900) { _waitFrames = 2; return; }

            var runner = FieldOrNull(demo, "agentRunner");
            var activity = runner == null ? null : FieldOrNull(runner, "Activity") as System.Collections.IList;
            _log.Add("[agent] turn finished: season=" + (engine == null ? -1 : engine.State.Season)
                + " runner=" + (runner != null) + " fake=" + AsBool(FieldOrNull(demo, "agentFakeMode"))
                + " chat_lines=" + (activity == null ? 0 : activity.Count));
            if (activity != null)
            {
                for (int i = Math.Max(0, activity.Count - 3); i < activity.Count; i++)
                    _log.Add("[agent] chat[" + i + "] "
                        + Convert.ToString(FieldOrNull(activity[i], "Kind")) + ": "
                        + Convert.ToString(FieldOrNull(activity[i], "Text")));
            }
            _log.Add("[agent] feed=" + string.Join(" | ", FeedLines(demo)));
            LogLines();
            Record(demo, "agent-turn", runner != null);
            Capture("02-agent-turn");
            Next(2, 8);
        }

        private static void StepSpectator(KingdomDemo demo)
        {
            var engine = Engine(demo);
            var session = FieldOrNull(demo, "spectatorSession");
            _log.Add("[spectator] scenario=" + (engine.State.ScenarioId ?? "none")
                + " profile=" + engine.Balance.Key
                + " roster=" + Roster(engine)
                + " session=" + (session != null)
                + " setupError=" + (FieldOrNull(demo, "spectatorSetupError") ?? "none"));
            _log.Add("[spectator] both seats are LLM-controlled: "
                + (engine.State.Factions.TrueForAll(f => f.Controller == ControllerType.Llm)));
            LogLines();
            Record(demo, "spectator-selection",
                engine.State.ScenarioId == CompactScenario.CompactContactId
                && engine.Balance.Key == ProfileKey
                && engine.State.Factions.TrueForAll(f => f.Controller == ControllerType.Llm));
            Capture("02-spectator");
            Next(9, 12);
        }

        // ---- step 2: an accepted field battle -----------------------------------

        private static void StepFieldBattle(KingdomDemo demo)
        {
            var engine = Engine(demo);
            DismissModals(demo);
            var wei = engine.State.FindArmy("army-wei-1");
            var shu = engine.State.FindArmy("army-shu-1");
            if (wei == null || shu == null) { Finish(1, "scenario vanguard missing"); return; }

            wei.X = 12; wei.Z = 8;
            shu.X = 13; shu.Z = 8;
            CallByName(demo, "SyncArmyVisual", wei.Id);
            CallByName(demo, "SyncArmyVisual", shu.Id);
            CallByName(demo, "RefreshObservation");
            _log.Add("[field] fixture: Wei at (12,8), Shu at (13,8) on the scenario map; banners synced");

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
            _log.Add("[field] banner army-wei-1 " + BannerActive(demo, "army-wei-1") + " pos=" + BannerPos(demo, "army-wei-1")
                + " / army-shu-1 " + BannerActive(demo, "army-shu-1") + " pos=" + BannerPos(demo, "army-shu-1"));
            LogLines();
            Record(demo, "field-battle", accepted && battle != null);
            Capture("03-field-battle");
            Next(4, 6);
        }

        // ---- step 4: an assault captures the capital ---------------------------

        private static void StepAssaultCaptured(KingdomDemo demo)
        {
            DismissModals(demo);
            var engine = Engine(demo);
            var city = engine.State.FindCity("city-shu");
            city.Garrison = 500;
            EnsureRehearsalArmy(demo, "army-wei-9", "Wei Siege Host", "seat-0", 19, 8, 2000);
            CallByName(demo, "RefreshObservation");
            string beforeTint = TintOf(demo, "city-shu");

            string json = "{\"type\":\"attack_city\",\"target_id\":\"army-wei-9\",\"param_a\":" + city.X
                 + ",\"param_b\":" + city.Z + ",\"summary\":\"Rehearsal: storm the capital.\"}";
            SubmitThroughDemoJson(demo, json, out bool accepted, out string error);
            var siege = LatestEvent(engine, "battle_siege");
            _log.Add("[assault] accepted=" + accepted + " error=" + (error ?? "-"));
            if (siege != null)
                _log.Add("[assault] outcome=" + siege.Payload["outcome"]
                    + " rounds=" + siege.Payload["rounds"]
                    + " garrison " + siege.Payload["garrison_before"] + "->" + siege.Payload["garrison_after"]);
            _log.Add("[assault] owner now " + engine.State.FindCity("city-shu").OwnerSeatId
                + " / seat-0 cities=" + engine.State.CitiesOf("seat-0").Count
                + " tint " + beforeTint + " -> " + TintOf(demo, "city-shu"));
            _log.Add("[assault] status=\"" + (string)Field(demo, "statusMsg") + "\"");
            _log.Add("[assault] scenario=" + (engine.State.ScenarioId ?? "none") + " profile=" + engine.Balance.Key);
            LogLines();
            Record(demo, "assault-captured", accepted && engine.State.FindCity("city-shu").OwnerSeatId == "seat-0");
            Capture("04-assault-captured");
            Next(9, 15);
        }

        // ------------------------------------------------------------------ helpers

        private static string Roster(GameEngine engine)
        {
            var parts = new List<string>();
            foreach (var f in engine.State.Factions) parts.Add(f.SeatId + "=" + f.Name + "/" + f.Controller);
            return string.Join(", ", parts.ToArray());
        }

        private static string Names(Observation obs)
        {
            if (obs == null) return "(none)";
            var names = new List<string>();
            foreach (var c in obs.Cities) names.Add("city " + c.Name);
            foreach (var a in obs.Armies) names.Add("army " + a.Name);
            foreach (var t in obs.WatchTowers) names.Add("tower " + t.Name);
            return names.Count == 0 ? "(nothing)" : string.Join(", ", names.ToArray());
        }

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
            if (AsBool(FieldOrNull(demo, "reportShowing")))
            {
                SetField(demo, "reportShowing", false);
                SetField(demo, "pendingReport", false);
            }
            if (AsBool(FieldOrNull(demo, "battleResultShowing")))
                SetField(demo, "battleResultShowing", false);
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
            sb.Append(",\"mode\":").Append(Json(Mode));
            sb.Append(",\"scenario\":").Append(Json(engine == null ? null : engine.State.ScenarioId));
            sb.Append(",\"profile\":").Append(Json(engine == null || engine.Balance == null ? null : engine.Balance.Key));
            sb.Append(",\"season\":").Append(engine == null ? -1 : engine.State.Season);
            sb.Append(",\"current_seat\":").Append(Json((string)FieldOrNull(demo, "currentSeat")));
            sb.Append(",\"status\":").Append(Json((string)FieldOrNull(demo, "statusMsg")));
            sb.Append(",\"battle_modal\":").Append(AsBool(FieldOrNull(demo, "battleResultShowing")) ? "true" : "false");
            sb.Append(",\"report_showing\":").Append(AsBool(FieldOrNull(demo, "reportShowing")) ? "true" : "false");
            sb.Append(",\"winner\":").Append(Json(engine == null ? null : engine.State.WinnerSeatId));
            sb.Append(",\"fog_visible\":").Append(VisibleFogCount(demo));
            sb.Append(",\"cities\":[");
            var cities = new List<string>();
            if (engine != null)
                foreach (var c in engine.State.Cities)
                    cities.Add("{\"id\":" + Json(c.Id) + ",\"owner\":" + Json(c.OwnerSeatId)
                        + ",\"x\":" + c.X + ",\"z\":" + c.Z + ",\"food\":" + c.Food + ",\"gold\":" + c.Gold
                        + ",\"tint\":" + Json(TintOf(demo, c.Id)) + ",\"garrison\":" + c.Garrison + "}");
            sb.Append(string.Join(",", cities.ToArray())).Append("]");
            sb.Append(",\"armies\":[");
            var armies = new List<string>();
            if (engine != null)
                foreach (var a in engine.State.Armies)
                    armies.Add("{\"id\":" + Json(a.Id) + ",\"owner\":" + Json(a.OwnerSeatId)
                        + ",\"x\":" + a.X + ",\"z\":" + a.Z + ",\"troops\":" + a.Troops
                        + ",\"banner\":" + Json(BannerActive(demo, a.Id))
                        + ",\"banner_pos\":" + Json(BannerPos(demo, a.Id)) + "}");
            sb.Append(string.Join(",", armies.ToArray())).Append("]");
            sb.Append(",\"feed\":[");
            var feedLines = new List<string>();
            foreach (var line in FeedLines(demo)) feedLines.Add(Json(line));
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
            envelope.Append("  \"scenario_selected\": ").Append(Json(ScenarioKey)).Append(",\n");
            envelope.Append("  \"profile_selected\": ").Append(Json(ProfileKey)).Append(",\n");
            envelope.Append("  \"mode\": ").Append(Json(Mode)).Append(",\n");
            envelope.Append("  \"project\": ").Append(Json(Path.GetFullPath(Path.Combine(Application.dataPath, "..")))).Append(",\n");
            envelope.Append("  \"result\": ").Append(Json(reason)).Append(",\n");
            envelope.Append("  \"exit_code\": ").Append(code).Append(",\n");
            envelope.Append("  \"wall_seconds\": ").Append(((float)EditorApplication.timeSinceStartup
                - SessionState.GetFloat(StartedKey, 0f)).ToString("0.0", CultureInfo.InvariantCulture)).Append(",\n");
            envelope.Append("  \"steps\": [\n    ").Append(string.Join(",\n    ", steps.ToArray())).Append("\n  ]\n}\n");
            string path = Path.Combine(OutDir, "rehearsal-evidence.json");
            File.WriteAllText(path, envelope.ToString());
            Debug.Log("[rehearsal] evidence -> " + path + " (" + reason + ", exit " + code + ")");
            Debug.Log("[rehearsal] scene file untouched; play-mode changes discarded");
            if (SessionState.GetBool(KeepOpenKey, false))
            {
                Debug.Log("[rehearsal] keep-open run: leaving Play Mode, editor stays open");
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                return;
            }
            EditorApplication.Exit(0);
        }

        private static KingdomDemo ResolveDemo()
        {
            var all = UnityEngine.Object.FindObjectsByType<KingdomDemo>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            KingdomDemo fallback = null;
            foreach (var candidate in all)
            {
                if (candidate == null) continue;
                if (fallback == null) fallback = candidate;
                if (EngineOrNull(candidate) != null) return candidate;
            }
            return fallback;
        }

        private static GameEngine Engine(KingdomDemo demo) => (GameEngine)Field(demo, "engine");

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
            var value = FieldOrNull(target, name);
            if (value == null && !FieldOrPropertyExists(target, name))
                throw new InvalidOperationException("no field " + name + " on " + target.GetType().Name);
            return value;
        }

        private static bool FieldOrPropertyExists(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null)
                    return true;
                if (type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null)
                    return true;
                type = type.BaseType;
            }
            return false;
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
            if (enumerable is System.Collections.IEnumerable e) foreach (var unused in e) n++;
            return n;
        }

        private static string BannerActive(KingdomDemo demo, string armyId)
        {
            var map = FieldOrNull(demo, "armyObjs") as System.Collections.IDictionary;
            if (map == null || !map.Contains(armyId)) return "no-banner";
            var go = map[armyId] as GameObject;
            return go == null ? "destroyed" : (go.activeInHierarchy ? "active" : "hidden");
        }

        private static string BannerPos(KingdomDemo demo, string armyId)
        {
            var map = FieldOrNull(demo, "armyObjs") as System.Collections.IDictionary;
            if (map == null || !map.Contains(armyId)) return null;
            var go = map[armyId] as GameObject;
            if (go == null) return null;
            var p = go.transform.position;
            return p.x.ToString("0.00", CultureInfo.InvariantCulture) + ","
                 + p.z.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string TintOf(KingdomDemo demo, string cityId)
        {
            var map = FieldOrNull(demo, "cityObjs") as System.Collections.IDictionary;
            if (map == null || !map.Contains(cityId)) return "no-marker";
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
            if (!(FieldOrNull(demo, "fogOverlays") is System.Collections.IEnumerable list)) return 0;
            foreach (var item in list)
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
