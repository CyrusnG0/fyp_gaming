# DECISIONS.md — Architecture Decision Records

> Append-only. Never edit an old ADR's conclusion; add a new ADR that supersedes it.

## ADR-000 — Turn structure: Option B (inherited from report)
**Date:** 2026-09-17 · **Status:** accepted
Each season: shared 朝報 (morning report) → shared 朝議 (public council) → each seat takes an individual turn in order (密奏 + 下令, immediate resolution) → 史官 (historian). Players act one-by-one, not simultaneous submission.

## ADR-001 — Unity-native architecture, no web stack
**Date:** 2026-09-24 · **Status:** accepted · **Supersedes:** report v1.0 §3.1, §3.3, §4.2–4.3, §10
The game is a **single Unity application** (6000.6.2f1, URP 2D). No React frontend, no FastAPI backend, no PostgreSQL, no Docker.
- Authoritative core: pure C# assembly inside the Unity project (`Assets/GameEngine/`), no UnityEngine dependencies → headless-testable via EditMode tests.
- Multiplayer: **hot-seat** on one machine (humans take turns at the same screen) + LLM/scripted seats in-process. Networked play is a stretch goal.
- Persistence: local JSON snapshots + append-only event log. No external database in MVP.
- LLM integration: in-Unity adapter calling provider APIs over HTTPS.
- Rationale: team is building in Unity with TBTK3; two-person team, limited time; the research question (human↔LLM switching) does not require networked play.
- Consequence: report sections describing HTTP/WebSocket APIs are reinterpreted as **in-process C# interfaces**; the command/event model, validation pipeline, and observation filtering are unchanged concepts.

## ADR-002 — Player count: data-driven, max 8, MVP 4
**Date:** 2026-09-24 · **Status:** accepted
At most 8 seats. Never hardcode seat count. MVP targets 4 seats (1–2 human, 1 LLM, rest scripted); architecture (seat collections, faction config, diplomacy UI grouping) must already support 8.

## ADR-003 — Controller interchange via single interface + HandoverBundle
**Date:** 2026-09-24 · **Status:** accepted
All seat controllers (human UI, LLM, scripted AI) implement one interface and submit identical `ActionCommand`s to `GameEngine.SubmitAction()`. Mid-game control transfer delivers a **HandoverBundle** (current filtered observation + full-season chronicle + detailed recent events + pending diplomacy). Spec: `docs/CONTROLLER_PROTOCOL.md`. This is the FYP research core; changes here need team agreement.

## ADR-004 — TBTK 3 as turn-tactics foundation
**Date:** 2026-09-24 · **Status:** accepted
TBTK 3.1.5 (unitypackage at repo root) provides grids (hex/square), turn control, units, A* pathfinding, fog-of-war support, UI scaffolding. Use as the presentation/tactics layer. **Do not modify TBTK source** — subclass or wrap, so upgrades stay possible. LuanShi's diplomacy/trust/espionage/season systems are our own code on top.

## ADR-005 — Persistence: JSON snapshot + append-only event log
**Date:** 2026-09-24 · **Status:** accepted
Per game: current-state snapshot (JSON, rewritten per season) + event log (append-only JSONL) + periodic full snapshots. Replays rebuild state by replaying events from a snapshot. LLM runs (prompt, output, validation, latency, tokens) are logged alongside actions (`LLMRun` records). Randomness: seeded RNG, seed stored per game; scripted-AI games must be reproducible.

## ADR-006 — docs/ is day-to-day truth, docx report is milestone truth
**Date:** 2026-09-24 · **Status:** accepted
`docs/CURRENT_STATE.md`, `FEATURES.md`, `DECISIONS.md`, `OPEN_QUESTIONS.md` are updated every session. The formal report (`doc/*.docx`) is regenerated/updated at milestones. v1 docx is superseded by v1.1 (Unity-native). Open decisions from report §15 live in `docs/OPEN_QUESTIONS.md` until resolved.

## ADR-007 — TBTK scoped to strategic-map scaffolding (not the battle game)
**Date:** 2026-09-25 · **Status:** accepted · **Supersedes:** ADR-004's "presentation/tactics layer" framing
Team decision (verbal, demo-driven): LuanShi's core loop is **strategic** (emperor ruling a kingdom: city growth, farming, raising armies, expansion) — TBTK's tank-battle gameplay is **not** our game. TBTK is used only for: hex grid + node data, A* pathfinding, camera control, turn counter. Disabled/unused: combat/HP/abilities, deployment phase, collectibles, fog of war (for now). Our authoritative GameEngine stays pure C# on top. Rendering path: **TBTK materials converted to URP** (scripted, 75 materials; `Custom/BlendColorsOverlayTexture` → provided URP shadergraph) — resolves the URP-vs-Built-in question from ADR-004.

## ADR-008 — Art direction: 2D fake-3D, old-China style
**Date:** 2026-09-25 · **Status:** accepted
Top-down 3/4-angled camera; visuals are **2D assets only**: painted tile textures on ground hexes + billboard sprites (`BillboardSprite.cs`, fixed 45° pitch) for props — mountains, forests, pagoda cities, faction army banners. No 3D models, no sci-fi. MVP art = procedural placeholder sprites drawn by `Assets/GameUI/Art/_make_sprites.py` (PIL); regenerate or replace with real art later. Gotcha: prop sprites need positive `sortingOrder` (we use `500 - z*10`) or TBTK overlay objects overdraw them.

