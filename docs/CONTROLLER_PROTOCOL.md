# CONTROLLER_PROTOCOL.md — Human ↔ LLM Seat Control Protocol

> **This is the FYP research core.** The thesis studies switching a seat between a human and an
> LLM mid-game. Everything below exists so that (a) both controller types play by identical rules,
> and (b) a controller taking over a seat receives everything it legitimately could know — no more,
> no less. Status: **specification, not yet implemented** (see `docs/FEATURES.md`).

## 1. Controller types

| Type | Who decides | How commands are produced |
|---|---|---|
| `human` | A person at the screen (hot-seat) | UI clicks/typing → UI builds `ActionCommand`s |
| `llm` | An LLM via provider API | Observation → prompt → JSON actions → parse/validate |
| `scripted_ai` | Rule-based deterministic code | Direct C# logic producing `ActionCommand`s |

Per-seat config also records: faction, whether mid-game switching is allowed, and (for `llm`) model, prompt version, persona, and goals.

## 2. The single interface

All controllers implement one interface (C#, in `Assets/Agents/`):

```csharp
public interface IPlayerController
{
    SeatId Seat { get; }
    ControllerType Type { get; }                 // human | llm | scripted_ai

    // Called once when control of this seat transfers to this controller (also at game start).
    void OnHandover(HandoverBundle bundle);

    // Called when this seat's individual turn begins within a season.
    // Returns the full ordered plan for the turn (≤ command-point budget).
    Task<TurnPlan> PlanTurnAsync(TurnContext ctx, CancellationToken ct);
}
```

`TurnPlan` is an ordered list of `ActionCommand`s. The engine feeds them **one at a time** through the same pipeline used for human clicks:

```
ActionCommand → Validator (turn? schema? resources? prerequisites? visibility?) 
             → Rule handler → State mutation → Domain events → Observation updates
```

**Invariant:** there is no code path by which any controller — human, LLM, or scripted — mutates state except through this pipeline. Humans and LLMs differ only in how commands are *produced*, never in how they are *validated or executed*.

## 3. The switch (handover) protocol

Control transfer may happen at season boundaries or mid-turn between actions (exact allowed moments: see `docs/OPEN_QUESTIONS.md`). Trigger sources: player choice (禪讓/託孤/親政 from the design doc), timeout fallback (LLM → scripted), or room admin.

**Sequence:**

1. Engine freezes the seat's action intake.
2. Engine emits event `control_transferred { game_id, seat_id, season, from_type, to_type, reason, at }` — public or private visibility per switch reason (禪讓 is public theatre; silent fallback may be private to researchers).
3. Engine builds a **HandoverBundle** for the incoming controller (below).
4. `newController.OnHandover(bundle)` — for a human, this renders as a "briefing screen"; for an LLM, it becomes the system/context prompt payload.
5. Seat resumes; from here the new controller's commands flow through the standard pipeline.

### HandoverBundle contents

| Field | Contents | Why |
|---|---|---|
| `observation` | Current **filtered** observation snapshot: own full state; visible enemy cities/armies with estimation levels; known treaties; map as currently revealed | The new controller may only know what the seat may know — fog rules apply across the switch |
| `chronicle` | 史官 summaries for **all** past seasons (compact, one entry per season) | Long-term context without token explosion |
| `recent_events` | Detailed event log for the last N seasons (N configurable, default 4) | Tactical catch-up: battles, betrayals, messages |
| `pending_diplomacy` | Open proposals awaiting this seat, unread messages/密信, active treaty obligations with deadlines | Things that demand decisions |
| `action_space` | Currently legal action types, costs, prerequisites, remaining command points | LLMs must not guess the rules |
| `persona` (llm only) | Faction personality, long-term goals, current priorities | Keeps LLM behaviour comparable across runs |
| `control_history` | When this seat was human/LLM/scripted before | Research metadata: who was driving during earlier decisions |

Key design point: **the log is the memory.** Because every past action/event is already recorded (ADR-005), a handover needs no special human-to-LLM "explain the game" step — the bundle is assembled from the canonical log + visibility filter. A human taking over from an LLM gets the same bundle rendered as UI (and can inspect the LLM's past action history).

## 4. LLM turn contract

At each LLM turn, the adapter sends: rules summary, `action_space`, current observation, `chronicle` + `recent_events`, persona, and any new messages. The LLM must return JSON:

```json
{
  "season": 4,
  "actions": [
    { "type": "build", "target_id": "city-12", "building_type": "granary",
      "reason": "Protect food reserves before winter" },
    { "type": "send_message", "recipient_seat_id": "seat-5",
      "text": "We propose a three-season non-aggression pact." }
  ],
  "summary": "Prioritise food security and maintain the eastern border."
}
```

Handling rules:
1. Validate JSON schema → validate each action against game rules (resources, targets, CP budget, visibility) → execute valid prefix in order.
2. Invalid output → retry with error feedback (max retries configurable, default 1–2) → then **fallback**: scripted AI plays the turn, or pass. Never block the game.
3. Free text is never executed; only structured actions change state. `reason` fields are stored as research data, not parsed.
4. Every call is logged as an `LLMRun`: prompt version, model, full observation hash, raw output, validation outcome, retries, latency, token counts.

## 5. Event log schema (enables handover, replay, 史官)

```json
{
  "seq": 142, "game_id": "g-001", "season": 4, "phase": "player_turn",
  "actor_seat_id": "seat-2", "controller_type": "human",
  "event_type": "army_moved",
  "visibility": "owner_and_observers",
  "payload": { "army_id": "army-7", "from": [12, 8], "to": [14, 8] },
  "source_action_id": "a-123", "llm_run_id": null
}
```

- `controller_type` on every action answers the thesis question directly from the log: *who was driving when this decision was made, and what happened next?*
- `visibility` drives per-seat filtered observations; canonical state is never sent to any controller.
- **Implemented event types (2026-10-08):** `season_start`, `season_end`, `city_resolved`, `starvation`, `action`, `action_rejected`, `tax_levied`, `labor_lightened`, `land_reclaimed`, `recruit`, `troops_trained`, `city_fortified`, `disaster_relieved`, `army_moved`, `city_captured`, `battle_field`, `battle_siege`, `army_routed`, `army_destroyed`, `war_declared`, `trust_changed`, `prestige_changed`, `message_sent`, `treaty_proposed`, `treaty_responded`, `treaty_lapsed`, `treaty_ratified`, `treaty_expired`, `treaty_broken`, `gift_sent`. Two-party events carry `to_seat_id`/`from_seat_id` in the payload — the three visibility values have no "both parties" case.
- **Observation contract (ADR-011):** `GameEngine.Observe(seatId)` returns that seat's filtered world — own cities/armies in full, foreign ones only within 城市 ±3 / 軍隊 ±2 hexes of an own city/army (absent, not redacted, outside the radius), its own treaties and pending offers, and the filtered log (public · its own · addressed to it · `owner_and_observers` whose subject it can currently see). `Observation.ToJson()` is the payload the LLM adapter and HandoverBundle will carry; `ScriptedController` is the documented exception (a referee-grade baseline that reads canonical state).
- Scripted-AI games with a fixed seed must replay bit-identical (ADR-005); LLM games replay identically up to LLM outputs, which are preserved in `LLMRun`s.

## 6. Delegation granularity (from design §12)

Beyond whole-seat switching, the same protocol supports: **single-domain delegation** (e.g. military only to LLM, human keeps domestic/diplomacy) and **single-decision consultation** (human asks LLM for advice on one action, keeps veto). All three are the same mechanism: route the relevant action subset through a different controller, log identically.

## 7. Fairness & privacy invariants

- An LLM never sees canonical state, another seat's private observation, or another LLM's prompts/rationales.
- A human taking over a seat sees exactly that seat's legal information — the switch leaks nothing.
- Switch events are recorded permanently; researchers can always reconstruct who controlled what, when.
