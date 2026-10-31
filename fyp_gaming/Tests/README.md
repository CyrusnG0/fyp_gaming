# Test tooling (`fyp_gaming/Tests`)

Headless, tracked tests for the pure C# engine plus the static audits that used to live outside the
repo. Nothing here needs the Unity editor: the harness compiles `Assets/GameEngine/*.cs` into a console
app, so it runs on any machine with the .NET SDK.

This folder sits **outside `Assets/`**, so Unity does not compile or import it. There are still no
Unity Test Framework tests under `Assets/Tests`; the suite below is the reproducible test baseline
for LS-001 and LS-002, and Unity-editor verification stays a separate step.

## Quick start

```bash
bash fyp_gaming/Tests/run-tests.sh
```

Exit code `0` means every assertion passed. Invoking through `bash` works on every platform and does
not depend on the executable bit surviving checkout (`chmod +x` is optional if you prefer
`fyp_gaming/Tests/run-tests.sh` directly). The script finds the Unity project from its own location
and uses whatever `dotnet` SDK is on `PATH`; it writes `bin/`/`obj/` next to the project, both ignored.

Equivalent without bash (any OS/shell):

```bash
dotnet run --project fyp_gaming/Tests/Harness/EngineHarness.csproj -c Release
```

## Provenance: recovered vs new coverage

The historical suite survived only as a temp-directory scratch project (`Program.cs` + a csproj with
absolute paths). The assertions were recovered verbatim, split by feature into `Harness.*.cs`, and
compiled against the current engine (base `48da5a7`) — no expectation, fixture value or message was
changed. Verification: the recovered assertion lines and their section headers reproduce the
historical binary's stdout exactly (a 348-line prefix, all 324 assertions), so the count quoted in
older notes is reconfirmed rather than assumed. Full stdout is not identical: the `[new]` sections are
appended and the final summary line reports the new total.

| Kind | Count | How it is marked |
| --- | --- | --- |
| Recovered historical assertions | 324 | section header `== revolt (...) ==` etc., `PASS`/`FAIL` names unchanged |
| LS-001 new assertions (recovery + review) | 74 | section header starts `== [new]`, every test name starts `[new]` |
| — domestic economy smoke | 33 | `Harness.EconomySmoke.cs` |
| — diplomacy smoke | 41 | `Harness.DiplomacySmoke.cs` |
| LS-002 new assertions (event privacy) | 141 | section header starts `[new] LS-002`, every test name starts `[LS-002]` |
| Later demo / LLM / chat suites | 525 | sections tagged `[DEMO-*]`, `[LLM*]` and `[CHAT]` |
| **Total** | **1064** | printed as `1064 passed, 0 failed` |

The LS-001 files close gaps the historical suite left; they are smoke tests, not second full
suites. LS-002 adds the privacy/history regression suite in three focused modules
(`Harness.EventPrivacy.cs`, `.History.cs`, `.PublicPayloads.cs`), all under 400 lines.

- `Harness.EconomySmoke.cs` — the D1–D3 domestic actions directly: 徵稅 (gold gain, 民心 cost, 號令
  cost, clamp at 0), foreign/unknown-city rejects with no mutation, 輕徭 (spend, 民心 gain, clamp at
  100, atomic gold gate), 開墾 (one use per season, cumulative to the cap with a partial last gain,
  harvest payout, replay determinism). The recovered tests only observed tax and harvest indirectly
  through revolt/research fixtures.
- `Harness.DiplomacySmoke.cs` — proposals, ratification at rollover, lapsed offers, 贈禮
  trust/prestige, 背盟 trust penalties with bystanders, 宣戰 → 停戰, treaty fog, and replay
  determinism; the historical harness had no diplomacy assertions at all.
- `Harness.EventPrivacy*.cs` — frozen event audiences, per-seat projection, paired hidden worlds,
  destroyed/newly-revealed history, public battle/siege/merge payloads, prestige/gift privacy,
  espionage target gating and fail-safe unknown event types.

Recovered coverage by feature (all 324):

