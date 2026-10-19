# CURRENT_STATE.md

> Day-to-day truth of where the project stands. **Update this file at the end of every work session.**
> Last updated: 2026-10-20

## Status summary

**2026-10-20: P14 unit types (兵種) — four types, research gates, per-type recruit/upkeep/movement, 相剋, siege waiver. Headless harness 297/297; Unity gate PENDING.**

- **New `UnitTypes.cs`** (engine, pure C#): frozen keys `infantry/archer/cavalry/siege` (步兵/弓兵/騎兵/
  攻城器械); exactly one type per army (`ArmyState.UnitType`) and per city garrison
  (`CityState.GarrisonType`). 兵法 2 unlocks cavalry, 兵法 3 siege — their previously deferred research
  effects are now real.
- **募兵** takes `args.unit_type` (default infantry); per-type gold 100/150/300/500 per 1,000 plus 1,000
  population; a non-empty garrison must match its type, an empty one adopts the recruited type. Per-type
  upkeep 0.1/0.1/0.2/0.05 per troop (garrison **and** field-army supply) and movement 6/6/8/3 (冬 ×0.7)
  through `BalanceConfig.MarchBudget(season, unitType)` — the UI highlight and the validator share it.
- **Combat:** 相剋 ×1.3/×0.8 (步>弓, 騎>步, 弓>騎), 攻城器械 ×0.5 in field battles, 弓兵 garrison ×1.25 in
  sieges, and a 攻城器械 attacker waives the 3:1 assault. Merges/garrison transfers require matching
  types; the observation JSON exposes `unit_type`/`garrison_type`; battle/garrison events carry
  `unit_type`; the UI shows 兵種 in the army panel, selection popup/status, city panel (garrison type +
  recruit picker) and 史官/report lines.
- **ScriptedController** recruits its garrison's type (infantry by default; cavalry every other season
  once 兵法 2 + a comfortable treasury), uses the type-aware march budget and mirrors the siege waiver.
- **Verification:** `/tmp/luanshi-p6` **297 passed / 0 failed** (+48 P14 assertions; all 249 previous
  assertions still pass); `/tmp/luanshi-ui-check` **0 warnings / 0 errors** (new `UnitTypes.cs` included).
  **Unity editor compile/play-mode remains PENDING**; the gate now also covers the recruit type picker,
  相剋 battles, archer city defence and the siege waiver.
- **Design gaps flagged for a decision:** a **type-mismatched defensive siege merge** (PR #3 auto-merges any
  same-owner defending army; P14 keeps one type per garrison by letting an empty garrison adopt the
  defender's type and otherwise keeping the city's type — a mixed-type defensive stack needs a ruling), and
  design §5.3's 「攻城器械 剋 城防」 has no numeric value, so only the 3:1 waiver is implemented.

**2026-10-19 (later): order-cost labels pluralized; modularity rule added; Track A roadmap confirmed by the team.**

- Every order-cost label now renders through one helper pair in `KingdomDemo.Panels.cs` (`OrderCost` /
  `OrderCostSuffix`): **"(0 order)" / "(1 order)" / "(2 orders)"** — singular for 0 and 1, plural for 2+.
  All labels read `BalanceConfig.CommandPointCost(type)` (including the two garrison buttons, previously
  hardcoded "1 order"), so UI text can never drift from the validator. The domestic tab's cost rows and help
  text use the same helper. `249/0` headless; UI dotnet build 0/0.
- **AGENTS.md gained a modularity rule**: no file grows past ~400 lines by appending; new responsibilities
  get their own file (engine feature → new class/partial in `GameEngine/`; UI feature → new
  `KingdomDemo.<Area>.cs` partial; serialized fields stay in `KingdomDemo.cs`; new partials get fresh `.meta`
  GUIDs).
- The team confirmed the ADR-017 complexity roadmap (P9 seeded worlds → P10 weather/events → P11 uncertain
  information → P12 multiple victory routes → P13 adaptive opponents) as the agreed direction for the next
  demo cycle, alongside Track B (the LLM-facing interface).
- **Design doc completed to v1.1: new §5.4 兵種的生產與訓練 + ADR-019.** The design named the four unit
  types but never said how they're produced or fed; the team locked three decisions: **one type per
  army/garrison** (same-type-only merge and garrison transfer), **research-only unlock** (步/弓 free,
  兵法 2 → 騎， 兵法 3 → 器械； no new buildings), **per-type upkeep** (騎 600/1000/季 vs 步/弓 300).
  §5.4 also reconciles the 6-vs-8 movement numbers (6 = 步/弓 baseline), prices recruiting per type (all
  待決), lets 器械 waive the §4.3 3:1 assault requirement, and gives 弓兵 garrisons the 守城 +25%.
  `recruit` will gain `args.unit_type` (schema v2 already carries `args`); ACTIONS.md D4 and FEATURES.md
  carry the pointer. **Design only — implementation is future engine work (P14, after P9).**

