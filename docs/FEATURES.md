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
| Canonical state model (Game/Seat/Nation/City/Army/General) | 🟡 | MVP: `GameState`/`FactionState`/`CityState`/`ArmyState`/`MapData` + `DiplomacyState` (Trust/Treaties/WarWith/Proposals) + `Observation`/`SeatRotation`; no General/Nation yet |
| ActionCommand DTOs + JSON (de)serialization | ✅ | schema v2 (`args` dict + `text`, backward compatible with v1) + hand-rolled JSON writer incl. dicts/arrays; JSON *parser* deferred to LLM adapter |
| Validation pipeline (permissions, prerequisites, cost, CP) | ✅ | `GameEngine.Submit()`: phase, CP, ownership, resources, once-per-season, Dijkstra path cost |
| Append-only event log + JSON snapshots | 🟡 | append-only JSONL event log done (protocol §5 shape); state snapshots not yet |
| Balance config (no magic numbers) | 🟡 | `BalanceConfig` constants class, values TBD; JSON/YAML config file still future |

## Turn structure (Option B)

| Feature | Status | Notes |
|---|---|---|
| Season loop: 朝報 → 朝議 → individual turns → 史官 | 🟡 | implemented: per-seat 朝報 (filtered to the reading seat) → individual turns in rotation → 史官 tab; **朝議** (the shared council phase) not yet |
| Hotseat pass-and-play (one screen per seat) | ✅ | `humanSeats` config (data-driven, engine supports 8) + `SeatRotation` + full-screen handover blocker; single-human mode unchanged |
| Rotating starting player | ❌ | MVP default; the rotation is fixed faction order (`SeatRotation.PlayOrder`) |
| Command points (5/season/seat) | ✅ | `BalanceConfig.CommandPointsPerSeason = 5`; per-action costs in `BalanceConfig.CommandPointCost` (M3 攻城 = 2, 遣使/提案/回覆 = 0) |
| Immediate resolution after validated action | ✅ | actions execute on `Submit()`; economy resolves at season end |
| Per-turn timer (60–90s) + timeout pass | ❌ | |

## MVP gameplay (report §14)

| Feature | Status | Notes |
|---|---|---|
| Map loading from data (map def + tiles) | ❌ | map = data, not code |
| Map 1: open plains | ❌ | |
| Map 2: rivers & passes | ❌ | |
| 4 factions (config-driven; architecture supports 8) | 🟡 | seat control is data-driven (`humanSeats`, `FactionState.Controller`); the demo scene ships 2 factions (魏/蜀 + a neutral city) |
| Cities: population/民心/治安/城防/garrison/buildings | 🟡 | population/民心/城防/駐軍/訓練/開墾 live; 治安 and buildings missing |
| Domestic actions (subset): 徵稅 輕徭 開墾 募兵 練兵 修城 建築 賑災 | ✅ | D1-D7 through the real pipeline; 屯田 (D9) / 建築 (D8) / 遷都 (D10) still phase 2 |
| Armies: movement (A*, terrain cost), supply, morale | ✅ | movement + 士氣 (P2) + **supply as a food ration (P6)**: each army eats `Troops × 0.1` from its faction's food (standing city first, then proportionally) or deserts −10% troops/−15 士氣 (ADR-013); the 補給 0–100 field, 劫糧/斷補 actions and cavalry remain ⏸ |
| Basic combat: field battle + siege + city capture | ✅ | M2 field battle (≤5 rounds, rout/annihilation) + M3 assault-only 攻城 (3:1, single resolution, capture); multi-season 圍城 / 器械 / 兵種 still ❌ |
| Food/gold economy with seasonal modifiers | ✅ | P6: 春 ×0.7 / 夏 ×1.0 / **秋 ×2.0** / 冬 ×0.3 harvest applied to every city, 冬 march budget 4, 冬 desertion +5% (ADR-013); 天時 per-region weather (豐收/旱災/蝗災…, design §10.1) still ❌ |
| Pressure & consequences: 民變 revolt, 逃兵 desertion, 一統天下 victory | ✅ | P6 (ADR-013): 民心<25 → per-season revolt roll `(25 − 民心) × 2%` × `RevoltChanceMult` zeroing that city's harvest+tax, −10% garrison, 民心 +15; unfed armies desert; one owner of every city (neutrals included) sets `WinnerSeatId` + public `game_over` and blocks `Submit()` — see `docs/ACTIONS.md` §8b |
| Research: 4 lines × 5 levels, point income, effects | ✅ | P7 (ADR-014): `research` action (1 號令 TBD + 20/40/70/110/160 points), +15/season at rollover (19 with 吏治 L4), private per seat (owner-only event), live 研究 tab; **implemented** 農政 L1/L5, 兵法 L1/L4, 吏治 L1-L5 · **deferred but purchasable** 農政 L2-L4, 兵法 L2/L3/L5, 諜報 L1-L5 — full matrix in `docs/ACTIONS.md` §7 |
| Basic diplomacy: public messages, private letters, NAP, declare war | ✅ | P1-P6: 遣使, 提出/接受/拒絕/反提案 (nap/alliance/truce), 贈禮, 宣戰, 毀約; 密信 / 共同防禦 / 朝貢 / 通商 still ⏸ |
| Trust (−100..+100, bidirectional) + prestige | ✅ | directional `Trust` + `Prestige` written by gifts/attacks/treaties; every change emits `trust_changed`/`prestige_changed` with a reason |
| Basic fog of war + scouts | ✅ | engine-side observations (±3 cities / ±2 armies, absent outside; ADR-011); **偵察 as a gated mission (P8)**: a report keeps a city visible for this season and the next, banded (少/中/多) below 諜報 3 and exact at 3+ (ADR-015); a *passive* scout unit and estimation levels are still ❌ (OPEN_QUESTIONS 20/23) |
| Espionage missions (諜報) | 🟡 | P8 (ADR-015): `scout` 200金, `rumor` 500金 (民心 −10), `sow_distrust` 800金 (信任 −10 both ways) — 1 號令 each, gated by 諜報 1/1/2, one seeded roll each (70% + level × 5%), covert logging, public exposure on failure (−8 威望, −10 信任); 竊取軍報 / 偽造軍報 / 收買將領 / 暗殺 / 巡查 / 清查 / 秘密反用 / spy entities / 嫌疑 all ⏸ |
| Victory: 一統天下 + score at season limit | 🟡 | **一統天下 ✅ (P6, ADR-013)** — every city (neutrals included) under one seat, public `game_over`, later commands rejected, IMGUI banner; 稱霸諸侯 / 受天明命 / 時間到 (score) ⏸ — need city counts, 威望 gating, 稱帝 and a season cap |