## ADR-009 — Strategic engine: pure C# assembly, engine owns seasons, TBTK turn flow bypassed
**Date:** 2026-09-26 · **Status:** accepted
The kingdom layer is implemented as `LuanShi.GameEngine` (own asmdef, `noEngineReferences`, zero UnityEngine deps) in `Assets/GameEngine/`: `GameState`/`FactionState`/`CityState`/`ArmyState` POCOs, flat `ActionCommand` DTO (`type`, `actor_seat_id`, `target_id`, `param_a/b`, `reason`) with hand-rolled JSON writer (`MiniJson` — no JsonUtility/System.Text.Json so the assembly stays pure), `GameEngine.Submit()` as the single validation+execution pipeline (CP budget, ownership, resources, once-per-season flags, Dijkstra path cost vs per-season move budget), `EventLog` (append-only, CONTROLLER_PROTOCOL §5 shape), seeded `System.Random`, and `ScriptedController` which only *proposes* commands through the same pipeline. The engine keeps its **own copy of the map** (`MapData`: walkable/cost/hex-adjacency) built by the adapter from TBTK nodes — rules never depend on Unity state. TBTK integration: `KingdomDemo` (adapter, `Assets/GameUI/Scripts/`) disables `GameControl` in Awake (its Awake still inits the grid; its Start never runs) and deactivates all `TBTK.UI*` objects — **the engine owns the season loop, TBTK supplies grid data and camera only**. Demo UI is IMGUI (`OnGUI`) as a deliberate MVP choice: zero scene wiring, and `Event.current`-based clicks work regardless of the project's Active Input Handling setting. All numbers live in `BalanceConfig` (constants class, marked TBD — a JSON/YAML balance file remains future work).

## ADR-010 — Action spec v1 locked; schema v2; human-first pass-and-play build order
**Date:** 2026-10-08 · **Status:** accepted (orchestrator recommendation, pending team override)
The canonical action catalog in `docs/ACTIONS.md` is locked for MVP: CP=5/season; `farm` renamed `reclaim` (+15% yield, cap +60%); MVP tier = domestic D1-D7, military M1-M3, diplomacy P1-P6, fog-filtered observations, control switch; 攻城 is assault-only single resolution; treaties take effect next season; 民心<30 → city yields −50%; 偷襲/背盟 are derived penalties from state, not separate actions. `ActionCommand` gains schema v2: optional `args` string-dict + free `text` field (backward compatible with v1 flat fields). **Build order decision (user-directed): human-first.** Complete engine state+schema → all human-playable UI (hotseat pass-and-play, one screen per seat) → only then the LLM adapter. Rationale: demo is human-played, and since human UI and LLM emit identical commands, finishing human play finishes ~80% of AI play; remaining AI work is the `IPlayerController` adapter + JSON parser.

