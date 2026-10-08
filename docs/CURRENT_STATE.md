# CURRENT_STATE.md

> Day-to-day truth of where the project stands. **Update this file at the end of every work session.**
> Last updated: 2026-10-08

## Status summary

**2026-10-08: ADR-010 locked; the locked MVP tier is implemented (P0–P4) and verified in Unity.** Five
sessions took the demo from 3 actions to the full locked tier, all through the one `GameEngine.Submit()`
pipeline: `06eece2` **P0** state fields + schema v2 (`args`/`text`, CP 5, `farm`→`reclaim`, 民心<30 →
yields −50%); `e25594b` **P1** domestic D1–D7; `f1d7ff9` **P2** military M2/M3 (field battle, assault-only
攻城, derived 偷襲/背盟 penalties — `Trust`/`Prestige`/`WarWith` finally have writers); `f831c98` **P3**
diplomacy P1–P6 (pending proposals, ratify/lapse at rollover, treaty expiry, working 外交 tab); `1d6f91d`
**P4** hotseat pass-and-play (`SeatRotation` + per-seat UI + handover blocker) and engine-side fog
observations (`GameEngine.Observe()`, ADR-011/ADR-012). The engine stays pure C# and is re-verified
headless every session (P1 57, P2 64, P3 92, P4 53 assertions); **the adapter has now passed three Unity
play-mode smoke rounds** — domestic orders, march, 朝報 and the event log; then combat (`battle_field`
with `round_log`, the 3:1 siege gate enforced), hotseat handover (陛下請坐 blocker, per-seat fog map) and
opaque panels; then the frame-1 synchronous init fix. Screenshots: `docs/screenshots/smoke*.png`.

**Phase 1 — the professor demo loop WORKS.** One full season plays end-to-end on `LuanShi_Demo`: human orders (屯田/徵兵/march) through the authoritative `GameEngine.Submit()` pipeline, scripted-AI seat through the *same* pipeline, season resolution, 朝報 report panel, 史官 line, append-only JSONL event log. All orders — UI clicks and AI alike — are the same `ActionCommand` JSON (the FYP thesis artifact, shown live in the on-screen feed). Engine architecture: ADR-009.

