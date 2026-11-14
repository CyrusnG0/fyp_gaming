# LuanShi headless playground

A reusable, deterministic playground for exercising the real engine/controller contracts with a
scripted **fake provider** by default. It is separate from the Unity scene and from the tracked
engine harness; it compiles the real `Assets/GameEngine` sources into a console app.

Run from the `fyp_gaming` folder:

```sh
dotnet run --project Tests/Playground/Playground.csproj -c Release -- run
dotnet run --project Tests/Playground/Playground.csproj -c Release -- test
```

`run` writes a readable `trace.txt` and machine-readable `results.json` under
`Tests/Playground/out/<run-id>/` (ignored by Git). `test` runs focused in-process checks and exits
non-zero on failure.

## What one fake session does

- Two LLM-controlled factions (`seat-a` / Wei, `seat-b` / Shu) on a deterministic 13-hex corridor.
  Fog is deliberate: seat-a sees the neutral Crossroads at (3,0); seat-b sees only its own side.
- The real prompt builder, action catalogue, movement planner, strict action/investigation parsers,
  investigation service, memory store and deliberation-budget ledger are used unchanged.
- The model may spend bounded requests on four read-only tools: `manual_lookup`, `inspect_entity`,
  `search_history`, `recall_memory`. Reads spend no command point and no action admission.
- The model may save a bounded strategic note (`{"note":"..."}`). It is stored per match+faction and
  shown only to that faction on its next turn, labelled untrusted model data.
- Action mode is either **Execute** (normal authoritative `SubmitAs` path) or **Check** (replay the
  accepted history into a fresh engine, verify the replay hash, validate the candidate there). Check
  never calls `Submit` on live state. The default Execute run also performs a preflight Check and
  records the live state hash before and after it.
- The trace records the exact system and user prompt per request, the filtered observation, offered
  actions, relevant memory, prior inquiry results, relevant configuration, prompt version and seed.

## Provider safety

The default provider is `fake` and performs no network I/O. A non-fake provider requires an explicit
local opt-in (`--allow-real-provider`) plus a local API key and is not exercised here. No paid call
is made by either command above.

## Useful options

```sh
--seed <n>            deterministic seed (default 4242)
--seasons <n>         seasons to play (default 2)
--out <dir>           output directory
--mode check          validate orders only; never execute
--no-preflight-check  skip the isolated Check before an executed order
--no-fixture-memories do not seed deterministic fixture memories
--keep-stores         keep the local memory/notes stores
```

## Production runner deliberation (opt-in, default OFF)

The real `Assets/GameEngine/Agents/LlmTurnRunner` now has its own opt-in deliberation mode. The
`test` command exercises that actual runner path (not a parallel loop) through a fake provider and
an `IPlanningHost` shaped like the Unity/spectator host.

Enable with `LlmConfig.EnableDeliberation = true` (config key `enable_deliberation`). When off,
every existing prompt, version, schema hash, counter and limit is unchanged; a test asserts the
legacy `demo-slice-4` prompt and the absence of a ledger.

When on, the model may spend requests on all four strict read-only tools (`manual_lookup`,
`inspect_entity`, `search_history`, `recall_memory`) and may save one bounded private note
(`{"note":"..."}`). Then it chooses a legal action or `end_turn`; the runner never forces it to act.

Deliberation-only settings (all ignored while the feature is off):

| Key | Default | Meaning |
| --- | --- | --- |
| `max_inquiries_per_turn` | `4` | Read-only tool admissions per seat turn |
| `max_inquiries_per_session` | `20` | Tool admissions across all seats on one match ledger |
| `max_accepted_actions_per_session` | `0` | Accepted actions across the session; `0` = no session cap |
| `max_note_chars` | `400` | One strategic note |
| `max_retained_inquiry_results` | `4` | Earlier bounded inquiry results kept per seat |
| `inquiry_max_response_chars` | `16000` | Serialized result cap; too-large results are replaced, never retried blindly |
| `deliberation_input_token_bound` | `0` | Reservation hint for the ledger only; **not an enforced token cap** (nothing is truncated or refused because of it; real usage is settled from the provider report) |
| `max_deliberation_turn_millis` | `0` | Per-turn wall-time cap; also stops an in-flight request (`TurnTimeLimit`); `0` = none |

Accounting semantics:

- One `DeliberationBudgetLedger` is created per match id for the runner's lifetime (or supplied by
  the host), so session request/action limits are shared by every seat on that runner.
  Per-turn counters are scoped to the host-bound (seat, turn token); per-seat totals are evidence.
- `max_requests_per_turn` / `max_total_requests` are the request budget.
- In deliberation mode the action budget counts **accepted** actions, not attempts. A rejected
  order releases its reservation and does not consume an accepted-action slot. Feature-off keeps
  the historical attempt-based counter (an accepted/rejected submit both count).
- Every provider request, tool dispatch and action dispatch is admitted, `MarkDispatched` before
  the work, and settled (or cancelled after a denial) exactly once, including failures,
  timeouts and host cancellation. Notes and results never cross seats or matches.

The strategic-note store is pluggable (`ILlmStrategicNoteStore`): the runner defaults to an
in-memory store and exposes `LocalLlmStrategicNoteStore` for persistent local evidence. Inquiry
results and notes are always labelled untrusted data in the prompt.

## Extension points (research/diplomacy workstream)

`LlmDeliberationExtension.cs` documents the clean seams. A future read-only tool is added through
the existing strict request contract/parser, a read-only service method and one
`InvestigationDispatcher` case; the runner dispatches it automatically. A future game action must
first exist in the engine validator, `ActionCatalogue` and `LlmActionParser`. This worker adds no
research or diplomacy mechanics and no command the engine lacks.

## Integration note

The production runner wires the four read-only tools and the private note only when
`EnableDeliberation` is on; otherwise it keeps the earlier `EnableManualLookup` manual-only path.
The Unity host needs no change to compile: new constructor parameters are optional, and the
default in-memory note store and lazy ledger require no scene hookup. To persist notes or share one
ledger across separately-created runners for the same match, the host may inject
`ILlmStrategicNoteStore` / `DeliberationBudgetLedger` through the existing constructor.
