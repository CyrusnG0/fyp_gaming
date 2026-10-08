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
