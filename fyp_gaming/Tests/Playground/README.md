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

## Integration note

The production `LlmTurnRunner` currently wires only `manual_lookup`. This playground shows the
contracts needed for the other three tools, the strategic note and a safe Check mode; those four
interfaces still need a production wiring change (see the worker report).