| File | Covers |
| --- | --- |
| `Harness.Revolt.cs` | 民變 revolt threshold, yields halted, `RevoltChanceMult` hook |
| `Harness.SupplyDesertion.cs` | 補給 / 斷補 desertion, morale loss |
| `Harness.SeasonalModifiers.cs` | season table, 嚴冬 food and march budget |
| `Harness.Victory.cs` | 一統天下 victory condition |
| `Harness.Observation.cs` | season name and observation payload (ADR-011) |
| `Harness.ScriptedController.cs` | scripted relief priority, winter march budget |
| `Harness.Research.cs` | 4 lines × 5 levels, point income, purchases, bonuses, privacy, replay |
| `Harness.Espionage.cs` | 偵察 / 散播謠言 / 挑撥離間, exposure, treasury, scripted behaviour |
| `Harness.Garrison.cs` | garrison ↔ army transfers, movement state, atomic rejects, fog, siege |
| `Harness.ResearchPrivacy.cs` | research actions and events are owner-only (ADR-014); Governance L5 prestige projection (LS-002) |
| `Harness.UnitTypes.cs` | P14 unit types: recruit, upkeep, movement, 相剋, siege defence, transfers |
| `Harness.UnitSpecials.cs` | P15 弓兵 range, 騎兵 double march + 平原衝鋒, 步兵 耐打, 器械 vs 城防, fog-edge march |

Shared fixtures live in `Harness.Scenarios.cs` and `Harness.CombatFixtures.cs`; `Program.cs` holds the
counters, `Check`/`Section` and `Main`, which calls each section in the original order.

Adding coverage: put feature tests in a new `Harness.<Feature>.cs` partial and call it from `Main`
after the recovered sections, so historical output stays a stable prefix. Name new assertions so they
cannot be mistaken for recovered ones.

## LLM memory MVP

The LLM runner supports game-scoped, seat-private memory through `LocalLlmMemoryStore`. It writes
bounded JSONL records under `runs/memory/` and extracts deterministic facts/lessons from engine
action results plus bounded decision summaries from the model. Records are keyed by game id and seat
id, so a new runner can reload the same game's memory without sharing it with another seat or game.
Memory retrieval and relevance ranking are intentionally deferred to a later phase; the current
memory list is exposed by `LlmTurnRunner.Memories` for that integration.
Before prompt construction, the runner now ranks matching memories by action/target relevance,
importance, confidence, and recency, then applies a 3,000-character budget. Retrieved records are
rendered as untrusted data and are rejected when their game or seat does not match the observation.
At a new season boundary, the runner compacts the store: expired records are removed, exact
duplicates are merged, the store is capped at 100 records, and conflicting action outcomes retain
supersession metadata so the newer outcome wins retrieval without erasing provenance.

## Version control

`fyp_gaming/.gitignore` ignores `*.csproj`/`*.sln` because Unity regenerates those files. The two
projects here are hand-written test tooling, so `Tests/.gitignore` re-includes them with `!*.csproj`;
`git check-ignore fyp_gaming/Tests/Harness/EngineHarness.csproj` reports that negation rather than an
ignore rule. Both scripts also carry `*.sh text eol=lf` in `Tests/.gitattributes`, so a Windows
checkout keeps them runnable under `bash`.

## UI compile check (optional, needs a Unity install)

`Tests/UiCompileCheck` type-checks `Assets/GameEngine` + `Assets/GameUI/Scripts` outside the editor.
It needs two inputs and refuses to guess them:

- `UnityManagedDir` — the editor's `Editor/Data/Managed/UnityEngine` folder. Pass
  `-p:UnityManagedDir=...` or set `UNITY_MANAGED_DIR`.
- `TbtkAssembly` — a Unity-compiled assembly that provides the `TBTK` types the UI calls. Defaults to
  `fyp_gaming/Library/ScriptAssemblies/Assembly-CSharp.dll`, which exists after the project has been
  opened in the editor once; override with `-p:TbtkAssembly=...` if TBTK lands elsewhere.

```bash
UNITY_MANAGED_DIR="/path/to/Unity/Hub/Editor/<version>/Editor/Data/Managed/UnityEngine" \
  dotnet build fyp_gaming/Tests/UiCompileCheck/UiCompileCheck.csproj
```

A fresh checkout (or a git worktree) has no `Library/`, so this target needs a machine where the
project has been opened in Unity. That is a check, not a test: it proves the scripts compile, not that
the game runs.

## Static audits

Recovered from the same temp workspace and made path-independent (they now import each other from
their own folder instead of a hardcoded temp path, take any number of files, and exit non-zero on a
finding):