**2026-10-19: PR #3 (garrison transfers, watchtower ownership, defensive siege) merged and hardened; the UI adapter split into seven partial files; ADR-014's research privacy rule enforced. Headless harness 249/249; Unity gate PENDING (now covers PR #2 + PR #3 + P6-P8 + the split).**

Teammate **PR #3** (`5416365`, head `b78b68e`) added **garrison ↔ army transfers** (`convert_garrison` /
`garrison_army`, 1 號令 each, `param_a` = troop count), **watchtower ownership** when an army marches onto a
tower, and a **defensive-siege merge** (a same-owner army standing on the city counts toward the effective
garrison and joins the garrison before the rounds). Independent review found four balance constants changed
outside the PR's stated scope plus integration defects; the scoped fixes are `9c6e452` (**ADR-018**):
the pre-PR economy restored (`CityBaseFoodYield` 200, `StartFood` 500, `SpringFoodYieldMult` 0.7 — spring is
design-LOCKED — and `FogWatchTowerRange` 5); once-per-season movement preserved through full/partial
garrison round-trips with the conservative `CityState.GarrisonMarchedThisSeason` flag (reset at rollover,
not in the observation JSON); the generic `action` records for the two garrison commands made **owner-only**
(their `param_a` is an exact troop count; their domain events stay `owner_and_observers`); the scripted AI's
`attack_city` guard now uses the validator's effective garrison; and garrison validation is atomic with the
transfer event logged before a full-transfer `army_destroyed`. The UI adapter was then split by
responsibility (`6b91dd0`) into seven `partial class KingdomDemo` files — contract/state/bootstrap +
`Visuals`, `Input`, `TurnFlow`, `Panels`, `Report`, `Tabs` — members moved verbatim, all serialized fields
kept in `KingdomDemo.cs` in their original order, and its `.meta` GUID unchanged so the scene reference
still resolves.

Verification: headless harness **249 passed / 0 failed** (all P6-P8 assertions plus the focused PR #3
regression tests and the research generic-action privacy checks; determinism replays included); `KingdomDemo`
dotnet-builds against Unity 6000.6.2f1's managed assemblies with **0 warnings / 0 errors**, all seven
partials included. **Unity editor compile and
play-mode verification remain PENDING** — the editor has not been available; no claim is made that the
split or the PR #3 UI renders correctly in-engine. Unresolved design semantics are **OPEN_QUESTIONS 24-27**
(morale laundering / mixed-garrison movement provenance; siege restore/rout/destruction; watchtower capture
consequences; watchtower visibility).

**2026-10-12: PR #2 merged; P6–P8 make the game push back — revolt, desertion, seasons, victory, research, espionage lite. All headless-verified (175 assertions); Unity play-mode gate PENDING (see Known issues).**

Teammate **PR #2** (`055f1a3`, reviewed + merged): English UI, bigger auto-sized buttons, photo-based fog
overlays on out-of-vision cells, **watchtower** (new `WatchTowerState`; capture by marching onto a neutral
tower; vision radius 5 via `BalanceConfig.FogWatchTowerRange`; `Observation.WatchTowers`), city/army
selection popups, enemy troop counts on attack. Engine invariants verified intact in review (all commands
via `Submit`, no UI state mutation). One exception documented: teammate patched
`TBTK/Scripts/GridIndicator.cs` in place (hex indicator Y=0.02).

Then three engine phases by deepseek subagent, orchestrated + reviewed by main agent (all pure C#,
all numbers in `BalanceConfig` with design citations, all deterministic/seeded):

- **`e619f7b` P6 pressure & consequences (ADR-013)**: 民變 revolt （民心<25, chance (25−民心)×2%/season,
  zeroes the city's yields that tick, garrison −10%, 民心 +15 venting); army supply (armies eat from the
  city they stand on, then the faction pool proportionally; shortage → **desertion** −10% troops, −15 士氣，
  winter −15%); seasonal modifiers (design §3 table: 春×0.7 夏×1.0 秋×2.0 冬×0.3 harvest, winter march
  budget ×0.7); **victory 一統天下** (one seat owns every city incl. neutrals → `WinnerSeatId`, public
  `game_over`, later `Submit()` rejected; IMGUI banner).
