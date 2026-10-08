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

        // concept tabs: 地圖 is the live view, the rest preview the full design
        // (doc/LuanShi-Game-Design-v1.md). Only 史官 renders real data.
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
            statusMsg = r.Ok ? "命令已執行" : r.Error == "no command points left"
                ? "行動點不足：本季已沒有可用行動點"
                : "命令被拒：" + r.Error;
        }

        void TryMarch(ArmyState army, int x, int z)
            => SubmitHuman(new ActionCommand { Type = ActionType.March, TargetId = army.Id, ParamA = x, ParamB = z });

        void SyncArmyVisual(string armyId)
        {
            var army = engine.State.FindArmy(armyId);
            var node = TBTK.GridManager.GetNode(army.X, army.Z);
            var t = armyObjs[armyId].transform;
            t.position = node.GetPos();

            foreach (var city in engine.State.Cities) TintCity(city.Id);   // capture may have changed owners
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
                $"第 {engine.State.Season} 季 · {fac.Name}（玩家） · 令 {fac.CommandPoints}/{BalanceConfig.CommandPointsPerSeason} · 人口 {pop} · 糧 {food} · 金 {gold}", title);

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
            var p = new Rect(10, 50, 300, city.OwnerSeatId == HumanSeat ? 200 : 120);
            uiRects.Add(p);
            GUI.Box(p, GUIContent.none);
            GUI.Label(new Rect(22, 58, 280, 24), $"{city.Name}（{OwnerName(city)}）", body);

            if (city.OwnerSeatId == HumanSeat)
            {
                GUI.Label(new Rect(22, 84, 280, 80),
                    $"人口 {city.Population}\n糧食 {city.Food}\n金錢 {city.Gold}\n駐軍 {city.Garrison}", body);
                var fac = engine.State.FindFaction(HumanSeat);
                if (GUI.Button(new Rect(22, 172, 120, 34), "開墾", btn))
                    SubmitHuman(new ActionCommand { Type = ActionType.Reclaim, TargetId = city.Id, Reason = "UI: reclaim order" });
                if (GUI.Button(new Rect(152, 172, 120, 34), "徵兵", btn))
                    SubmitHuman(new ActionCommand { Type = ActionType.Recruit, TargetId = city.Id, Reason = "UI: recruit order" });
            }
            else
            {
                GUI.Label(new Rect(22, 84, 280, 30), city.IsNeutral ? "無主之地" : "敵境 — 詳情不明", body);
            }
        }

        void DrawArmyPanel(ArmyState army, GUIStyle body, GUIStyle btn)
        {
            var p = new Rect(10, 50, 300, 110);
            uiRects.Add(p);
            GUI.Box(p, GUIContent.none);
            GUI.Label(new Rect(22, 58, 280, 60),
                $"{army.Name} · 兵力 {army.Troops}\n位置 ({army.X}, {army.Z})" +
                (army.MarchedThisSeason ? "\n本季已行軍" : "\n點選六邊格以行軍"), body);
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
                case "domestic":  return "內政 · 待開發";
                case "diplomacy": return "外交 · 待開發";
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

            string[] rows =
            {
                "徵稅　　耗令 1　金 ＋、民心 −5",
                "輕徭　　耗令 1　民心 ＋8、金 −",
                "開墾　　耗令 1　該城糧產 ＋15%（上限 ＋60%）",
                "練兵　　耗令 1　訓練 ＋1（上限 5）",
                "修城　　耗令 1　城防 ＋1（上限 5）",
                "興建建築　耗令 1　金足夠、有空位",
                "賑災　　耗令 1　糧 −、民心 ＋15",
                "遷都　　耗令 1　威望 −5、換都城",
            };
            foreach (var row in rows)
            {
                PlannedLabel(new Rect(p.x + 36, y, 744, 22), "〔待開發〕 " + row, body);
                y += 24;
            }

            y += 10;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                "已開放：開墾、徵兵 — 點選我方城池後，於左側城池面板下達命令。\n其餘行動待內政模組完成後陸續開放。", body);
        }

        void DrawDiplomacyPanel(Rect p, GUIStyle body)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22), "條約　（本局尚無任何條約）", body);
            y += 26;
            PlannedLabel(new Rect(p.x + 36, y, 744, 40),
                "可締結：互不侵犯 · 同盟 · 共同防禦 · 停戰 · 朝貢\n　　　　　割地 · 通商 · 借道 · 聯姻 · 稱臣", body);
            y += 48;

            GUI.Label(new Rect(p.x + 20, y, 760, 22), "勢力關係", body);
            y += 26;
            PlannedLabel(new Rect(p.x + 36, y, 744, 44),
                "魏（玩家）　↔　蜀（腳本 AI）　　信任 —　　關係【未開放】\n威望 —　（待外交模組）", body);
            y += 54;

            GUI.Label(new Rect(p.x + 20, y, 760, 22), "外交行動", body);
            y += 26;
            PlannedLabel(new Rect(p.x + 36, y, 744, 44),
                "〔待開發〕 遣使 · 提出條約 · 贈禮 · 索貢 · 通商\n〔待開發〕 斷交 · 宣戰 · 招降", body);
            y += 54;

            GUI.Label(new Rect(p.x + 20, y, 760, 40),
                "設計要點：說話不改狀態，只有簽了字的條款才算數；對方簽不簽，取決於信任值。", body);
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
