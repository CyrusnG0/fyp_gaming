using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    /// <summary>
    /// Thin Unity adapter between the pure C# kingdom engine and the TBTK-scaffolded
    /// scene. TBTK provides the hex grid, node data and camera only; its turn/combat
    /// flow is disabled (GameControl is switched off after its Awake has initialised
    /// the grid). All game rules live in LuanShi.Engine; this script only translates
    /// clicks into ActionCommands and engine state back into visuals.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class KingdomDemo : MonoBehaviour
    {
        [Header("Scene wiring (object names in LuanShi_Demo)")]
        public string weiCityObj = "City_Wei";
        public string shuCityObj = "City_Shu";
        public string southCityObj = "City_South";
        public string weiArmyObj = "Wei_Army1";
        public string shuArmyObj = "Shu_Army1";
        public string[] unusedArmyObjs = { "Wei_Army2", "Wei_Army3", "Wei_Army4",
                                           "Shu_Army2", "Shu_Army3", "Shu_Army4" };

        [Header("Game setup")]
        public int randomSeed = 12345;

        private GameEngine engine;
        private bool ready;
        private string initError;

        private const string HumanSeat = "seat-0";
        private const string AiSeat = "seat-1";

        private readonly Dictionary<string, GameObject> cityObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> armyObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Color> factionColors = new Dictionary<string, Color>();

        // selection & UI state
        private CityState selCity;
        private ArmyState selArmy;
        private string statusMsg = "";
        private readonly List<string> jsonFeed = new List<string>();   // recent ActionCommand JSON
        private bool reportShowing;
        private readonly List<string> reportLines = new List<string>();
        private readonly List<Rect> uiRects = new List<Rect>();
        private string envoyText = "";        // 遣使 draft for the 外交 tab (never sent automatically)

        // concept tabs: 地圖 is the live view, the rest preview the full design
        // (doc/LuanShi-Game-Design-v1.md). 內政/外交 are live, 諜報/研究 previews, 史官 renders real data.
        private string activeTab = "map";
        private Vector2 historianScroll;
        private static readonly string[,] Tabs =
        {
            { "map",       "地圖" },
            { "domestic",  "內政" },
            { "diplomacy", "外交" },
            { "intel",     "諜報" },
            { "research",  "研究" },
            { "history",   "史官" },
        };

        // ------------------------------------------------------------------ setup

        void Awake()
        {
        }

        System.Collections.IEnumerator Start()
        {
            // Let TBTK's Awake methods initialize the serialized grid before replacing
            // its tactical flow with the kingdom controller.
            yield return null;

            var gc = FindAnyObjectByType<TBTK.GameControl>();
            if (gc != null) gc.enabled = false;

            // TBTK's tactical-layer UI (HUD, perk menu, ability bars) is not part of the
            // kingdom demo; hide it so only the strategic IMGUI shows.
            foreach (var mb in FindObjectsByType<MonoBehaviour>())
                if (mb.GetType().Namespace == "TBTK" && mb.GetType().Name.StartsWith("UI"))
                    mb.gameObject.SetActive(false);

            try { InitGame(); ready = true; }
            catch (System.Exception e)
            {
                initError = e.Message;
                Debug.LogError("[KingdomDemo] init failed: " + e);
            }
        }

        void InitGame()
        {
            var node00 = TBTK.GridManager.GetNode(0, 0);
            if (node00 == null) throw new System.Exception("TBTK grid not initialised");

            int w = TBTK.GridManager.DimensionX(), h = TBTK.GridManager.DimensionZ();
            var map = new MapData
            {
                Width = w,
                Height = h,
                Walkable = new bool[w * h],
                Cost = new int[w * h],
                Neighbors = new int[w * h][],
            };

            var nodes = new TBTK.Node[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    // TBTK hex grids are ragged: even columns have dimensionZ-1 nodes,
                    // so GetNode returns null for the missing cells. Treat as unwalkable.
                    var n = TBTK.GridManager.GetNode(x, z);
                    nodes[x, z] = n;
                    int i = map.Index(x, z);
                    map.Walkable[i] = n != null && n.walkable;
                    map.Cost[i] = n != null ? Mathf.Max(1, Mathf.RoundToInt(n.cost)) : 1;
                }

            // hex adjacency via TBTK's per-node cube coordinates (distance 1)
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    var list = new List<int>();
                    var a = nodes[x, z];
                    if (a != null)
                        for (int x2 = Mathf.Max(0, x - 1); x2 <= Mathf.Min(w - 1, x + 1); x2++)
                            for (int z2 = Mathf.Max(0, z - 1); z2 <= Mathf.Min(h - 1, z + 1); z2++)
                            {
                                if (x2 == x && z2 == z) continue;
                                var b = nodes[x2, z2];
                                if (b == null) continue;
                                int cube = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
                                if (cube == 2) list.Add(map.Index(x2, z2));
                            }
                    map.Neighbors[map.Index(x, z)] = list.ToArray();
                }

            engine = new GameEngine(randomSeed);
            engine.State.Map = map;

            factionColors[HumanSeat] = new Color(0.40f, 0.55f, 0.95f);   // 魏 blue
            factionColors[AiSeat] = new Color(0.45f, 0.80f, 0.45f);      // 蜀 green

            engine.State.Factions.Add(new FactionState { SeatId = HumanSeat, Name = "魏", Controller = ControllerType.Human });
            engine.State.Factions.Add(new FactionState { SeatId = AiSeat, Name = "蜀", Controller = ControllerType.ScriptedAi });

            AddCity(weiCityObj, "city-wei", "魏都", HumanSeat);
            AddCity(shuCityObj, "city-shu", "成都", AiSeat);
            AddCity(southCityObj, "city-south", "南城", null);

            AddArmy(weiArmyObj, "army-wei-1", "魏軍", HumanSeat);
            AddArmy(shuArmyObj, "army-shu-1", "蜀軍", AiSeat);
            foreach (var name in unusedArmyObjs)
            {
                var go = FindSceneObject(name);
                if (go == null || !go.activeInHierarchy) continue;

                bool isWei = name.StartsWith("Wei_", System.StringComparison.Ordinal);
                string faction = isWei ? "wei" : "shu";
                string seat = isWei ? HumanSeat : AiSeat;
                string number = name.Substring(name.Length - 1);
                AddArmy(name, "army-" + faction + "-" + number, isWei ? "魏軍" : "蜀軍", seat);
            }

            engine.BeginSeason();
            statusMsg = "第一季開始 — 點選城市或部隊下達命令";
            Debug.Log("[KingdomDemo] game started, season 1");
        }

        void AddCity(string objName, string id, string displayName, string ownerSeat)
        {
            var go = FindOrCreateMarker(objName, id == "city-wei" ? 2 : id == "city-shu" ? 9 : 6, id == "city-wei" ? 2 : id == "city-shu" ? 6 : 4);
            if (go == null) throw new System.Exception("scene object missing: " + objName);
            var node = TBTK.GridManager.GetNode(go.transform.position, null);
            if (node == null) throw new System.Exception(objName + " is not on a grid node");

            var city = new CityState
            {
                Id = id,
                Name = displayName,
                X = node.idxX,
                Z = node.idxZ,
                OwnerSeatId = ownerSeat,
            };
            if (ownerSeat != null)
            {
                city.Population = BalanceConfig.StartPopulation;
                city.Food = BalanceConfig.StartFood;
                city.Gold = BalanceConfig.StartGold;
            }
            else
            {
                city.Population = 3000; city.Food = 200; city.Gold = 100;   // TBD: neutral city stats
            }
            engine.State.Cities.Add(city);
            cityObjs[id] = go;
            TintCity(id);
        }

        void AddArmy(string objName, string id, string displayName, string ownerSeat)
        {
            var go = FindOrCreateMarker(objName, id == "army-wei-1" ? 2 : 9, id == "army-wei-1" ? 3 : 5);
            if (go == null) throw new System.Exception("scene object missing: " + objName);
            if (!go.activeInHierarchy)
            {
                Debug.Log("[KingdomDemo] skipping disabled army: " + objName);
                return;
            }
            var node = TBTK.GridManager.GetNode(go.transform.position, null);
            if (node == null) throw new System.Exception(objName + " is not on a grid node");
            go.transform.position = node.GetPos();

            engine.State.Armies.Add(new ArmyState
            {
                Id = id,
                Name = displayName,
                OwnerSeatId = ownerSeat,
                Troops = BalanceConfig.StartFieldArmyTroops,
                X = node.idxX,
                Z = node.idxZ,
            });
            armyObjs[id] = go;
        }

        GameObject FindOrCreateMarker(string objName, int x, int z)
        {
            var go = FindSceneObject(objName);
            if (go != null) return go;

            var node = TBTK.GridManager.GetNode(x, z);
            if (node == null)
                for (int ix = 0; ix < TBTK.GridManager.DimensionX() && node == null; ix++)
                    for (int iz = 0; iz < TBTK.GridManager.DimensionZ() && node == null; iz++)
                        node = TBTK.GridManager.GetNode(ix, iz);
            if (node == null) return null;

            go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = objName;
            go.transform.position = node.GetPos();
            go.transform.localScale = new Vector3(0.45f, 0.2f, 0.45f);
            return go;
        }

        GameObject FindSceneObject(string objName)
        {
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go.name == objName) return go;
            return null;
        }

        void TintCity(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            var sr = cityObjs[cityId].GetComponentInChildren<SpriteRenderer>();
            if (sr == null) return;
            sr.color = city.OwnerSeatId != null && factionColors.TryGetValue(city.OwnerSeatId, out var c)
                ? c : new Color(0.85f, 0.82f, 0.75f);
        }

        // ------------------------------------------------------------------ input
        // All clicks are handled in OnGUI via Event.current, which works regardless of
        // the project's Active Input Handling setting (TBTK's legacy-input Update loops
        // are broken until the editor restarts; IMGUI is not).

        void HandleClicks()
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            // a concept tab is modal over the map, exactly like the 朝報 report
            if (!ready || reportShowing || activeTab != "map") return;
            if (PointerOverUI(e.mousePosition)) return;

            if (e.button == 1) { ClearArmyMoveTargets(); selCity = null; selArmy = null; e.Use(); return; }
            if (e.button != 0) return;

            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var node = ResolveClickedNode(ray);
            if (node == null) return;

            var city = engine.State.CityAt(node.idxX, node.idxZ);
            var army = engine.State.ArmiesOf(HumanSeat).Find(a => a.X == node.idxX && a.Z == node.idxZ);

            if (army != null)
            {
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(TBTK.GridManager.GetNode(army.X, army.Z));
                ShowArmyMoveTargets(army);
                statusMsg = "已選擇 " + army.Name + " — 點選目的地";
                e.Use();
                return;
            }
            if (selArmy != null)
            {
                TryMarch(selArmy, node.idxX, node.idxZ);
                e.Use();
                return;
            }
            if (city != null && city.OwnerSeatId == HumanSeat)
            {
                ClearArmyMoveTargets();
                selCity = city; selArmy = null; e.Use(); return;
            }
            if (city != null)
            {
                ClearArmyMoveTargets();
                selCity = city; selArmy = null;
                statusMsg = city.IsNeutral ? "中立城市 — 派軍進駐以佔領" : "敵方城市";
                e.Use();
                return;
            }

            e.Use();
        }

        TBTK.Node ResolveClickedNode(Ray ray)
        {
            int nodeLayerMask = 1 << TBTK.TBTK.GetLayerNode();
            if (Physics.Raycast(ray, out var nodeHit, 500f, nodeLayerMask))
                return TBTK.GridManager.GetNode(nodeHit.point, nodeHit.collider.gameObject);

            if (Physics.Raycast(ray, out var hit, 500f))
                return TBTK.GridManager.GetNode(hit.point, null);

            return null;
        }

        void ShowArmyMoveTargets(ArmyState army)
        {
            var targets = new List<TBTK.Node>();
            if (!army.MarchedThisSeason)
                for (int x = 0; x < engine.State.Map.Width; x++)
                    for (int z = 0; z < engine.State.Map.Height; z++)
                    {
                        if (x == army.X && z == army.Z) continue;
                        if (!engine.State.Map.IsWalkable(x, z)) continue;
                        if (engine.State.ArmyAt(x, z) != null) continue;

                        var city = engine.State.CityAt(x, z);
                        if (city != null && !city.IsNeutral && city.OwnerSeatId != HumanSeat) continue;
                        if (engine.State.Map.PathCost(army.X, army.Z, x, z,
                                BalanceConfig.ArmyMoveCostPerSeason) < 0) continue;

                        var node = TBTK.GridManager.GetNode(x, z);
                        if (node != null) targets.Add(node);
                    }

            TBTK.GridIndicator.ShowMovable(targets);
        }

        void ClearArmyMoveTargets()
        {
            TBTK.GridIndicator.HideAll();
        }

        bool PointerOverUI(Vector2 m)
        {
            float s = Screen.width / 1600f;
            foreach (var r in uiRects)
                if (new Rect(r.x * s, r.y * s, r.width * s, r.height * s).Contains(m)) return true;
            return false;
        }

        // ------------------------------------------------------------------ orders

        void SubmitHuman(ActionCommand cmd)
        {
            cmd.ActorSeatId = HumanSeat;
            cmd.ControllerType = ControllerType.Human;
            var r = engine.Submit(cmd);
            PushJson(cmd, r);
            if (r.Ok && cmd.Type == ActionType.March)
            {
                ClearArmyMoveTargets();
                SyncArmyVisual(cmd.TargetId);
            }
            if (r.Ok && IsBattle(cmd.Type))
            {
                ClearArmyMoveTargets();
                SyncBattleVisuals();
            }

            if (!r.Ok)
                statusMsg = r.Error == "no command points left"
                    ? "行動點不足：本季已沒有可用行動點"
                    : "命令被拒：" + r.Error;
            else if (IsBattle(cmd.Type))
                statusMsg = LastBattleSummary() ?? "命令已執行";
            else
                statusMsg = "命令已執行";
        }

        static bool IsBattle(string type)
            => type == ActionType.AttackArmy || type == ActionType.AttackCity;

        void TryMarch(ArmyState army, int x, int z)
            => SubmitHuman(new ActionCommand { Type = ActionType.March, TargetId = army.Id, ParamA = x, ParamB = z });

        void SyncArmyVisual(string armyId)
        {
            if (!armyObjs.TryGetValue(armyId, out var go) || go == null) return;
            var army = engine.State.FindArmy(armyId);
            if (army == null) return;                                  // destroyed in battle
            var node = TBTK.GridManager.GetNode(army.X, army.Z);
            if (node == null) return;

            go.transform.position = node.GetPos();
            foreach (var city in engine.State.Cities) TintCity(city.Id);   // capture may have changed owners
        }

        /// <summary>After a battle: hide banners of destroyed armies, move the survivors, recolour
        /// captured cities, drop a selection that no longer exists.</summary>
        void SyncBattleVisuals()
        {
            foreach (var kv in armyObjs)
                if (kv.Value != null && kv.Value.activeSelf && engine.State.FindArmy(kv.Key) == null)
                    kv.Value.SetActive(false);
            foreach (var army in engine.State.Armies) SyncArmyVisual(army.Id);
            if (selArmy != null && engine.State.FindArmy(selArmy.Id) == null) selArmy = null;
            if (selCity != null && engine.State.FindCity(selCity.Id) == null) selCity = null;
        }

        // ---- 戰報: read the last battle back out of the log (design §6.4) ------

        string LastBattleSummary()
        {
            var records = engine.Log.Records;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].EventType == "battle_field") return FieldSummary(records[i]);
                if (records[i].EventType == "battle_siege") return SiegeSummary(records[i]);
            }
            return null;
        }

        string FieldSummary(EventRecord rec)
            => $"野戰 {rec.Payload["rounds"]}輪：{SeatShort(rec.Payload["attacker_seat_id"])}" +
               $"{rec.Payload["attacker_troops_before"]}→{rec.Payload["attacker_troops_after"]} 對 " +
               $"{SeatShort(rec.Payload["defender_seat_id"])}" +
               $"{rec.Payload["defender_troops_before"]}→{rec.Payload["defender_troops_after"]}　{OutcomeLabel(rec.Payload["outcome"])}";

        string SiegeSummary(EventRecord rec)
            => $"攻城 {CityName(rec.Payload["city_id"])} {rec.Payload["rounds"]}輪：" +
               $"攻方{rec.Payload["troops_before"]}→{rec.Payload["troops_after"]}、守軍{rec.Payload["garrison_before"]}→{rec.Payload["garrison_after"]}　{OutcomeLabel(rec.Payload["outcome"])}";

        string CityName(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            return city != null ? city.Name : cityId;
        }

        static string OutcomeLabel(string outcome)
        {
            switch (outcome)
            {
                case "defender_destroyed": return "敵軍覆沒";
                case "attacker_destroyed": return "我軍覆沒";
                case "mutual_destruction": return "兩敗俱傷";
                case "defender_routed":    return "敵軍潰散";
                case "attacker_routed":    return "我軍潰散";
                case "mutual_rout":        return "兩軍潰散";
                case "defender_holds":     return "守方留場";
                case "captured":           return "城破佔領";
                case "city_held":          return "守城成功";
                case "assault_failed":     return "攻方覆沒";
                default:                   return outcome;
            }
        }

        void PushJson(ActionCommand cmd, ValidationResult r)
        {
            jsonFeed.Add((r.Ok ? "✓ " : "✗ ") + cmd.ToJson());
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }

        void EndSeasonFlow()
        {
            selCity = null; selArmy = null;

            // AI seats play through the exact same pipeline.
            foreach (var fac in engine.State.Factions)
            {
                if (fac.Controller == ControllerType.Human) continue;
                foreach (var cmd in ScriptedController.PlanTurn(engine.State, fac.SeatId))
                {
                    var r = engine.Submit(cmd);
                    PushJson(cmd, r);
                    if (r.Ok && cmd.Type == ActionType.March) SyncArmyVisual(cmd.TargetId);
                    if (r.Ok && IsBattle(cmd.Type)) SyncBattleVisuals();
                }
            }

            var report = engine.EndSeason();

            reportLines.Clear();
            reportLines.Add($"—— 第{engine.State.Season}季 · 朝報 ——");
            reportLines.Add("");
            foreach (var rec in engine.Log.Records)
                if (rec.Season == engine.State.Season && rec.EventType == "city_captured")
                {
                    var city = engine.State.FindCity(rec.Payload["city_id"]);
                    var fac = engine.State.FindFaction(city.OwnerSeatId);
                    reportLines.Add($"★ {fac.Name}軍進駐{city.Name}，開拓版圖！");
                }
            foreach (var rec in engine.Log.Records)
                if (rec.Season == engine.State.Season && rec.EventType == "battle_field")
                    reportLines.Add("戰報：" + FieldSummary(rec));
                else if (rec.Season == engine.State.Season && rec.EventType == "battle_siege")
                    reportLines.Add("戰報：" + SiegeSummary(rec));
            // 外交 news of the season (the same events the 外交 tab works from).
            foreach (var rec in engine.Log.Records)
            {
                if (rec.Season != engine.State.Season) continue;
                if (rec.EventType == "war_declared")
                    reportLines.Add($"外交：{SeatShort(rec.Payload["attacker_seat_id"])}向" +
                                    $"{SeatShort(rec.Payload["defender_seat_id"])}宣戰");
                else if (rec.EventType == "treaty_broken")
                    reportLines.Add($"外交：{SeatShort(rec.Payload["breaker_seat_id"])}毀棄與" +
                                    $"{SeatShort(rec.Payload["counterpart_seat_id"])}的{TreatyLabel(rec.Payload["treaty_type"])}條約");
                else if (rec.EventType == "message_sent")
                    reportLines.Add($"外交：{SeatShort(rec.ActorSeatId)}來使 — {rec.Payload["text"]}");
            }
            foreach (var line in report.Lines) reportLines.Add(line);
            reportLines.Add("");
            reportLines.Add($"史官記：本季共錄得 {CountSeasonEvents()} 事。");

            File.WriteAllText(Path.Combine(Application.persistentDataPath, "luanshi_log.jsonl"),
                              engine.Log.ToJsonLines());
            reportShowing = true;
        }

        int CountSeasonEvents()
        {
            int n = 0;
            foreach (var rec in engine.Log.Records) if (rec.Season == engine.State.Season) n++;
            return n;
        }

        void CloseReport()
        {
            reportShowing = false;
            engine.BeginSeason();
            statusMsg = $"第{engine.State.Season}季開始";
        }

        // ------------------------------------------------------------------ UI

        void OnGUI()
        {
            uiRects.Clear();
            float s = Screen.width / 1600f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            var body = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            var mono = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Normal };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16 };

            if (!ready)
            {
                GUI.Label(new Rect(20, 20, 900, 40), "KingdomDemo init failed: " + initError, title);
                return;
            }

            var fac = engine.State.FindFaction(HumanSeat);
            int food = 0, gold = 0, pop = 0;
            foreach (var c in engine.State.CitiesOf(HumanSeat)) { food += c.Food; gold += c.Gold; pop += c.Population; }

            // top bar
            var top = new Rect(10, 8, 1000, 34);
            uiRects.Add(top);
            GUI.Box(top, GUIContent.none);
            GUI.Label(new Rect(22, 12, 980, 28),
                $"第 {engine.State.Season} 季 · {fac.Name}（玩家） · 令 {fac.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
                $" · 威望 {fac.Prestige} · 人口 {pop} · 糧 {food} · 金 {gold}", title);

            // tab bar — 地圖 is the live view; the others preview the full design concept
            var tabBtn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            for (int i = 0; i < Tabs.GetLength(0); i++)
            {
                var r = new Rect(1020 + i * 66, 8, 62, 34);
                uiRects.Add(r);
                var prevBg = GUI.backgroundColor;
                if (activeTab == Tabs[i, 0]) GUI.backgroundColor = new Color(0.60f, 0.78f, 1f);
                if (GUI.Button(r, Tabs[i, 1], tabBtn)) ShowTab(Tabs[i, 0]);
                GUI.backgroundColor = prevBg;
            }

            // selection panel (map view only)
            if (activeTab == "map")
            {
                if (selCity != null) DrawCityPanel(selCity, body, btn);
                else if (selArmy != null) DrawArmyPanel(selArmy, body, btn);
            }

            // end season
            var end = new Rect(1420, 8, 170, 44);
            uiRects.Add(end);
            if (GUI.Button(end, "結束季節", btn)) EndSeasonFlow();

            // status + JSON command feed (the FYP artifact: UI clicks == LLM JSON)
            var feed = new Rect(10, 800, 1100, 92);
            uiRects.Add(feed);
            GUI.Box(feed, GUIContent.none);
            GUI.Label(new Rect(20, 804, 1080, 22), statusMsg, body);
            for (int i = 0; i < jsonFeed.Count; i++)
                GUI.Label(new Rect(20, 826 + i * 16, 1080, 16), jsonFeed[i], mono);

            if (activeTab != "map" && !reportShowing) DrawTabPanel(body, btn, title, mono);
            if (reportShowing) DrawReport(body, btn, title);

            HandleClicks();
        }

        void DrawCityPanel(CityState city, GUIStyle body, GUIStyle btn)
        {
            bool mine = city.OwnerSeatId == HumanSeat;
            var p = new Rect(10, 50, 300, mine ? 400 : 120);
            uiRects.Add(p);
            GUI.Box(p, GUIContent.none);
            GUI.Label(new Rect(22, 58, 280, 24), $"{city.Name}（{OwnerName(city)}）", body);

            if (mine)
            {
                string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? "（產出減半）" : "";
                GUI.Label(new Rect(22, 86, 280, 116),
                    $"人口 {city.Population}\n糧食 {city.Food}\n金錢 {city.Gold}\n駐軍 {city.Garrison}\n" +
                    $"民心 {city.Morale}{moraleWarning}\n" +
                    $"開墾 {city.ReclaimPct}%　城防 {city.Defense}/{BalanceConfig.DefenseMax}　訓練 {city.Training}/{BalanceConfig.TrainingMax}",
                    body);

                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(22, 210, 120, 34), ActionType.LevyTax, city.Id, "徵稅", null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(152, 210, 120, 34), ActionType.LightenLabor, city.Id, "輕徭",
                    city.Gold >= BalanceConfig.LightenLaborGoldCost ? null : "金不足", 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(22, 248, 120, 34), ActionType.Reclaim, city.Id, "開墾",
                    city.ReclaimPct >= BalanceConfig.ReclaimPctCap ? "已達上限"
                    : city.ReclaimedThisSeason ? "本季已開墾" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(152, 248, 120, 34), ActionType.Recruit, city.Id, "募兵",
                    city.Gold < BalanceConfig.RecruitGoldCost ? "金不足"
                    : city.Population < BalanceConfig.RecruitPopCost ? "人口不足" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(22, 286, 120, 34), ActionType.Train, city.Id, "練兵",
                    city.Training >= BalanceConfig.TrainingMax ? "已達上限" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(152, 286, 120, 34), ActionType.Fortify, city.Id, "修城",
                    city.Defense >= BalanceConfig.DefenseMax ? "已達上限" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(22, 324, 120, 34), ActionType.Relief, city.Id, "賑災",
                    city.Food < BalanceConfig.ReliefFoodCost ? "糧不足" : null, 0, 0, btn));

                if (hint != null) GUI.Label(new Rect(22, 364, 280, 22), hint, body);
            }
            else
            {
                GUI.Label(new Rect(22, 84, 280, 30), city.IsNeutral ? "無主之地" : "敵境 — 詳情不明", body);
            }
        }

        /// <summary>
        /// One order button. It always submits a real ActionCommand through SubmitHuman and shows the
        /// action's 號令 cost from BalanceConfig; the engine stays the only authority, so a stale hint
        /// just means the click comes back rejected with a reason.
        /// </summary>
        string OrderButton(Rect r, string type, string targetId, string label, string blockedReason,
            int paramA, int paramB, GUIStyle btn,
            Dictionary<string, string> args = null, string text = null)
        {
            bool available = blockedReason == null;
            var prev = GUI.enabled;
            GUI.enabled = available;
            if (GUI.Button(r, $"{label} {BalanceConfig.CommandPointCost(type)}令", btn))
                SubmitHuman(new ActionCommand
                {
                    Type = type, TargetId = targetId, ParamA = paramA, ParamB = paramB,
                    Args = args, Text = text, Reason = "UI: " + type,
                });
            GUI.enabled = prev;
            return available ? null : label + "：" + blockedReason;
        }

        static string FirstReason(string current, string candidate) => current ?? candidate;

        void DrawArmyPanel(ArmyState army, GUIStyle body, GUIStyle btn)
        {
            var enemyArmies = AdjacentEnemyArmies(army);
            var enemyCities = AdjacentEnemyCities(army);
            int orders = enemyArmies.Count + enemyCities.Count;

            var p = new Rect(10, 50, 300, orders > 0 ? 236 + orders * 38 : 150);
            uiRects.Add(p);
            GUI.Box(p, GUIContent.none);
            GUI.Label(new Rect(22, 58, 280, 24), $"{army.Name} · 兵力 {army.Troops}", body);
            GUI.Label(new Rect(22, 86, 280, 66),
                $"位置 ({army.X}, {army.Z})\n士氣 {army.Morale}\n" +
                (army.MarchedThisSeason ? "本季已行軍" : "點選六邊格以行軍"), body);

            if (orders == 0)
            {
                GUI.Label(new Rect(22, 156, 280, 22), "四周無敵軍或敵城", body);
                return;
            }

            string hint = null;
            int row = 0;
            foreach (var enemy in enemyArmies)
            {
                hint = FirstReason(hint, OrderButton(new Rect(22, 160 + row * 38, 280, 34), ActionType.AttackArmy,
                    army.Id, "攻擊 " + enemy.Name,
                    army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "士氣潰散，無法出戰" : null,
                    enemy.X, enemy.Z, btn));
                row++;
            }
            foreach (var city in enemyCities)
            {
                string blocked = army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "士氣潰散，無法出戰"
                    : army.Troops < BalanceConfig.AssaultTroopRatio * city.Garrison
                        ? $"需 {BalanceConfig.AssaultTroopRatio}:1 兵力（守軍 {city.Garrison}）" : null;
                hint = FirstReason(hint, OrderButton(new Rect(22, 160 + row * 38, 280, 34), ActionType.AttackCity,
                    army.Id, "攻城 " + city.Name, blocked, city.X, city.Z, btn));
                row++;
            }

            if (hint != null) GUI.Label(new Rect(22, 166 + row * 38, 280, 22), hint, body);
        }

        List<ArmyState> AdjacentEnemyArmies(ArmyState army)
        {
            var list = new List<ArmyState>();
            foreach (var other in engine.State.Armies)
                if (other.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.AreAdjacent(army.X, army.Z, other.X, other.Z)) list.Add(other);
            return list;
        }

        List<CityState> AdjacentEnemyCities(ArmyState army)
        {
            var list = new List<CityState>();
            foreach (var city in engine.State.Cities)
                if (!city.IsNeutral && city.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.AreAdjacent(army.X, army.Z, city.X, city.Z)) list.Add(city);
            return list;
        }

        void DrawReport(GUIStyle body, GUIStyle btn, GUIStyle title)
        {
            var p = new Rect(400, 150, 800, 420);
            uiRects.Add(p);
            GUI.Box(p, GUIContent.none);
            GUI.Label(new Rect(p.x + 20, p.y + 12, 760, 30), reportLines.Count > 0 ? reportLines[0] : "", title);
            var sb = new StringBuilder();
            for (int i = 1; i < reportLines.Count; i++) sb.AppendLine(reportLines[i]);
            GUI.Label(new Rect(p.x + 20, p.y + 50, 760, 300), sb.ToString(), body);
            if (GUI.Button(new Rect(p.x + 330, p.y + 360, 140, 40), "繼續", btn)) CloseReport();
        }

        // ------------------------------------------------------------ concept tabs
        // 內政 / 外交 / 諜報 / 研究 are display-only previews of the full design;
        // 史官 renders the real in-memory event log.

        /// <summary>Opens a concept panel by tab key. Same entry point the tab bar uses,
        /// so scripted demos and screenshot passes can drive the UI without a mouse.</summary>
        public void ShowTab(string key)
        {
            activeTab = key;
        }

        void DrawTabPanel(GUIStyle body, GUIStyle btn, GUIStyle title, GUIStyle mono)
        {
            var p = new Rect(400, 190, 800, 480);
            uiRects.Add(p);
            GUI.Box(p, GUIContent.none);
            GUI.Label(new Rect(p.x + 20, p.y + 12, 760, 30), TabTitle(), title);

            switch (activeTab)
            {
                case "domestic":  DrawDomesticPanel(p, body); break;
                case "diplomacy": DrawDiplomacyPanel(p, body); break;
                case "intel":     DrawIntelPanel(p, body); break;
                case "research":  DrawResearchPanel(p, body); break;
                default:          DrawHistorianPanel(p, body, mono); break;
            }

            if (GUI.Button(new Rect(p.x + 320, p.y + 424, 160, 40), "關閉", btn)) activeTab = "map";
        }

        string TabTitle()
        {
            switch (activeTab)
            {
                case "domestic":  return "內政 · 行動一覽";
                case "diplomacy": return "外交 · 行動一覽";
                case "intel":     return "諜報 · 待開發";
                case "research":  return "研究 · 待開發";
                default:          return "史官 · 本局史書";
            }
        }

        void DrawDomesticPanel(Rect p, GUIStyle body)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22), "內政行動（設計文件第 11 章 · 共 10 項）", body);
            y += 30;

            // Live actions mirror ACTIONS.md §3 and read their numbers from BalanceConfig,
            // so this panel cannot drift from what the engine actually charges.
            string[] live =
            {
                $"徵稅　　耗令 {BalanceConfig.CommandPointsPerOrder}　金 ＋{BalanceConfig.TaxLevyAmount}、民心 −{BalanceConfig.TaxLevyMoraleCost}",
                $"輕徭　　耗令 {BalanceConfig.CommandPointsPerOrder}　金 −{BalanceConfig.LightenLaborGoldCost}、民心 ＋{BalanceConfig.LightenLaborMoraleGain}",
                $"開墾　　耗令 {BalanceConfig.CommandPointsPerOrder}　該城糧產 ＋{BalanceConfig.ReclaimPctPerUse}%（上限 ＋{BalanceConfig.ReclaimPctCap}%）",
                $"募兵　　耗令 {BalanceConfig.CommandPointsPerOrder}　金 −{BalanceConfig.RecruitGoldCost}、人口 −{BalanceConfig.RecruitPopCost}、駐軍 ＋{BalanceConfig.RecruitTroopGain}",
                $"練兵　　耗令 {BalanceConfig.CommandPointsPerOrder}　訓練 ＋1（上限 {BalanceConfig.TrainingMax}）",
                $"修城　　耗令 {BalanceConfig.CommandPointsPerOrder}　城防 ＋1（上限 {BalanceConfig.DefenseMax}）",
                $"賑災　　耗令 {BalanceConfig.CommandPointsPerOrder}　糧 −{BalanceConfig.ReliefFoodCost}、民心 ＋{BalanceConfig.ReliefMoraleGain}",
            };
            foreach (var row in live)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 22), "已開放　" + row, body);
                y += 24;
            }

            y += 10;
            string[] planned =
            {
                "屯田　　耗令 1　需農政 3：駐軍自給 30%",
                "興建建築　耗令 1　金足夠、有空位（市集／農田／兵營…）",
                "遷都　　耗令 1　需另一座城：威望 −5、換都城",
            };
            foreach (var row in planned)
            {
                PlannedLabel(new Rect(p.x + 36, y, 744, 22), "〔待開發〕 " + row, body);
                y += 24;
            }

            y += 10;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                $"點選我方城池後，於左側城池面板下達命令；條件不足的按鈕呈灰色。\n" +
                $"點選我方部隊，可對相鄰敵軍發動野戰（1令）、對敵城強攻（{BalanceConfig.CommandPointsPerAttackCity}令，需 {BalanceConfig.AssaultTroopRatio}:1 兵力）。", body);
        }

        void DrawDiplomacyPanel(Rect p, GUIStyle body)
        {
            var dip = engine.State.Diplomacy;
            var me = engine.State.FindFaction(HumanSeat);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            const int shownFactions = 3;      // placeholder layout: the rest are summarised

            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"外交　號令 {me.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}　威望 {me.Prestige}" +
                $"　條約 {dip.Treaties.Count}　待決提案 {dip.Proposals.Count}", body);
            y += 26;

            // 遣使: type here, then press 遣使 on a faction row to submit it.
            GUI.Label(new Rect(p.x + 36, y, 120, 22), "遣使文書", body);
            envoyText = GUI.TextField(new Rect(p.x + 160, y, 604, 24), envoyText, 120);
            y += 34;

            // Incoming offers first: they are the only diplomacy that demands an answer.
            var incoming = new List<TreatyProposal>();
            foreach (var proposal in dip.Proposals)
                if (proposal.ToSeatId == HumanSeat) incoming.Add(proposal);

            GUI.Label(new Rect(p.x + 20, y, 760, 22), $"待決提案（{incoming.Count}）", body);
            y += 24;

            if (incoming.Count == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), "目前無人來使提案。", body);
                y += 24;
            }
            foreach (var proposal in incoming)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20),
                    $"{SeatShort(proposal.FromSeatId)} 提議 {TreatyLabel(proposal.Type)}　{proposal.DurationSeasons} 季" +
                    (string.IsNullOrEmpty(proposal.Text) ? "" : $"　附言：{proposal.Text}"), body);
                y += 22;
                OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "接受",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "accept" } });
                OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "拒絕",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "reject" } });
                y += 36;
            }

            // Relations and the actions available against each other seat.
            int drawn = 0;
            foreach (var other in engine.State.Factions)
            {
                if (other.SeatId == HumanSeat) continue;
                if (drawn == shownFactions)
                {
                    GUI.Label(new Rect(p.x + 36, y, 744, 20), "…（其餘勢力省略）", body);
                    break;
                }

                GUI.Label(new Rect(p.x + 20, y, 760, 20),
                    $"{other.Name}　信任 我→他 {dip.GetTrust(HumanSeat, other.SeatId)}" +
                    $"／他→我 {dip.GetTrust(other.SeatId, HumanSeat)}　{StatusLine(other.SeatId)}", body);
                y += 22;

                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.DeclareWar,
                    other.SeatId, "宣戰",
                    dip.IsAtWar(HumanSeat, other.SeatId) ? "已在交戰" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.Gift,
                    other.SeatId, $"贈禮 {BalanceConfig.GiftGoldPerTrustUnit}",
                    Treasury() < BalanceConfig.GiftGoldPerTrustUnit ? "金不足" : null,
                    BalanceConfig.GiftGoldPerTrustUnit, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 400, y, 176, 30), ActionType.SendMessage,
                    other.SeatId, "遣使", string.IsNullOrEmpty(envoyText) ? "請先輸入文書" : null,
                    0, 0, btn, null, envoyText));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 582, y, 182, 30), ActionType.BreakTreaty,
                    other.SeatId, "毀約", BreakBlocked(other.SeatId), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", BreakableType(other.SeatId) } }));
                y += 34;

                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "互不侵犯", ProposalBlocked(other.SeatId, TreatyType.Nap), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 282, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "同盟", ProposalBlocked(other.SeatId, TreatyType.Alliance), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Alliance } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 528, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "停戰提案", ProposalBlocked(other.SeatId, TreatyType.Truce), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Truce } }));
                y += 34;

                if (hint != null)
                {
                    GUI.Label(new Rect(p.x + 20, y, 760, 20), hint, body);
                    y += 22;
                }
                y += 4;
                drawn++;
            }
        }

        /// <summary>和平 / 交戰 plus every treaty in force (with seasons left) and any pending offer.</summary>
        string StatusLine(string seatId)
        {
            var dip = engine.State.Diplomacy;
            var sb = new StringBuilder(dip.IsAtWar(HumanSeat, seatId) ? "交戰" : "和平");
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
            {
                var treaty = dip.FindTreaty(type, HumanSeat, seatId);
                if (treaty == null) continue;
                int seasonsLeft = treaty.ExpirySeason - engine.State.Season + 1;
                if (seasonsLeft < 1) seasonsLeft = 1;
                sb.Append($"　[{TreatyLabel(type)} 剩 {seasonsLeft} 季]");
            }
            var proposal = dip.FindProposal(HumanSeat, seatId);
            if (proposal != null)
                sb.Append("　[").Append(proposal.FromSeatId == HumanSeat ? "我方提案待覆" : "待你回覆")
                  .Append("：").Append(TreatyLabel(proposal.Type)).Append(']');
            return sb.ToString();
        }

        static string TreatyLabel(string type)
        {
            switch (type)
            {
                case TreatyType.Nap: return "互不侵犯";
                case TreatyType.Alliance: return "同盟";
                case TreatyType.Truce: return "停戰";
                default: return type;
            }
        }

        /// <summary>Reason a treaty offer is obviously illegal, or null when it can be sent.</summary>
        string ProposalBlocked(string seatId, string type)
        {
            var dip = engine.State.Diplomacy;
            if (dip.HasTreaty(type, HumanSeat, seatId)) return "已在生效";
            if (dip.FindProposal(HumanSeat, seatId) != null) return "已有提案待覆";
            return null;
        }

        string BreakBlocked(string seatId)
            => BreakableType(seatId) == null ? "沒有條約可毀" : null;

        /// <summary>First treaty in force with that seat (nap → alliance → truce), or null.</summary>
        string BreakableType(string seatId)
        {
            var dip = engine.State.Diplomacy;
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
                if (dip.HasTreaty(type, HumanSeat, seatId)) return type;
            return null;
        }

        int Treasury()
        {
            int gold = 0;
            foreach (var city in engine.State.CitiesOf(HumanSeat)) gold += city.Gold;
            return gold;
        }

        void DrawIntelPanel(Rect p, GUIStyle body)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                "戰爭迷霧：己方城池周邊 3 格、己方軍隊周邊 2 格可見，其餘一片模糊。\n目前無間諜潛伏 — 敵軍位置與兵力皆為推測。", body);
            y += 52;

            GUI.Label(new Rect(p.x + 20, y, 760, 22), "間諜任務", body);
            y += 26;
            PlannedLabel(new Rect(p.x + 36, y, 744, 66),
                "〔待開發〕 偵察 200 金　　〔待開發〕 竊取軍報 400 金\n" +
                "〔待開發〕 散播謠言 500 金　〔待開發〕 偽造軍報 600 金\n" +
                "〔待開發〕 挑撥離間 800 金　〔待開發〕 收買將領 1000 金", body);
            y += 74;

            GUI.Label(new Rect(p.x + 20, y, 760, 22), "反諜", body);
            y += 26;
            PlannedLabel(new Rect(p.x + 36, y, 744, 22), "〔待開發〕 巡查　　〔待開發〕 清查", body);
            y += 30;

            GUI.Label(new Rect(p.x + 20, y, 760, 40),
                "暴露判定：基礎 5% ＋ 所在城治安 ＋ 目標國諜報等級；嫌疑 ≥ 80 必定暴露。", body);
        }

        void DrawResearchPanel(Rect p, GUIStyle body)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22), "四條國策線（各 5 級：20 / 40 / 70 / 110 / 160 研究點）", body);
            y += 30;
            PlannedLabel(new Rect(p.x + 36, y, 744, 96),
                "〔待開發〕 農政 1　開墾 — 糧 ＋10%\n" +
                "〔待開發〕 兵法 1　練兵 — 訓練 ＋1\n" +
                "〔待開發〕 諜報 1　細作 — 可派間諜\n" +
                "〔待開發〕 吏治 1　賦稅 — 金 ＋10%", body);
            y += 108;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                "開局約 15 研究點／季，一局之內通常只能專精兩條線。\n研究模組完成後可於此花費研究點逐級解鎖。", body);
        }

        void DrawHistorianPanel(Rect p, GUIStyle body, GUIStyle mono)
        {
            var records = engine.Log.Records;
            GUI.Label(new Rect(p.x + 20, p.y + 52, 760, 22),
                $"本局史書 — 共 {records.Count} 條（第 {engine.State.Season} 季 · {PhaseLabel(engine.State.Phase)}）", body);

            float lineH = 18f;
            var view = new Rect(p.x + 20, p.y + 80, 760, 300);
            var content = new Rect(0, 0, 744, Mathf.Max(view.height, records.Count * lineH + 8f));
            historianScroll = GUI.BeginScrollView(view, historianScroll, content, false, true);
            for (int i = 0; i < records.Count; i++)
                GUI.Label(new Rect(4, 4 + i * lineH, 736, lineH), HistorianLine(records[i]), mono);
            GUI.EndScrollView();

            GUI.Label(new Rect(p.x + 20, p.y + 388, 760, 22),
                $"史官記：本季共錄得 {CountSeasonEvents()} 事。", body);
        }

        string HistorianLine(EventRecord rec)
        {
            var sb = new StringBuilder();
            sb.Append(rec.Seq.ToString().PadLeft(2));
            sb.Append("　第").Append(rec.Season).Append("季　");
            sb.Append(rec.EventType);
            sb.Append("　").Append(SeatShort(rec.ActorSeatId));
            string payload = PayloadText(rec.Payload);
            if (payload.Length > 0) sb.Append("　").Append(payload);
            return sb.ToString();
        }

        string SeatShort(string seatId)
        {
            if (seatId == null) return "—";
            var f = engine.State.FindFaction(seatId);
            return f != null ? f.Name : seatId;
        }

        string PayloadText(Dictionary<string, string> payload)
        {
            if (payload == null || payload.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in payload)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        string PhaseLabel(string phase)
        {
            if (phase == "player_orders") return "下令";
            if (phase == "resolution") return "結算";
            return phase;
        }

        void PlannedLabel(Rect r, string text, GUIStyle style)
        {
            bool prev = GUI.enabled;
            GUI.enabled = false;      // grayed out: designed, not yet implemented
            GUI.Label(r, text, style);
            GUI.enabled = prev;
        }

        string OwnerName(CityState city)
        {
            if (city.IsNeutral) return "中立";
            var f = engine.State.FindFaction(city.OwnerSeatId);
            return f != null ? f.Name : "?";
        }
    }
}
