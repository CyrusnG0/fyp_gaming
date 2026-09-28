# CURRENT_STATE.md

> Day-to-day truth of where the project stands. **Update this file at the end of every work session.**
> Last updated: 2026-09-26

## Status summary

**Phase 1 — the professor demo loop WORKS.** One full season plays end-to-end on `LuanShi_Demo`: human orders (屯田/徵兵/march) through the authoritative `GameEngine.Submit()` pipeline, scripted-AI seat through the *same* pipeline, season resolution, 朝報 report panel, 史官 line, append-only JSONL event log. All orders — UI clicks and AI alike — are the same `ActionCommand` JSON (the FYP thesis artifact, shown live in the on-screen feed). Engine architecture: ADR-009.

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
- **Unity adapter `KingdomDemo.cs`** (`Assets/GameUI/Scripts/`, on `KingdomDemo` object in scene): disables TBTK `GameControl` in Awake (grid still inits; tactical flow never starts), deactivates all `TBTK.UI*` objects, builds engine from TBTK grid, IMGUI demo UI — top bar (season/CP/人口/糧/金), city panel (屯田/徵兵), army panel + click-to-march, 結束季節 button, JSON command feed, 朝報 report modal. Clicks via `Event.current` in OnGUI (work regardless of Active Input Handling). Log dumped to `persistentDataPath/luanshi_log.jsonl` each season.
- **Verified 2026-09-26** (headless + play mode, screenshots `luanshi_demo_s1/s2/report.png` at repo root): farm/recruit/march accepted; double-farm, CP-exhaustion, enemy-army-steal rejected; human march (2,6)→(5,6) cost 3; AI seat farm+recruit via same pipeline; resolution report numbers match event log; season 2 begins with CP reset and march flags cleared; city capture verified headless.

## In progress

- (nothing actively in progress)

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

## TBTK quick facts (from inspection)

- `GridManager.GetNode(x,z)` / `GetNode(point,null)`; `Node`: `walkable`, `cost`, cube coords `x,y,z`, `idxX/idxZ`, `GetPos()`. `AStar.SearchWalkableNode(origin, dest, bypassUnit, bypassObs, returnNearest)`.
- `GameControl.Awake` runs even when the component is disabled (grid init); its `Start` coroutine (battle flow) does not → disabling GameControl = grid without tactics.
- Turn modes: UnitPerTurn / FactionPerTurn. Proper end-turn API: `GameControl.EndTurn()`.
- Runtime DB: `Assets/TBTK/Resources/DB_TBTK/`. Full doc: `Assets/TBTK/TBTK_3_Documentation.pdf`.

## Next actions (priority order)

1. **User: restart Unity editor** (applies activeInputHandler=Both → TBTK camera pan/zoom works), then play `LuanShi_Demo` hands-on: click 魏都 → 屯田/徵兵, click red banner → march, 結束季節 → 朝報.
2. **Commit pending work**: inner repo `fyp_gaming/` holds uncommitted `Assets/GameEngine/`, `Assets/GameUI/`, scene + ProjectSettings changes; outer repo has doc updates (conventional commits).
3. **Professor demo rehearsal**: capture neutral 南城 by marching onto it (capture event + recolor), show JSON feed = the same commands an LLM will send.
4. **Resolve open decisions** — `docs/OPEN_QUESTIONS.md` (MVP seat count, faction roster, map dimensions, LLM provider/budget, turn-order rule).
5. Next engine slices (post-demo): `IPlayerController` interface extraction (Human/Scripted/LLM stubs per CONTROLLER_PROTOCOL), more actions （徵稅/開墾/練兵…), basic combat for enemy cities, fog-of-war filtered observations, state snapshots, JSON balance config, LLM controller adapter (observation→prompt→JSON→validate→retry/fallback + LLMRun logging).

## Blockers

- None technical. Waiting on team decisions in `docs/OPEN_QUESTIONS.md`.

## LuanShi_Demo scene anatomy (as of 2026-09-26)

