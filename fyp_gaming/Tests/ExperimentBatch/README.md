# Offline experiment batch

Runs a **matched pair of matches** (one declared difference) through the PRODUCTION `LlmTurnRunner`,
`SpectatorSession`, `LlmRunRecorder` and `SpectatorSummary`, with a scripted fake provider, and keeps
auditable evidence. It exists so a later real-provider batch is repeatable. No network call, no
credential, no Unity assembly. Scripted replies are plumbing evidence, never model behaviour.

```sh
dotnet run --project fyp_gaming/Tests/ExperimentBatch/ExperimentBatch.csproj -c Release -- validate  <manifest>
dotnet run --project fyp_gaming/Tests/ExperimentBatch/ExperimentBatch.csproj -c Release -- run       <manifest> --out <dir>
dotnet run --project fyp_gaming/Tests/ExperimentBatch/ExperimentBatch.csproj -c Release -- test
python tools/luanshi_experiment_batch.py run <manifest> --out <dir> --require-head   # check, run, then independent audit
python tools/luanshi_experiment_batch.py audit <dir>/<batch_id>                      # re-verify saved evidence from files alone
python tools/test_luanshi_experiment_batch.py
```

## Manifest (`examples/example-manifest.json`)

| Section | Content |
|---|---|
| `pinned_commit`, `scenario`, `balance_profile`, `seasons`, `first_mover`, `factions` | what is played; `first_mover` and the seat→faction map are checked against the scenario (`compact-contact-v1`, or `-swapped` to give the other faction the first move) |
| `personas` | `enabled` plus seat→persona overrides, applied through `LlmConfig.SetPersonaOverride` |
| `provider` | `kind` must be `fake`; `model`; optional `faults` (seat, nth request of that seat, `provider_error` / `missing_usage` / `length_truncated` / `empty_reply`) |
| `features` | `deliberation`, `rich_profile`, `research`, `spatial_context`, `visible_march_routes`, `attack`, `manual_lookup` |
| `ceilings` | per-match: requests/turn and total, actions, inquiries, turn and session seconds, request timeout, output and session input/output tokens, prompt chars. All required and positive. |
| `batch_ceilings` | whole batch: matches, requests, input/output tokens, wall seconds, per-match wall seconds, `unknown_usage_policy` |
| `seeds`, `experiment` | one pair (control, treatment) per seed; `experiment.variable` names the single flag that differs |

The loader refuses unknown fields and any credential-shaped key or value (never echoed). Validation also
builds the production `LlmConfig` and asks the production validator, so a ceiling set the runner would
refuse is refused before any match.

## What a run keeps (`<out>/<batch_id>/`)

`manifest.json`, `batch-result.json`, `batch-report.txt`, and per match `settings.json` (flat settings and
hash), `<run>.jsonl` (per-request records), `<run>-exchanges.ndjson` (recorder's prompts and replies),
`prompts.ndjson` (the batch's own independent capture of every prompt and reply with hashes),
`pilot-<run>-summary.json`, `match.json` (outcome, file hashes, audit). Completed, stopped, failed,
errored and **skipped** matches are all kept; an existing non-empty output directory is refused.

Status words: `completed` = every turn ended by the model's own end_turn; `completed_with_stops` = the
seasons finished but some turn was cut by a ceiling or provider error; `stopped` = per-match wall limit;
`failed` = the session did not complete; `error` = the harness threw; `skipped` = a batch ceiling forbade it.

## Audit rules

Every figure is rebuilt from the **per-request records**, not the summary: request counts (records,
exchanges, prompt capture, runner, summary), token sums, accepted/rejected orders, per-turn requests and
accepted orders. The summary's per-turn `commands` list holds only the **last** command, so orders earlier
in the turn are reported as not visible there. Missing usage makes a total *unknown* (never zero); cut
prompt sections, `finish_reason=length`, empty replies, unknown cost and reasoning counts are listed.

## Fake and not-yet-real

- `spatial_context` is **not integrated** in production. The fake switch only steers which legal march the
  script picks (toward / away from the prize) and every result says `production_integrated=false`. The
  arms' system prompts are byte-identical; a final-state difference is scripting.
- The scripted "model" and its token counts are deterministic functions of the text.
- Only the headless `SpectatorSession` host is exercised; the Unity `AgentTurnHost` path is not.
- Run ids contain a timestamp and a random suffix, so file names differ between runs; settings hashes,
  prompts and final states do not.