## Controllers (FYP core — see docs/CONTROLLER_PROTOCOL.md)

| Feature | Status | Notes |
|---|---|---|
| IPlayerController interface (Human/LLM/Scripted) | ❌ | protocol specified; not extracted yet — `ControllerType` + `SeatRotation` are in place, the interface itself is P5 work |
| HumanController (UI-driven) | 🟡 | `KingdomDemo` IMGUI produces real ActionCommands for whichever human seat is current; hotseat rotation done; not yet behind IPlayerController |
| ScriptedController (rule-based AI, deterministic baseline) | ✅ | `PlanTurn()`: diplomacy (0 CP) → military → domestic → march, every guard mirroring an engine precondition so it never plans a rejected command; same `Submit()` pipeline; documented as reading canonical state (ADR-011) |
| LLMController (observation → prompt → JSON actions → validate → retry/fallback) | ❌ | next phase; `Observation.ToJson()` is its input payload |
| Mid-game seat switching + HandoverBundle | ❌ | the thesis feature; its data (`Observation`, pending `Proposals`, filtered event log) is ready |
| LLM run log (prompt version, tokens, latency, validation outcome) | ❌ | |

## UI (Unity)

| Feature | Status | Notes |
|---|---|---|
| Map view: zoom/pan, terrain, cities, armies, fog mask | 🟡 | terrain/cities/banners/camera done (2D fake-3D, ADR-008); units outside the current seat's observation are hidden (ADR-011), but there is no terrain fog *mask* |
| Nation/city/army panels + action menu (legal actions only) | ✅ | city panel: D1-D7 + 民心/開墾/城防/訓練; army panel: 士氣 + 攻擊/攻城 against adjacent enemies; CP cost on every label, greyed with a reason |
| 朝報 / 朝議 / 史官 screens | 🟡 | per-seat filtered 朝報 (city lines, 戰報, 外交 news) + filtered 史官 tab; 朝議 not yet |
| Concept tab bar （內政/外交/諜報/研究 live) | ✅ | all four tabs are live: 內政, 外交, 諜報 (P8, ADR-015) and 研究 (P7, ADR-014); 史官 renders the real log |
| Diplomacy panel (grouped relations, treaty deadlines) | ✅ | per-seat relations (信任 both ways, 和平/交戰), treaties with seasons left, incoming-proposal inbox with 接受/拒絕, 遣使 text field; beyond 3 neighbours are summarised |
| Notifications + turn progress | 🟡 | status line + JSON command feed + "回合：魏" tag; no toasts/popups |
| Replay viewer | ❌ | |

## Stretch goals (⏸ by default)

8-seat full games · full treaty set (割地/朝貢/聯姻/稱臣…) · the remaining §8.2 missions (竊取軍報, 偽造軍報, 收買將領, 暗殺) and §8.4 counter-intelligence (巡查/清查, 嫌疑, 秘密反用) · general personalities & betrayal · 稱帝/結義/禪讓-託孤 actions · multiple simultaneous LLM seats · procedural maps · networked (non-hot-seat) multiplayer · research analytics dashboard · the deferred research levels (農政 2/3/4, 兵法 2/3/5, 諜報 3-5 effects)

## Evaluation (Phase 7)

Scripted-AI mass simulations (balance, deadlock, game length) · LLM metrics (legal-action rate, retry/fallback rate, diplomacy acceptance, betrayal frequency, survival) · human vs LLM vs scripted comparison on same seeds · replay demo for thesis
