using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
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
        [FormerlySerializedAs("unusedArmyObjs")]   // renamed after the smoke test: they are used
        public string[] extraArmyObjs = { "Wei_Army2", "Wei_Army3", "Wei_Army4",
                                          "Shu_Army2", "Shu_Army3", "Shu_Army4" };

        [Header("Game setup")]
        public int randomSeed = 12345;

        [Header("Seats (data-driven; the engine supports up to 8, AGENTS.md #6)")]
        [Tooltip("Seat ids played by people at this keyboard. Every other seat is scripted. " +
                 "Set two ids here for hotseat pass-and-play.")]
        public string[] humanSeats = { "seat-0" };

        private GameEngine engine;
        private bool ready;
        private string initError;

        private const string SeatA = "seat-0";     // 魏 — scene roster, not "the human"
        private const string SeatB = "seat-1";     // 蜀

        // panel & marker art: the demo skin's GUI.Box is see-through, so panels get their own
        // opaque plate first (smoke-test finding); markers get a runtime tinted material.
        private static readonly Color PanelColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        private static readonly Color NeutralColor = new Color(0.85f, 0.82f, 0.75f);
        private static readonly Vector3 FallbackMarkerScale = new Vector3(0.7f, 0.28f, 0.7f);
        private const float ReportLineHeight = 20f;
        private const float ReportTextMaxHeight = 300f;
        private const float HistorianLineHeight = 18f;
        private const float HistorianViewMaxHeight = 300f;

        // hotseat state (CONTROLLER_PROTOCOL §2: each seat takes one ordered turn per season)
        private string currentSeat;                // whose turn the UI is showing right now
        private bool handoverShowing;              // full-screen blocker between human seats
        private bool pendingReport;                // 朝報 queued behind the handover screen
        private Observation obs;                   // the current seat's filtered world (AGENTS.md #3)

        private readonly Dictionary<string, GameObject> cityObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> armyObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Color> factionColors = new Dictionary<string, Color>();
        private readonly Dictionary<Color, Material> markerMaterials = new Dictionary<Color, Material>();

        // selection & UI state
        private CityState selCity;
        private ArmyState selArmy;
        private string statusMsg = "";
        private readonly List<string> jsonFeed = new List<string>();   // recent ActionCommand JSON
        private bool reportShowing;
        private readonly List<string> reportLines = new List<string>();
        private readonly List<Rect> uiRects = new List<Rect>();
        private string envoyText = "";        // 遣使 draft for the 外交 tab (never sent automatically)
        private Vector2 reportScroll;         // 朝報 scroll offset when a season outruns its panel

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

        /// <summary>
        /// Frame 1, synchronous — deliberately not a coroutine. The demo used to disable TBTK and
        /// initialise one frame later (<c>yield return null</c>), but an unfocused editor window never
        /// delivers that second frame, so the demo silently never initialised (smoke-test finding #3).
        ///
        /// Ordering replaces the delay, guaranteed by [DefaultExecutionOrder(-100)] on this class:
        /// <list type="number">
        /// <item>every Awake has already run, so TBTK's grid exists — GameControl.Awake calls
        /// GridManager.Init (Assets/TBTK/Scripts/GameControl.cs:67);</item>
        /// <item>disabling GameControl here, before its own Start is reached, keeps that grid but stops
        /// GameControl.Start — the 0.5s tactical flow whose last act, TBTK.OnGameStart(), is what would
        /// switch the tactical UI back on (GameControl.cs:103);</item>
        /// <item>the TBTK UI objects are hidden after their own Awake has run, so the static singletons
        /// the still-enabled TBTK code may touch are already set.</item>
        /// </list>
        /// </summary>
        void Start()
        {
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

            factionColors[SeatA] = new Color(0.40f, 0.55f, 0.95f);   // 魏 blue
            factionColors[SeatB] = new Color(0.45f, 0.80f, 0.45f);   // 蜀 green

            engine.State.Factions.Add(new FactionState { SeatId = SeatA, Name = "魏", Controller = ControlOf(SeatA) });
            engine.State.Factions.Add(new FactionState { SeatId = SeatB, Name = "蜀", Controller = ControlOf(SeatB) });

            AddCity(weiCityObj, "city-wei", "魏都", SeatA);
            AddCity(shuCityObj, "city-shu", "成都", SeatB);
            AddCity(southCityObj, "city-south", "南城", null);

            AddArmy(weiArmyObj, "army-wei-1", "魏軍", SeatA);
            AddArmy(shuArmyObj, "army-shu-1", "蜀軍", SeatB);
            foreach (var name in extraArmyObjs)
            {
                var go = FindSceneObject(name);
                if (go == null || !go.activeInHierarchy) continue;

                bool isWei = name.StartsWith("Wei_", System.StringComparison.Ordinal);
                string faction = isWei ? "wei" : "shu";
                string seat = isWei ? SeatA : SeatB;
                string number = name.Substring(name.Length - 1);
                AddArmy(name, "army-" + faction + "-" + number, isWei ? "魏軍" : "蜀軍", seat);
            }

            engine.BeginSeason();
            currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();
            string humans = string.Join(",", SeatRotation.HumanSeats(engine.State).ToArray());
            statusMsg = $"第一季開始 — 由 {currentSeat} 執政；點選城市或部隊下達命令";
            Debug.Log($"[KingdomDemo] game started, season 1, human seats: {humans}");
        }

        /// <summary>humanSeats decides who is a person; every other seat is scripted
        /// (AGENTS.md #6: never hardcode the player count — up to 8 seats).</summary>
        string ControlOf(string seatId)
        {
            if (humanSeats != null)
                foreach (var id in humanSeats)
                    if (id == seatId) return ControllerType.Human;
            return ControllerType.ScriptedAi;
        }

        void AddCity(string objName, string id, string displayName, string ownerSeat)
        {
            var go = FindOrCreateMarker(objName, id == "city-wei" ? 2 : id == "city-shu" ? 9 : 6,
                                        id == "city-wei" ? 2 : id == "city-shu" ? 6 : 4, ownerSeat);
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
            var go = FindOrCreateMarker(objName, id == "army-wei-1" ? 2 : 9, id == "army-wei-1" ? 3 : 5, ownerSeat);
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

        /// <summary>
        /// The scene object if the scene has one, otherwise a runtime marker: a flat, faction-coloured
        /// disc. The smoke test found plain grey cylinders where the scene has no object yet (all of 蜀
        /// today), which read as debris — the marker now carries its owner's colour from the start.
        /// </summary>
        GameObject FindOrCreateMarker(string objName, int x, int z, string ownerSeat)
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
            go.transform.localScale = FallbackMarkerScale;

            var marker = go.GetComponentInChildren<MeshRenderer>();
            if (marker != null)
            {
                var material = MarkerMaterial(OwnerColor(ownerSeat));
                if (material != null) marker.sharedMaterial = material;
            }
            return go;
        }

        GameObject FindSceneObject(string objName)
        {
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go.name == objName) return go;
            return null;
        }

        /// <summary>Faction colour, neutral grey for unowned ground (ADR-008 palette).</summary>
        Color OwnerColor(string seatId)
            => seatId != null && factionColors.TryGetValue(seatId, out var color) ? color : NeutralColor;

        /// <summary>
        /// One runtime material per colour, cached, so a fallback marker shows its owner's colour.
        /// Tries URP first (this project renders URP), then built-in unlit — no material assets added.
        /// </summary>
        Material MarkerMaterial(Color color)
        {
            if (markerMaterials.TryGetValue(color, out var cached)) return cached;

            Material material = null;
            foreach (var shaderName in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default" })
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) continue;
                material = new Material(shader);
                break;
            }
            if (material == null) return null;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);   // URP/Unlit
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);           // built-in
            markerMaterials[color] = material;
            return material;
        }

        void TintCity(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            if (city == null) return;
            var color = OwnerColor(city.OwnerSeatId);

            var go = cityObjs[cityId];
            var sprite = go.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) { sprite.color = color; return; }

            // the runtime fallback marker is a mesh, not a sprite
            var marker = go.GetComponentInChildren<MeshRenderer>();
            if (marker == null) return;
            var material = MarkerMaterial(color);
            if (material != null) marker.sharedMaterial = material;
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
            if (!ready || reportShowing || handoverShowing || activeTab != "map") return;
            if (PointerOverUI(e.mousePosition)) return;

            if (e.button == 1) { ClearArmyMoveTargets(); selCity = null; selArmy = null; e.Use(); return; }
            if (e.button != 0) return;

            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var node = ResolveClickedNode(ray);
            if (node == null) return;

            var city = engine.State.CityAt(node.idxX, node.idxZ);
            var army = engine.State.ArmiesOf(currentSeat).Find(a => a.X == node.idxX && a.Z == node.idxZ);

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
            if (city != null && city.OwnerSeatId == currentSeat)
            {
                ClearArmyMoveTargets();
                selCity = city; selArmy = null; e.Use(); return;
            }
            // a foreign city can only be selected while the current seat can actually see it (fog)
            if (city != null && obs != null && obs.FindCity(city.Id) != null)
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

                        // the highlight follows the seat's observation: units it cannot see do not
                        // block a destination it can click (the engine still has the last word)
                        var blocker = engine.State.ArmyAt(x, z);
                        if (blocker != null && blocker.OwnerSeatId != currentSeat
                            && (obs == null || obs.FindArmy(blocker.Id) == null)) blocker = null;
                        if (blocker != null) continue;

                        var city = engine.State.CityAt(x, z);
                        if (city != null && !city.IsNeutral && city.OwnerSeatId != currentSeat
                            && obs != null && obs.FindCity(city.Id) != null) continue;
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
            cmd.ActorSeatId = currentSeat;
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
            RefreshObservation();            // the world just moved: the fog radius moves with it

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

        // ------------------------------------------------------------- hotseat seat flow

        /// <summary>Ends the current seat's turn: scripted seats play out through the same pipeline,
        /// then either the next human takes the chair or the season resolves (ADR-000).</summary>
        void EndTurn()
        {
            selCity = null; selArmy = null;
            ClearArmyMoveTargets();

            string next = SeatRotation.NextAfter(engine.State, currentSeat);
            while (next != null && !SeatRotation.IsHuman(engine.State, next))
            {
                RunScriptedSeat(next);
                next = SeatRotation.NextAfter(engine.State, next);
            }

            if (next != null)
            {
                currentSeat = next;
                StartSeatTurn();
                return;
            }

            ResolveSeason();
        }

        void RunScriptedSeat(string seatId)
        {
            foreach (var cmd in ScriptedController.PlanTurn(engine.State, seatId))
            {
                var r = engine.Submit(cmd);
                PushJson(cmd, r);
                if (r.Ok && cmd.Type == ActionType.March) SyncArmyVisual(cmd.TargetId);
                if (r.Ok && IsBattle(cmd.Type)) SyncBattleVisuals();
            }
        }

        /// <summary>
        /// Hands the chair over. With more than one human seat a full-screen blocker covers the map and
        /// every panel — the previous player's screen is not the next player's briefing (hotseat rule).
        /// With a single human seat the flow stays exactly as before: no blocker, play on.
        /// </summary>
        void StartSeatTurn()
        {
            selCity = null; selArmy = null;
            ClearArmyMoveTargets();
            jsonFeed.Clear();                     // one player's command feed is not the next one's
            if (string.IsNullOrEmpty(currentSeat)) currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();

            handoverShowing = SeatRotation.HumanSeats(engine.State).Count > 1;
            if (handoverShowing) return;

            if (pendingReport) { pendingReport = false; reportShowing = true; }
            else statusMsg = SeatTurnPrompt();
        }

        string SeatTurnPrompt()
        {
            var fac = engine.State.FindFaction(currentSeat);
            return $"{fac?.Name} 的回合（第 {engine.State.Season} 季）— 點選城池或部隊下達命令";
        }

        /// <summary>The only world the UI draws: the current seat's filtered observation.</summary>
        void RefreshObservation()
        {
            obs = engine.Observe(currentSeat);
            ApplyFog();
        }

        /// <summary>
        /// Fog on the map (AGENTS.md #3): foreign cities and armies outside the seat's observation are
        /// hidden outright — the UI never shows a unit the seat cannot see, and a selection that drops
        /// out of sight is dropped with it.
        /// </summary>
        void ApplyFog()
        {
            foreach (var kv in cityObjs)
            {
                bool visible = obs != null && obs.FindCity(kv.Key) != null;
                if (kv.Value != null && kv.Value.activeSelf != visible) kv.Value.SetActive(visible);
            }
            foreach (var kv in armyObjs)
            {
                bool visible = obs != null && obs.FindArmy(kv.Key) != null;
                if (kv.Value != null && kv.Value.activeSelf != visible) kv.Value.SetActive(visible);
            }
            if (selCity != null && (obs == null || obs.FindCity(selCity.Id) == null)) selCity = null;
            if (selArmy != null && (obs == null || obs.FindArmy(selArmy.Id) == null)) selArmy = null;
        }

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

        /// <summary>Season rollover: resolve the world, then open the next seat's 朝報 (design §2 ①).</summary>
        void ResolveSeason()
        {
            var report = engine.EndSeason();
            engine.BeginSeason();
            currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();

            BuildReport(report);
            pendingReport = true;
            handoverShowing = false;
            StartSeatTurn();
        }

        /// <summary>
        /// The 朝報 is the reader's own ministry report: public news plus only the city lines this seat
        /// can see (the engine keeps the canonical report; the observation does the filtering).
        /// </summary>
        void BuildReport(SeasonReport report)
        {
            reportLines.Clear();
            reportLines.Add($"—— 第{report.Season}季 · 朝報（{engine.State.FindFaction(currentSeat)?.Name}）——");
            reportLines.Add("");

            foreach (var rec in obs.Events)
            {
                if (rec.Season != report.Season) continue;
                if (rec.EventType == "city_captured")
                {
                    var city = engine.State.FindCity(rec.Payload["city_id"]);
                    var fac = engine.State.FindFaction(city.OwnerSeatId);
                    reportLines.Add($"★ {fac.Name}軍進駐{city.Name}，開拓版圖！");
                }
                else if (rec.EventType == "battle_field")
                    reportLines.Add("戰報：" + FieldSummary(rec));
                else if (rec.EventType == "battle_siege")
                    reportLines.Add("戰報：" + SiegeSummary(rec));
                else if (rec.EventType == "war_declared")
                    reportLines.Add($"外交：{SeatShort(rec.Payload["attacker_seat_id"])}向" +
                                    $"{SeatShort(rec.Payload["defender_seat_id"])}宣戰");
                else if (rec.EventType == "treaty_broken")
                    reportLines.Add($"外交：{SeatShort(rec.Payload["breaker_seat_id"])}毀棄與" +
                                    $"{SeatShort(rec.Payload["counterpart_seat_id"])}的{TreatyLabel(rec.Payload["treaty_type"])}條約");
                else if (rec.EventType == "message_sent")
                    reportLines.Add($"外交：{SeatShort(rec.ActorSeatId)}來使 — {rec.Payload["text"]}");
            }

            foreach (var kv in report.CityLines)
                if (obs.FindCity(kv.Key) != null) reportLines.Add(kv.Value);

            reportLines.Add("");
            reportLines.Add($"史官記：本季共錄得 {CountSeasonEvents(report.Season)} 事。");

            File.WriteAllText(Path.Combine(Application.persistentDataPath, "luanshi_log.jsonl"),
                              engine.Log.ToJsonLines());
        }

        int CountSeasonEvents(int season)
        {
            if (obs == null) return 0;
            int n = 0;
            foreach (var rec in obs.Events) if (rec.Season == season) n++;
            return n;
        }

        void CloseReport()
        {
            reportShowing = false;
            reportScroll = Vector2.zero;
            statusMsg = SeatTurnPrompt();
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

            // Between human seats the whole screen is replaced by the handover blocker: the previous
            // player must not see the next player's map, panels or report (hotseat rule).
            if (handoverShowing)
            {
                DrawHandoverOverlay(title, body);
                return;
            }

            var fac = engine.State.FindFaction(currentSeat);
            int food = 0, gold = 0, pop = 0;
            // render from the seat's observation, never from canonical state
            foreach (var c in obs.CitiesOf(currentSeat)) { food += c.Food; gold += c.Gold; pop += c.Population; }

            bool hotseat = SeatRotation.HumanSeats(engine.State).Count > 1;
            // top bar
            var top = new Rect(10, 8, 1000, 34);
            uiRects.Add(top);
            DrawBackdrop(top);
            GUI.Label(new Rect(22, 12, 980, 28),
                $"第 {engine.State.Season} 季 · {fac.Name}（{(hotseat ? currentSeat + " · 玩家" : "玩家")}）" +
                $" · 令 {fac.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
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

            // end this seat's turn (single-human mode ends the season, as before)
            var end = new Rect(1400, 8, 190, 44);
            uiRects.Add(end);
            if (GUI.Button(end, hotseat ? $"結束 {fac.Name} 回合" : "結束季節", btn)) EndTurn();
            if (hotseat)
                GUI.Label(new Rect(1400, 56, 190, 22), $"回合：{fac.Name}", mono);

            // status + JSON command feed (the FYP artifact: UI clicks == LLM JSON)
            var feed = new Rect(10, 800, 1100, 92);
            uiRects.Add(feed);
            DrawBackdrop(feed);
            GUI.Label(new Rect(20, 804, 1080, 22), statusMsg, body);
            for (int i = 0; i < jsonFeed.Count; i++)
                GUI.Label(new Rect(20, 826 + i * 16, 1080, 16), jsonFeed[i], mono);

            if (activeTab != "map" && !reportShowing) DrawTabPanel(body, btn, title, mono);
            if (reportShowing) DrawReport(body, btn, title);

            HandleClicks();
        }

        /// <summary>
        /// Opaque plate behind a panel or modal. This skin's GUI.Box renders see-through, so map sprites
        /// bled through and collided with panel text (smoke-test finding): plate the rect in solid
        /// charcoal first and keep the box for its border. Every modal and side panel goes through here.
        /// </summary>
        void DrawBackdrop(Rect r)
        {
            var prev = GUI.color;
            GUI.color = PanelColor;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(r, GUIContent.none);
        }

        /// <summary>
        /// Full-screen opaque blocker between seats: whoever is at the keyboard presses a key to take
        /// the chair. Nothing of the previous player's screen survives it.
        /// </summary>
        void DrawHandoverOverlay(GUIStyle title, GUIStyle body)
        {
            var e = Event.current;
            var centered = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
            var centeredBody = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };

            if (e.type == EventType.KeyDown || e.type == EventType.MouseDown)
            {
                handoverShowing = false;
                statusMsg = SeatTurnPrompt();
                e.Use();
                if (pendingReport) { pendingReport = false; reportShowing = true; }
                return;
            }

            // fully opaque (alpha 1) — the map, panels and report are all skipped while this is up
            var prevColor = GUI.color;
            GUI.color = new Color(0.07f, 0.07f, 0.09f, 1f);
            GUI.DrawTexture(new Rect(0, 0, 1600, 900), Texture2D.whiteTexture);
            GUI.color = prevColor;

            var fac = engine.State.FindFaction(currentSeat);
            GUI.Label(new Rect(0, 330, 1600, 50), $"第 {engine.State.Season} 季 · {fac?.Name}（{currentSeat}）", centered);
            GUI.Label(new Rect(0, 400, 1600, 40), "陛下請坐", centeredBody);
            GUI.Label(new Rect(0, 460, 1600, 40), "按任意鍵開始", centeredBody);
        }

        void DrawCityPanel(CityState city, GUIStyle body, GUIStyle btn)
        {
            bool mine = city.OwnerSeatId == currentSeat;
            var p = new Rect(10, 50, 300, mine ? 400 : 120);
            uiRects.Add(p);
            DrawBackdrop(p);
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
            DrawBackdrop(p);
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
            // the panel hugs its lines (capped at the old 420-tall size) instead of a fixed 800×420,
            // and scrolls when a season was busier than the cap
            int lines = System.Math.Max(1, reportLines.Count - 1);
            float textHeight = System.Math.Min(ReportTextMaxHeight, lines * ReportLineHeight);
            var p = new Rect(400, 150, 800, 116f + textHeight);
            uiRects.Add(p);
            DrawBackdrop(p);
            GUI.Label(new Rect(p.x + 20, p.y + 12, 760, 30), reportLines.Count > 0 ? reportLines[0] : "", title);

            var sb = new StringBuilder();
            for (int i = 1; i < reportLines.Count; i++) sb.AppendLine(reportLines[i]);

            var view = new Rect(p.x + 20, p.y + 50, 760, textHeight);
            // scroll only when the cap actually bites, and without a permanently visible scrollbar
            if (lines * ReportLineHeight > textHeight)
            {
                float contentHeight = lines * ReportLineHeight + 4f;
                reportScroll = GUI.BeginScrollView(view, reportScroll,
                                                   new Rect(0, 0, 744, contentHeight), false, false);
                GUI.Label(new Rect(0, 0, 736, contentHeight), sb.ToString(), body);
                GUI.EndScrollView();
            }
            else
            {
                GUI.Label(new Rect(view.x, view.y, 760, textHeight + 8f), sb.ToString(), body);
            }

            if (GUI.Button(new Rect(p.x + 330, p.y + 50 + textHeight + 14, 140, 40), "繼續", btn)) CloseReport();
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

        // ---- test hooks -------------------------------------------------------
        // The smoke-test harness drives the running game through Unity's SendMessage, which can only
        // reach public methods with at most one argument. These two hooks exist only for that: they do
        // the same things a mouse would, nothing more.

        /// <summary>
        /// Test hook. Selects <c>"city:&lt;id&gt;"</c> or <c>"army:&lt;id&gt;"</c> exactly as a map
        /// click would, fog included: anything the current seat cannot see is refused, and a foreign
        /// army cannot be selected (only its own panel exists).
        /// </summary>
        public void DebugSelect(string kindAndId)
        {
            if (!ready || string.IsNullOrEmpty(kindAndId)) return;

            int split = kindAndId.IndexOf(':');
            if (split <= 0) return;
            string kind = kindAndId.Substring(0, split);
            string id = kindAndId.Substring(split + 1);

            if (kind == "city")
            {
                var city = obs != null ? obs.FindCity(id) : null;          // fog rules apply
                if (city == null)
                {
                    statusMsg = $"DebugSelect: city '{id}' is not visible to {currentSeat}";
                    return;
                }
                ClearArmyMoveTargets();
                selCity = city;
                selArmy = null;
                statusMsg = city.OwnerSeatId == currentSeat ? "已選擇 " + city.Name
                    : city.IsNeutral ? "中立城市 — 派軍進駐以佔領" : "敵方城市";
                return;
            }

            if (kind == "army")
            {
                var army = obs != null ? obs.FindArmy(id) : null;
                if (army == null)
                {
                    statusMsg = $"DebugSelect: army '{id}' is not visible to {currentSeat}";
                    return;
                }
                if (army.OwnerSeatId != currentSeat)
                {
                    statusMsg = $"DebugSelect: '{id}' is not {currentSeat}'s army";
                    return;
                }
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(TBTK.GridManager.GetNode(army.X, army.Z));
                ShowArmyMoveTargets(army);
                statusMsg = "已選擇 " + army.Name + " — 點選目的地";
            }
        }

        /// <summary>
        /// Test hook. Submits <c>"&lt;action_type&gt;|&lt;target_id&gt;"</c>, with an optional third
        /// segment <c>"x,z"</c> for the hex-parameterised actions (<c>march</c>, <c>attack_army</c>,
        /// <c>attack_city</c>), e.g. <c>"march|army-wei-1|4,3"</c>. Goes through the same
        /// <see cref="SubmitHuman"/> path as the buttons, so the engine validates it identically.
        /// </summary>
        public void DebugOrder(string spec)
        {
            if (!ready || string.IsNullOrEmpty(spec)) return;

            string[] parts = spec.Split('|');
            if (parts.Length < 2 || string.IsNullOrEmpty(parts[0])) return;

            var cmd = new ActionCommand
            {
                Type = parts[0].Trim(),
                TargetId = parts[1].Trim(),
                Reason = "debug hook",
            };
            if (parts.Length > 2)
            {
                string[] coords = parts[2].Split(',');
                if (coords.Length == 2
                    && int.TryParse(coords[0], out int x) && int.TryParse(coords[1], out int z))
                {
                    cmd.ParamA = x;
                    cmd.ParamB = z;
                }
            }
            SubmitHuman(cmd);
        }

        void DrawTabPanel(GUIStyle body, GUIStyle btn, GUIStyle title, GUIStyle mono)
        {
            float height = TabPanelHeight();
            var p = new Rect(400, 190, 800, height);
            uiRects.Add(p);
            DrawBackdrop(p);
            GUI.Label(new Rect(p.x + 20, p.y + 12, 760, 30), TabTitle(), title);

            switch (activeTab)
            {
                case "domestic":  DrawDomesticPanel(p, body); break;
                case "diplomacy": DrawDiplomacyPanel(p, body); break;
                case "intel":     DrawIntelPanel(p, body); break;
                case "research":  DrawResearchPanel(p, body); break;
                default:          DrawHistorianPanel(p, body, mono); break;
            }

            // pinned to the panel bottom, so a content-sized panel keeps its button inside
            if (GUI.Button(new Rect(p.x + 320, p.y + height - 56, 160, 40), "關閉", btn)) activeTab = "map";
        }

        /// <summary>
        /// Panel height per tab: the design previews keep the full shell; the 史官 chronicle hugs its
        /// contents (capped) so a two-record log does not open an 80%-empty box.
        /// </summary>
        float TabPanelHeight()
            => activeTab == "history" ? 178f + HistorianViewHeight() : 480f;

        /// <summary>Height of the 史官 scroll view for the current seat: content-sized, capped.</summary>
        float HistorianViewHeight()
            => Mathf.Min(HistorianViewMaxHeight,
                         (obs != null ? obs.Events.Count : 1) * HistorianLineHeight + 8f);

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
            var me = engine.State.FindFaction(currentSeat);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            const int shownFactions = 3;      // placeholder layout: the rest are summarised

            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"外交（{me.Name}）　號令 {me.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}　威望 {me.Prestige}" +
                $"　條約 {(obs != null ? obs.Treaties.Count : 0)}" +
                $"　待決提案 {(obs != null ? obs.Proposals.Count : 0)}", body);
            y += 26;

            // 遣使: type here, then press 遣使 on a faction row to submit it.
            GUI.Label(new Rect(p.x + 36, y, 120, 22), "遣使文書", body);
            envoyText = GUI.TextField(new Rect(p.x + 160, y, 604, 24), envoyText, 120);
            y += 34;

            // Incoming offers first: they are the only diplomacy that demands an answer.
            var incoming = new List<TreatyProposal>();
            if (obs != null)
                foreach (var proposal in obs.Proposals)
                    if (proposal.ToSeatId == currentSeat) incoming.Add(proposal);

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
                if (other.SeatId == currentSeat) continue;
                if (drawn == shownFactions)
                {
                    GUI.Label(new Rect(p.x + 36, y, 744, 20), "…（其餘勢力省略）", body);
                    break;
                }

                var relation = obs != null ? obs.Relation(other.SeatId) : null;
                int myTrust = relation != null ? relation.MyTrust : 0;
                int theirTrust = relation != null ? relation.TheirTrust : 0;
                bool atWar = relation != null && relation.AtWar;

                GUI.Label(new Rect(p.x + 20, y, 760, 20),
                    $"{other.Name}　信任 我→他 {myTrust}／他→我 {theirTrust}　{StatusLine(other.SeatId)}", body);
                y += 22;

                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.DeclareWar,
                    other.SeatId, "宣戰", atWar ? "已在交戰" : null, 0, 0, btn));
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

        /// <summary>和平 / 交戰 plus every treaty in force (with seasons left) and any pending offer,
        /// read from this seat's observation only.</summary>
        string StatusLine(string seatId)
        {
            var relation = obs != null ? obs.Relation(seatId) : null;
            var sb = new StringBuilder(relation != null && relation.AtWar ? "交戰" : "和平");
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
            {
                var treaty = MyTreaty(type, seatId);
                if (treaty == null) continue;
                int seasonsLeft = treaty.ExpirySeason - engine.State.Season + 1;
                if (seasonsLeft < 1) seasonsLeft = 1;
                sb.Append($"　[{TreatyLabel(type)} 剩 {seasonsLeft} 季]");
            }
            var proposal = MyProposal(seatId);
            if (proposal != null)
                sb.Append("　[").Append(proposal.FromSeatId == currentSeat ? "我方提案待覆" : "待你回覆")
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

        /// <summary>This seat's own treaty of that type with another seat, from the observation.</summary>
        TreatyRecord MyTreaty(string type, string otherSeatId)
        {
            if (obs == null) return null;
            foreach (var treaty in obs.Treaties)
                if (treaty.Type == type && treaty.Covers(currentSeat, otherSeatId)) return treaty;
            return null;
        }

        /// <summary>The pending offer between this seat and another, in either direction.</summary>
        TreatyProposal MyProposal(string otherSeatId)
        {
            if (obs == null) return null;
            foreach (var proposal in obs.Proposals)
                if ((proposal.FromSeatId == currentSeat && proposal.ToSeatId == otherSeatId)
                    || (proposal.ToSeatId == currentSeat && proposal.FromSeatId == otherSeatId))
                    return proposal;
            return null;
        }

        /// <summary>Reason a treaty offer is obviously illegal, or null when it can be sent.</summary>
        string ProposalBlocked(string seatId, string type)
        {
            if (MyTreaty(type, seatId) != null) return "已在生效";
            if (MyProposal(seatId) != null) return "已有提案待覆";
            return null;
        }

        string BreakBlocked(string seatId)
            => BreakableType(seatId) == null ? "沒有條約可毀" : null;

        /// <summary>First treaty in force with that seat (nap → alliance → truce), or null.</summary>
        string BreakableType(string seatId)
        {
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
                if (MyTreaty(type, seatId) != null) return type;
            return null;
        }

        int Treasury()
        {
            int gold = 0;
            if (obs == null) return 0;
            foreach (var city in obs.CitiesOf(currentSeat)) gold += city.Gold;
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
            // the chronicle is the seat's filtered view of the log (CONTROLLER_PROTOCOL §5)
            var records = obs != null ? obs.Events : new List<EventRecord>();
            GUI.Label(new Rect(p.x + 20, p.y + 52, 760, 22),
                $"本局史書（{engine.State.FindFaction(currentSeat)?.Name} 可見）— 共 {records.Count} 條" +
                $"（第 {engine.State.Season} 季 · {PhaseLabel(engine.State.Phase)}）", body);

            // hug the chronicle: the scroll view is as tall as its content (capped), and the
            // scrollbar only appears when the cap actually bites
            float viewHeight = HistorianViewHeight();
            float contentHeight = records.Count * HistorianLineHeight + 8f;
            var view = new Rect(p.x + 20, p.y + 80, 760, viewHeight);
            historianScroll = GUI.BeginScrollView(view, historianScroll,
                                                  new Rect(0, 0, 744, contentHeight), false, false);
            for (int i = 0; i < records.Count; i++)
                GUI.Label(new Rect(4, 4 + i * HistorianLineHeight, 736, HistorianLineHeight),
                          HistorianLine(records[i]), mono);
            GUI.EndScrollView();

            GUI.Label(new Rect(p.x + 20, p.y + 80 + viewHeight + 12, 760, 22),
                $"史官記：本季共錄得 {CountSeasonEvents(engine.State.Season)} 事。", body);
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
