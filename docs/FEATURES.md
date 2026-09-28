# FEATURES.md

> Feature checklist with honest status. Update as work lands.
> Status: `❌ not started` · `🟡 in progress` · `✅ done` · `⏸ deferred (stretch)`

## Foundation

| Feature | Status | Notes |
|---|---|---|
| Unity project (6000.6.2f1, URP 2D) | ✅ | `fyp_gaming/`, template only |
| TBTK3 import & demo verification | ✅ | imported 2026-09-25; Demo_Classic AI-vs-AI ran 12 rounds to GAME OVER; materials converted to URP |
| Strategic demo map (LuanShi_Demo scene) | ✅ | 26×17 hex, river/mountain/forest terrain, 3 cities, banner armies, 3/4 camera — see `docs/CURRENT_STATE.md` |
| GameEngine pure-C# assembly scaffold | ✅ | `LuanShi.GameEngine` asmdef, `noEngineReferences`, verified headless 2026-09-26 (ADR-009) |
| Canonical state model (Game/Seat/Nation/City/Army/General) | 🟡 | MVP subset: GameState/FactionState/CityState/ArmyState/MapData; no General/Nation yet |
| ActionCommand DTOs + JSON (de)serialization | ✅ | flat DTO + hand-rolled JSON writer; JSON *parser* deferred to LLM adapter |
| Validation pipeline (permissions, prerequisites, cost, CP) | ✅ | `GameEngine.Submit()`: phase, CP, ownership, resources, once-per-season, Dijkstra path cost |
| Append-only event log + JSON snapshots | 🟡 | append-only JSONL event log done (protocol §5 shape); state snapshots not yet |
| Balance config (no magic numbers) | 🟡 | `BalanceConfig` constants class, values TBD; JSON/YAML config file still future |

## Turn structure (Option B)

| Feature | Status | Notes |
|---|---|---|
| Season loop: 朝報 → 朝議 → individual turns → 史官 | 🟡 | demo loop: orders → resolution → 朝報 panel + 史官 line; 朝議 and rotating individual turns not yet |
| Rotating starting player | ❌ | MVP default; make rule configurable |
| Command points (5/season/seat) | 🟡 | implemented; demo uses 3/season (TBD, `BalanceConfig.CommandPointsPerSeason`) |
| Immediate resolution after validated action | ✅ | actions execute on `Submit()`; economy resolves at season end |
| Per-turn timer (60–90s) + timeout pass | ❌ | |

## MVP gameplay (report §14)

| Feature | Status | Notes |
|---|---|---|
| Map loading from data (map def + tiles) | ❌ | map = data, not code |
| Map 1: open plains | ❌ | |
| Map 2: rivers & passes | ❌ | |
| 4 factions (config-driven; architecture supports 8) | ❌ | |
| Cities: population/民心/治安/城防/garrison/buildings | ❌ | |
| Domestic actions (subset): 徵稅 輕徭 開墾 募兵 練兵 修城 建築 賑災 | 🟡 | demo has 屯田 (farm) + 徵兵 (recruit) through the real pipeline; rest to add |
| Armies: movement (A*, terrain cost), supply, morale | 🟡 | movement w/ terrain cost done (engine Dijkstra); supply/morale not yet |
| Basic combat: field battle + siege + city capture | 🟡 | neutral-city capture by marching done; combat not yet |
| Food/gold economy with seasonal modifiers | ❌ | autumn ×2.0 etc. |
| Basic diplomacy: public messages, private letters, NAP, declare war | ❌ | |
| Trust (−100..+100, bidirectional) + prestige | ❌ | all changes emit events with reasons |
| Basic fog of war + scouts | ❌ | filtered observations per seat |
| Victory: 一統天下 + score at season limit | ❌ | configurable per map |

## Controllers (FYP core — see docs/CONTROLLER_PROTOCOL.md)

| Feature | Status | Notes |
|---|---|---|
| IPlayerController interface (Human/LLM/Scripted) | ❌ | protocol specified; interface itself not yet extracted from KingdomDemo |
| HumanController (UI-driven) | 🟡 | `KingdomDemo` IMGUI produces real ActionCommands; not yet behind IPlayerController |
| ScriptedController (rule-based AI, deterministic baseline) | ✅ | `ScriptedController.PlanTurn()` in engine; plays via same Submit() pipeline |
| LLMController (observation → prompt → JSON actions → validate → retry/fallback) | ❌ | |
| Mid-game seat switching + HandoverBundle | ❌ | the thesis feature |
| LLM run log (prompt version, tokens, latency, validation outcome) | ❌ | |

## UI (Unity)

| Feature | Status | Notes |
|---|---|---|
| Map view: zoom/pan, terrain, cities, armies, fog mask | 🟡 | LuanShi_Demo: terrain/cities/army-banners/camera done (2D fake-3D, ADR-008); fog mask not yet |
| Nation/city/army panels + action menu (legal actions only) | 🟡 | IMGUI city/army panels with 屯田/徵兵/march orders (KingdomDemo); legal-action filtering minimal |
| 朝報 / 朝議 / 史官 screens | 🟡 | 朝報 season-report panel done; 史官 tab shows the real event log (scrollable); 朝議 not yet |
| Concept tab bar （內政/外交/諜報/研究 placeholders) | 🟡 | 2026-09-28: display-only previews of the design doc in KingdomDemo; no mechanics behind them |
| Diplomacy panel (grouped relations, treaty deadlines) | ❌ | 8 seats → up to 56 directed trust values; must filter/group |
| Notifications + turn progress | ❌ | |
| Replay viewer | ❌ | |

## Stretch goals (⏸ by default)

8-seat full games · full treaty set (割地/朝貢/聯姻/稱臣…) · espionage missions beyond 偵察 (偽造軍報, 收買, 暗殺, 秘密反用) · research trees (4 lines × 5) · general personalities & betrayal · 稱帝/結義/禪讓-託孤 actions · multiple simultaneous LLM seats · procedural maps · networked (non-hot-seat) multiplayer · research analytics dashboard

## Evaluation (Phase 7)

Scripted-AI mass simulations (balance, deadlock, game length) · LLM metrics (legal-action rate, retry/fallback rate, diplomacy acceptance, betrayal frequency, survival) · human vs LLM vs scripted comparison on same seeds · replay demo for thesis
