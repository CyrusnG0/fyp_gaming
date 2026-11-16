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
| `features` | `deliberation`, `rich_profile`, `research`, `spatial_context`, `visible_march_routes`, `attack`, `manual_lookup`. `spatial_context` drives the production `LlmConfig.SpatialContext.Level`; a spatial pair needs `visible_march_routes=true` (both arms) |
| `spatial` | `level`: `standard` or `rich`, the production preset the arm with `spatial_context=true` uses. Identical in both arms; the other arm is `off` |
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
seasons finished but some turn was cut by a ceiling or provider error; `stopped` = a match or batch wall-clock limit ended it mid-match;
`failed` = the session did not complete; `error` = the harness threw; `skipped` = a batch ceiling forbade it.

## Records consistent is not batch complete

`batch-result.json` and the report keep two separate verdicts. `all_audits_ok` / "records internally
consistent" means the evidence that exists agrees with itself. `matched_batch_complete` is true only when every
planned pair ran BOTH arms to the end of their seasons (`completed` or `completed_with_stops`); a skipped,
stopped, failed or errored arm leaves its pair in `incomplete_pairs` and the report says "not a completed
experiment". The Python audit re-derives this from the saved files and exits **3** (records consistent, batch
incomplete) rather than 0. A skipped arm keeps `settings.json` and `match.json`; the audit tolerates a missing
`settings.json` and reports the pair incomplete.

## Ceilings: what is and is not exact

- **Match and batch wall-clock limits** are checked between session ticks, during a match (the earlier of the
  match limit and the batch time remaining). One request in flight can overrun a limit by up to roughly
  `request_timeout_ms`; this is not measured against a real provider. A match cut mid-turn is `stopped`, names the
  limit (`wall_limit`), and the audit reports the one request that was sent without a recorded reply.
- **Input-token ceilings** are checked before each request against usage already reported, so the request that
  crosses the ceiling completes: a match can overshoot by up to one request's input, and the batch total by up
  to one request per started match. Output ceilings are strict. The batch checks token ceilings before a match
  using the per-match ceilings, so a ceiling below one match's own ceiling starts nothing.
- `unknown_usage_policy`: `stop_batch` skips later matches once any match's usage is unknown;
  `continue_on_unknown` keeps going and enforces token ceilings on known usage only. `matches_with_unknown_usage`
  counts each ran match by its own usage.
- **System prompts** are compared per request, aligned by (seat, nth request of that seat). The arms are
  "identical" only with the same requests and equal prompts; otherwise the report gives aligned / differing /
  one-sided counts.

## Pinned commit and the working tree

`python tools/luanshi_experiment_batch.py run <manifest> --out <dir> --require-head` refuses when HEAD is not the
pinned commit, **any tracked file is modified**, or **an untracked file would be built**: `*.cs` directly in the
batch project, `**/*.cs` under `Assets/GameEngine`, the `.csproj`, `Harness/demo-map.json` and MSBuild/NuGet config
(`Directory.Build.*`, `Directory.Packages.props`, `global.json`, `nuget.config`) in the project's ancestor folders.
Gitignored files count too, since an ignored `.cs` in a compiled path is still compiled. Other untracked files (runs,
logs, notes, `bin/`, `obj/`) are ignored. Git is read with NUL-delimited output, so non-ASCII file names are not
lost to Git's quoted display form. `--allow-dirty` is the explicit override for both. Every `run` writes
`provenance.json` (HEAD, dirty files, untracked build inputs, flags) into the batch directory and the audit prints it.
The check covers **repository inputs only**: external .NET configuration (user or machine NuGet config, `DOTNET_*`
environment variables, the installed SDK and runtime) is separate environment evidence and is not verified here.
The example manifest's `pinned_commit` is the commit its code was tested at; a manifest cannot name the commit that
contains it. For a real batch generate a copy pinned to the final integration commit and keep it outside the tracked
tree, so writing it does not move HEAD: `python tools/luanshi_pin_manifest.py <example> --out <path>/manifest.json`
(HEAD by default, `--commit <rev>` otherwise; never overwrites), then `run ... --require-head`.
`LUANSHI_BATCH_TEST_DELAY_MS` is a test-only seam that delays every fake reply; it is never set in a real run.