## ADR-011 — Observation contract: engine-side fog, one filtered world per seat
**Date:** 2026-10-08 · **Status:** accepted
`GameEngine.Observe(seatId)` is the only world a controller may act on (AGENTS.md invariant #3); `null` for an unknown seat.
- **Radii** (locked, ACTIONS.md §9): a hex is in sight iff within `BalanceConfig.FogCityRange` (±3) of any **own city** or `FogArmyRange` (±2) of any **own army**; ownership of the hex/occupant is irrelevant. Sight is a *radius, not line-of-sight* — `MapData.HexDistance` walks the hex adjacency graph ignoring `Walkable`, so terrain never blocks it.
- **Absent, not redacted**: foreign cities and armies outside the radius are simply not in the observation (no last-known-position in MVP).
- **Visible ⇒ full detail** for MVP: an in-radius foreign city/army carries the same fields as an own one (troops, garrison, 民心…). The design's estimation levels are deferred (OPEN_QUESTIONS 20).
- **Contents**: seat/season/phase/command points/威望; cities+armies; `Factions` (name, controller, 威望 — public per design §7.3); `Relations` (per other seat: my 信任 of them, their 信任 of me, at-war); `Treaties`/`Proposals` limited to those this seat is a party to; `Events` = the filtered log. Pure function of state+log: two calls give byte-identical JSON and nothing mutates.
- **Event filter** (`GameEngine.SeesEvent`): public → everyone; actor → sees it; a seat named a party (`to_seat_id`/`from_seat_id`/`attacker_seat_id`/`defender_seat_id`) → sees it; `owner_and_observers` → also visible when the event's *subject* is currently in sight (`city_id`, or `army_id`/`attacker_army_id`/`defender_army_id`); otherwise only the actor and named parties. Consequence: history about a city becomes visible once you bring an army next to it (a *present* view, not a per-moment snapshot). Two-party events carry their recipient in the payload because the three visibility values have no "both parties" case.
- **`Observation.ToJson()`** is the payload the LLM adapter and HandoverBundle will carry. **`ScriptedController` is the documented exception**: it reads canonical `GameState` as a referee-grade deterministic baseline — the human/LLM comparison runs through the observation path.
- **UI**: KingdomDemo renders only from the current seat's observation — foreign units outside the radius are hidden on the map, unselectable, and absent from the 史官/朝報/diplomacy panels.

## ADR-012 — Diplomacy command shapes + proposal lifecycle
**Date:** 2026-10-08 · **Status:** accepted
P1-P6 are implemented with these command shapes (the LLM contract; `target_id` is always a **seat**, never a hex):
- `send_message` (0 CP): `text` = the message; **no state change** (design §7.1 說話不改狀態) — `message_sent` (public).
- `propose_treaty` (0 CP): `args.treaty_type` ∈ `nap|alliance|truce`, optional `args.duration` (default `BalanceConfig.TreatyDurationSeasons`, clamped 1–12); one pending offer per pair (either direction); refused if that type is already in force.
- `respond_treaty` (0 CP): `args.response` ∈ `accept|reject|counter`; a counter needs `args.treaty_type` (+ optional duration) and replaces the offer with a reverse one.
- `gift` (1 CP): `param_a` = gold (≥ `GiftGoldPerTrustUnit` = 1,000) → `信任[victim→giver] +3` and `威望[giver] +1` per 1,000; paid from the faction's cities in state order (gold is national in the design, per-city in the model) and **nothing is paid on failure**.
- `declare_war` (1 CP): war state on, `威望 −5` (cheaper than a 偷襲, design §11 #18); refused if already at war.
- `break_treaty` (1 CP): `args.treaty_type` required → treaty removed; alliance/NAP −50 信任/−15 威望/bystanders −15, truce −30/−10.
**Lifecycle**: propose → pending (`treaty_proposed`, two-party) → accept (marked, **not yet in force**) / reject / counter → at rollover (`BeginSeason`) unanswered offers lapse (`treaty_lapsed`) and accepted ones are ratified into `TreatyRecord`s with `ExpirySeason = season + duration − 1` (`treaty_ratified`, public); a ratified 停戰 calls `MakePeace` (`war_ended=true`). Treaties past `ExpirySeason` are removed (`treaty_expired`, public). Consequences: P3 sits before any war/peace effect (treaties take effect next season, ADR-010 #5 — so 求和 is the 停戰 proposal path, not a direct peace action), 守約's +15/+5 design row is **not** implemented (no P-row and a 1-season NAP round-trip would farm trust), and 宣戰 does not itself break treaties — a later attack is still priced as 背盟 (OPEN_QUESTIONS 16).

## ADR-013 — Pressure & consequences: 民變, army supply/desertion, 天時 seasons, 一統天下
**Date:** 2026-10-11 · **Status:** accepted · **Phase:** P6

The world now pushes back on every seat, engine-side and deterministically (seeded `System.Random`, same
stream as combat). No new actions: these are rules of resolution, so a human, an LLM and a scripted seat
are all subject to them identically (AGENTS.md invariants #1–#3). All numbers live in `BalanceConfig`;
each carries its design-doc section, and design-silent values are marked 待決/TBD there.

- **天時 seasons (design §3 table, §10.1 嚴冬).** Seasons cycle 春→夏→秋→冬 by `(Season−1) % 4`;
  `BalanceConfig.SeasonFoodYieldMult` scales **every** city's harvest at rollover — 春 0.7 (LOCKED,
  design table) · 夏 1.0 · 秋 **2.0** (大收) · 冬 **0.3** — multiplying on top of the 民心<30 halving
  (ADR-010 #6) and 開墾. 冬 additionally cuts the per-army hex budget to `MarchBudget = floor(6 × 0.7) = 4`
  (enforced in `GameEngine.DoMarch`, mirrored by `ScriptedController` so the AI never plans a move the
  validator will refuse) and adds +5% to desertion losses. The season name rides `SeasonReport.SeasonName`,
  the `season_start` payload and the observation (`Observation.SeasonName`, JSON `season_name`) so the
  朝報 header and the LLM payload both say which season it is.
- **民變 revolt (design §4.1 民心 <25 + §10.2 產出停擺).** At `EndSeason()`, before a city's yields are
  computed, each owned city with 民心 < `RevoltRiskMoraleThreshold` (25, design's number) rolls
  `chance = (25 − 民心) × RevoltChancePerMoralePoint (0.02 待決) × FactionState.RevoltChanceMult` — the
  multiplier is a **P7 research hook** (吏治 3 will set it to 0.5) and is read from day one; it defaults to
  1.0. On a revolt the city's **harvest and tax are zero for that tick** (the following starvation check
  still runs — a revolting city can starve), the garrison loses 10% (待決, design is silent) and 民心 gains
  +15 after venting (待決, clamped ≤100). Event `revolt` (payload `city_id`, `garrison_lost`, `morale`) is
  **public** — the design's 朝報 is full of such news and hiding it would be unfair to LLM seats that cannot
  feel the map. Rolling before the yields (rather than cancelling them afterwards) keeps the tick
  deterministic and order-independent from the tax/harvest code paths.
- **逃兵 army supply & desertion (design §5.1 補給, §6.3 斷補 10%/季, §10.1 嚴冬).** The design gives armies
  a 補給 0–100 field that decays with distance from home and reaches 0 in the field. MVP models **only the
  consequence**: each army eats `Troops × FoodPerGarrisonTroop` (0.1) per season out of its faction's food.
  The city the army stands on pays first (deducted there), the faction's remaining cities cover the rest
  **in proportion to their stores** (`PayFromFoodPool`, integer shares with the remainder handed to cities in
  state order — same "food is national in the design, per-city in the model" bridge as `PayFromTreasury`,
  ADR-012). If the faction pool cannot cover the **whole** ration, nothing is eaten and the army deserts:
  兵力 −10% (LOCKED, §6.3; 冬 −15%), 士氣 −15 (待決). Event `desertion` is **owner-only** — a starving army
  is the owner's secret, matching how 斷補 is described as a surprise. Supply resolves *after* the seasonal
  morale regrowth so the −15 is not refunded in the same tick. Deliberate simplifications: no 補給 field, no
  distance decay, no fractional ration, no per-army supply state to snapshot. 劫糧/斷補 remain ⏸ as *actions*
  (they need cavalry); their consequence is now live for anyone who cannot feed their army.
- **一統天下 victory (design §13).** `GameState.WinnerSeatId` is set when **every** city on the map has the
  same non-empty owner — neutral cities included, since a neutral city is territory nobody has taken, so
  "all cities except the neutral ones" is not unification. `CheckVictory()` runs at the two places ownership
  can change (neutral capture on 行軍, capture on 攻城), emits one public `game_over` (`winner_seat_id`,
  `reason=unification`) and is irreversible: every later `Submit()` returns `game over` before phase/CP
  checks, and the observation exposes `winner_seat_id` so the LLM/HandoverBundle path can see the game is
  over. 稱霸諸侯 / 受天明命 / 時間到 (score at the season limit) are **not** implemented — they need city
  counts, 威望 gating, a 稱帝 action and a season cap (OPEN_QUESTIONS 10/11).
- **Consequences for the thesis.** The pressure systems are the first mechanics that punish a *passive*
  seat, which is exactly what the human-vs-LLM comparison needs: an unfed army and an unhappy city show up
  as numbers in the same `SeasonReport`/observation JSON for every controller type. Both new private
  methods (`RollRevolt`, `ResolveArmySupply`) read canonical state — they are engine rules, not
  observation-filtered reads.
- **Verified** headless 2026-10-11: 58/58 assertions (revolt fires below 25 and zeroes both yields, never
  at/above 25 over 256 runs, `RevoltChanceMult=0` suppresses it; desertion on an empty pool, the standing
  city pays first, proportional pool draw, winter +5%; autumn ×2.0 / winter ×0.3 / spring ×0.7 / summer
  ×1.0 harvests; winter march of cost 6 refused while cost 4 is allowed; victory on the last neutral city,
  public `game_over`, later `Submit()` rejected for winner and loser alike; `ScriptedController` relieves a
  revolt-risk city first and never plans an unaffordable winter march). Engine + adapter also compile
  clean against Unity's managed assemblies (0 warnings/errors).

## ADR-014 — Research: four §9 lines, point income at rollover, effects read where they apply
**Date:** 2026-10-12 · **Status:** accepted · **Phase:** P7

Design §9 (四條線，各 5 級) is implemented as **one action + four state lines**, engine-first and
deterministic like everything since P6. All numbers live in `BalanceConfig` with the design section in the
comment; values the design does not state are marked TBD.

- **Model & schema.** `FactionState.ResearchPoints` (int) plus `FactionState.Research`, a `line → level`
  (0..5) map. The four keys are **English and frozen**, because they are the LLM contract: `agri` 農政 ·
  `military` 兵法 · `espionage` 諜報 · `governance` 吏治 (`ResearchLine.*`). `ResearchLine.MapToJson`
  renders all four keys **in schema order** rather than dictionary order, so replay JSON is byte-stable
  (ADR-005). The observation carries the seat's own `research_points` and `research` only — a tech tree is
  a seat's secret, and the `FactionView`s other seats receive carry no research fields at all (they are
  built from `FactionView`, not `FactionState`).
- **Action R1 `research`.** `args.line` required ∈ the four keys; `target_id` unused. Costs **1 號令**
  (TBD — design ch.11 never prices it) *plus* the level's points (20/40/70/110/160, LOCKED design §9),
  deducted immediately on success. Refused for: unknown/missing line, line already at level 5,
  insufficient points, no 號令 left (the generic pipeline checks CP first, so the CP message wins). One
  level per command, so a faction can advance at most 5 levels a season given the CP budget of 5. Event
  `research` is **owner-only** — locked choice: in a game with a fog of war and a 諜報 tree, publishing
  everyone's progress would gut the 諜報 line and hand LLM seats free information a human cannot get from
  the screen. The event payload is `line`, `level`, `cost`, `research_points`.
- **Income at rollover.** `+15/season` (TBD; design says 「約 15 點／季」) at `BeginSeason()`, ×1.25 →
  **19** with 吏治 L4 學堂 (rounding, documented in `BalanceConfig.ResearchIncome`). Income is deliberately
  *not* granted in `EndSeason()`: rollover is where a season's budget is set (CP are set there too), and it
  makes the demo's very first order phase follow the design's "開局約 15 點" reading. **學堂 the building**
  (design §4.2, +3/季) is deferred with D8 建築 — no building model exists.
- **Effects — implemented only where a system exists, each read at the point of use** (no cached derived
  state): 農政 L1 harvest ×1.10 and L5 a further ×1.25 (multiplicative with 開墾 and the 天時 season —
  `EndSeason`), 兵法 L1 `TrainingCap` 5→6 for that faction (`DoTrain`, the UI and `ScriptedController` all
  read `BalanceConfig.TrainingCap(level)`), 兵法 L4 戰力 ×1.15 **faction-side** (`Strength`/`GarrisonStrength`
  take the multiplier, so the two sides of one battle can differ — a defender with 兵法 4 defends a city at
  ×1.15 while the attacker's troops are unchanged), 吏治 L1 tax ×1.10, L2 民心 +5/季 per city (granted in the
  city loop so it lands on the season's report line), L3 `RevoltChanceMult` 0.5, L4 income ×1.25, L5 威望
  +10 once at purchase (via `AddPrestige`, so it is public and logged like every other 威望 change).
- **Timing.** Point-of-use effects (harvest, tax, combat, training cap, the 民心 grant) apply as soon as the
  level exists — buying before `EndSeason()` already pays in that season. The two **derived** values —
  吏治 L3's `RevoltChanceMult` and the L4 income rate — are settled at the season rollover instead, so they
  take effect from the next season, the same rule treaties follow (ADR-010 #5). `FactionState.SyncResearchDerived`
  computes L3 from the level rather than the purchase writing 0.5 directly, so the hook can never drift from
  the tech tree. This is the intended shape for the P6 note "P7 吏治 3 will set it to 0.5".
- **Deferred (still purchasable — a level with a deferred effect is progress up its line):** 農政 L2 水利,
  L3 屯田 (D9, needs the research gate it was always waiting on), L4 常平倉 (no food cap exists); 兵法 L2 騎兵,
  L3 器械, L5 統帥 (no unit types, no siege weapons, no generals); 諜報 L1–L5 (they gate the espionage phase,
  which is still ⏸). Also deferred: 學堂/其他 building effects, per-region 天時 weather, and any research
  analytics (the thesis metric). `ACTIONS.md` §7 carries the full implemented-vs-deferred matrix and the UI
  prints "(deferred)" on every level whose system is missing, so the panel never promises an effect the
  engine does not apply.
- **兵法 L1 interpretation (flagged).** Design §9 L1 reads 「練兵（訓練 +1）」, which can be read as a free
  training level or as a raised cap. Implemented as **cap 5→6** per the P7 brief; recorded here so the team
  can supersede it (OPEN_QUESTIONS-style flag rather than a silent pick).
- **Scripted baseline.** With a 號令 still unspent after diplomacy, military, domestic and march, the
  scripted seat buys the next 農政 level (its food-first play style's bottleneck) — **at most one research
  order per season**, and only when the points are already in hand; it never plans a purchase the validator
  will refuse.
- **UI (KingdomDemo).** The 研究 tab is live: four rows with pips, the seat's points and income, "Now:" and
  "Next: level n for c points — <effect>", and one `Research` button per row using the shared `OrderButton`
  helper (so its label carries the CP cost and it is greyed with a reason: *Line complete* / *Needs c points*
  / *No orders left*). Everything is rendered from the seat's observation, and the city panel's
  訓練 display/blocked reason use the research-aware cap.
- **Verified** headless 2026-10-12: **109/109 assertions** (58 from P6 still green + 51 for P7) — income 15,
  19 with 吏治 L4, income only at rollover; unknown line / missing `args.line` / insufficient points /
  max level / no-CP all refused with their own messages; level 1 = 20 and level 2 = 40 deducted; the
  `research` event is owner-only and unreadable by another seat; 農政 L1 harvest ×1.10 (exact numbers),
  L5 ×1.25 on top; 兵法 L1 lifts 訓練 to 6 and holds there; 兵法 L4 multiplies the boosted faction's
  first-round losses by exactly ×1.15 while the other side's first round is identical; 吏治 L1 tax ×1.10,
  L2 民心 +5 named on the report line, L3 `RevoltChanceMult` 0.5 only from the next rollover, L5 one-time
  +10 威望; the observation carries all four lines plus points; the scripted seat buys exactly one 農政 level
  when rich and none when poor; two runs of an 8-season research replay on one seed are byte-identical and a
  different seed diverges. Engine + `KingdomDemo` also compile clean against Unity's managed assemblies
  (0 warnings / 0 errors); the editor compile and play-mode smoke are still pending.

## ADR-015 — Espionage lite: three gated missions, one seeded roll, deniable on success
**Date:** 2026-10-12 · **Status:** accepted · **Phase:** P8

Design §8.2 lists seven spy missions and §8.3–§8.4 a 暴露率 formula, suspicion accumulation, cover types and
two counter-intelligence tools. This phase ships the **MVP slice** the P8 brief locked: **three missions,
no persistent spy entity, no 嫌疑, no 偽造軍報, no 秘密反用, no 巡查/清查.** Rationale: the thesis needs
espionage to exist as a *deniable, research-gated action* on the same validation path as everything else,
and each deferred piece needs a model that does not exist yet (spies as entities, 治安, generals,
per-seat false reports).

- **Missions (design §8.2 + §11 #30/#32).** `scout` 偵察 200 金 (諜報 L1), `rumor` 散播謠言 500 金
  (諜報 L1 — gate TBD, §8.2 lists 難度 中 but §11 gives no research gate), `sow_distrust` 挑撥離間 800 金
  (諜報 L2 per the brief; **design §11 #32 says 諜報 3** → OPEN_QUESTIONS 21). Each costs **1 號令**
  (LOCKED, design §11 #30/#32). Gold comes out of the faction treasury through `PayFromTreasury` — the same
  "gold is national in the design, per-city in the model" rule 贈禮 uses (ADR-012): the cities pay in state
  order, and a mission refused at the gate pays nothing before the roll.
- **Success/exposure (simplified §8.3).** One seeded `rng.NextDouble()` per mission:
  `chance = 70% (TBD) + 諜報 level × 5% (TBD)` → 75/80/85/90/95% at L1-L5. The design's 暴露率 formula
  (base 5% + 所在城治安 × 0.3% + 目標國諜報等級 × 4% + 嫌疑 × 0.2% − 掩護) is **replaced by
  `1 − success chance`**, because 治安, suspicion and cover types do not exist. On failure the mission does
  nothing, the gold is gone anyway, and the attacker is exposed by the §8.3 公開處決 path: a **public**
  `spy_exposed` naming `attacker_seat_id` and `mission`, the attacker's 威望 **−8** (LOCKED — design §7.3's
  散播謠言被識破 row says 威望 −8), and the victim's 信任 toward the attacker **−10** (TBD; the same row says
  −20 for the rumour case → OPEN_QUESTIONS 22). For 挑撥離間 both manipulated seats take the −10. The
  design's paired 對方威望 +5 (the executor's reward) is **not** paid out, so exposure is a pure loss.
- **Covert logging (the one place the log is private).** AGENTS.md #4 says everything is logged; it does not
  say everything is logged *publicly*. Espionage is worthless if the target can read who sent the agent, so
  `ActionType.IsCovert` makes the generic `action` record **owner-only** for the three missions, and a
  successful mission's own event is constructed with **no actor**: `rumor` names only the victim seat
  (`to_seat_id`) and `sow_distrust` only the two victims, so `SeesEvent` reaches them and nobody else. A
  failure is public by design — that *is* the §8.3 consequence. Consequence for the thesis: the canonical log
  still holds the full truth (the researcher can read every mission), while each seat's *filtered* log holds
  only what it earned — the human/LLM information symmetry the project needs.
  *(Superseded in part by ADR-018: the two PR #3 garrison transfers are owner-only too.)*
- **偵察 as a report, not a leak.** A successful scout writes `FactionState.ScoutedCities` (city id →
  `untilSeason = season + ScoutReportExtraSeasons`), pruned at rollover. `Observe()` adds the city to
  `ScoutedCities` **only when it is not already in sight** — inside the fog radius `Cities` still carries the
  canonical `CityState` at full detail (ADR-011), and duplicating it as a second, weaker entry would be
  incoherent. Accuracy is **not stored on the report**: it is read from the seat's current 諜報 level when
  the observation is built, so researching 諜報 3 upgrades reports already held (the same "read the level at
  the point of use" rule as P7). At L1-2 the report is `accuracy: "band"` with `garrison_band`/`food_band`
  (few/some/many; thresholds TBD — 少 <1,000 ≤ 中 <3,000 ≤ 多 troops, a separate TBD pair for 糧草) and the
  exact fields **are not present at all**; at L3+ it is `accuracy: "exact"` with the real numbers. The
  canonical `CityState` is never touched by a mission except through the effects of `rumor` (民心 −10,
  clamped) and `sow_distrust` (信任 −10 both ways, clamped by `DiplomacyState`).
- **Where the value is.** In this fog model a scout is worth 200 gold because the report **outlives the
  vision radius**: nothing of yours must stay near the city, and next season you still hold the numbers (or
  the bands) after your army has marched on. The UI can only *offer* cities the seat can name — in sight or
  already scouted — because ADR-011 keeps no "known cities" memory; the engine accepts any city id, so an
  LLM may target a city named in its own chronicle (OPEN_QUESTIONS 23).
- **Scripted baseline.** Deliberately un-clever and readable: when the seat is at war, its treasury is above
  `ScriptedSpyGoldThreshold` and 諜報 L1 is researched, it spreads rumours in the enemy's **least loyal**
  city — every third season only (`ScriptedSpyRumorEveryNthSeason`, TBD), once a season at most, as the last
  thing in its plan. It never sows distrust (the brief's call: an AI that manipulates third parties is
  harder to read in the human-vs-LLM comparison).
- **UI (KingdomDemo).** The 諜報 tab is live: seat-level header (諜報 level, success rate, gold, orders), the
  three missions with cost/gate/effect, a target list of the foreign cities the seat can name with `Scout`
  and `Rumours` buttons (greyed with *needs Espionage n* / *needs n gold* / *no orders left* / *no faction to
  undermine* for a neutral city), a "Live reports" block showing band or exact detail with its expiry season
  **and the same two buttons** — re-scouting there is the only way to keep an eye on a city outside the fog —
  and 挑撥離間 seat-pair buttons. All of it is rendered from the seat's own observation and submits real
  commands through `SubmitHuman`.
- **Verified** headless 2026-10-12: **175/175 assertions** (109 from P6/P7 still green + 66 for P8) —
  every gate (諜報 level, gold, own city, neutral city, own seat, duplicate target seats, missing
  `args.target_b`), a refused mission spending nothing, the scout window (live this season and the next,
  gone one season later) and band-vs-exact reporting at L1/L3 with the canonical city untouched, `rumor`
  −10 with clamping and a nameless record only the victim sees, `sow_distrust` −10 both directions with
  exactly the two victims informed, the failure path (nothing happens, gold gone anyway, public `spy_exposed`
  naming attacker + mission + detector, 威望 −8, victim 信任 −10), treasury payment spread over cities in
  state order, the scripted seat's one-in-three-seasons rumour aimed at the weakest enemy city (and never
  挑撥離間), and byte-identical replays for identical seeds. Engine + `KingdomDemo` compile clean against
  Unity's managed assemblies (0 warnings / 0 errors); the editor compile and a play-mode smoke of the live
  諜報 tab are still pending.

## ADR-016 — Fog-visible city markers, observation-shaped city panel, march-before-popup
**Date:** 2026-10-12 · **Status:** accepted · **Phase:** post-PR#2 fix (`2ce2c48`)

Four UI-level rulings made while fixing the hotseat privacy leak and the broken march path:

1. **Out-of-vision cities keep a marker but lose their colour.** PR #2 kept city markers visible outside
   vision ("a city stands here"); the marker is now painted **neutral** — existence is public map
   knowledge, ownership is not. Anything richer must come from sight or a scout report.
2. **The city panel is observation-shaped.** Own city → full panel; in sight → the observation's numbers
   (ADR-011 "visible ⇒ full detail"); scouted-only → the P8 report (bands at 諜報 L1-2, exact at L3+, with
   expiry shown); otherwise "nothing is known", and the title degrades to "Unknown city". This supersedes
   PR #2's blanket "Enemy territory — details unknown", which hid legitimate observations and made 偵察
   pointless in the panel.
3. **Army selected + click on a city hex = march order**, not inspection (standard RTS "move here").
   Inspection requires deselecting first. Root cause of "armies can't move": PR #2's city/army popup
   branch ran before the march branch and swallowed every march onto a city hex.
4. **The handover blocker hides by early-returning from OnGUI** (nothing else draws at all) and fills in
   screen pixels (aspect-proof), rather than drawing a near-black overlay inside the 1600×900 design
   matrix, which leaked a visible strip on non-16:9 windows.

Also locked: the movement **highlight budget and the validator budget are the same call** —
`BalanceConfig.MarchBudget(season)` — so winter (×0.7) can never highlight a hex the engine then rejects.

## ADR-017 — Design north star: Polytopia-inspired, but no fixed policy should survive
**Date:** 2026-10-12 · **Status:** accepted · **Source:** team direction (user, 2026-10-12)

Battle of Polytopia is the reference for *feel* (clean hex map, one-tap orders, pass-and-play), but the
design target is deliberately beyond it: **the game must be unwinnable by a memorised decision tree or a
scripted auto-player.** Every match must present a different problem, so that winning play requires
model-level reasoning under uncertainty — which is exactly the FYP's justification for LLM seats.

Consequences, in planned phase order (NOT yet implemented — roadmap only):

1. **P9 seeded world variability** — map defs as data (several handcrafted maps + a seeded parameterized
   generator), asymmetric starting resources/terrain per faction. The seed stays an explicit experiment
   parameter: identical seeds for fair human-vs-LLM comparison, different seeds for robustness runs.
2. **P10 天時 weather + events** (design §10) — per-region seasonal rolls (豐收/旱災/蝗災/疫病/祥瑞) and
   choice-bearing events (流民南下: accept = +人口 −民心, refuse = −威望). Needs a small choice-prompt UI;
   pairs with the P5 controller refactor.
3. **P11 uncertain information** — estimation levels for distant armies (少/中/多 bands, design §8.1),
   counter-intelligence (巡查/清查), 偽造軍報 (pollute an enemy's observation for a season). Information
   itself becomes attackable.
4. **P12 multiple victory routes** — 一統天下 / 稱帝·天命 (威望≥70 + 30 城) / score-at-season-limit, so
   opponents pursue different goals and no fixed counter exists.
5. **P13 adaptive opponents** — LLM seats with free-text negotiation, betrayal timing, and memory of
   deception: the dimension no precomputed policy can cover. This is where the thesis pays off.

The scripted controller remains in the game deliberately: it is the **baseline a real player (or LLM)
should beat**, and the evaluation harness measures exactly that.

## ADR-018 — PR #3 garrison transfers: action shapes, movement state, fog privacy, scope restoration
**Date:** 2026-10-19 · **Status:** accepted · **Phase:** PR #3 integration (merge `5416365`, fixes `9c6e452`, UI split `6b91dd0`)

PR #3 added garrison ↔ army transfers, watchtower ownership and defensive-siege handling. An independent
review found four balance constants changed outside the PR's stated scope and several integration defects;
this ADR records the rulings made while fixing them. **Nothing here changes engine rules beyond what is
listed**; unresolved design semantics are in OPEN_QUESTIONS 24-27.

- **Balance restored (the PR's description covers only garrison/tower/siege, not economy).**
  `FogWatchTowerRange` back to **5**, `CityBaseFoodYield` **200**, `StartFood` **500**, and
  `SpringFoodYieldMult` **0.7** (spring is LOCKED by design §3). The four drifts had silently changed the
  P6-P8 harness expectations; restoring them returns every prior assertion to green.
- **Action shapes (the LLM contract, ACTIONS.md §4b).** `convert_garrison`: 1 號令, `target_id` = own city,
  `param_a` = troops (1..garrison); a new `ArmyState` is **formed** unless an own army already stands on the
  hex, in which case the troops **merge** into it. `garrison_army`: 1 號令, `target_id` = own army standing
  in an own city, `param_a` = troops (1..army troops); a full transfer destroys the army. Events:
  `garrison_converted` (`mode=formed|merged`) and `army_garrisoned` (with `remaining_troops`), both
  `owner_and_observers`. The transfer event is emitted **before** the full-transfer `army_destroyed` so the
  replay reads transfer → destruction. Validation happens before any mutation, so a refused command is
  atomic (the old code deducted first and rolled back).
- **Movement-state ruling.** The once-per-season movement constraint must survive full and partial garrison
  round-trips and merges. Minimal canonical state: one boolean, `CityState.GarrisonMarchedThisSeason`
  ("this garrison has absorbed troops that already marched"). It is set when a marched army transfers in
  (and when a marched defending army is merged by a siege), inherited by a newly formed army, OR-ed into a
  merged army, cleared when the garrison empties or the city is captured, and reset in `BeginSeason()`
  alongside `ArmyState.MarchedThisSeason`. Deliberately **conservative**: a single flag over-restricts a
  mixed garrison (fresh troops in the same city cannot form a marching army until rollover) but can never
  let marched troops move twice. Per-troop provenance and morale laundering are OPEN_QUESTIONS 24. The flag
  is internal rule state and is **not** in the observation JSON; if mid-season snapshots land (ADR-005),
  they must persist it or a reload could reopen the exploit.
- **Fog-privacy ruling.** The generic `action` record for these two commands is **owner-only**
  (`ActionType.IsOwnerOnlyLog`), because `param_a` is an exact troop count and the current UI/log would
  otherwise publish it to every seat. Their domain events remain `owner_and_observers`, so a seat that can
  see the city still gets the city/army context. The same pass also wired the **pre-existing ADR-014 rule**
  for `research` into that set: a tech tree is a seat's secret, yet the generic action was still public and
  leaked `args.line` — an enforcement gap, not a new design rule. Covert missions keep their owner-only
  records too. **Every other ordinary action keeps a public `action` record**: the private set is exactly the
  covert missions, `research`, and the two garrison transfers.
- **Scripted siege guard.** `ScriptedController`'s `attack_city` planning now computes the same **effective
  garrison** as the validator (`city.Garrison` + same-owner defending army) while keeping its existing 50%
  safety margin, so the baseline no longer plans assaults the engine rejects.
- **Siege integration (as merged; semantics not locked).** A same-owner defending army counts toward the
  3:1 check and is **merged into the city garrison and removed before the rounds**. On a held assault the
  troops remain as garrison with no army entity; on capture the combined garrison is zeroed while the old
  retreat branch is effectively unreachable. Restoration / destruction / rout is a design decision, not an
  implementation detail → OPEN_QUESTIONS 25. Watchtower capture on march (any tower not owned by the mover)
  is recorded and public, with no war/trust/prestige consequences → OPEN_QUESTIONS 26; tower existence is
  always visible in the PR #3 UI → OPEN_QUESTIONS 27.
- **KingdomDemo organization (`6b91dd0`).** The 2330-line adapter is now seven partial-class files in
  `Assets/GameUI/Scripts/`, all `public partial class KingdomDemo : MonoBehaviour`:
  `KingdomDemo.cs` (class contract, **all serialized fields and shared state**, `Start`/`InitGame`),
  `.Visuals.cs` (markers, tinting, banners, fog), `.Input.cs` (clicks, selection, march, debug hooks),
  `.TurnFlow.cs` (`SubmitHuman`, seat rotation, rollover), `.Panels.cs` (OnGUI shell, map panels, order
  buttons), `.Report.cs` (court report, handover/victory screens, formatting) and `.Tabs.cs` (the five tab
  panels). Members were moved verbatim (1857 code lines before and after, line-multiset equality); the
  serialized-field block and its order are untouched; `KingdomDemo.cs` keeps its original `.meta` GUID, so
  the scene reference is unchanged; the six new files have fresh `.meta` GUIDs. This is a structural split,
  not a redesign — no gameplay, layout or text changes.
- **Verification.** Headless harness **249 passed / 0 failed** (`/tmp/luanshi-p6`, including the new PR #3
  regression tests: formation/merge, full/partial round-trips, merge-into-unmoved-army, rollover reset,
  atomic rejects, event order, fog privacy, scripted siege guard, the restored balance values, and the
  research generic-action privacy checks). `KingdomDemo` dotnet-builds against Unity 6000.6.2f1 assemblies
  with **0 warnings / 0 errors** (`/tmp/luanshi-ui-check`, all seven partials included). **Unity editor
  compile and play-mode verification remain PENDING** — the editor has not been available; no claim is made
  that the split or the PR #3 UI renders correctly in-engine.
- **Review items left out of scope here:** siege defensive-merge/capture-retreat semantics, watchtower
  capture reputation, UI fog policy for towers, and the TBTK `Hex_Highlight` material touched by PR #3 —
  each needs its own review (OPEN_QUESTIONS 25-27).

## ADR-019 — Unit types: one type per army, research-only unlock, per-type upkeep
**Date:** 2026-10-19 · **Status:** accepted · **Source:** team decision (user, 2026-10-19) · **Phase:** design completion (design doc §5.4)

The design doc named the four unit types (§5.3 相剋 table, §6.1 兵種係數) and their research gates
(§9 兵法 2/3) but never designed how they are produced, paid for, fed, or moved. Three questions were
put to the team; all three recommended options were chosen:

1. **One type per army.** Every field army and every city garrison has exactly one 兵種 (matching
   §5.1's single-valued 兵種 field). Merge / garrison-convert only between the same type; 相剋 plays
   out between armies on the map, not inside a mixed stack. Rationale: keeps the UI slider, the
   `ActionCommand` schema and combat resolution simple — the LLM contract stays flat.
2. **Research-only gating.** 步兵/弓兵 available from the start; 騎兵 requires 兵法 2, 攻城器械
   requires 兵法 3. No new buildings (no 馬廄/工坊) — 兵營's effects apply to all types. Rationale:
   research lines stay the "national direction" lever; the building system (D8) stays deferred.
3. **Per-type upkeep.** 步/弓 300 糧/1000/季, 騎 600, 器械 150 (all 待決) — replaces §3's flat 300.
   Rationale: cavalry becomes an economic decision, not a free upgrade; feeds directly into the
   養兵壓力 engine.

Design doc amended as **v1.1, new §5.4**: recruit costs per type (步 100 / 弓 150 / 騎 300 /
器械 500 金 per 1,000, all 待決), movement budgets (步/弓 6 — reconciling §5.1's generic 6 with
§5.3's 騎 8 — 騎 8, 器械 3), 器械 removes the §4.3 3:1 assault requirement, 弓兵 garrison gives
the 守城 +25%. 募兵 gains an `args.unit_type` parameter (schema v2 already carries `args`, so no
schema change); training stays per-army and type-agnostic; re-typing an army = disband and re-recruit
(training does not transfer).

**Implementation status: design only — no engine work in this commit.** The current engine has a
single troop count with no types; 兵法 L2/L3 remain purchasable-but-deferred until the type system
lands. When implemented, this is P14 engine work (after P9): `ArmyState.UnitType` +
`CityState.GarrisonType`, per-type recruit costs/upkeep/movement in `BalanceConfig`, type-aware
相剋 in the combat resolver, and the 募兵 UI gaining a type picker.

## ADR-020 — Unit specials (P15) and the fog-edge march rule
**Date:** 2026-10-20 · **Status:** accepted · **Source:** team direction (user, 2026-10-20) · **Phase:** P15

Two design rulings, implemented together. Design doc amended to **v1.2** (new §5.5 兵種特性 and
§5.6 迷霧邊界).

- **Unit specials — each 兵種 gets one signature mechanic** beyond its P14 stat line, so a type is a
  way of playing rather than a bigger number:
  - 步兵 **耐打**: takes ×0.85 losses in field battles (`InfantryLossTakenMult`, TBD).
  - 弓兵 **遠射**: may `attack_army` at range ≤ 2 (`ArcherAttackRange`, TBD); everyone else stays at
    adjacency. Combat resolution unchanged — 相剋 still applies.
  - 騎兵 **奔襲 ×2**: marches twice per season (`CavalryMarchesPerSeason`, TBD) — each march costs its
    own 號令 and its own 8-point budget. State: `ArmyState.MarchCountThisSeason` alongside the binary
    `MarchedThisSeason`, which keeps its ADR-018 meaning ("marched at all") for the garrison
    movement-laundering guard — marched cavalry garrisoned and re-formed does NOT get its second march
    back. The §5.3 平原衝鋒 (+30%, LOCKED) is also now implemented: cavalry attacking a defender on a
    plains hex gets ×1.3 (`CavalryPlainsChargeBonus`; plains = walkable, cost ≤ 1).
  - 攻城器械 **破城**: ×1.3 attacker multiplier against cities (`SiegeVsCityBonus`, LOCKED — the §5.3
    相剋 range), on top of the P14 3:1 waiver. **This answers OPEN_QUESTIONS 29.**
  Helpers live in `UnitTypes.cs` (`AttackRange` / `MarchesPerSeason` / `LossTakenMultiplier` /
  `SiegeVsCityMultiplier` / `FieldAttackMultiplier`); all numbers in `BalanceConfig`.
- **Fog-edge march rule (§5.6).** A march destination must be inside the acting seat's sight — the
  same `InSight` radii as the observation (city ±3 / army ±2 / tower ±5), enforced engine-side for
  every controller (invariant #3). Armies advance to the fog edge, reveal it instantly, and push
  further next season; effective per-season movement is ~2–3 hexes regardless of the march budget,
  which now bounds a single march instead of total seasonal movement. Scouts, watchtowers and
  garrison-forward basing gain purpose. The scripted baseline mirrors the sight check (it remains the
  documented canonical-state exception, ADR-011). Alternatives considered and rejected by the team:
  marching through fog with reveal-along-path (faster expansion, but fog stops being a boundary).
- **UI consequences.** March highlights skip out-of-sight hexes; ranged targets get an
  "Attack (ranged)" button; the movement-spent message is march-count aware. Panel wheel scrolling was
  hardened the same round (`TryWheelScroll`: whole-panel hit area, clamped offset, event consumed
  before `BeginScrollView` can double-apply; content height is `max(measured, view.height)` so a
  fitting panel has no phantom scroll range).
- **Verified** headless 2026-10-20: **324/324** assertions (+27 P15: ranged accept/reject by type and
  range, double-march accept/reject, garrison round-trip exhausts the second march, exact plains-charge
  and 耐打 first-round numbers, 破城 ×1.3, fog-edge accept/reject with no state change, scripted
  controller never plans out-of-sight marches). UI build 0/0; arity/namespace/external audits clean.
  Unity editor compiled and entered play mode during this round (0 console errors, demo initialised);
  full interactive gate (human wheel/click testing) still pending.
