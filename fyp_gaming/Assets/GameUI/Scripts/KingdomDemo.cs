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
        public string watchTowerObj = "Watch Tower";
        public string weiArmyObj = "Wei_Army1";
        public string shuArmyObj = "Shu_Army1";
        public Texture2D watchTowerImage;
        [FormerlySerializedAs("unusedArmyObjs")]   // renamed after the smoke test: they are used
        public string[] extraArmyObjs = { "Wei_Army2", "Wei_Army3", "Wei_Army4",
                                          "Shu_Army2", "Shu_Army3", "Shu_Army4" };

        [Header("Game setup")]
        public int randomSeed = 12345;

        [Header("Fog of war")]
        [Tooltip("Photo used to cover grid cells outside the current seat's vision. Defaults to Assets/GameUI/Art/fog.png.")]
        public Texture2D fogPhoto;
        public Color fogPhotoTint = Color.white;
        public float fogPhotoHeight = 0.08f;

        [Header("Seats (data-driven; the engine supports up to 8, AGENTS.md #6)")]
        [Tooltip("Seat ids played by people at this keyboard. Every other seat is scripted. " +
                 "Set two ids here for hotseat pass-and-play.")]
        public string[] humanSeats = { "seat-0" };

        private GameEngine engine;
        private bool ready;
        private string initError;

        private const string SeatA = "seat-0";     // Wei — scene roster, not "the human"
        private const string SeatB = "seat-1";     // Shu

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
        private bool pendingReport;                // court report queued behind the handover screen
        private Observation obs;                   // the current seat's filtered world (AGENTS.md #3)

        private readonly Dictionary<string, GameObject> cityObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> armyObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> watchTowerObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Color> factionColors = new Dictionary<string, Color>();
        private readonly Dictionary<Color, Material> markerMaterials = new Dictionary<Color, Material>();

        private bool locationChoiceOpen;
        private int locationChoiceX;
        private int locationChoiceZ;
        private Rect locationChoiceRect;
        private readonly List<SpriteRenderer> fogOverlays = new List<SpriteRenderer>();
        private Transform fogOverlayRoot;

        // selection & UI state
        private CityState selCity;
        private ArmyState selArmy;
        private string statusMsg = "";
        private readonly List<string> jsonFeed = new List<string>();   // recent ActionCommand JSON
        private bool reportShowing;
        private readonly List<string> reportLines = new List<string>();
        private readonly List<Rect> uiRects = new List<Rect>();
        private string envoyText = "";        // envoy draft for the diplomacy tab (never sent automatically)
        private float envoyConfirmationUntil;
        private Vector2 reportScroll;         // court report scroll offset when a season outruns its panel

        // concept tabs: Map is the live view, the rest preview the full design
        // (doc/LuanShi-Game-Design-v1.md). Domestic/Diplomacy/Research are live, Intel is a preview, History renders real data.
        private string activeTab = "map";
        private Vector2 historianScroll;
        private static readonly string[,] Tabs =
        {
            { "map",       "Map" },
            { "domestic",  "Domestic" },
            { "diplomacy", "Diplomacy" },
            { "intel",     "Intel" },
            { "research",  "Research" },
            { "history",   "History" },
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
            InitializeFogOverlays(nodes, w, h);

            factionColors[SeatA] = new Color(0.40f, 0.55f, 0.95f);   // Wei blue
            factionColors[SeatB] = new Color(0.45f, 0.80f, 0.45f);   // Shu green

            engine.State.Factions.Add(new FactionState { SeatId = SeatA, Name = "Wei", Controller = ControlOf(SeatA) });
            engine.State.Factions.Add(new FactionState { SeatId = SeatB, Name = "Shu", Controller = ControlOf(SeatB) });

            AddCity(weiCityObj, "city-wei", "Wei Capital", SeatA);
            AddCity(shuCityObj, "city-shu", "Chengdu", SeatB);
            AddCity(southCityObj, "city-south", "Southern City", null);
            AddWatchTower(watchTowerObj, "watch-tower-1", "Watch Tower", 6, 2);

            AddArmy(weiArmyObj, "army-wei-1", "Wei Army", SeatA);
            AddArmy(shuArmyObj, "army-shu-1", "Shu Army", SeatB);
            foreach (var name in extraArmyObjs)
            {
                var go = FindSceneObject(name);
                if (go == null || !go.activeInHierarchy) continue;

                bool isWei = name.StartsWith("Wei_", System.StringComparison.Ordinal);
                string faction = isWei ? "wei" : "shu";
                string seat = isWei ? SeatA : SeatB;
                string number = name.Substring(name.Length - 1);
                AddArmy(name, "army-" + faction + "-" + number, isWei ? "Wei Army" : "Shu Army", seat);
            }

            engine.BeginSeason();
            currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();
            string humans = string.Join(",", SeatRotation.HumanSeats(engine.State).ToArray());
            statusMsg = $"Season 1 begins — {currentSeat} rules; select a city or army to issue orders";
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

        void AddWatchTower(string objName, string id, string displayName, int x, int z)
        {
            var node = TBTK.GridManager.GetNode(x, z);
            if (node == null) throw new System.Exception("watch tower is not on a grid node");

            var go = FindSceneObject(objName);
            if (go == null)
            {
                go = new GameObject(objName);
                go.transform.position = node.GetPos();
                var renderer = go.AddComponent<SpriteRenderer>();
                if (watchTowerImage != null)
                    renderer.sprite = Sprite.Create(watchTowerImage,
                        new Rect(0f, 0f, watchTowerImage.width, watchTowerImage.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            else
            {
                var sceneNode = TBTK.GridManager.GetNode(go.transform.position, null);
                if (sceneNode == null) throw new System.Exception(objName + " is not on a grid node");
                node = sceneNode;
                go.transform.position = node.GetPos();
            }

            go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            go.transform.localEulerAngles = new Vector3(45f, 0f, 0f);
            var towerPosition = go.transform.position;
            towerPosition.y = 0.55f;
            go.transform.position = towerPosition;

            engine.State.WatchTowers.Add(new WatchTowerState
            {
                Id = id,
                Name = displayName,
                X = node.idxX,
                Z = node.idxZ,
                OwnerSeatId = null,
            });
            watchTowerObjs[id] = go;
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
        /// disc. The smoke test found plain grey cylinders where the scene has no object yet (all of Shu
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

        /// <summary>
        /// A city marker is tinted with its owner's colour only while the current seat can actually see the
        /// city (its own, or one inside the fog radius). A city outside the observation keeps its marker —
        /// the map still says "a city stands here" — but is painted in the neutral colour, because the
        /// owner's banner is exactly the information the fog is supposed to withhold (hotseat privacy).
        /// </summary>
        void TintCity(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            if (city == null) return;
            bool known = city.OwnerSeatId == currentSeat || (obs != null && obs.FindCity(cityId) != null);
            var color = OwnerColor(known ? city.OwnerSeatId : null);

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
            // a concept tab is modal over the map, exactly like the court report
            if (!ready || reportShowing || handoverShowing || activeTab != "map") return;
            if (PointerOverUI(e.mousePosition)) return;

            if (e.button == 1) { ClearArmyMoveTargets(); selCity = null; selArmy = null; e.Use(); return; }
            if (e.button != 0) return;

            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var node = ResolveClickedNode(ray);
            if (node == null)
            {
                locationChoiceOpen = false;
                ClearArmyMoveTargets();
                selCity = null;
                selArmy = null;
                e.Use();
                return;
            }

            var city = engine.State.CityAt(node.idxX, node.idxZ);
            var army = engine.State.ArmiesOf(currentSeat).Find(a => a.X == node.idxX && a.Z == node.idxZ);
            bool citySelectable = city != null && (city.OwnerSeatId == currentSeat
                || (obs != null && obs.FindCity(city.Id) != null));

            if (locationChoiceOpen && node.idxX == locationChoiceX && node.idxZ == locationChoiceZ)
            {
                locationChoiceOpen = false;
                e.Use();
                return;
            }

            locationChoiceOpen = false;

            if (selCity != null && city == null && army == null)
            {
                selCity = null;
                e.Use();
                return;
            }

            // clicking the selected army again drops it (works on a city hex too)
            if (army != null && selArmy != null && army.Id == selArmy.Id)
            {
                selArmy = null;
                ClearArmyMoveTargets();
                e.Use();
                return;
            }

            // An army is selected: the next click is a march order, on whatever hex it lands — an empty
            // hex, a neutral city (capture) or our own city (return home). This MUST be decided before
            // the city/army popup below, or a click aimed at a city hex opens the popup instead and the
            // march never happens (the demo's headline move — taking 南城 by marching onto it — was
            // impossible, and so was marching home).
            if (selArmy != null && army == null)
            {
                locationChoiceOpen = false;
                TryMarch(selArmy, node.idxX, node.idxZ);
                e.Use();
                return;
            }

            if (army != null && citySelectable)
            {
                locationChoiceOpen = true;
                locationChoiceX = node.idxX;
                locationChoiceZ = node.idxZ;
                selCity = null;
                selArmy = null;
                ClearArmyMoveTargets();
                e.Use();
                return;
            }

            if (army != null)
            {
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(TBTK.GridManager.GetNode(army.X, army.Z));
                ShowArmyMoveTargets(army);
                statusMsg = "Selected " + army.Name + " — choose a destination";
                e.Use();
                return;
            }
            if (city != null && city.OwnerSeatId == currentSeat)
            {
                ClearArmyMoveTargets();
                if (selCity != null && selCity.Id == city.Id)
                {
                    selCity = null;
                    e.Use();
                    return;
                }
                selCity = city; selArmy = null; e.Use(); return;
            }
            // a foreign city can only be selected while the current seat can actually see it (fog)
            if (city != null && obs != null && obs.FindCity(city.Id) != null)
            {
                ClearArmyMoveTargets();
                if (selCity != null && selCity.Id == city.Id)
                {
                    selCity = null;
                    e.Use();
                    return;
                }
                selCity = city; selArmy = null;
                statusMsg = city.IsNeutral ? "Neutral city — send an army to occupy it" : "Enemy city";
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
            // the highlight and the validator must agree on the season's budget, or 冬 (floor(6 × 0.7) = 4)
            // highlights hexes the engine then refuses (P6 §8b; ACTIONS.md §2)
            int budget = BalanceConfig.MarchBudget(engine.State.Season);
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
                        if (engine.State.Map.PathCost(army.X, army.Z, x, z, budget) < 0) continue;

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

        bool SubmitHuman(ActionCommand cmd)
        {
            cmd.ActorSeatId = currentSeat;
            cmd.ControllerType = ControllerType.Human;
            var r = engine.Submit(cmd);
            PushJson(cmd, r);
            if (r.Ok && cmd.Type == ActionType.March)
            {
                ClearArmyMoveTargets();
                SyncArmyVisual(cmd.TargetId);
                selArmy = null;
            }
            if (r.Ok && IsBattle(cmd.Type))
            {
                ClearArmyMoveTargets();
                SyncBattleVisuals();
            }
            RefreshObservation();            // the world just moved: the fog radius moves with it

            if (!r.Ok)
                statusMsg = r.Error == "no command points left"
                    ? "Not enough command points: no command points remain this season"
                    : "Order rejected: " + r.Error;
            else if (IsBattle(cmd.Type))
                statusMsg = LastBattleSummary() ?? "Order executed";
            else
                statusMsg = "Order executed";

            return r.Ok;
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
            locationChoiceOpen = false;           // the previous seat's popup is not the next one's
            envoyText = "";                       // nor is a half-written envoy message
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
            return $"{fac?.Name}'s turn (season {engine.State.Season}) — select a city or army to issue orders";
        }

        /// <summary>The only world the UI draws: the current seat's filtered observation.</summary>
        void RefreshObservation()
        {
            obs = engine.Observe(currentSeat);
            ApplyFog();
            RefreshFogOverlays();
        }

        void InitializeFogOverlays(TBTK.Node[,] nodes, int width, int height)
        {
#if UNITY_EDITOR
            if (fogPhoto == null)
                fogPhoto = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameUI/Art/fog.png");
            if (fogPhoto == null)
            {
                string fogPath = Path.Combine(Application.dataPath, "GameUI/Art/fog.png");
                if (File.Exists(fogPath))
                {
                    fogPhoto = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    fogPhoto.name = "fog_runtime";
                    if (!fogPhoto.LoadImage(File.ReadAllBytes(fogPath), false))
                        fogPhoto = null;
                }
            }
#endif
            if (fogPhoto == null)
            {
                Debug.LogWarning("[KingdomDemo] Fog photo is not assigned; grid fog overlays are disabled.");
                return;
            }

            var root = new GameObject("FogOfWarOverlays");
            fogOverlayRoot = root.transform;
            var fogSprite = Sprite.Create(fogPhoto, new Rect(0f, 0f, fogPhoto.width, fogPhoto.height),
                                          new Vector2(0.5f, 0.5f), 100f);

            float nodeSize = TBTK.GridManager.GetNodeSize();
            float spriteSize = Mathf.Max(fogSprite.bounds.size.x, fogSprite.bounds.size.y);
            float overlayScale = spriteSize > 0f ? nodeSize * 1.08f / spriteSize : 1f;

            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var node = nodes[x, z];
                    if (node == null) continue;

                    var overlay = new GameObject("Fog_" + x + "_" + z);
                    overlay.transform.SetParent(fogOverlayRoot, true);
                    overlay.transform.position = node.GetPos() + Vector3.up * fogPhotoHeight;
                    overlay.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    overlay.transform.localScale = Vector3.one * overlayScale;

                    var renderer = overlay.AddComponent<SpriteRenderer>();
                    renderer.sprite = fogSprite;
                    renderer.color = fogPhotoTint;
                    renderer.sortingOrder = 100;
                    renderer.enabled = false;
                    fogOverlays.Add(renderer);
                }

                    Debug.Log("[KingdomDemo] Created " + fogOverlays.Count + " fog overlays from " + fogPhoto.name + ".");
        }

        void RefreshFogOverlays()
        {
            if (fogOverlayRoot == null || engine == null || currentSeat == null) return;

            int index = 0;
            int hiddenCount = 0;
            int width = engine.State.Map.Width;
            int height = engine.State.Map.Height;
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var node = TBTK.GridManager.GetNode(x, z);
                    if (node == null) continue;

                        bool hidden = !engine.InSight(currentSeat, x, z);
                        fogOverlays[index].enabled = hidden;
                        if (hidden) hiddenCount++;
                    index++;
                }

                    Debug.Log("[KingdomDemo] Fog refresh: " + hiddenCount + " hidden cells for " + currentSeat + ".");
        }

        /// <summary>
        /// Fog on the map (AGENTS.md #3): foreign armies and towers outside the seat's observation are
        /// hidden outright, a city marker outside it keeps only its existence (see <see cref="TintCity"/>),
        /// and a selection that drops out of sight is dropped with it.
        /// </summary>
        void ApplyFog()
        {
            foreach (var kv in armyObjs)
            {
                bool visible = obs != null && obs.FindArmy(kv.Key) != null;
                if (kv.Value != null && kv.Value.activeSelf != visible) kv.Value.SetActive(visible);
            }
            foreach (var kv in watchTowerObjs)
            {
                bool visible = obs != null && obs.FindWatchTower(kv.Key) != null;
                if (kv.Value != null && kv.Value.activeSelf != visible) kv.Value.SetActive(visible);
            }
            foreach (var kv in cityObjs) TintCity(kv.Key);      // owner colour only while the city is seen
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

        // ---- Battle report: read the last battle back out of the log (design §6.4) ------

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
                => $"Field battle, {rec.Payload["rounds"]} rounds: {SeatShort(rec.Payload["attacker_seat_id"])}" +
                    $" {rec.Payload["attacker_troops_before"]}→{rec.Payload["attacker_troops_after"]} vs. " +
               $"{SeatShort(rec.Payload["defender_seat_id"])}" +
                    $" {rec.Payload["defender_troops_before"]}→{rec.Payload["defender_troops_after"]} {OutcomeLabel(rec.Payload["outcome"])}";

        string SiegeSummary(EventRecord rec)
                => $"Siege of {CityName(rec.Payload["city_id"])} for {rec.Payload["rounds"]} rounds: " +
                    $"attacker {rec.Payload["troops_before"]}→{rec.Payload["troops_after"]}, garrison {rec.Payload["garrison_before"]}→{rec.Payload["garrison_after"]} {OutcomeLabel(rec.Payload["outcome"])}";

        string CityName(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            return city != null ? city.Name : cityId;
        }

        static string OutcomeLabel(string outcome)
        {
            switch (outcome)
            {
                case "defender_destroyed": return "Defender destroyed";
                case "attacker_destroyed": return "Attacker destroyed";
                case "mutual_destruction": return "Mutual destruction";
                case "defender_routed":    return "Defender routed";
                case "attacker_routed":    return "Attacker routed";
                case "mutual_rout":        return "Both sides routed";
                case "defender_holds":     return "Defender holds";
                case "captured":           return "City captured";
                case "city_held":          return "City held";
                case "assault_failed":     return "Assault failed";
                default:                   return outcome;
            }
        }

        void PushJson(ActionCommand cmd, ValidationResult r)
        {
            jsonFeed.Add((r.Ok ? "✓ " : "✗ ") + cmd.ToJson());
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }

        /// <summary>Season rollover: resolve the world, then open the next seat's court report (design §2 ①).</summary>
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
        /// The court report is the reader's own ministry report: public news plus only the city lines this seat
        /// can see (the engine keeps the canonical report; the observation does the filtering).
        /// </summary>
        void BuildReport(SeasonReport report)
        {
            reportLines.Clear();
            reportLines.Add($"-- Season {report.Season} ({report.SeasonName}) court report ({engine.State.FindFaction(currentSeat)?.Name}) --");
            reportLines.Add("");

            foreach (var rec in obs.Events)
            {
                if (rec.Season != report.Season) continue;
                if (rec.EventType == "city_captured")
                {
                    var city = engine.State.FindCity(rec.Payload["city_id"]);
                    var fac = engine.State.FindFaction(city.OwnerSeatId);
                    reportLines.Add($"* {fac.Name} army occupied {city.Name}, expanding its territory!");
                }
                else if (rec.EventType == "battle_field")
                    reportLines.Add("Battle report: " + FieldSummary(rec));
                else if (rec.EventType == "battle_siege")
                    reportLines.Add("Battle report: " + SiegeSummary(rec));
                else if (rec.EventType == "war_declared")
                    reportLines.Add($"Diplomacy: {SeatShort(rec.Payload["attacker_seat_id"])} declared war on " +
                                    $"{SeatShort(rec.Payload["defender_seat_id"])}");
                else if (rec.EventType == "treaty_broken")
                    reportLines.Add($"Diplomacy: {SeatShort(rec.Payload["breaker_seat_id"])} broke the " +
                                    $"{TreatyLabel(rec.Payload["treaty_type"])} treaty with {SeatShort(rec.Payload["counterpart_seat_id"])}");
                else if (rec.EventType == "message_sent")
                    reportLines.Add($"Diplomacy: message from {SeatShort(rec.ActorSeatId)} — {rec.Payload["text"]}");
            }

            foreach (var kv in report.CityLines)
                if (obs.FindCity(kv.Key) != null) reportLines.Add(kv.Value);

            reportLines.Add("");
            reportLines.Add($"Historian: {CountSeasonEvents(report.Season)} events recorded this season.");

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

            // 一統天下 (design §13): the game is decided — the land is one, there is nothing left to order.
            if (engine.State.WinnerSeatId != null)
            {
                DrawVictoryBanner(title, body);
                return;
            }

            bool hotseat = SeatRotation.HumanSeats(engine.State).Count > 1;
            // top bar
            var top = new Rect(10, 8, 1000, 34);
            uiRects.Add(top);
            DrawBackdrop(top);
            GUI.Label(new Rect(22, 12, 980, 28),
                $"Season {engine.State.Season} ({BalanceConfig.SeasonName(engine.State.Season)}) · {fac.Name} ({(hotseat ? currentSeat + " · Player" : "Player")})" +
                $" · Orders {fac.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
                $" · Prestige {fac.Prestige} · Population {pop} · Food {food} · Gold {gold}", title);

            // tab bar — Map is the live view; the others preview the full design concept
            var tabBtn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            for (int i = 0; i < Tabs.GetLength(0); i++)
            {
                var r = new Rect(1400, 60 + i * 46, 190, 40);
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
            DrawLocationChoice(body, btn);

            // end this seat's turn (single-human mode ends the season, as before)
            var end = new Rect(1400, 8, 190, 44);
            uiRects.Add(end);
            if (GUI.Button(end, hotseat ? $"End {fac.Name}'s turn" : "End season", btn))
            {
                locationChoiceOpen = false;
                EndTurn();
            }
            if (hotseat)
                GUI.Label(new Rect(1400, 56, 190, 22), $"Turn: {fac.Name}", mono);

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

        void DrawLocationChoice(GUIStyle body, GUIStyle btn)
        {
            if (!locationChoiceOpen || activeTab != "map") return;

            var node = TBTK.GridManager.GetNode(locationChoiceX, locationChoiceZ);
            // drawn from the observation, like everything else: a hex whose city or army this seat cannot
            // see has nothing to offer (the click handler only opens this for a city it may inspect)
            var city = obs != null ? obs.Cities.Find(c => c.X == locationChoiceX && c.Z == locationChoiceZ) : null;
            var army = obs != null ? obs.ArmiesOf(currentSeat).Find(a => a.X == locationChoiceX && a.Z == locationChoiceZ) : null;
            if (node == null || city == null || army == null)
            {
                locationChoiceOpen = false;
                return;
            }

            float scale = Screen.width / 1600f;
            Vector3 screenPoint = Camera.main != null
                ? Camera.main.WorldToScreenPoint(node.GetPos())
                : new Vector3(800f * scale, Screen.height * 0.5f, 1f);
            float x = screenPoint.x / scale + 24f;
            float y = (Screen.height - screenPoint.y) / scale - 42f;
            const float width = 220f;
            const float height = 112f;
            x = Mathf.Clamp(x, 8f, 1600f - width - 8f);
            y = Mathf.Clamp(y, 50f, 900f - height - 8f);

            locationChoiceRect = new Rect(x, y, width, height);
            uiRects.Add(locationChoiceRect);
            DrawBackdrop(locationChoiceRect);
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 22f), "Choose a target to inspect", body);

            if (GUI.Button(new Rect(x + 12f, y + 42f, width - 24f, 28f), city.Name, btn))
            {
                locationChoiceOpen = false;
                selCity = city;
                selArmy = null;
            }
            if (GUI.Button(new Rect(x + 12f, y + 76f, width - 24f, 28f), army.Name, btn))
            {
                locationChoiceOpen = false;
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(node);
                ShowArmyMoveTargets(army);
                statusMsg = "Selected " + army.Name + " — choose a destination";
            }
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
        /// Full-screen opaque blocker between seats (hotseat rule): whoever holds the keyboard sees a black
        /// screen and a prompt, and presses a key or clicks to take the chair. The fill is drawn in *screen*
        /// space, not the 1600×900 design space — a scaled rect leaves the last pixels of a non-16:9 window
        /// showing the previous player's map. OnGUI renders above every camera, so an opaque full-screen
        /// fill is enough to hide the scene as well as the panels, and the early return in OnGUI means
        /// nothing else is drawn at all while this is up.
        /// </summary>
        void DrawHandoverOverlay(GUIStyle title, GUIStyle body)
        {
            var e = Event.current;
            var centered = new GUIStyle(title) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            var centeredBody = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };

            if (e.type == EventType.KeyDown || e.type == EventType.MouseDown)
            {
                handoverShowing = false;
                statusMsg = SeatTurnPrompt();
                e.Use();
                if (pendingReport) { pendingReport = false; reportShowing = true; }
                return;
            }

            FillScreen(Color.black);

            var fac = engine.State.FindFaction(currentSeat);
            GUI.Label(new Rect(0, 330, 1600, 50),
                $"Season {engine.State.Season} · {fac?.Name} ({currentSeat})", centered);
            GUI.Label(new Rect(0, 400, 1600, 40),
                $"Pass to seat {fac?.Name} ({currentSeat}) — click to take the throne", centeredBody);
            GUI.Label(new Rect(0, 460, 1600, 40), "Nothing of the previous player's screen is shown.", centeredBody);
        }

        /// <summary>
        /// Fills the whole game view in *screen* pixels, whatever the window aspect is. Callers run inside
        /// OnGUI's design-space matrix, so the matrix is reset for the fill and restored afterwards.
        /// </summary>
        void FillScreen(Color color)
        {
            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// 一統天下 (design §13): the whole screen becomes the result. Order buttons, panels and map clicks
        /// are unreachable — the engine refuses further commands anyway (Submit → "game over").
        /// </summary>
        void DrawVictoryBanner(GUIStyle title, GUIStyle body)
        {
            var centered = new GUIStyle(title) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            var centeredBody = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };

            FillScreen(Color.black);

            var winner = engine.State.FindFaction(engine.State.WinnerSeatId);
            GUI.Label(new Rect(0, 380, 1600, 60), $"{winner?.Name} unifies the land — game over", centered);
            GUI.Label(new Rect(0, 450, 1600, 40), "一統天下 · all cities are one banner", centeredBody);
        }

        /// <summary>
        /// City details for the current seat. An own city shows everything and offers orders; a foreign or
        /// neutral city shows **only what this seat actually holds** — the observation when the city is in
        /// sight, otherwise a live 偵察 report (bands or exact numbers, P8), otherwise nothing at all. It
        /// never falls back to canonical state, so the panel cannot leak an unseen city's garrison.
        /// </summary>
        void DrawCityPanel(CityState city, GUIStyle body, GUIStyle btn)
        {
            bool mine = city.OwnerSeatId == currentSeat;
            var inSight = obs != null ? obs.FindCity(city.Id) : null;
            var report = obs != null ? obs.FindScoutedCity(city.Id) : null;
            var p = new Rect(10, 50, 420, mine ? 400 : (inSight != null || report != null ? 210 : 120));
            uiRects.Add(p);
            DrawBackdrop(p);
            // the name and owner are themselves information: an unseen city with no report stays unnamed,
            // even if a debug hook selects it by id
            bool knowsCity = mine || inSight != null || report != null;
            GUI.Label(new Rect(22, 58, 400, 24),
                knowsCity ? $"{city.Name} ({OwnerName(city)})" : "Unknown city", body);

            if (mine)
            {
                string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? " (yield halved)" : "";
                GUI.Label(new Rect(22, 86, 400, 116),
                    $"Population {city.Population}\nFood {city.Food}\nGold {city.Gold}\nGarrison {city.Garrison}\n" +
                    $"Morale {city.Morale}{moraleWarning}\n" +
                    $"Reclaimed {city.ReclaimPct}%  Defense {city.Defense}/{BalanceConfig.DefenseMax}  Training {city.Training}/{MyTrainingCap()}",
                    body);

                float buttonLeft = p.x + 12f;
                float buttonTop = p.y + 160f;
                float buttonGap = 12f;
                float buttonWidth = (p.width - 24f - buttonGap) * 0.5f;
                float rightButtonLeft = buttonLeft + buttonWidth + buttonGap;
                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop, buttonWidth, 34), ActionType.LevyTax, city.Id, "Tax", null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop, buttonWidth, 34), ActionType.LightenLabor, city.Id, "Lighten labor",
                    city.Gold >= BalanceConfig.LightenLaborGoldCost ? null : "Not enough gold", 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 38f, buttonWidth, 34), ActionType.Reclaim, city.Id, "Reclaim",
                    city.ReclaimPct >= BalanceConfig.ReclaimPctCap ? "Limit reached"
                    : city.ReclaimedThisSeason ? "Already reclaimed this season" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop + 38f, buttonWidth, 34), ActionType.Recruit, city.Id, "Recruit",
                    city.Gold < BalanceConfig.RecruitGoldCost ? "Not enough gold"
                    : city.Population < BalanceConfig.RecruitPopCost ? "Not enough population" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 76f, buttonWidth, 34), ActionType.Train, city.Id, "Train",
                    city.Training >= MyTrainingCap() ? "Limit reached" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop + 76f, buttonWidth, 34), ActionType.Fortify, city.Id, "Fortify",
                    city.Defense >= BalanceConfig.DefenseMax ? "Limit reached" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 114f, buttonWidth, 34), ActionType.Relief, city.Id, "Relief",
                    city.Food < BalanceConfig.ReliefFoodCost ? "Not enough food" : null, 0, 0, btn));

                if (hint != null) GUI.Label(new Rect(22, 364, 400, 22), hint, body);
            }
            else
            {
                if (inSight != null)
                {
                    string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? " (yield halved)" : "";
                    GUI.Label(new Rect(22, 84, 400, 126),
                        $"{city.Name} — in sight\n" +
                        $"Garrison {city.Garrison}\nMorale {city.Morale}{moraleWarning}\n" +
                        $"Food {city.Food}  Gold {city.Gold}\n" +
                        $"Population {city.Population}\n" +
                        $"Defense {city.Defense}/{BalanceConfig.DefenseMax}  Training {city.Training}",
                        body);
                }
                else if (report != null)
                {
                    string detail = report.Accuracy == "exact"
                        ? $"Garrison {report.Garrison}  Food {report.Food}\nMorale {report.Morale}  Gold {report.Gold}\nPopulation {report.Population}"
                        : $"Troops: {report.GarrisonBand}\nFood: {report.FoodBand}";
                    GUI.Label(new Rect(22, 84, 400, 126),
                        $"{(city.IsNeutral ? "Neutral city" : "Enemy city")} — scout report ({report.Accuracy})\n" +
                        $"{detail}\nReport valid to season {report.UntilSeason}",
                        body);
                }
                else
                {
                    GUI.Label(new Rect(22, 84, 400, 30), "Outside our sight — nothing is known", body);
                }
            }
        }

        /// <summary>
        /// One order button. It always submits a real ActionCommand through SubmitHuman and shows the
        /// action's order cost from BalanceConfig; the engine stays the only authority, so a stale hint
        /// just means the click comes back rejected with a reason.
        /// </summary>
        string OrderButton(Rect r, string type, string targetId, string label, string blockedReason,
            int paramA, int paramB, GUIStyle btn,
            Dictionary<string, string> args = null, string text = null, System.Action onSubmitted = null)
        {
            bool available = blockedReason == null;
            string buttonText = $"{label} {BalanceConfig.CommandPointCost(type)} orders";
            r.width = Mathf.Max(r.width, btn.CalcSize(new GUIContent(buttonText)).x + 16f);
            var prev = GUI.enabled;
            GUI.enabled = available;
            if (GUI.Button(r, buttonText, btn) && SubmitHuman(new ActionCommand
                {
                    Type = type, TargetId = targetId, ParamA = paramA, ParamB = paramB,
                    Args = args, Text = text, Reason = "UI: " + type,
                }))
                onSubmitted?.Invoke();
            GUI.enabled = prev;
            return available ? null : label + "：" + blockedReason;
        }

        static string FirstReason(string current, string candidate) => current ?? candidate;

        void DrawArmyPanel(ArmyState army, GUIStyle body, GUIStyle btn)
        {
            var enemyArmies = AdjacentEnemyArmies(army);
            var enemyCities = AdjacentEnemyCities(army);
            int orders = enemyArmies.Count + enemyCities.Count;

            var p = new Rect(10, 50, 330, orders > 0 ? 236 + orders * 38 : 150);
            uiRects.Add(p);
            DrawBackdrop(p);
            GUI.Label(new Rect(22, 58, 308, 24), $"{army.Name} · Troops {army.Troops}", body);
            GUI.Label(new Rect(22, 86, 308, 66),
                $"Position ({army.X}, {army.Z})\nMorale {army.Morale}\n" +
                (army.MarchedThisSeason ? "Already marched this season" : "Select a hex to march"), body);

            if (orders == 0)
            {
                GUI.Label(new Rect(22, 156, 308, 22), "No enemy armies or cities nearby", body);
                return;
            }

            string hint = null;
            int row = 0;
            float buttonLeft = p.x + 12f;
            float buttonTop = p.y + 110f;
            float buttonWidth = p.width - 24f;
            foreach (var enemy in enemyArmies)
            {
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + row * 38f, buttonWidth, 34), ActionType.AttackArmy,
                    army.Id, $"Attack {enemy.Name} · Troops {enemy.Troops}",
                    army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "Army routed and unable to fight" : null,
                    enemy.X, enemy.Z, btn));
                row++;
            }
            foreach (var city in enemyCities)
            {
                string blocked = army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "Army routed and unable to fight"
                    : army.Troops < BalanceConfig.AssaultTroopRatio * city.Garrison
                        ? $"Requires {BalanceConfig.AssaultTroopRatio}:1 troop ratio (garrison {city.Garrison})" : null;
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + row * 38f, buttonWidth, 34), ActionType.AttackCity,
                    army.Id, "Assault " + city.Name, blocked, city.X, city.Z, btn));
                row++;
            }

            if (hint != null) GUI.Label(new Rect(22, 166 + row * 38, 308, 22), hint, body);
        }

        /// <summary>
        /// Enemy armies the seat may actually act against: candidates come from this seat's observation, so
        /// the panel can never name or count a unit the fog hides (it used to read canonical state, which
        /// also let a player attack an army they could not see). The engine still validates the order.
        /// </summary>
        List<ArmyState> AdjacentEnemyArmies(ArmyState army)
        {
            var list = new List<ArmyState>();
            if (obs == null) return list;
            foreach (var other in obs.Armies)
                if (other.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.AreAdjacent(army.X, army.Z, other.X, other.Z)) list.Add(other);
            return list;
        }

        /// <summary>Enemy cities within reach, from this seat's observation only (see above).</summary>
        List<CityState> AdjacentEnemyCities(ArmyState army)
        {
            var list = new List<CityState>();
            if (obs == null) return list;
            foreach (var city in obs.Cities)
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

            if (GUI.Button(new Rect(p.x + 330, p.y + 50 + textHeight + 14, 140, 40), "Continue", btn)) CloseReport();
        }

        // ------------------------------------------------------------ concept tabs
        // Domestic / Diplomacy / Intelligence / Research are all live now;
        // History renders the real in-memory event log.

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
                statusMsg = city.OwnerSeatId == currentSeat ? "Selected " + city.Name
                    : city.IsNeutral ? "Neutral city — send an army to occupy it" : "Enemy city";
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
                statusMsg = "Selected " + army.Name + " — choose a destination";
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
                case "intel":     DrawIntelPanel(p, body, btn); break;
                case "research":  DrawResearchPanel(p, body, btn); break;
                default:          DrawHistorianPanel(p, body, mono); break;
            }

            // pinned to the panel bottom, so a content-sized panel keeps its button inside
            if (GUI.Button(new Rect(p.x + 320, p.y + height - 56, 160, 40), "Close", btn)) activeTab = "map";
        }

        /// <summary>
        /// Panel height per tab: the design previews keep the full shell; the historian chronicle hugs its
        /// contents (capped) so a two-record log does not open an 80%-empty box.
        /// </summary>
        float TabPanelHeight()
            => activeTab == "history" ? 178f + HistorianViewHeight() : 480f;

        /// <summary>Height of the historian scroll view for the current seat: content-sized, capped.</summary>
        float HistorianViewHeight()
            => Mathf.Min(HistorianViewMaxHeight,
                         (obs != null ? obs.Events.Count : 1) * HistorianLineHeight + 8f);

        string TabTitle()
        {
            switch (activeTab)
            {
                case "domestic":  return "Domestic · Orders";
                case "diplomacy": return "Diplomacy · Orders";
                case "intel":     return "Intel · Orders";
                case "research":  return "Research · Orders";
                default:           return "History · Chronicle";
            }
        }

        void DrawDomesticPanel(Rect p, GUIStyle body)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22), "Domestic actions (design document chapter 11 · 10 total)", body);
            y += 30;

            // Live actions mirror ACTIONS.md §3 and read their numbers from BalanceConfig,
            // so this panel cannot drift from what the engine actually charges.
            string[] live =
            {
                $"Tax      costs {BalanceConfig.CommandPointsPerOrder} order  Gold +{BalanceConfig.TaxLevyAmount}, morale -{BalanceConfig.TaxLevyMoraleCost}",
                $"Lighten labor  costs {BalanceConfig.CommandPointsPerOrder} order  Gold -{BalanceConfig.LightenLaborGoldCost}, morale +{BalanceConfig.LightenLaborMoraleGain}",
                $"Reclaim  costs {BalanceConfig.CommandPointsPerOrder} order  City food yield +{BalanceConfig.ReclaimPctPerUse}% (cap +{BalanceConfig.ReclaimPctCap}%)",
                $"Recruit  costs {BalanceConfig.CommandPointsPerOrder} order  Gold -{BalanceConfig.RecruitGoldCost}, population -{BalanceConfig.RecruitPopCost}, garrison +{BalanceConfig.RecruitTroopGain}",
                $"Train    costs {BalanceConfig.CommandPointsPerOrder} order  Training +1 (cap {MyTrainingCap()})",
                $"Fortify  costs {BalanceConfig.CommandPointsPerOrder} order  Defense +1 (cap {BalanceConfig.DefenseMax})",
                $"Relief   costs {BalanceConfig.CommandPointsPerOrder} order  Food -{BalanceConfig.ReliefFoodCost}, morale +{BalanceConfig.ReliefMoraleGain}",
            };
            foreach (var row in live)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 22), "Available  " + row, body);
                y += 24;
            }

            y += 10;
            string[] planned =
            {
                "Farm fields  costs 1 order  Requires agriculture 3: army supply +30%",
                "Build structure  costs 1 order  Requires gold and an empty slot (market / farm / barracks...)",
                "Move capital  costs 1 order  Requires another city: prestige -5, change capital",
            };
            foreach (var row in planned)
            {
                PlannedLabel(new Rect(p.x + 36, y, 744, 22), "[Planned] " + row, body);
                y += 24;
            }

            y += 10;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                $"Select one of our cities, then issue orders from the city panel on the left; unavailable buttons are grey.\n" +
                $"Select one of our armies to start a field battle against an adjacent enemy (1 order), or assault an enemy city ({BalanceConfig.CommandPointsPerAttackCity} orders, requiring a {BalanceConfig.AssaultTroopRatio}:1 troop ratio).", body);
        }

        void DrawDiplomacyPanel(Rect p, GUIStyle body)
        {
            var me = engine.State.FindFaction(currentSeat);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            const int shownFactions = 3;      // placeholder layout: the rest are summarised

            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"Diplomacy ({me.Name})  Orders {me.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}  Prestige {me.Prestige}" +
                $"  Treaties {(obs != null ? obs.Treaties.Count : 0)}" +
                $"  Pending proposals {(obs != null ? obs.Proposals.Count : 0)}", body);
            y += 26;

            // Envoy message: type here, then press Send message on a faction row to submit it.
            GUI.Label(new Rect(p.x + 36, y, 120, 22), "Envoy message", body);
            envoyText = GUI.TextField(new Rect(p.x + 160, y, 604, 24), envoyText, 120);
            if (envoyConfirmationUntil > Time.unscaledTime)
                GUI.Box(new Rect(p.x + 270, y + 34, 300, 52), "Message sent successfully");
            y += 34;

            // Incoming offers first: they are the only diplomacy that demands an answer.
            var incoming = new List<TreatyProposal>();
            if (obs != null)
                foreach (var proposal in obs.Proposals)
                    if (proposal.ToSeatId == currentSeat) incoming.Add(proposal);

            GUI.Label(new Rect(p.x + 20, y, 760, 22), $"Pending proposals ({incoming.Count})", body);
            y += 24;

            if (incoming.Count == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), "No incoming proposals.", body);
                y += 24;
            }
            foreach (var proposal in incoming)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20),
                    $"{SeatShort(proposal.FromSeatId)} proposes {TreatyLabel(proposal.Type)} for {proposal.DurationSeasons} seasons" +
                    (string.IsNullOrEmpty(proposal.Text) ? "" : $"  Note: {proposal.Text}"), body);
                y += 22;
                OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "Accept",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "accept" } });
                OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "Reject",
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
                    GUI.Label(new Rect(p.x + 36, y, 744, 20), "... (remaining factions omitted)", body);
                    break;
                }

                var relation = obs != null ? obs.Relation(other.SeatId) : null;
                int myTrust = relation != null ? relation.MyTrust : 0;
                int theirTrust = relation != null ? relation.TheirTrust : 0;
                bool atWar = relation != null && relation.AtWar;

                GUI.Label(new Rect(p.x + 20, y, 760, 20),
                    $"{other.Name}  Trust me→them {myTrust} / them→me {theirTrust}  {StatusLine(other.SeatId)}", body);
                y += 22;

                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.DeclareWar,
                    other.SeatId, "Declare war", atWar ? "Already at war" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.Gift,
                    other.SeatId, $"Gift {BalanceConfig.GiftGoldPerTrustUnit}",
                    Treasury() < BalanceConfig.GiftGoldPerTrustUnit ? "Not enough gold" : null,
                    BalanceConfig.GiftGoldPerTrustUnit, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 400, y, 176, 30), ActionType.SendMessage,
                    other.SeatId, "Send message", string.IsNullOrEmpty(envoyText) ? "Enter a message first" : null,
                    0, 0, btn, null, envoyText, () =>
                    {
                        envoyText = "";
                        envoyConfirmationUntil = Time.unscaledTime + 2f;
                    }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 582, y, 182, 30), ActionType.BreakTreaty,
                    other.SeatId, "Break treaty", BreakBlocked(other.SeatId), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", BreakableType(other.SeatId) } }));
                y += 34;

                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Non-aggression pact", ProposalBlocked(other.SeatId, TreatyType.Nap), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 282, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Alliance", ProposalBlocked(other.SeatId, TreatyType.Alliance), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Alliance } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 528, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Propose truce", ProposalBlocked(other.SeatId, TreatyType.Truce), 0, 0, btn,
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

        /// <summary>Peace / war plus every treaty in force (with seasons left) and any pending offer,
        /// read from this seat's observation only.</summary>
        string StatusLine(string seatId)
        {
            var relation = obs != null ? obs.Relation(seatId) : null;
            var sb = new StringBuilder(relation != null && relation.AtWar ? "At war" : "Peace");
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
            {
                var treaty = MyTreaty(type, seatId);
                if (treaty == null) continue;
                int seasonsLeft = treaty.ExpirySeason - engine.State.Season + 1;
                if (seasonsLeft < 1) seasonsLeft = 1;
                sb.Append($"  [{TreatyLabel(type)} {seasonsLeft} seasons left]");
            }
            var proposal = MyProposal(seatId);
            if (proposal != null)
                                sb.Append(" [").Append(proposal.FromSeatId == currentSeat ? "Our proposal pending" : "Your response needed")
                                    .Append(": ").Append(TreatyLabel(proposal.Type)).Append(']');
            return sb.ToString();
        }

        static string TreatyLabel(string type)
        {
            switch (type)
            {
                case TreatyType.Nap: return "Non-aggression pact";
                case TreatyType.Alliance: return "Alliance";
                case TreatyType.Truce: return "Truce";
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
            if (MyTreaty(type, seatId) != null) return "Already active";
            if (MyProposal(seatId) != null) return "Proposal already pending";
            return null;
        }

        string BreakBlocked(string seatId)
            => BreakableType(seatId) == null ? "No treaty to break" : null;

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

        /// <summary>
        /// 諜報 (design §8.2, P8/ADR-015): three missions, each an instant action gated by 諜報 research and
        /// paid from the treasury. Rows submit real commands through the shared OrderButton pipeline, so the
        /// engine stays the only authority; the targets are the foreign cities this seat can actually name
        /// (in sight, or already scouted) and the seat pairs for 挑撥離間.
        /// </summary>
        void DrawIntelPanel(Rect p, GUIStyle body, GUIStyle btn)
        {
            int espionage = obs != null ? obs.ResearchLevel(ResearchLine.Espionage) : 0;
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"Intelligence ({engine.State.FindFaction(currentSeat)?.Name})  Espionage level {espionage}" +
                $"  ·  success {BalanceConfig.SpySuccessChance(espionage) * 100f:F0}% per mission" +
                $"  ·  gold {Treasury()}  Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}", body);
            y += 24;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                "Fog of war: 3 hexes around our cities, 2 around our armies. A failed mission exposes the agent publicly (prestige −8).", body);
            y += 26;

            GUI.Label(new Rect(p.x + 20, y, 760, 20), "Missions", body);
            y += 20;
            string[] missions =
            {
                $"Scout {BalanceConfig.ScoutGoldCost} gold · Espionage {BalanceConfig.ScoutResearchLevel} — target city's garrison and food (banded below Espionage {BalanceConfig.ScoutExactAccuracyLevel})",
                $"Spread rumours {BalanceConfig.RumorGoldCost} gold · Espionage {BalanceConfig.RumorResearchLevel} — target city loses {BalanceConfig.RumorMoraleLoss} morale, deniably",
                $"Sow distrust {BalanceConfig.SowDistrustGoldCost} gold · Espionage {BalanceConfig.SowDistrustResearchLevel} — two other seats lose {BalanceConfig.SowDistrustTrustPenalty} trust in each other",
            };
            foreach (string mission in missions)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), mission, body);
                y += 20;
            }
            y += 8;

            // targets: the foreign cities this seat can see right now
            GUI.Label(new Rect(p.x + 20, y, 760, 20), "Targets (foreign cities in sight)", body);
            y += 22;
            var targets = new List<CityState>();
            if (obs != null)
                foreach (var city in obs.Cities)
                    if (city.OwnerSeatId != currentSeat) targets.Add(city);
            if (targets.Count == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20),
                    "No foreign city is in sight — march an army closer, then send an agent.", body);
                y += 22;
            }
            int shown = 0;
            foreach (var city in targets)
            {
                if (shown == 2) { GUI.Label(new Rect(p.x + 36, y, 744, 20), "... (remaining cities omitted)", body); y += 22; break; }
                shown++;
                var report = obs.FindScoutedCity(city.Id);
                GUI.Label(new Rect(p.x + 36, y, 384, 20),
                    $"{city.Name} ({OwnerName(city)})  garrison {city.Garrison}, morale {city.Morale}" +
                    (report != null ? $", report until season {report.UntilSeason}" : ""), body);
                OrderButton(new Rect(p.x + 424, y, 158, 24), ActionType.Scout, city.Id, "Scout",
                    SpyBlocked(BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost), 0, 0, btn);
                OrderButton(new Rect(p.x + 590, y, 148, 24), ActionType.Rumor, city.Id, "Rumours",
                    city.IsNeutral ? "no faction to undermine"
                                   : SpyBlocked(BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost),
                    0, 0, btn);
                y += 26;
            }

            // live reports on cities nothing of ours can see — and the way to extend them, since a report
            // is the only handle a seat has on a city outside its fog (ADR-015)
            if (obs != null && obs.ScoutedCities.Count > 0)
            {
                GUI.Label(new Rect(p.x + 20, y, 760, 20), "Live reports", body);
                y += 22;
                foreach (var report in obs.ScoutedCities)
                {
                    string detail = report.Accuracy == "exact"
                        ? $"exact · garrison {report.Garrison}, food {report.Food}, morale {report.Morale}"
                        : $"band · troops {report.GarrisonBand}, food {report.FoodBand}";
                    GUI.Label(new Rect(p.x + 36, y, 384, 20),
                        $"{report.Name} ({SeatShort(report.OwnerSeatId)})  {detail}  ·  to season {report.UntilSeason}", body);
                    OrderButton(new Rect(p.x + 424, y, 158, 24), ActionType.Scout, report.CityId, "Scout",
                        SpyBlocked(BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost), 0, 0, btn);
                    OrderButton(new Rect(p.x + 590, y, 148, 24), ActionType.Rumor, report.CityId, "Rumours",
                        string.IsNullOrEmpty(report.OwnerSeatId) ? "no faction to undermine"
                            : SpyBlocked(BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost),
                        0, 0, btn);
                    y += 26;
                }
                y += 4;
            }

            // 挑撥離間 needs two other seats: offer the first pairs and say so when there are more
            GUI.Label(new Rect(p.x + 20, y, 760, 20), "Sow distrust between", body);
            y += 22;
            var others = new List<string>();
            if (obs != null)
                foreach (var view in obs.Factions)
                    if (view.SeatId != currentSeat) others.Add(view.SeatId);
            int pairs = 0;
            for (int i = 0; i < others.Count; i++)
            {
                for (int j = i + 1; j < others.Count; j++)
                {
                    if (pairs == 2) break;
                    pairs++;
                    OrderButton(new Rect(p.x + 36, y, 300, 24), ActionType.SowDistrust, others[i],
                        $"{SeatShort(others[i])} ↔ {SeatShort(others[j])}",
                        SpyBlocked(BalanceConfig.SowDistrustResearchLevel, BalanceConfig.SowDistrustGoldCost),
                        0, 0, btn, new Dictionary<string, string> { { "target_b", others[j] } });
                    y += 28;
                }
                if (pairs == 2) break;
            }
            if (pairs == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), "No other seat is known yet.", body);
                y += 20;
            }
            else if (others.Count > 2)
            {
                GUI.Label(new Rect(p.x + 348, y - 26, 420, 20), "... (remaining pairs omitted)", body);
            }
        }

        /// <summary>Why a mission cannot be ordered right now (mirrors the engine's SpyGate), or null.</summary>
        string SpyBlocked(int requiredLevel, int goldCost)
        {
            int level = obs != null ? obs.ResearchLevel(ResearchLine.Espionage) : 0;
            if (level < requiredLevel) return $"needs Espionage {requiredLevel}";
            if (Treasury() < goldCost) return $"needs {goldCost} gold";
            if (obs.CommandPoints < BalanceConfig.CommandPointsPerSpyMission) return "no orders left";
            return null;
        }

        /// <summary>
        /// 研究 (design §9, 四條線各 5 級): the four lines are live. Each row shows the seat's level, the
        /// next level's price and what that level does; the button submits a real `research` command
        /// (1 號令 + the level's points, TBD) through the same pipeline as every other order.
        /// Every number on screen comes from BalanceConfig and the seat's own observation — research is
        /// private (ADR-014), so another seat's tech tree is never rendered.
        /// </summary>
        void DrawResearchPanel(Rect p, GUIStyle body, GUIStyle btn)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"Research points {obs.ResearchPoints}  (+{BalanceConfig.ResearchIncome(obs.ResearchLevel(ResearchLine.Governance))}/season)" +
                $"  Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
                $"  ·  level costs {string.Join(" / ", System.Array.ConvertAll(BalanceConfig.ResearchLevelCosts, v => v.ToString()))}", body);
            y += 30;

            for (int i = 0; i < ResearchLine.All.Length; i++)
            {
                string line = ResearchLine.All[i];
                int level = obs.ResearchLevel(line);
                int cost = BalanceConfig.ResearchLevelCost(level + 1);
                bool maxed = level >= BalanceConfig.ResearchMaxLevel;
                string blocked = maxed ? "Line complete"
                               : obs.ResearchPoints < cost ? $"Needs {cost} points"
                               : obs.CommandPoints < BalanceConfig.CommandPointCost(ActionType.Research) ? "No orders left"
                               : null;

                GUI.Label(new Rect(p.x + 20, y, 560, 22),
                    $"{ResearchLine.Label(line)}  {ResearchPips(level)}  {level}/{BalanceConfig.ResearchMaxLevel}", body);
                OrderButton(new Rect(p.x + 596, y, 184, 30), ActionType.Research, null, "Research",
                    blocked, 0, 0, btn, new Dictionary<string, string> { { "line", line } });

                GUI.Label(new Rect(p.x + 36, y + 26, 744, 20),
                    "Now:  " + (level > 0 ? ResearchEffect(line, level) : "—"), body);
                GUI.Label(new Rect(p.x + 36, y + 44, 744, 20),
                    maxed ? "All five levels researched."
                          : $"Next: level {level + 1} for {cost} points — {ResearchEffect(line, level + 1)}", body);
                y += 70;
            }

            y += 6;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                "Points arrive at every season rollover. Harvest, tax, combat and training-cap effects apply as soon as they resolve;\n" +
                "律法 (level 3) and the income bonus settle at the next rollover. 學堂 the building (+3/season) and Intelligence 3-5 effects are deferred.", body);
        }

        /// <summary>
        /// The current seat's 訓練 cap: 兵法 L1 練兵 raises it (design §9). Read from the seat's own
        /// observation, like everything else the panel shows.
        /// </summary>
        int MyTrainingCap()
            => BalanceConfig.TrainingCap(obs != null ? obs.ResearchLevel(ResearchLine.Military) : 0);

        static string ResearchPips(int level)
        {
            var sb = new StringBuilder(BalanceConfig.ResearchMaxLevel);
            for (int i = 1; i <= BalanceConfig.ResearchMaxLevel; i++) sb.Append(i <= level ? '\u25cf' : '\u25cb');
            return sb.ToString();
        }

        /// <summary>
        /// Design §9's effect for one level, in one line. Levels whose systems do not exist yet say so
        /// ("deferred") instead of promising an effect the engine does not implement (ADR-014).
        /// </summary>
        static string ResearchEffect(string line, int level)
        {
            switch (line)
            {
                case ResearchLine.Agri:
                    switch (level)
                    {
                        case 1: return $"Reclaim — city harvest +{(int)(BalanceConfig.ResearchAgriFoodBonus * 100)}%";
                        case 2: return "Water works — drought damage halved (deferred: no 天時 events)";
                        case 3: return "Military farms — garrison self-supply 30% (deferred: 屯田 D9)";
                        case 4: return "State granaries — food cap +50% (deferred: no food cap)";
                        case 5: return $"Shennong — all harvests +{(int)(BalanceConfig.ResearchAgriL5FoodBonus * 100)}%";
                    }
                    break;
                case ResearchLine.Military:
                    switch (level)
                    {
                        case 1: return $"Drill — training cap +{BalanceConfig.ResearchMilTrainingCapBonus} (now {BalanceConfig.TrainingCap(1)})";
                        case 2: return "Cavalry unlock (deferred: no unit types)";
                        case 3: return "Siege engines unlock (deferred: no 器械)";
                        case 4: return $"Art of war — combat power ×{1f + BalanceConfig.ResearchMilCombatBonus:F2}";
                        case 5: return "Command — general coefficient +0.2 (deferred: no generals)";
                    }
                    break;
                case ResearchLine.Espionage:
                    switch (level)
                    {
                        case 1: return "Spies — unlocks Scout and Spread rumours missions (intel tab)";
                        case 2: return "Counter-intelligence — unlocks Sow distrust missions (the +10% exposure bonus is deferred)";
                        case 3: return "Forged reports (deferred: needs per-seat poisoned intel)";
                        case 4: return "Spy network — +2 agent slots (deferred: no spy entities)";
                        case 5: return "No shadows — capture chance +25% (deferred: no counter-intelligence)";
                    }
                    break;
                default:
                    switch (level)
                    {
                        case 1: return $"Taxation — city tax +{(int)(BalanceConfig.ResearchGovTaxBonus * 100)}%";
                        case 2: return $"Recruitment — morale +{BalanceConfig.ResearchGovMoralePerSeason}/season in every city";
                        case 3: return $"Law — revolt chance ×{BalanceConfig.ResearchGovRevoltMult} (from next season)";
                        case 4: return $"Academy — research +{(int)(BalanceConfig.ResearchGovIncomeBonus * 100)}% ({BalanceConfig.ResearchIncome(4)} points/season)";
                        case 5: return $"Nine ranks — prestige +{BalanceConfig.ResearchGovPrestigeL5} once";
                    }
                    break;
            }
            return "no effect yet";
        }

        void DrawHistorianPanel(Rect p, GUIStyle body, GUIStyle mono)
        {
            // the chronicle is the seat's filtered view of the log (CONTROLLER_PROTOCOL §5)
            var records = obs != null ? obs.Events : new List<EventRecord>();
            GUI.Label(new Rect(p.x + 20, p.y + 52, 760, 22),
                $"Campaign chronicle (visible to {engine.State.FindFaction(currentSeat)?.Name}) — {records.Count} entries" +
                $" (season {engine.State.Season} · {PhaseLabel(engine.State.Phase)})", body);

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
                $"Historian: {CountSeasonEvents(engine.State.Season)} events recorded this season.", body);
        }

        string HistorianLine(EventRecord rec)
        {
            var sb = new StringBuilder();
            sb.Append(rec.Seq.ToString().PadLeft(2));
            sb.Append(" Season ").Append(rec.Season).Append(" ");
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
            if (phase == "player_orders") return "Orders";
            if (phase == "resolution") return "Resolution";
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
            if (city.IsNeutral) return "Neutral";
            var f = engine.State.FindFaction(city.OwnerSeatId);
            return f != null ? f.Name : "?";
        }
    }
}