**2026-09-28: concept tab UI + repo flattened.** KingdomDemo now has a tab bar （地圖/內政/外交/諜報/研究/史官）: four display-only design previews （待開發） and a **real scrollable 史官 event-log panel**. The repo is now ONE repo — the nested `fyp_gaming/.git` is gone, the Unity project lives as plain files under `fyp_gaming/`, pushed to `github.com/CyrusnG0/fyp_gaming`. Teammate quickstart is in `README.md`. **Graphics API pinned to D3D11** (ProjectSettings) — D3D12 crashed the editor reliably on first modal render (see Known issues #10).

## Done

- Game design document v1.0 (`doc/LuanShi-Game-Design-v1.md`) — full design: seasons, resources, combat, diplomacy/trust, espionage, research, victory conditions.
- Design & implementation report v1.1 (`doc/LuanShi-Design-Implementation-Report-v1.1.docx`) — Unity-native architecture (ADR-001); v1.0 docx superseded.
- Repo docs discipline: `AGENTS.md`, `docs/` (this file, FEATURES, DECISIONS, CONTROLLER_PROTOCOL, OPEN_QUESTIONS).
- Controller switch protocol specified (`docs/CONTROLLER_PROTOCOL.md`) — the FYP research core.
- Unity MCP configured and verified working — tools register only in sessions created after workspace trust.
- **TBTK 3.1.5 imported**, URP-converted (75 materials), `Demo_Classic` AI-vs-AI verified. **TBTK's role: scaffolding for grid/pathfinding/camera only (ADR-007).**
- **`LuanShi_Demo.unity` strategic map** (2026-09-25): 26×17 hex grid, river (unwalkable), mountains (unwalkable), forests (cost 2), 3 cities, banner armies, 3/4 camera. Art = procedural 2D old-China placeholders (ADR-008, `Assets/GameUI/Art/_make_sprites.py`).
- **Kingdom engine, pure C# (2026-09-26, ADR-009)** in `Assets/GameEngine/` (`LuanShi.GameEngine` asmdef, no UnityEngine refs):
  - `Model.cs` — GameState/FactionState/CityState/ArmyState/MapData (own map copy: walkable/cost/hex-adjacency; Dijkstra `FindPath`/`PathCost`).
  - `ActionCommand.cs` — flat command DTO + `MiniJson` writer; `ActionType.Farm/Recruit/March`.
  - `GameEngine.cs` — `Submit()` validation pipeline (phase, CP budget, ownership, resources, once-per-season flags, path-cost limit), `BeginSeason()`, `EndSeason()` resolution (taxes, harvest, consumption, growth with seeded variance, starvation), neutral-city capture on march.
  - `EventLog.cs` — append-only JSONL, CONTROLLER_PROTOCOL §5 record shape.
  - `ScriptedController.cs` — deterministic rule-based seat that only proposes commands (farm → recruit → march to nearest neutral city).
  - `BalanceConfig.cs` — all tunables, marked TBD (3 CP/season, move cost 6, etc.).
- **Unity adapter `KingdomDemo.cs`** (`Assets/GameUI/Scripts/`, on `KingdomDemo` object in scene): disables TBTK `GameControl` synchronously in frame-1 `Start` (the grid still inits from `GameControl.Awake`; the tactical flow and its `TBTK.OnGameStart()` never run), deactivates all `TBTK.UI*` objects, builds the engine from the TBTK grid, and draws the IMGUI demo UI — top bar for the current seat (season/CP/威望/人口/糧/金), city panel (D1-D7, CP cost on every label, greyed with a reason), army panel (攻擊/攻城), 內政 + 外交 tabs, per-seat 史官, selected-seat 朝報, hotseat handover blocker, JSON command feed. Clicks via `Event.current` in OnGUI (work regardless of Active Input Handling). Log dumped to `persistentDataPath/luanshi_log.jsonl` each season.
- **Verified 2026-09-26** (headless + play mode, screenshots in `docs/screenshots/`): farm/recruit/march accepted; double-farm, CP-exhaustion, enemy-army-steal rejected; human march (2,6)→(5,6) cost 3; AI seat farm+recruit via same pipeline; resolution report numbers match event log; season 2 begins with CP reset and march flags cleared; city capture verified headless.
- **Concept tab bar (2026-09-28)**: 地圖/內政/外交/諜報/研究/史官 tabs in `KingdomDemo.OnGUI`. 內政/外交/諜報/研究 are greyed-out display-only previews of the design doc (no mechanics); **史官 renders the real event log** (scrollable, `engine.Log.Records`). Tab state is modal over map clicks. Public `ShowTab(key)` allows scripted demos/screenshots. Verified in play mode, screenshots `luanshi_ui_*.png` in `docs/screenshots/`.
- **Repo flattened + pushed (2026-09-28)**: single repo `github.com/CyrusnG0/fyp_gaming`; Unity project as plain files under `fyp_gaming/` (nested git repo removed; its 1-commit history backed up as a bundle in `%TEMP%`). Root `.gitignore` (agent config, `*.unitypackage`, OS junk); `fyp_gaming/.gitignore` = Unity template + `.slnx` + `.kilo/` + `Assets/_Recovery/`; `.gitattributes` rewritten minimal **no-LFS** (binaries committed as-is). 918 files, ~90 MB. Teammate: clone → open `fyp_gaming/` in Unity Hub (6000.6.2f1) → play `LuanShi_Demo`.
- **Teammate PR #1 merged (2026-10-06)**: "armies can move now" — GameControl disable deferred one frame (Start coroutine); clicks raycast the TBTK node layer via `Input.mousePosition`; reachable-hex highlight via `TBTK.GridIndicator` (engine `PathCost` budget); all 8 army objects registered (4 per side); friendly CP-exhaustion message. `VersionControlSettings.asset` reverted to Visible Meta Files (was set to Unity Version Control/Plastic — wrong for git).
- **`docs/ACTIONS.md` drafted (2026-10-06)** then **locked (2026-10-08, ADR-010)**: canonical action spec — schema v2 (`args`+`text`), every designed action inventoried, MVP tier = D1-D7, M1-M3, P1-P6, fog observations, control switch. **Every row in that tier is now implemented**; §3–§5 carry ✅ statuses plus the command-shape contract (§5).
- **P0 — engine state + schema v2 (`06eece2`)**: `CityState` 民心/城防/訓練/開墾%, `FactionState` 威望, `DiplomacyState` (directional `Trust`, `Treaties`, `WarWith`), `ActionCommand.Args`/`Text`, `MiniJson` dict/array writers, CP 5, `farm`→`reclaim` (+15%, cap 60%), 民心<30 → yields −50%.
- **P1 — domestic D1-D7 (`e25594b`)**: 徵稅/輕徭/開墾/募兵/練兵/修城/賑災 with the spec's costs, caps and event names; city panel surfaces 民心/開墾/城防/訓練 and every order button (CP cost on the label, greyed with a reason).
- **P2 — military M2/M3 (`f1d7ff9`)**: `ArmyState.Morale`; 戰力 = 兵力 × 訓練 × 士氣 (城防 for garrisons); ≤5 rounds with seeded variance, 潰散 (extra 30% + retreat, or annihilation), 0 troops removes the army; assault-only 攻城 (3:1, single resolution) captures cities (駐軍 0, 民心 0, +威望); **derived 偷襲/背盟** penalties price undeclared attacks from diplomacy state; army panel gains 攻擊/攻城 buttons, 朝報 gains 戰報 lines.
- **P3 — diplomacy P1-P6 (`f831c98`)**: 遣使 (text-only), 提出條約/接受/拒絕/反提案 via pending `TreatyProposal`s that ratify or lapse at rollover, `gift` (信任/威望), `declare_war`, `break_treaty`; treaty expiry at rollover; 停戰 ends war; the 外交 tab is live (relations, treaties with terms left, incoming inbox, 遣使 field, CP costs, greyed reasons).
- **P4 — hotseat + fog observations (`1d6f91d`)**: data-driven `humanSeats` + `SeatRotation` + a full-screen handover blocker between human seats (single-human mode behaves exactly as before); `GameEngine.Observe(seatId)`/`InSight`/`SeesEvent` (ADR-011) and a per-seat UI — map hiding, filtered 史官/朝報/diplomacy; `Observation.ToJson()` is the payload the LLM adapter/HandoverBundle will carry.
- **Smoke round 1 — adapter fixes (`8dc0cc7`)**: the 史官 footer's stale `CountSeasonEvents()` call (CS7036 — a standalone compile cannot see arity errors inside methods whose signature uses unresolved Unity types) now passes `engine.State.Season`; two public test hooks (`DebugSelect("city:<id>"|"army:<id>")`, `DebugOrder("<type>|<target_id>[|x,z]")`) let the harness drive selection and orders through the same `SubmitHuman` path as the buttons.
- **Smoke round 1 — panel & marker art (`1ebffb7`)**: every panel and modal draws an opaque charcoal plate (`DrawBackdrop`) before its `GUI.Box` — the skin's box is see-through, so map sprites used to collide with panel text; the 朝報 hugs its lines and the 史官 scroll view its content; fallback markers (scene objects that don't exist yet) are tinted flat discs using a cached runtime material per faction colour instead of grey cylinders; `unusedArmyObjs` → `extraArmyObjs` (`FormerlySerializedAs`).
- **Smoke round 1 — namespace fix (`af024e6`)**: `FormerlySerializedAs` lives in `UnityEngine.Serialization`, not `UnityEngine` — the same blind spot as the CS7036 (every UnityEngine type resolves as CS0234 in the standalone check).
- **Smoke round 2 — PASSED**: combat (`battle_field` with `round_log`, the 3:1 siege gate enforced), hotseat handover (陛下請坐 blocker, per-seat fog map) and opaque panels, screenshots `docs/screenshots/smoke2_*.png`.
- **Smoke round 3 — gate PASSED (`12af419`)**: initialisation is frame-1 synchronous (the old `yield return null` meant an unfocused editor window never initialised the demo at all); scrollbars appear only on overflow; the 史官 panel is content-sized and the report panel hugs its lines.
- **Docs sync (`6950a78`)**: FEATURES/ACTIONS/CURRENT_STATE aligned with P0-P4, ADR-011/ADR-012 appended, OPEN_QUESTIONS 16-20 added.

