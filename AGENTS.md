# AGENTS.md — 《亂世》(LuanShi) FYP Project

Read this file first. It orients any agent (or human) starting a fresh session with zero context.

## What this project is

《亂世》(LuanShi, "Turbulent Times") is a **turn-based Three Kingdoms strategy game** built as a two-person **Final-Year Project**. Players rule a faction: domestic management (cities, food, gold, armies), diplomacy (treaties, trust, betrayal), espionage, and research — under fog of war, one turn = one season.

**The FYP research core is the human ↔ LLM control switch.** Every seat (up to 8) can be driven by a human, an LLM, or a scripted AI, and control can be handed over mid-game. All controllers act through **one identical action schema and one authoritative validator**. The thesis compares how humans vs. models play the same game under the same rules and information limits. See `docs/CONTROLLER_PROTOCOL.md` — it is the most important document in this repo after the design docs.

## Non-negotiable invariants

1. **Authoritative game core only.** Only the game engine mutates canonical state. UI, LLMs, and scripted AI submit `ActionCommand`s; the engine validates and executes.
2. **One schema for everyone.** Humans clicking UI and LLMs emitting JSON produce the same commands through the same validation path.
3. **Fog of war is enforced server-side (engine-side).** Each seat gets a *filtered observation*, never raw canonical state. The UI is not trusted.
4. **Everything is logged.** Every action records who/what/result into an append-only event log. This powers replay, the historian (史官), and LLM handover catch-up.
5. **No magic numbers.** Undecided values are marked 待決 (TBD) and live in balance config, not scattered in code.
6. **Max 8 seats, data-driven.** Never hardcode player count (3 or 8); use faction/map config.

## Repo layout

```
fyp_gaming/                  ← repo root (you are here)
├── AGENTS.md                ← this file
├── doc/                     ← design & report documents (source of truth for WHAT we build)
│   ├── LuanShi-Game-Design-v1.md                     full game design (Chinese)
│   ├── LuanShi-Design-Implementation-Report-v1.1.docx architecture/report (v1.1 = Unity-native)
│   └── LuanShi-Design-Implementation-Report-v1.docx   superseded (web-stack version, kept for history)
├── docs/                    ← working documents (source of truth for WHERE WE ARE)
│   ├── CURRENT_STATE.md     what is done / in progress / next — UPDATE THIS after every work session
│   ├── FEATURES.md          feature checklist with status
│   ├── DECISIONS.md         architecture decision records (ADRs) — check before changing architecture
│   ├── CONTROLLER_PROTOCOL.md  human↔LLM switch interface & handover protocol (FYP core)
│   └── OPEN_QUESTIONS.md    unresolved decisions waiting on the team
└── fyp_gaming/              ← Unity project (Unity 6000.6.2f1, URP 2D)
    └── Assets/              (planned structure — see DECISIONS.md ADR-001)
        ├── TBTK/            Turn Based ToolKit 3 (third-party, do not modify source)
        ├── GameEngine/      pure C# authoritative core (no UnityEngine dependencies)
        ├── Agents/          Human / LLM / Scripted controller adapters
        ├── GameUI/          map, panels, morning report, diplomacy UI
        └── Tests/           EditMode rule & simulation tests
```

`Turn Based ToolKit 3 TBTK-3 3.1.5.unitypackage` (repo root) is the not-yet-imported TBTK3 package: hex/square grids, turn control, units, A* pathfinding, fog-of-war support, UI scaffolding.

## Documentation discipline (the open loop)

This project is worked on across many sessions by different agents and two students. To keep continuity:

- **After any work session**, update `docs/CURRENT_STATE.md`: what changed, what's next, any blockers.
- **Any architectural choice** gets an entry in `docs/DECISIONS.md` (append-only; supersede, don't rewrite).
- **Any question that needs a human decision** goes into `docs/OPEN_QUESTIONS.md`. Check it at session start; when the team answers one, move the answer into DECISIONS.md and remove it.
- **Feature status** lives in `docs/FEATURES.md`; keep checkboxes honest.
- The formal report (`doc/*.docx`) is updated at milestones, not every session. `docs/` is day-to-day truth.

## Working in this repo

- Unity project: `fyp_gaming/` — open with Unity 6000.6.2f1. Do not edit `Library/`, `Temp/`, `Logs/`.
- TBTK3 source under `Assets/TBTK/` is third-party: subclass/extend, don't patch in place.
- Game rules go in the pure C# engine (`GameEngine/`), never in MonoBehaviours, UI, or LLM prompts.
- LLM API keys: environment variables or untracked local config only. Never commit secrets.
- Commit messages: conventional commits (`feat:`, `fix:`, `docs:`, …).

## Tooling notes

- Unity MCP is configured for this project (`.kimi-code/mcp.json` + user-level `~/.kimi-code/mcp.json`, server `unity-mcp`). In the desktop app, `mcp.json` is read only at app start (file watchers are off by default; `[watch] enabled = true` is set in `~/.kimi-code/config.toml` to allow live reload), and project-level MCP requires the workspace to be trusted — after changing MCP config, fully restart the app, then start a new session.
- Windows environment; shell is Git Bash. Python 3.13 is available at `/c/Python313/python` (python-docx installed) for document scripts.
