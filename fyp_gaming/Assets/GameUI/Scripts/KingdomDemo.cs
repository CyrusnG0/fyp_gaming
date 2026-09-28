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

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            // GameControl.Awake still runs on a disabled component (grid init), but its
            // Start coroutine never fires — TBTK's tactical battle flow stays off.
            var gc = FindAnyObjectByType<TBTK.GameControl>();
            if (gc != null) gc.enabled = false;
        }

        void Start()
        {
            // TBTK's tactical-layer UI (HUD, perk menu, ability bars) is not part of the
            // kingdom demo; hide it so only the strategic IMGUI shows.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
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
                var go = GameObject.Find(name);
                if (go != null) go.SetActive(false);
            }

            engine.BeginSeason();
            statusMsg = "第一季開始 — 點選城市或部隊下達命令";
            Debug.Log("[KingdomDemo] game started, season 1");
        }

        void AddCity(string objName, string id, string displayName, string ownerSeat)
        {
            var go = GameObject.Find(objName);
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
            var go = GameObject.Find(objName);
            if (go == null) throw new System.Exception("scene object missing: " + objName);
            var node = TBTK.GridManager.GetNode(go.transform.position, null);
            if (node == null) throw new System.Exception(objName + " is not on a grid node");

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
            if (!ready || reportShowing) return;
            if (PointerOverUI(e.mousePosition)) return;

            if (e.button == 1) { selCity = null; selArmy = null; e.Use(); return; }
            if (e.button != 0) return;

            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(new Vector3(e.mousePosition.x, Screen.height - e.mousePosition.y, 0f));
            if (!Physics.Raycast(ray, out var hit, 500f)) return;
            var node = TBTK.GridManager.GetNode(hit.point, null);
            if (node == null) return;

            var city = engine.State.CityAt(node.idxX, node.idxZ);
            var army = engine.State.ArmiesOf(HumanSeat).Find(a => a.X == node.idxX && a.Z == node.idxZ);

            if (army != null) { selArmy = army; selCity = null; statusMsg = "已選擇 " + army.Name + " — 點選目的地"; return; }
            if (city != null && city.OwnerSeatId == HumanSeat) { selCity = city; selArmy = null; return; }
            if (city != null) { selCity = city; selArmy = null; statusMsg = city.IsNeutral ? "中立城市 — 派軍進駐以佔領" : "敵方城市"; return; }

            if (selArmy != null) TryMarch(selArmy, node.idxX, node.idxZ);
            e.Use();
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
            if (r.Ok && cmd.Type == ActionType.March) SyncArmyVisual(cmd.TargetId);
            statusMsg = r.Ok ? "命令已執行" : "命令被拒：" + r.Error;
        }

        void TryMarch(ArmyState army, int x, int z)
            => SubmitHuman(new ActionCommand { Type = ActionType.March, TargetId = army.Id, ParamA = x, ParamB = z });

        void SyncArmyVisual(string armyId)
        {
            var army = engine.State.FindArmy(armyId);
            var node = TBTK.GridManager.GetNode(army.X, army.Z);
            var t = armyObjs[armyId].transform;
            t.position = node.GetPos() + Vector3.up * 0.1f;

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

            // selection panel
            if (selCity != null) DrawCityPanel(selCity, body, btn);
            else if (selArmy != null) DrawArmyPanel(selArmy, body, btn);

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
                GUI.enabled = fac.CommandPoints > 0;
                if (GUI.Button(new Rect(22, 172, 120, 34), "屯田", btn))
                    SubmitHuman(new ActionCommand { Type = ActionType.Farm, TargetId = city.Id, Reason = "UI: farm order" });
                if (GUI.Button(new Rect(152, 172, 120, 34), "徵兵", btn))
                    SubmitHuman(new ActionCommand { Type = ActionType.Recruit, TargetId = city.Id, Reason = "UI: recruit order" });
                GUI.enabled = true;
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

        string OwnerName(CityState city)
        {
            if (city.IsNeutral) return "中立";
            var f = engine.State.FindFaction(city.OwnerSeatId);
            return f != null ? f.Name : "?";
        }
    }
}