## In progress

- (nothing actively in progress — P0-P4 are implemented and smoke-verified in play mode; P5 is next, see below)

## Known integration issues (TBTK ↔ this project)

1. ~~URP vs Built-in shaders~~ — resolved 2026-09-25 (URP conversion).
2. **Input handling**: `activeInputHandler=Both` set on disk; **Unity editor restart still pending** — TBTK's own legacy-input Update loops (camera pan/zoom, UIInput) throw until then. KingdomDemo's own clicks are unaffected (OnGUI events).
3. TBTK prefabs: harmless old-serialization warnings.
4. **TBTK hex grids are ragged**: even columns have `dimensionZ-1` nodes, so `GridManager.GetNode(evenX, 16)` returns null (429 real nodes of 442 cells; `GetNodeCount()` overstates). `KingdomDemo.InitGame` treats missing cells as unwalkable with empty adjacency — handle nulls in any future grid code.
5. **`GridManager.Init` calls `CombineMeshes.Combine()`** if present on the Grid object — merges node meshes, kills terrain colors. Removed from LuanShi_Demo.
6. Template junk: CollectibleManager runtime crates disabled (`generateInGame=false, spawnChance=0, activeItemLimit=0`).
7. Sprite draw order: billboard props need positive `sortingOrder` (`500 - z*10`).
8. **TBTK tactical UI overlaps our demo UI** if not deactivated — `KingdomDemo.Start` deactivates all `TBTK.UI*` GameObjects (PERK MENU/HUD/ability bars). Residual `Selectable:OnEnable` "Parameter 'Selected' does not exist" warnings remain (harmless, but they still make `Unity_RunCommand` report failure).
9. **TBTK's leftover `Unit` components** on army objects are unused by the kingdom layer (banner transforms are moved directly by the adapter). Wei_Army2..4/Shu_Army2..4 are deactivated at runtime.
10. **D3D12 editor crash — RESOLVED by pinning D3D11** (2026-09-28): the editor died twice with `d3d12: Unrecoverable GPU device error` (E_INVALIDARG closing a command list, memory nowhere near limits, NVIDIA overlay `nvspcap64.dll` injected) on the **first render of a CJK-heavy IMGUI modal during a forced repaint** — likely a font-atlas texture upload surfacing a latent D3D12 driver fault (RTX 4060 Ti, driver 32.0.16.1074). Fix: `ProjectSettings.asset` now pins `m_BuildTargetGraphicsAPIs` to Direct3D11 (`m_Automatic: 0`) — verified stable on D3D11, all modals render. If D3D12 is ever needed again: disable the GeForce Experience overlay / update the driver first. Crash dumps: `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes\Crash_2026-09-28_*`.