- Scene: `Assets/GameUI/Scenes/LuanShi_Demo.unity`, built from TBTK New-Scene (Hex-Grid) template + scripted edits.
- Grid: 26×17 hex, nodeSize 1 (429 real nodes — ragged columns, see issue 4). Grid object must NOT have `CombineMeshes`.
- Terrain: river `x = 12 + round(sin(z*0.7))` unwalkable; 14 mountain nodes unwalkable + obstacle prefabs (renderers off, mountain sprite children); 20 forest nodes cost=2 + ForestSprite billboards.
- Cities: `City_Wei` (3,8), `City_Shu` (22,8), `City_South` (14,15) under `GridItems`; pagoda billboards, tinted by owner (魏 blue / 蜀 green / neutral grey) at runtime by KingdomDemo. Engine ids `city-wei/shu/south`; 南城 neutral (capture target).
- Armies: `Wei_Army1` (魏軍, human field army, 2000 troops) and `Shu_Army1` (蜀軍, scripted) active; the other 6 army objects deactivated at runtime. Banner transforms moved directly by adapter on accepted march.
- `KingdomDemo` GameObject with `LuanShi.KingdomDemo` component (scene wiring fields = object names; `randomSeed=12345`).
- GameControl present but disabled at runtime (KingdomDemo.Awake); useGlobalSetting=false, fogOfWar/deployment off. All `TBTK.UI*` objects deactivated at runtime.
- Camera: `CameraPivot` (holds CameraControl) at position **(-1,0,0)**, rot (45,0,0); Main Camera child local z=-22, fov 30; limits rotate 25–65, zoom 8–45, pan ±16/±14. (x=-1 frames both capitals + Wei banner; wider margins need z=-26 backoff, not done.)
- Art: `Assets/GameUI/Art/` (6 PNGs + `_make_sprites.py`, run with `/c/Python313/python`). `BillboardSprite.cs` locks 45° pitch in LateUpdate.

## MCP / visual-verification workflow (proven)

- **Screenshots of IMGUI**: `Camera.Render`→RenderTexture does NOT capture OnGUI. Use `ScreenCapture.CaptureScreenshotAsTexture()` + `ImageConversion.EncodeToPNG` + `File.WriteAllBytes`. It returns a *stale frame* unless you first (separate RunCommand) `EditorApplication.QueuePlayerLoopUpdate()` + `UnityEditorInternal.InternalEditorUtility.RepaintAllViews()`, `sleep 2`, then capture. `ScreenCapture.CaptureScreenshot(path)` flushes minutes late — avoid.
- **RunCommand quirks**: any console warning → call reports failure; judge by `[Log]` lines. Transient "Unity not detected" after domain reload → retry. **`System.Reflection` is sandbox-blocked** in RunCommand — use `MonoBehaviour.SendMessage` (works for ≤1-parameter methods) instead. Avoid `AssetDatabase.DeleteAsset`; `GetInstanceID()` doesn't compile.
- **GetConsoleLogs**: the filter token for `Debug.Log` is `Info`, not `Log`.
- **Play-mode discipline**: Enter/ExitPlaymode async — `sleep 10` after entering, `sleep 3` after exiting; never SaveScene in the same RunCommand as ExitPlaymode; play-mode edits are lost on exit.
- **Subagent note**: subagents don't inherit the parent's MCP tool schema — a coder subagent drove the relay directly (`.kimi-code/mcp.json` → `relay_win.exe --mcp`) with a minimal stdio MCP client in %TEMP%. Works fine.

## Notes for the next agent

- Read `AGENTS.md` first, then this file, then `docs/DECISIONS.md` and `docs/CONTROLLER_PROTOCOL.md`.
- The formal report docx lags `docs/`; on conflict, **v1.1 docx + docs/ win**.
- Game design detail (numbers, tables, 41 actions) in `doc/LuanShi-Game-Design-v1.md`; MVP scope per report §14: 4 seats, 2 maps, 15–20 seasons.
- The demo's JSON feed + `luanshi_log.jsonl` are the thesis artifact: identical schema for human clicks and (future) LLM output. Keep it that way — no side channels.
- Unity editor restart still pending for `activeInputHandler=Both` (TBTK legacy-input loops broken until then; KingdomDemo UI unaffected).