| Script | Checks |
| --- | --- |
| `audits/arity_audit.py` | every call site of a *locally declared* method passes between its required and total parameter counts (optional parameters, interpolation holes, comments/strings) |
| `audits/external_audit.py` | calls into the engine (`.Observe(...)`, `.FindCity(...)`, …) against a recorded signature table in the script |
| `audits/namespace_guard.py` | every attribute and fully-qualified type resolves through a namespace the file imports (e.g. `[FormerlySerializedAs]` needs `UnityEngine.Serialization`) |
| `audits/canonical_log_audit.py` | no `engine.Log`, `.Log.Records`, `EventLog`, `EventRecord`, `ToJsonLines` or `luanshi_log` anywhere in `Assets/GameUI/Scripts` (LS-014); comments are stripped and spacing around dots is normalised |
| `audits/canonical_log_audit_selftest.py` | the audit rejects spaced, `?.`, aliased and declaration forms, while accepting `Debug.Log` and comment-only mentions |
| `audits/report_state_audit.py` | `KingdomDemo.Report.cs` builds the court report from the seat's observation, never from canonical city/army state (LS-003 privacy) |

```bash
bash fyp_gaming/Tests/run-audits.sh              # defaults to Assets/GameUI/Scripts/*.cs
bash fyp_gaming/Tests/run-audits.sh path/to/File.cs ...
```

Known limits, unchanged from the historical scripts:

- `external_audit.py` carries a hand-maintained signature table. Update it when an engine signature
  changes; it is a guard, not a source of truth.
- `arity_audit.py` compares names, not types. It is written for the adapter/UI layer; running it over
  `Assets/GameEngine/*.cs` reports false positives where a local method shadows a BCL or foreign
  method of the same name (`ToString("x4")`, `Log.Emit` vs the engine's own `Emit`). The runner
  therefore defaults to the UI scripts only.

## Layout

```
fyp_gaming/Tests/
├── README.md                    this file
├── .gitattributes               *.sh text eol=lf
├── .gitignore                   ignores bin/obj, re-includes *.csproj
├── run-tests.sh                 portable harness runner
├── run-audits.sh                portable audit runner
├── Harness/
│   ├── EngineHarness.csproj     net<installed SDK> console app, compiles ../../Assets/GameEngine/*.cs
│   ├── Program.cs               counters, Check/Section, Main
│   ├── Harness.Scenarios.cs     shared fixtures
│   ├── Harness.CombatFixtures.cs P14/P15 combat fixtures
│   ├── Harness.<Feature>.cs     one file per recovered section
│   ├── Harness.EconomySmoke.cs  [new] D1–D3 domestic actions
│   ├── Harness.DiplomacySmoke.cs [new] treaties, trust, war
│   ├── Harness.EventPrivacy.cs  [LS-002] fixtures, action/gift/prestige privacy, fail-safe
│   ├── Harness.EventPrivacy.History.cs [LS-002] paired worlds and stable history
│   ├── Harness.EventPrivacy.PublicPayloads.cs [LS-002] battles, sieges, espionage oracle
│   ├── Harness.HistoryExport.cs [LS-014] serializer, writer/path, seat isolation
│   └── Harness.HistoryExport.Names.cs [LS-014] filename encoding, collisions, length cap
├── UiCompileCheck/
│   └── UiCompileCheck.csproj    optional, needs UnityManagedDir + TbtkAssembly
└── audits/
    ├── arity_audit.py
    ├── canonical_log_audit.py
    ├── canonical_log_audit_selftest.py
    ├── external_audit.py
    ├── namespace_guard.py
    └── report_state_audit.py
```

No generated fixtures are checked in: every expectation is an inline assertion, and the harness is
fully seeded, so a run needs no baseline files. If a future test needs recorded engine output, store
it as a small hand-reviewed fixture, never as a captured dump.

## Requirements

- .NET SDK 8 or newer on `PATH`. The csproj targets `net$(BundledNETCoreAppTargetFrameworkVersion)`,
  so the harness builds and runs on the SDK's own runtime without extra downloads. To force a
  framework, restore and build with that framework (a plain build reuses the previous restore):

  ```bash
  dotnet restore fyp_gaming/Tests/Harness/EngineHarness.csproj -p:TargetFramework=net8.0
  dotnet build fyp_gaming/Tests/Harness/EngineHarness.csproj --no-restore -p:TargetFramework=net8.0
  ```
- Python 3 for the audits (set `PYTHON` if `python3`/`python`/`py` are not what you want).
- No NuGet packages. The engine is pure C# and the harness references nothing else.