## TBTK quick facts (from inspection)

- `GridManager.GetNode(x,z)` / `GetNode(point,null)`; `Node`: `walkable`, `cost`, cube coords `x,y,z`, `idxX/idxZ`, `GetPos()`. `AStar.SearchWalkableNode(origin, dest, bypassUnit, bypassObs, returnNearest)`.
- `GameControl.Awake` runs even when the component is disabled (grid init); its `Start` coroutine (battle flow) does not → disabling GameControl = grid without tactics.
- Turn modes: UnitPerTurn / FactionPerTurn. Proper end-turn API: `GameControl.EndTurn()`.
- Runtime DB: `Assets/TBTK/Resources/DB_TBTK/`. Full doc: `Assets/TBTK/TBTK_3_Documentation.pdf`.

## Next actions (priority order)

1. **P5 — controllers** (the FYP core): extract `IPlayerController` (Human/Scripted/LLM,
   CONTROLLER_PROTOCOL §2) out of KingdomDemo, then build the LLM adapter (observation → prompt → JSON →
   validate → retry/fallback + `LLMRun` logging) and the HandoverBundle from `Observation` + pending
   proposals. Two blockers of its own: **the engine's JSON support is writer-only** (`MiniJson` has no
   parser — a small hand-rolled one is the first engine task), and the provider/model/budget decision is
   still open (OPEN_QUESTIONS 4). After that: state snapshots and a JSON balance file.
2. **Professor demo rehearsal** on the smoke-tested build: capture neutral 南城 by marching onto it,
   propose → accept a treaty and show it come into force next season, fight a field battle (戰報 +
   `round_log`), hand the chair to a second human (陛下請坐) to show per-seat fog, and use the JSON feed to
   make the thesis argument that UI clicks and LLM output are the same commands.
3. **Team: answer `docs/OPEN_QUESTIONS.md`** — items 16-20 raised during P2-P4 (宣戰 vs existing treaties,
   遣使 trust, the validator-oracle leak, 民心 25 vs 30, estimation levels), then the blocking Phase 0/1
   list (seat count, faction roster, map size, LLM provider/budget, turn order).

## Blockers

- None technical. Waiting on team decisions in `docs/OPEN_QUESTIONS.md`.

## LuanShi_Demo scene anatomy (as of 2026-09-26)