## Audit rules

Every figure is rebuilt from the **per-request records**, not the summary: request counts (records,
exchanges, prompt capture, runner, summary), token sums, accepted/rejected orders, per-turn requests and
accepted orders. The summary's per-turn `commands` list holds only the **last** command, so orders earlier
in the turn are reported as not visible there. Missing usage makes a total *unknown* (never zero); cut
prompt sections, `finish_reason=length`, empty replies, unknown cost and reasoning counts are listed.

## The spatial-context pair

`experiment.variable = "spatial_context"` runs the PRODUCTION spatial map context as the one difference:

- **Configuration.** Control sets `LlmConfig.SpatialContext.Level = Off`, treatment sets it to `spatial.level`.
  `RequireVisibleMarchRoutes` is true in both arms (validation refuses a spatial pair otherwise, because spatial context
  would silently enforce it in one arm only). Seed, scenario, factions, personas, research, deliberation and every ceiling
  are the same, which the settings check proves (`features.spatial_context` is the only difference). Both arms get the
  same `game_id`, so the arm name never reaches a prompt.
- **The prompts are production prompts.** The fake provider receives exactly what `LlmTurnRunner` builds. The pair check
  (`spatial_problems` in `batch-result.json`, "spatial pair check" in the report, `spatial_evidence` in each `match.json`)
  verifies on the dispatched requests: the control carries no spatial material and every treatment request carries the
  production addendum and `SPATIAL CONTEXT` section; the treatment system prompt is exactly the control's plus the
  addendum; the prompt version names spatial only in the treatment and the schema hashes differ; the seat observation hash
  is equal in every aligned request. `tools/luanshi_batch_spatial_audit.py` repeats these checks from the saved files.
- **Truncation is recorded, not assumed.** Each treatment request carries the production `spatial_truncation` report; the
  evidence counts reports and truncated requests, names the reasons (for example `move_option_cap`), sums the items
  dropped and keeps the largest prompt size. In the example pair every treatment request truncates the move list.
- **Privacy, per dispatch.** At every dispatch the batch records what that seat's own `Observe(seat)` showed of the other
  seats' cities and armies (visible, scouted or hidden). A city id, city name or army id that was hidden at that dispatch
  and had never been shown to the seat must not appear in that request's prompt; any hit fails the pair. One that was shown
  earlier and is hidden again is retained knowledge: it is counted (`retained_known_mentions`), not called a leak, and
  whether its wording is stale is not judged. This proves absence of those exact strings only. It trusts `Observe` as the
  oracle, and does not cover numbers, treaty or event text, paraphrase or neutral cities (`BatchPrivacy.Scope`).
- **World digest.** A deterministic digest walks every public field and property reachable from `GameState` (factions,
  diplomacy, cities, armies, watch towers and the whole map: size, walkable, costs, neighbour tables) plus the balance
  profile. It does not cover private fields (the RNG stream, the action counter, the next proposal id) or the event log
  (`BatchWorldDigest.Scope`, copied into each result as `world_digest_scope`). It is compared at the start (must match) and
  the end (`final_world_identical`). The scripted replies ignore the spatial setting, so the end states match by
  construction: that shows the prompt is the only channel, not that a model would play the same.
- **Memory is not exercised.** The runner is built with no durable `ILlmMemoryStore` (only the in-memory strategic-note
  store), so production memory extraction, consolidation and recall do not run in this batch and nothing here validates
  long-term memory.
- **What differs by design.** `INFORMATION ACCESS` reports the session tokens left, which the larger treatment prompts
  consume faster, so that line differs between the arms; it is a consequence of the treatment, not a leak.

An arm that is stopped, failed or skipped leaves its pair incomplete; the spatial check is only judged on a pair whose
arms both reached the end of their seasons.

## Fake and not-yet-real

- Spatial context goes through the production prompt path, but the replies are scripted and identical in both arms, so
  nothing here says how a model uses the map. A final-state difference between arms would need a real model.
- The scripted "model" and its token counts are deterministic functions of the text.
- Only the headless `SpectatorSession` host is exercised; the Unity `AgentTurnHost` path is not.
- Run ids contain a timestamp and a random suffix, so file names differ between runs; settings hashes,
  prompts and final states do not.
