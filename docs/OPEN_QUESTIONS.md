# OPEN_QUESTIONS.md — Decisions waiting on the team

> The open loop. Agents: check this file at session start; do NOT silently pick an answer —
> implement behind config where possible and flag the question. Team: answer in this file
> (or tell an agent to), then the answer moves to `docs/DECISIONS.md` and the item is removed.

## Blocking Phase 0/1 (need answers soon)

1. **MVP seat count: 4 or straight to 8?** Report recommends 4 (architecture supports 8). Confirm.
2. **Faction roster**: extended Three Kingdoms / Warring-States-style 8 states / fictional 8? Affects map & faction config authoring. (Report §2.3)
3. **Map dimensions & city counts** for the two MVP maps. Design doc says 1024×1024 — too big for MVP; pick concrete sizes (e.g. 128×128? 64×64?) and city counts (~90 total in the design; MVP likely far fewer).
4. **LLM provider, model, and budget.** Which API/model, cost cap per game, local-model option? Blocks LLMController work.
5. **Turn-order rule**: report MVP suggestion is rotating starting player (A→B→C→D, then B→C→D→A). Confirm or pick fixed/shuffle/initiative.

## Design-level (answer before the relevant phase)

6. **Treaty activation timing**: immediately on acceptance, or next season? (Report leans immediate; MVP needs a lock.)
7. **Secret army movement reporting**: are hidden moves revealed only next season's 朝報?
8. **What do players see during other players' turns?** Which events are public in real time under Option B?
9. **Eliminated players**: convert to scripted AI? May they keep watching / sending diplomacy?
10. **Victory thresholds per map/season count** (20/30/40 seasons? city-control % basis?).
11. **Season limit & per-turn timer** for MVP (report suggests 15–20 seasons, 60–90s/turn).
12. **Control-switch moments**: only at season boundaries, or also mid-turn between actions? (Affects CONTROLLER_PROTOCOL §3.)
13. **LLM rationale retention**: store structured reason summaries only, or raw outputs too? (Report suggests structured; raw outputs are useful for the thesis but raise privacy/size questions.)

## Process

14. **Is `docs/` + report v1.1 the agreed source of truth?** If the team maintains the docx elsewhere, decide how changes flow back.
15. **Hot-seat UX**: how do human players pass the machine without seeing each other's private info (screen handover flow, per-seat hidden panels)?