- Scene: `Assets/GameUI/Scenes/LuanShi_Demo.unity`, built from TBTK New-Scene (Hex-Grid) template + scripted edits.
- Grid: 26×17 hex, nodeSize 1 (429 real nodes — ragged columns, see issue 4). Grid object must NOT have `CombineMeshes`.
- Terrain: river `x = 12 + round(sin(z*0.7))` unwalkable; 14 mountain nodes unwalkable + obstacle prefabs (renderers off, mountain sprite children); 20 forest nodes cost=2 + ForestSprite billboards.
- Cities: `City_Wei` (3,8), `City_Shu` (22,8), `City_South` (14,15) under `GridItems`; pagoda billboards, tinted by owner (魏 blue / 蜀 green / neutral grey) at runtime by KingdomDemo. Engine ids `city-wei/shu/south`; 南城 neutral (capture target).
- Armies: `Wei_Army1` (魏軍, human field army, 2000 troops) and `Shu_Army1` (蜀軍, scripted) active; the other 6 army objects deactivated at runtime. Banner transforms moved directly by adapter on accepted march.
- `KingdomDemo` GameObject with `LuanShi.KingdomDemo` component (scene wiring fields = object names; `randomSeed=12345`).
- GameControl present but disabled at runtime (KingdomDemo's frame-1 `Start`; `[DefaultExecutionOrder(-100)]` puts it ahead of `GameControl.Start`); useGlobalSetting=false, fogOfWar/deployment off. All `TBTK.UI*` objects deactivated at runtime.
- Camera: `CameraPivot` (holds CameraControl) at position **(-1,0,0)**, rot (45,0,0); Main Camera child local z=-22, fov 30; limits rotate 25–65, zoom 8–45, pan ±16/±14. (x=-1 frames both capitals + Wei banner; wider margins need z=-26 backoff, not done.)
- Art: `Assets/GameUI/Art/` (6 PNGs + `_make_sprites.py`, run with `/c/Python313/python`). `BillboardSprite.cs` locks 45° pitch in LateUpdate.

## MCP / visual-verification workflow (proven)

- **Screenshots of IMGUI**: backend-dependent (2026-09-28 update). On **D3D11** (current pinned API): use `ScreenCapture.CaptureScreenshot(path)` + `QueuePlayerLoopUpdate()` + `RepaintAllViews()`; the file lands within ~4 s (poll, repainting each round). `CaptureScreenshotAsTexture()` FAILS on D3D11 ("called before end of frame" — RunCommand never is; `WaitForEndOfFrame` doesn't fire while the editor is unfocused). On D3D12 the opposite held (in-memory API worked, file API flushed late). Since D3D11 is pinned, use `CaptureScreenshot(path)`.
- **RunCommand quirks**: any console warning → call reports failure; judge by `[Log]` lines. Transient "Unity not detected" after domain reload → retry. **`System.Reflection` is sandbox-blocked** in RunCommand — use `MonoBehaviour.SendMessage` (works for ≤1-parameter methods) instead. Avoid `AssetDatabase.DeleteAsset`; `GetInstanceID()` doesn't compile.
- **GetConsoleLogs**: the filter token for `Debug.Log` is `Info`, not `Log`.
- **Play-mode discipline**: Enter/ExitPlaymode async — `sleep 10` after entering, `sleep 3` after exiting; never SaveScene in the same RunCommand as ExitPlaymode; play-mode edits are lost on exit.
- **Subagent note**: subagents don't inherit the parent's MCP tool schema — a coder subagent drove the relay directly (`.kimi-code/mcp.json` → `relay_win.exe --mcp`) with a minimal stdio MCP client in %TEMP%. Works fine.

## Notes for the next agent

- Read `AGENTS.md` first, then this file, then `docs/DECISIONS.md` and `docs/CONTROLLER_PROTOCOL.md`.
- The formal report docx lags `docs/`; on conflict, **v1.1 docx + docs/ win**.
- Game design detail (numbers, tables, 41 actions) in `doc/LuanShi-Game-Design-v1.md`; MVP scope per report §14: 4 seats, 2 maps, 15–20 seasons.
- The demo's JSON feed + `luanshi_log.jsonl` are the thesis artifact: identical schema for human clicks and (future) LLM output. Keep it that way — no side channels.
- **Process rule (standing, earned over three smoke rounds):** a change to `KingdomDemo.cs` must pass a **compile check in the editor before the next full smoke round**. A standalone compile of the adapter cannot see Unity-namespace, arity or serialization-attribute errors — CS7036 and the `UnityEngine.Serialization` namespace both slipped through it. Cheap headless pre-checks that do work: `arity_audit.py` (call-site arity vs declaration, including code inside interpolated strings), `external_audit.py` (adapter→engine signatures), `namespace_guard.py` (attribute and fully-qualified-name namespaces), plus a grep for fully-qualified `UnityEngine.*` usages after any adapter edit.
- Unity editor restart still pending for `activeInputHandler=Both` (TBTK legacy-input loops broken until then; KingdomDemo UI unaffected).
