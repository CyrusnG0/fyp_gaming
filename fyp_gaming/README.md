# LuanShi team integration

Open this folder in Unity **6000.6.2f1**. The spectator scene is `Assets/GameUI/Scenes/LuanShi_Demo.unity`. Read the [teammate handoff](../TEAM_HANDOFF.md) for task ownership, provider setup, baseline evidence and reporting.

## Where to work

| Base | Use it for |
| --- | --- |
| `main` | Stable baseline and clean comparisons with the 20-season run. |
| `codex/team-integration` | Shared test baseline combining the combat candidate, expanded manual, offline replay and its in-progress summary fixes. Start a task branch from this base when testing against the combined work. |
| Your task branch | Your own changes. Open a PR targeting `codex/team-integration` when ready for the integration owner to review. |

Once the integration owner has published the branch, start from a clean checkout:

```sh
git fetch origin
git switch -c teammate/<task> origin/codex/team-integration
```

Record the starting SHA. Ask the integration owner before moving a change to `main`. Keep API keys, machine settings, paid run logs and raw exchanges local. Use a fake provider for initial tests.

## What you can inspect

- **Shared baseline at `8c879fe`.** Spectator play, legal movement and domestic actions, personas, balance profiles, manual lookup and provider diagnostics. The real 20-season results and the empty-reply case are in the handoff.
- **Combat at `5fbf0ef`.** The branch contains opt-in LLM field attack and city assault with legal options and bounded feedback. Set `enable_attack` only when testing this candidate. Unity battle dispatch and visuals still need a scripted smoke test before a paid run.
- **Manual topics at `e5922a0`.** The expanded manual can explain mechanics. Actual LLM action coverage depends on the runner, parser and engine validator.
- **Replay at `7b71ef6` plus the saved work-in-progress files.** The original demo recording is the supported input. Ask the integration owner for approved local fixtures. General run selection, the 20-season replay and target Unity playback still need verification. The normal harness does not register the replay-specific suite.
- **Paused or proposed.** Deliberation ledger wiring, richer decision context, food relief and upkeep, the compact scenario and victory scoring, LLM diplomacy and research, and analysis improvements. The handoff has the design gates and owners. The older standalone movement pilot and alternate budget prototype are historical references. The current base already contains the integrated movement and deliberation foundation. UI scaling and movement/memory review branches have patch equivalents on main.

## Checks from this Unity project folder

```sh
dotnet run --project Tests/Harness/EngineHarness.csproj -c Release
dotnet run --project Tests/ResponseDiagnostics/ResponseDiagnostics.csproj -c Release
bash Tests/run-audits.sh
```

The diagnostics start a local HTTP stub and need a runner that permits loopback. Use the Unity editor to confirm Play Mode behavior, and report the commit, exact scenario, observed result and any pending check.
