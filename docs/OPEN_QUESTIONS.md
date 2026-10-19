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
19. ~~**民心 thresholds disagree between docs**~~ — **RESOLVED 2026-10-11 (ADR-013): keep both, they are a ladder.**
    The two numbers describe two different states, so both are implemented as the design wrote them:
    **民心 < 30 → city yields −50%** (ADR-010 #6, the warning band) and **民心 < 25 → 民變 risk**
    (design §4.1: `(25 − 民心) × 2%` per season × `FactionState.RevoltChanceMult`, and a revolt zeroes that
    city's harvest and tax outright — the harsher, rarer tier, design §10.2 產出停擺). No team decision
    needed; see `docs/ACTIONS.md` §8b for the locked numbers.
20. **Visible ⇒ full detail vs estimation levels**: an in-radius foreign city/army currently exposes all
    fields (troops, garrison, 民心, food…). The design §8.1 implies estimated enemy strength. Keep exact
    values for MVP, or introduce coarse estimation bands before the LLM comparison?

## Raised during P8 implementation (2026-10-12)

21. **挑撥離間 research gate: 諜報 2 (P8 brief) vs 諜報 3 (design §11 #32).** Implemented at **L2** per the
    P8 brief (`BalanceConfig.SowDistrustResearchLevel`, TBD). The design's action table asks for 諜報 3,
    which would make the mission a late-game tool. Team: keep 2, or restore the design's 3?
22. **Exposure trust penalty is flat −10, but the design prices the rumour case at −20.** Design §7.3's
    reputation table has a row for 散播謠言被識破 → 信任 −20 / 威望 −8; this phase uses the LOCKED 威望 −8 but a
    single flat `SpyExposedTrustPenalty = 10` (TBD) for all three missions because 偵察 and 挑撥離間 have no
    row of their own. Related and also un-paid: the design gives the exposed victim **威望 +5** for choosing
    公開處決 — nothing is awarded yet. Team: one flat number, per-mission numbers, or the design's table?
23. **The 諜報 UI cannot target a city the seat has never seen.** ADR-011 deliberately keeps no "known
    cities" memory (absent, not redacted), so the intel panel can only offer cities in sight or already
    scouted, while the engine accepts any city id. That makes 偵察 useful mainly for *keeping* eyes on a
    city (its report outlives the vision radius) rather than for reaching out to an unseen one. Team: add a
    per-seat "known city" memory (last-seen map + names) so agents can be sent into the fog?

## Raised during PR #3 review (2026-10-19)

24. **Morale laundering through full garrison/re-form; mixed-garrison movement provenance.**
    A full `garrison_army` transfer removes the army, while a later `convert_garrison` forms a new army with
    the default **60 morale**, so a battered army can use the round-trip to discard its low morale (and,
    before the fix, gain a fresh movement allowance). The new `CityState.GarrisonMarchedThisSeason` blocks
    the **movement** half by treating a garrison that absorbed marched troops as movement-spent until
    rollover — deliberately conservative, so it over-restricts mixed garrisons — but it does not model morale
    and cannot tell which troops in a mixed garrison have moved. Team: keep the conservative flag, or track
    per-troop movement provenance? Should morale be preserved through transfers, reset by design, or pay a
    transfer cost? (Morale laundering is recorded separately from the movement fix by design.)
25. **Defensive siege merge: what happens to the merged army after a failed/held assault or a capture?**
    PR #3 merges a same-owner defending army into the city garrison **before** siege resolution and removes
    the army entity. If the assault fails or the city holds, the troops remain as garrison with no army to
    command; if the city is captured, `city.Garrison` is zeroed and the old "retreat the defending army"
    branch is effectively unreachable. Choose the semantics: restore survivors as an army (where?), destroy
    them with the city, rout them to another city, or treat the merge as permanent? Also decide how losses
    are attributed between the merged army and the original garrison.
26. **Watchtower capture consequences.** Marching onto any tower not owned by the mover transfers ownership
    (public `watch_tower_captured`), including an ally's tower, with no war/trust/prestige consequence.
    Should taking an enemy tower count as an act of war / 偷襲 (with the derived reputation pricing), taking
    an ally's tower be priced as betrayal, or is a tower a deniable neutral object?
27. **Watchtower visibility.** The PR #3 UI always shows tower markers (neutral-tinted when out of sight),
    while armies are fog-hidden and cities keep only a bare marker (ADR-016). Should tower **existence** be
    always-visible terrain knowledge (current behaviour), or should towers be fog-hidden like armies
    (ADR-011's "absent, not redacted" rule)?
28. **Type-mismatched defensive siege merge (P14).** The player-driven merge/transfer paths require matching
    兵種 (ADR-019), but PR #3's defensive siege *auto-merges* any same-owner defending army into the city
    garrison before the rounds. Current implementation keeps one-type-per-garrison by letting an **empty**
    garrison adopt the defender's type, while a **non-empty** garrison keeps its own type (the merged troops
    are treated as the garrison's type). Is a mixed-type defensive stack acceptable as-is, should the
    defender's type win, or should a mismatched defending army refuse the merge (and fight separately)?
29. **攻城器械 剋 城防 has no number.** Design §5.3 lists 攻城器械 as countering 城防, but gives no
    multiplier; P14 implements only the 3:1 assault waiver (§4.3). If a numeric 破城 bonus is wanted
    (e.g. attacker ×1.3 vs garrison when bringing 器械）, set the value here.
30. **偵察 reports and 兵種.** Scout reports (`ScoutedCityView`) expose garrison size/food (banded or
    exact) but not `garrison_type`; only in-sight cities carry it (ADR-011 visibility rules). Should a
    scout report also reveal the garrison's 兵種 (and at which 諜報 accuracy level)?
