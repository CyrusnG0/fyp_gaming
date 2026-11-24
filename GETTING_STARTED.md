# Getting started with LuanShi (《亂世》)

LuanShi is a Unity turn-based strategy game in which a person, a language model or a scripted player
can drive each faction. All of them act through one identical action schema and one authoritative
engine. This guide takes a teammate from a fresh clone to a running demo. Nothing here needs an API
key until the step that says so.

## 1. What you need

| Need | Detail |
| --- | --- |
| Git | Any recent version. |
| Unity | **6000.6.2f1** exactly (Unity Hub: install by version). |
| .NET SDK | Any current SDK. The engine tests build with the SDK's own target framework; `dotnet --version` must work. |
| Python 3 | Only for the static audits (`run-audits.sh`). |
| Disk | The first Unity import creates a large, untracked `Library/` folder. |
| An API key | Only for real-model matches. Not needed for the fake provider, the harness or replay. |

## 2. Get the code

```sh
git clone <repository-url>
cd <repository folder>
git fetch --tags
git checkout <tag or branch your coordinator gave you>
```

Use exactly the tag or commit you were given. Do not commit to it; create your own branch for any work.

The clone already contains the third-party **Turn Based ToolKit 3** source (`fyp_gaming/Assets/TBTK/`), so
no package import is needed. The repository root also holds the original `.unitypackage`; you do not
use it. If Unity reports a missing script or asset on first import, stop and tell the coordinator the
exact message rather than reimporting anything.

## 3. Open the project

1. In Unity Hub choose **Add project from disk** and select the **inner** `fyp_gaming/` folder, the
   one that contains `Assets/`, `Packages/` and `ProjectSettings/`. Do not select the repository root.
2. Open it with 6000.6.2f1 and wait for the first import (several minutes).
3. Open the scene `Assets/GameUI/Scenes/LuanShi_Demo.unity`.

**Do not save the scene.** Mode switches below are fields on the scene's `KingdomDemo` component. Change
them in the Inspector for a session, press Play, and discard the change afterwards (or revert with Git).
A saved scene diff will conflict with everyone else's.

## 4. Check the engine without Unity (no key, no network)

From the repository root:

```sh
dotnet run --project fyp_gaming/Tests/Harness/EngineHarness.csproj -c Release
```

The last line reports `N passed, 0 failed`. `0 failed` is the pass condition; the count grows as tests
are added. This runs every engine, prompt, parser, fake-provider and replay check offline. Two groups of
checks are skipped with a printed "skipped external ..." line when the recorded runs they use are not on
your machine; that is expected on a fresh clone (see section 8).

Related checks:

```sh
bash fyp_gaming/Tests/run-audits.sh                         # static audits
dotnet build fyp_gaming/Tests/UiCompileCheck/UiCompileCheck.csproj -c Release \
  -p:UnityManagedDir="<Unity install>/Editor/Data/Managed/UnityEngine"
```

The UI compile check type-checks the Unity scripts against your installed editor. It needs the project to
have been opened in Unity once (it reads `fyp_gaming/Library/ScriptAssemblies/Assembly-CSharp.dll`) and is
not a substitute for pressing Play.

## 5. Play with the fake provider (no key)

Select the `KingdomDemo` object in the scene and tick **Use Fake Llm** (`useFakeLlm`). The LLM seat then
answers from a scripted fake; every panel line and run record is labelled FAKE. Press Play.

- **Single LLM (default):** `humanSeats` is Wei, `llmSeats` is Shu.
- **Two-model spectator:** tick `spectatorMode`; set `spectatorSeasons`. Both seats are model-driven and you
  watch. With `useFakeLlm` ticked no provider is contacted.
- **Offline replay:** tick `replayMode` (section 8). No model is contacted in this mode, ever.

The fake proves the plumbing (parser, validator, turn flow, recording). It says nothing about how a real
model plays.

## 6. Local configuration and your key

Real-model matches read **`fyp_gaming/llm.local.json`**. That file is ignored by Git and is never shared.

```sh
cp fyp_gaming/llm.example.json fyp_gaming/llm.local.json
```

Put the key in an environment variable, never in a file:

```sh
export LUANSHI_LLM_API_KEY=...        # Windows PowerShell: $env:LUANSHI_LLM_API_KEY = "..."
```

Start Unity (or the harness) from a shell where that variable is set; a Unity Hub launched from the desktop
only sees variables that were set for your user account before it started. The example file only names the
variable (`api_key_env`). Key, model and base URL can also be overridden by `LUANSHI_LLM_API_KEY`,
`LUANSHI_LLM_MODEL` and `LUANSHI_LLM_BASE_URL`. Provider behaviour the code actually implements:

- The transport speaks **OpenAI-compatible chat completions**: `POST <base_url>/chat/completions` with a
  bearer key, `https` only. `json_mode` adds `response_format: json_object`.
- The defaults are DeepSeek (`https://api.deepseek.com`, model `deepseek-flash`). Recorded evidence in this
  project is from that provider only. Another OpenAI-compatible endpoint is accepted by the code; this
  guide makes no claim that it behaves the same.
- Reported reasoning tokens and a separate reasoning field are recorded when a provider returns them, and
  are never executed as orders.

Feature switches, all **off** in the example and off by default. Each only widens what the model is told
and allowed to submit; the engine still validates every order:

| Key | Adds |
| --- | --- |
| `enable_attack` | `attack_army` and `attack_city`, with the legal targets listed. |
| `enable_research` | The `research` order. |
| `enable_diplomacy` | Messages, treaty proposals and replies. |
| `enable_garrison_conversion` | `convert_garrison`: turn garrison troops into a field army. |
| `enable_manual_lookup` | Read-only rule lookups that cost no command points. |
| `enable_action_reference` | A fact block of costs and rules for the orders that are enabled. |
| `enable_unit_types` | Recruiting archer, cavalry and siege as well as infantry, with this match's costs and research gates. |

`enable_unit_types` states costs, research gates and rules as facts. It does not tell the model what to
recruit. Research gates and garrison rules are enforced by the engine exactly as for a human.

## 7. Probe the provider before a real match

A real match costs money. Before one, send at most three requests:

1. Copy the example to a separate probe file, e.g. `fyp_gaming/llm.probe.local.json` (ignored), and set
   `"max_total_requests": 3`.
2. Run the live smoke, which plays one turn through the same host path the game uses and caps itself at
   six requests whatever the file says:

```sh
dotnet run --project fyp_gaming/Tests/Harness/EngineHarness.csproj -c Release -- --live fyp_gaming/llm.probe.local.json
```

3. Read the `LIVE-SMOKE` lines: `state`, `requests`, `tokens` (or `unknown`), `last_result` and
   `state-changing action`. A model reply that is not valid JSON, an empty reply, a timeout and a
   rejected order are all visible there. If requests succeed but no order executes, stop and tell the
   coordinator; do not raise the caps to compensate.

Only then start a match, and keep `max_total_requests` and, if you set prices, `max_usd` small.

## 8. Replay a recorded match (LLM-only, zero API calls)

Tick `replayMode` and press Play. The replay re-executes the recorded orders through the real engine and
shows the recorded model statements. It makes no model call and does not accept input as a player.

- **It is LLM-only.** Mixed human/LLM replay is not supported, and a recording that contains non-LLM
  controllers is refused with a message rather than shown.
- **Choose a run:** the panel's **Load run...** button opens a run-ID box and the preset list. Presets come
  from `fyp_gaming/replay-presets.local.json` if it exists (copy `replay-presets.example.json` and edit),
  otherwise from a short built-in list. A missing, unreadable or empty preset file is stated on screen.
  `replayRunId` on the component sets the run opened at start.
- **A run opens only if its files are found and valid.** Each run needs four files, named after its id:
  `pilot-<id>-summary.json`, `<id>.jsonl`, `<id>-exchanges.ndjson` and `<id>-bootstrap.json`. The panel says
  *files not found*, *incomplete files* (and which are missing) or *failed validation* with the reason.
  Typing an id does not mean it can be replayed.
