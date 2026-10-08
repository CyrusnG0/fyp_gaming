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

## Raised during P2–P4 implementation (2026-10-08)

16. **Does 宣戰 break existing treaties?** Declaring war on a NAP/ally partner currently does not dissolve
    the pact (no design row prices it) — a subsequent attack is still charged as 背盟 (−50/−15). Design
    §11 #18 deliberately makes a formal declaration *cheaper* than a 偷襲, but should the declaration
    itself tear up a signed pact (and if so, at 背盟 or 撕毀停戰 pricing)?
17. **遣使 (send_message) has no trust effect.** ACTIONS.md §5 and design §7.1/§11 all say text-only, so
    messages change no state — and they cost 0 CP, so any trust delta would be an unbounded trust farm.
    Confirm as-is, or add a capped effect (e.g. once per pair per season, +1)?
18. **Validator-oracle leak.** Rejection messages reveal hidden information: marching onto an unseen enemy
    army returns `destination occupied by an army`, onto an unseen enemy city `enemy city — battle is
    beyond this demo`, and an illegal 攻城 names the defender's exact garrison (`assault needs 3:1 troops
    (have X, garrison Y)`). Acceptable for the MVP (humans and LLMs face the same validator), or should
    error strings be coarsened into a probe-proof form?
19. **民心 thresholds disagree between docs**: design §4.1 says 民變 risk below 25, ADR-010 locks
    "民心<30 → yields −50%". Only the yield rule is implemented (no rebellion system). Reconcile the two
    numbers (one threshold for both, or keep 25 for 民變 when that system lands)?
20. **Visible ⇒ full detail vs estimation levels**: an in-radius foreign city/army currently exposes all
    fields (troops, garrison, 民心, food…). The design §8.1 implies estimated enemy strength. Keep exact
    values for MVP, or introduce coarse estimation bands before the LLM comparison?