- **`8870dc1` P7 research (ADR-014)**: 4 lines （農政/兵法/諜報/吏治， schema keys agri/military/espionage/
  governance) × 5 levels, costs 20/40/70/110/160 points, income 15/season (×1.25 at 吏治4); `research`
  action 1 CP. Implemented effects: 農政1 harvest ×1.1, 農政5 ×1.25; 兵法1 training cap 5→6, 兵法4 戰力
  ×1.15; 吏治1 tax ×1.1, 吏治2 民心+5/季， 吏治3 revolt ×0.5 (the P6 hook), 吏治4 income ×1.25, 吏治5 威望+10
  once. Other levels purchasable but deferred-effect (labelled in UI). **研究 tab is LIVE.**
- **`e22eed4` P8 espionage lite (ADR-015)**: no spy entities — instant missions, 1 CP + gold, gated by
  諜報 level, seeded success 70%+5%/level. `scout` (200金， L1: city report in observation for 2 seasons,
  banded 少/中/多 at L1-2, exact at L3+), `rumor` (500金， L1: 民心−10, hidden from target on success),
  `sow_distrust` (800金， L2: trust −10 both directions). Failure → public `spy_exposed`, 威望−8, victim
  信任−10, gold spent. Covert actions log owner-only. **諜報 tab is LIVE.**

Verification: headless harness **175 passed / 0 failed** (determinism replays included); KingdomDemo
dotnet-builds against Unity 6000.6.2f1 assemblies 0/0. **Not yet done: Unity editor compile + play-mode
smoke of PR #2 + P6-P8** — the MCP bridge wedged on 2026-10-11 when the merge changed files under the live
editor (see Known issues); the editor needs a restart, then run the gate (compile, play, watchtower capture
at (6,2), fog overlays, 研究/諜報 tabs rendered, screenshot `pr2_smoke.png`).

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
- **PR #3 + integration fixes + UI split (2026-10-19)**: teammate PR #3 (`5416365`) merged garrison
  transfers (`convert_garrison`/`garrison_army`), watchtower ownership on march and defensive-siege merging;
  `9c6e452` restored the four out-of-scope balance constants, closed the march-reset exploit with
  `CityState.GarrisonMarchedThisSeason`, made the garrison `action` records owner-only, fixed the scripted
  siege guard and hardened validation/event order (ADR-018); `6b91dd0` split `KingdomDemo` into seven
  responsibility-based partial files without behavioural change. Headless: 249/0; UI dotnet build 0/0.
  **Unity editor gate still pending.**
- **P14 unit types (2026-10-20)**: new `UnitTypes.cs`; four frozen keys (`infantry/archer/cavalry/siege`),
  one per army/garrison; `args.unit_type` on 募兵 with per-type gold and research gates (兵法 2/3);
  per-type upkeep and movement; 相剋 ×1.3/×0.8, 器械 field ×0.5, 弓兵 garrison ×1.25, 器械 3:1 waiver;
  same-type transfers; `unit_type`/`garrison_type` in the observation and events; recruit type picker and
  type labels in the UI. Headless: 297/0; UI dotnet build 0/0. **Unity editor gate still pending.**

## In progress

- **Unity verification gate PENDING for PR #2 + PR #3 + P6-P8 + P14 + the partial split.** Everything since
  `d4c38d0` is headless-verified only (297 assertions + Unity-assembly dotnet builds). The editor's MCP
  bridge wedged on 2026-10-11 when the PR #2 merge changed sources under the live editor (compile requested,
  never completed; assemblies in `Library/ScriptAssemblies/` predate the merge and every later commit).
  **Fix: restart the Unity editor** (scene is not dirty), then re-run the gate with the Unity-driving
  subagent: compile check (all seven `KingdomDemo*.cs` partials + the new `UnitTypes.cs`, assembly
  timestamps must postdate the P14 changes; the scene still references the unchanged `KingdomDemo.cs` GUID),
  play mode, and check **PR #3 garrison convert/enter in both directions** (formation, merge, full and
  partial), **P14 unit types** (recruit picker + research gates, cavalry/siege movement, 相剋 battle
  reports, archer city defence, siege waiver), **defensive siege** (same-owner army merged; effective
  garrison gate; held vs captured result), watchtower capture at (6,2) and tower tint, fog overlays, the
  live 研究/諜報 tabs, hotseat handover, victory banner; screenshot `docs/screenshots/p14_smoke.png`.
  Lesson recorded: merge git work while Unity is CLOSED, or restart it right after.