- **Who controlled the seats must be shown.** The request log holds only model requests, so a person's
  actions are never in it. A recording opens only if it declares an LLM-only host and LLM seats, or (an
  older recording) every seat made requests in every finished season and every command is attributed to the
  LLM; the panel says which. Otherwise it is refused as *unknown* or *not LLM-only*.
- **Switching runs.** Opening the run list pauses playback and it stays paused when you close the list. A run
  on a different map than the loaded scene is refused with the reason; the run already open is kept. City,
  tower and army markers are rebuilt for a run on the same map.
- **Rules are the recording's.** A run replays under the rules it was recorded with. If a recording does
  not say which city-assault rule it used, the panel says its provenance is unestablished instead of
  claiming an exact match.
- **What you see:** the model's own written explanation, labelled *model statement*; the provider's reported
  reasoning-token count (or *not reported*); and, behind a separate **Show debug** button, the provider's
  recorded reasoning stream when the recording has one. The statement is what the model chose to write. It
  is not verified private reasoning, and the debug text is unverified and may contain private context.
- **Recorded runs do not come with a clone.** `runs/` is ignored by Git. To get a recording, ask the
  coordinator for a curated replay package (the four files for each approved run). Put them in a `runs/`
  folder at the repository root, or in any folder and point `LUANSHI_RUNS_DIR` at it
  (`LUANSHI_REPLAY_PRESETS` can point at a preset file elsewhere). Exchange files hold full prompts and the
  provider's raw replies and bootstrap files hold the hidden world, so share them privately and never
  commit them.

## 9. Where things are logged

| What | Where |
| --- | --- |
| Run records of any game with a real or fake LLM | `runs/` at the repository root (ignored): `run-<id>.jsonl`, `run-<id>-exchanges.ndjson`, `run-<id>-bootstrap.json`, `pilot-run-<id>-summary.json`, and `runs/memory/`. |
| Unity console | Prefixes `[KingdomDemo]` and `[Replay]`. The agent line prints the redacted config, never the key. |
| Unity editor log | Windows `%LOCALAPPDATA%\Unity\Editor\Editor.log`; macOS `~/Library/Logs/Unity/Editor.log`; Linux `~/.config/unity3d/Editor.log`. |
| Harness | Standard output. The live smoke writes its recorder under the system temp folder and prints the path. |

## 10. What is not in a clone

| Not included | Why | What to do |
| --- | --- | --- |
| `fyp_gaming/llm.local.json`, `replay-presets.local.json` | Per-machine, ignored. | Create from the `*.example.json` files. |
| Your API key | Secret. | Environment variable only. |
| `runs/` recordings and memory | Local evidence, ignored; may hold private prompts. | A curated package from the coordinator. |
| `fyp_gaming/Library/`, `Temp/`, `Logs/` | Unity creates them. | Open the project once. |
| `docs/`, `doc/`, `AGENTS.md`, `README.md` at the repository root | Local-only team knowledge, ignored. | Ask for a copy if you need it; do not commit it. |

Never commit credentials, run files or local configs. `git status` should show none of them.

## 11. Availability in this snapshot

State what you actually exercised when you report. Modes and features that are not in this snapshot must
not be demonstrated as if they were.

- **Human to model handover on the same faction** is a separate piece of work and is **not** part of this
  snapshot unless the release notes for your tag say so.
- **Mixed human/LLM replay** is not implemented. Replay is LLM-only.
- **The fake provider** proves plumbing only. A recorded real-model match proves only what it recorded.
- Unity visual behaviour (what is drawn, how buttons feel) is verified by pressing Play, not by the engine
  harness. The harness and the UI compile check cannot show that a panel looks right.

## 12. If something goes wrong

- **Unity version prompt or script errors on import:** confirm the version is 6000.6.2f1 and the folder is
  the inner `fyp_gaming/`.
- **`0 failed` is not shown:** copy the first `FAIL` line and the commit you are on to the coordinator.
- **A real match stops early:** read the last `LIVE-SMOKE` or run-summary lines. A provider timeout, an
  empty reply that used the whole output budget and a rejected order each have their own recorded reason;
  the match never hands control to a person automatically.
- **Replay says files not found:** it searched the preset roots, `LUANSHI_RUNS_DIR` and the usual `runs/`
  locations. Check the four file names in section 8.
