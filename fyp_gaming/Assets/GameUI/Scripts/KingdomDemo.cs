// KingdomDemo — the Unity adapter for the LuanShi strategic layer.
// This file holds the class contract, serialized scene wiring and the shared UI state; the rest of
// the class is split by responsibility into the partial files below (all compiled together):
//   .Visuals.cs   scene markers, tinting, fog overlays
//   .Visuals.Banners.cs  army banner spawn, scale, stagger and idle bob
//   .Input.cs     map clicks, selection, march targeting, debug hooks
//   .TurnFlow.cs  SubmitHuman pipeline, seat rotation, season rollover
//   .Panels.cs    OnGUI shell and shared helpers
//   .PanelLayout.cs     side-dock and selection-panel placement
//   .SelectionPanels.cs city/army order panels
//   .Presentation.cs    observation-authorized labels, seams, framing, text fitting
//   .AgentPanel.cs      docked LLM agent details
//   .AgentStatus.cs     agent status helpers and play-mode lifecycle
//   .Report.cs    court report, handover/victory screens, formatting helpers
//   .Tabs.cs      Domestic / Diplomacy / Intel / Research / History tab panels

using System.Collections.Generic;
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
    public partial class KingdomDemo : MonoBehaviour
    {
        [Header("Scene wiring (object names in LuanShi_Demo)")]
        public string weiCityObj = "City_Wei";
        public string shuCityObj = "City_Shu";
        public string southCityObj = "City_South";
        public string watchTowerObj = "Watch Tower";
        public string weiArmyObj = "Wei_Army1";
        public string shuArmyObj = "Shu_Army1";
        public Texture2D watchTowerImage;
        public Texture2D armyBannerImage;
        [FormerlySerializedAs("unusedArmyObjs")]   // renamed after the smoke test: they are used
        public string[] extraArmyObjs = { "Wei_Army2", "Wei_Army3", "Wei_Army4",
                                          "Shu_Army2", "Shu_Army3", "Shu_Army4" };
        public int convertedArmySortingOrder;

        [Header("Game setup")]
        public int randomSeed = 12345;

        [Header("Fog of war")]
        [Tooltip("Height of the procedural, non-repeating fog shroud above each hex node.")]
        public float fogPhotoHeight = 0.08f;

        [Header("Seats (data-driven; the engine supports up to 8, AGENTS.md #6)")]
        [Tooltip("Seat ids played by people at this keyboard. Every seat not listed here or in llmSeats is scripted. " +
                 "Set two ids here for hotseat pass-and-play.")]
        public string[] humanSeats = { "seat-0" };
        [Tooltip("Seat ids driven by the live LLM agent. The demo roster is Wei human, Shu LLM.")]
        public string[] llmSeats = { "seat-1" };

        [Header("LLM vs LLM spectator pilot (optional)")]
        [Tooltip("Second demo mode: every seat is driven by the same shared LLM runner and a human only " +
                 "watches. Overrides humanSeats/llmSeats. Off by default; the single-LLM demo is unchanged.")]
        public bool spectatorMode;
        [Tooltip("Seasons the spectator pilot plays before completing. The panel can extend it by 3.")]
        public int spectatorSeasons = 3;

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

        // selection & UI state — detached views from the current seat's observation (LS-003)
        private CityView selCity;
        private ArmyView selArmy;
        private string recruitUnitType = UnitTypes.Infantry;   // type picker in the city panel (ADR-019)
        private bool garrisonConversionOpen;
        private string garrisonConversionCityId;
        private int garrisonConversionAmount;
        private string garrisonArmyId;
        private int garrisonArmyAmount;
        private bool garrisonArmyConversionOpen;
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
            // A provider request may outlive editor focus; keep the per-frame runner tick alive.
            Application.runInBackground = true;

            var gc = FindAnyObjectByType<TBTK.GameControl>();
            if (gc != null) gc.enabled = false;

            // TBTK's tactical-layer UI (HUD, perk menu, ability bars) is not part of the
            // kingdom demo; hide it so only the strategic IMGUI shows.
            foreach (var mb in FindObjectsByType<MonoBehaviour>())
                if (mb.GetType().Namespace == "TBTK" && mb.GetType().Name.StartsWith("UI"))
                    mb.gameObject.SetActive(false);

            try
            {
                InitGame();
                InitializeAgent();
                InitializeSpectator();
                ready = true;
                StartAgentTicker();
                StartFirstPlannedTurn();
            }
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
            engine.CombatDebugLogger = message => Debug.Log(message);
            engine.State.Map = map;
            InitializeFogOverlays(nodes, w, h);
            BuildGridSeamUnderlay();

            factionColors[SeatB] = new Color(0.8509804f, 0.3490196f, 0.1843137f, 1f); // Shu #D95947
            factionColors[SeatA] = new Color(0.3490196f, 0.5019608f, 0.8509804f, 1f); // Wei #5980D9

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
            TurnFlowStep firstStep = TurnFlowPlanner.First(engine.State);
            currentSeat = firstStep.IsSeat ? firstStep.SeatId : SeatRotation.FirstSeat(engine.State);
            RefreshObservation();
            string humans = string.Join(",", SeatRotation.HumanSeats(engine.State).ToArray());
            statusMsg = $"Season 1 begins — {currentSeat} rules; select a city or army to issue orders";

            // Do not open a gate here. Start() dispatches TurnFlowPlanner.First only after
            // InitializeAgent/InitializeSpectator has built the controller for that first chair; this
            // also keeps the old init-time GridIndicator access out of the startup path. In spectator
            // mode there is no human chair; SpectatorSession opens every seat's gate itself.
            activeTurnToken = 0;
            ReleaseControlLock();
            ApplyInitialCameraFraming();

            string roster = string.Join(", ", engine.State.Factions
                .ConvertAll(f => $"{f.SeatId}={f.Controller}").ToArray());
            Debug.Log($"[KingdomDemo] roster: {roster} (human seats: {humans})");
            Debug.Log($"[KingdomDemo] game started, season 1, human seats: {humans}");
        }

        /// <summary>humanSeats and llmSeats decide the controllers; every other seat is scripted
        /// (AGENTS.md #6: never hardcode the player count — up to 8 seats).
        /// LLM membership wins over humanSeats so an old hotseat roster cannot claim the LLM seat.</summary>
        string ControlOf(string seatId)
        {
            if (spectatorMode) return ControllerType.Llm;   // both seats are model-driven
            if (llmSeats != null)
                foreach (var id in llmSeats)
                    if (id == seatId) return ControllerType.Llm;
            if (humanSeats != null)
                foreach (var id in humanSeats)
                    if (id == seatId) return ControllerType.Human;
            return ControllerType.ScriptedAi;
        }
    }
}