- P5 (LLM-facing controller) not started — see Next actions, Track B.

## Known integration issues (TBTK ↔ this project)

0. **MCP bridge wedged after under-editor merge (2026-10-11)**: merging PR #2 while the editor was open
   triggered a compile the bridge never finished; every relay call queued behind it (Editor.log silent
   12+ min, process "Responding"). Editor restart required. Also: PR #2's `fog.png.meta` ships an ancient
   TextureImporter (v1) — Unity auto-upgrades on reimport; commit the result.
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

1. **Unity gate for PR #2 + PR #3 + P6-P8 + P14 + the partial split** (see In progress) — restart the editor,
   compile, play-mode smoke, screenshot.
2. **Track A — meaningful decision depth (ADR-017 north star).** P9 seeded world variability (map/setup as
   data + a seeded parameterized generator; the seed stays an explicit experiment parameter — identical
   seeds for human-vs-LLM, different seeds for robustness) → P10 天時 weather and choice-bearing events →
   P11 uncertain information / counter-intelligence → P12 multiple victory routes → P13 adaptive LLM
   opponents. Smallest next step: P9.
3. **Track B — the LLM-facing interface (P5 core).** Extract `IPlayerController`, then build the LLM
   adapter as: rules summary + **filtered observation** (`Observation.ToJson()`) + **private memory**
   (own research/tech state, its own past actions and reasoning) + legal-action list → **strict JSON
   output** → validate/retry/fallback → `LLMRun` logging; then the HandoverBundle (`Observation` + pending
   proposals + filtered log). Two blockers of its own: **the engine's JSON support is writer-only**
   (`MiniJson` has no parser — a small hand-rolled parser is the first engine task), and the
   provider/model/budget decision is still open (OPEN_QUESTIONS 4). After that: state snapshots (ADR-005)
   and a JSON balance file.
4. **Professor demo rehearsal** on the smoke-tested build: grow 魏都 (recruit/train with the economy
   ticking), capture neutral 南城, deploy a garrison into an army and re-enter it, fight a field battle
   (戰報 + `round_log`), run a defensive siege, capture the watchtower, buy a research level, run a spy
   mission, propose → accept a treaty, hand the chair to a second human (陛下請坐) to show per-seat fog,
   and use the JSON feed for the thesis argument that UI clicks and LLM output are the same commands.
5. **Team: answer `docs/OPEN_QUESTIONS.md`** — items 16-23 (宣戰 vs treaties, 遣使 trust, validator-oracle
   leak, 民心 25 vs 30, estimation levels, 諜報 gates, exposure trust, known-cities memory) **and the new
   24-27** (morale laundering / mixed-garrison provenance, siege restore/rout/destruction, watchtower
   capture consequences, watchtower visibility), then the blocking Phase 0/1 list (seat count, faction
   roster, map size, LLM provider/budget, turn order).

## Blockers

- **Unity editor restart needed** (In progress #1) — only the user can do this on their machine.
- LLM provider/model/budget decision (OPEN_QUESTIONS 4) blocks Track B's adapter tail.
- Design decisions pending for garrison/siege/tower semantics (OPEN_QUESTIONS 24-27); the implementation
  is conservative in the meantime and flags each gap rather than guessing.

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
- **Process rule (standing, earned over three smoke rounds):** a change to the `KingdomDemo*.cs` partials
  must pass a **compile check in the editor before the next full smoke round**. A standalone compile of the
  adapter cannot see Unity-namespace, arity or serialization-attribute errors — CS7036 and the
  `UnityEngine.Serialization` namespace both slipped through it. Cheap headless pre-checks that do work:
  `arity_audit.py` (call-site arity vs declaration, including code inside interpolated strings),
  `external_audit.py` (adapter→engine signatures), `namespace_guard.py` (attribute and
  fully-qualified-name namespaces), plus a grep for fully-qualified `UnityEngine.*` usages after any
  adapter edit. Current harnesses: `/tmp/luanshi-p6` (engine, 297 assertions) and `/tmp/luanshi-ui-check`
  (engine + all seven UI partials against Unity assemblies).
- **`fyp_gaming/Assets/_Recovery.meta` is untracked user state** (left by the editor after a crash) — leave
  it untracked and do not add it to git.
- Unity editor restart still pending for `activeInputHandler=Both` (TBTK legacy-input loops broken until then; KingdomDemo UI unaffected).
