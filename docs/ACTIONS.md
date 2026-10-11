# ACTIONS.md — Canonical Action Spec (v1, decisions locked 2026-10-08 per ADR-010)

> **Purpose.** One canonical catalog of every action a seat can take, with exact costs,
> preconditions, and state effects. **Human UI buttons and LLM JSON emit the exact same
> commands through the same validator** (AGENTS.md invariant #2) — this document is the
> contract that makes the human-vs-LLM comparison a fair game.
>
> Sources: `doc/LuanShi-Game-Design-v1.md` (WHAT), `docs/CONTROLLER_PROTOCOL.md` (HOW).
> All numbers live in `BalanceConfig` (no magic numbers); values marked 待決 are undecided.
>
> Status legend: ✅ implemented · 🎯 MVP target · ⏸ deferred (post-MVP) · ❓ needs team decision

---

## 1. ActionCommand schema v2 (proposal)

Current DTO: `action_id, type, actor_seat_id, controller_type, target_id, param_a, param_b, reason`.
This covers farm/recruit/march but not diplomacy or espionage. Proposed extension — one flat
JSON object, adding two optional fields:

```json
{
  "action_id": "a-7",            // engine-assigned
  "type": "propose_treaty",       // action id from §3–§7 tables
  "actor_seat_id": "seat-0",      // set by controller; engine re-stamps controller_type
  "controller_type": "human",     // engine-overwritten from seat record (anti-spoof)
  "target_id": "city-wei",        // city / army / faction / general id, per action
  "param_a": 5, "param_b": 6,     // int params (hex coords, amounts, counts)
  "args": { "treaty_type": "nap", "duration": "4" },   // string-keyed extras (NEW)
  "text": "願結同盟，共抗強敵",     // free text for 遣使/條約附言 (NEW — logged, never executed)
  "reason": "..."                 // controller rationale; research data, never parsed
}
```

- `args`/`text` are optional; existing 3-action commands stay valid (backward compatible).
- The LLM envelope (`{season, actions[], summary}`) wraps a list of these; validation and
  execution are one-at-a-time, same as human clicks (CONTROLLER_PROTOCOL §2, §4).
- **Fair-play rule:** any action the UI exposes must be submittable as JSON and vice versa.
  No side channels, no UI-only shortcuts (e.g. drag-to-select-path is UI sugar; the command
  is still just `march` to a destination hex).

## 2. Global conventions

| Rule | Value | Notes |
|---|---|---|
| Command points (號令) per season | **5** (LOCKED, ADR-010 #1) | `BalanceConfig.CommandPointsPerSeason`; D1-D7/M1/M2/P4-P6 cost 1, M3 攻城 costs 2, 遣使/提案/回覆 cost 0 |
| 遣使 / 提案 / 回覆 (talk, propose, reply) | **0 CP** | design §11: 說話永遠免費 |
| Move points per army per season | 6 (cavalry 8 ⏸) | terrain cost via Dijkstra; **冬 ×0.7 ✅** (`BalanceConfig.MarchBudget`, P6 §8b) |
| Once-per-season flags | per city （開墾…) / per army (marched) | reset in `BeginSeason()` |
| Seasonal harvest multiplier | 春 ×0.7 · 夏 ×1.0 · 秋 ×2.0 · 冬 ×0.3 | applied to every city's harvest at `EndSeason()` ✅ (P6 §8b); seasons cycle 春→夏→秋→冬 by `(Season−1) % 4` |
| 研究 points per season | **15** (TBD) | `BalanceConfig.ResearchPointsPerSeason`; ×1.25 with 吏治 L4 學堂 → 19; paid at every rollover (`BeginSeason()`), no building income yet |
| Research line level | 1–5 per line, price 20 / 40 / 70 / 110 / 160 | LOCKED (design §9); `research` costs **1 號令** (TBD — design ch.11 does not price it), §7 |
| Event log | every accepted action emits an event with `visibility` (public / owner_and_observers / owner_only); rejections emit `action_rejected` (owner_only) | CONTROLLER_PROTOCOL §5 |
| World events (no action behind them) | `season_start`/`season_end`, `city_resolved`, `starvation`, `revolt`, `desertion`, `game_over` — emitted by `BeginSeason()`/`EndSeason()` | P6 §8b: `revolt`/`game_over` public, `desertion` owner-only |
| Execution | immediate on `Submit()`; economy/combat resolve at season end or per action as specified | |

---

## 3. 內政 Domestic — `target_id` = own city

| # | id | 中文 | CP | Cost | Preconditions | Effect (state mutation) | Event | Status |
|---|---|---|---|---|---|---|---|---|
| D1 | `levy_tax` | 徵稅 | 1 | — | — | `Gold += TaxLevyAmount (TBD 200)`; `民心 −5` | `tax_levied` public | ✅ |
| D2 | `lighten_labor` | 輕徭 | 1 | `Gold −LightenLaborGoldCost (TBD 100)` | enough gold | `民心 +8` | `labor_lightened` | ✅ |
| D3 | `reclaim` | 開墾 | 1 | — | `ReclaimPct < +60%` cap, once per city per season | city food yield `+15%` (cumulative, stored as `ReclaimPct`) | `land_reclaimed` | ✅ (renamed from `farm`, ADR-010 #2) |
| D4 | `recruit` | 募兵 | 1 | 200 金， 1000 人口 | enough both | `Garrison += 1000` | `recruit` | ✅ |
| D5 | `train` | 練兵 | 1 | — | `Training < 5` | `Training +1` (city garrison; +10% combat per level, §6.1) | `troops_trained` | ✅ |
| D6 | `fortify` | 修城 | 1 | — | `城防 < 5` | `城防 +1` (defender 戰力 +15%/級, §4.1) | `city_fortified` | ✅ |
| D7 | `relief` | 賑災 | 1 | `糧 −ReliefFoodCost (TBD 300)` | enough food | `民心 +15` | `disaster_relieved` | ✅ |
| D8 | `build` | 建建築 | 1 | per building (§4.2: 市集800/農田700/兵營1000/學堂1200/諜樓1200/糧倉900/醫館900) | free slot (3–6/city), gold | building added; passive per §4.2 | `building_built` | ⏸ needs Building model + `args.building_type` |
| D9 | `farm_garrison` | 屯田 | 1 | — | **農政 3** | garrison food self-supply 30% | `garrison_farmed` | ⏸ research-gated |
| D10 | `move_capital` | 遷都 | 1 | — | owns ≥2 cities | capital changes; `威望 −5` | `capital_moved` public | ⏸ |

**Naming reconciliation ✅ (implemented):** the old `farm` (flat +400 food) is gone — it is now D3
`reclaim` (+15% stacking, cap +60%). `farm` survives only as a deprecated alias that `Submit()` maps to
`reclaim`, so a controller (or a stale UI build) still sending `farm` keeps working. 屯田 stays D9,
deferred behind research. All seven domestic actions emit their event with the visibility noted above
(D1 public per spec, the rest `owner_and_observers`); 民心/城防/訓練 changes clamp to the
`BalanceConfig` ranges.

## 4. 軍事 Military — `target_id` = own army; `param_a`/`param_b` = the target hex

| # | id | 中文 | CP | Preconditions | Effect | Event | Status |
|---|---|---|---|---|---|---|---|
| M1 | `march` | 行軍 | 1/軍 | path cost ≤ 6 (冬 ≤ 4, §8b), dest walkable, no army on the destination | move; neutral city → **capture** (may end the game, §8b 一統天下) | `army_moved` (+`city_captured` public) | ✅ |
| M2 | `attack_army` | 野戰 | 1 | enemy army within 1 格 (`param_a/b` = its hex) | field battle (§6): ≤5 rounds, loss/round = enemy 戰力×8%; 士氣 0 → 潰散 +30% and retreat | `battle_field` public | ✅ |
| M3 | `attack_city` | 攻城 | 2 | adjacent to enemy city (`param_a/b` = its hex); 兵力 ≥**3:1** (no 器械 in MVP) | **assault only, single resolution** (ADR-010 #4): capture → ownership transfer, `駐軍 0`, `民心 0`, attacker +威望 (may end the game, §8b) | `battle_siege` public (+`city_captured`) | ✅ |
| M4 | `massacre` | 屠城 | 0 | just captured a city | instant pacify; `民心 −30` perm, `威望 −20`, all factions' trust −35 | `city_massacred` public | ⏸ (designed §4.3/§7.3, missing from ch.11) |
| — | `encamp` | 紮營 | 1 | — | 士氣+10, 補給+20 | | ⏸ (feeding an army is now a P6 resolution rule, §8b — the 補給 *field* this action restores is still 待決) |
| — | `ambush` | 設伏 | 1 | forest/hill | first-round dmg ×1.5, 敵士氣−20 | | ⏸ |
| — | `raid_supply` / `cut_supply` | 劫糧 / 斷補 | 1 | 騎兵 | 補給−40 / 逃兵10%/季 | | ⏸ as actions (need cavalry); the 逃兵 10%/季 **consequence** is live for any unfed army since P6 (§8b) |
| — | `retreat` / `relieve` | 撤退 / 救援 | 1 | — | 士氣−10 / join ally battle (信任+20, 威望+8) | | ⏸ |

## 5. 外交 Diplomacy — `target_id` = a **seat** (never a hex); Trust (−100..+100 per ordered pair) + 威望 + Treaty model

| # | id | 中文 | CP | Preconditions | Effect | Status |
|---|---|---|---|---|---|---|
| P1 | `send_message` | 遣使 | 0 | — | none (text logged from the `text` field) | ✅ |
| P2 | `propose_treaty` | 提出條約 | 0 | no same-type treaty in force and no offer pending with that seat | creates a pending offer; `args.treaty_type` ∈ {`nap` 互不侵犯， `alliance` 同盟， `truce` 停戰}, `args.duration` optional (default 6, clamped 1–12) | ✅ (3 of 10 types) |
| P3 | `respond_treaty` | 接受/拒絕/修改 | 0 | pending offer **from** that seat | `args.response` ∈ {`accept`,`reject`,`counter`}; a counter needs `args.treaty_type` and replaces the offer with the reverse one | ✅ |
| P4 | `gift` | 贈禮 | 1 | treasury gold ≥ 1,000 (`param_a` = amount) | per 1,000 金： 對方信任 +3, 自威望 +1 (§7.3) | ✅ |
| P5 | `declare_war` | 宣戰 | 1 | not already at war | war state on; 威望 −5 | ✅ |
| P6 | `break_treaty` | 背盟/毀約 | 1 | treaty of `args.treaty_type` in force | 信任 −50 (停戰 −30), 威望 −15, 第三方 −15 (§7.3); the treaty is removed | ✅ |
| — | `demand_tribute` 索貢 / `trade` 通商 / `sever` 斷交 / `vassalize` 招降 / 聯姻 / 稱臣 / 割地 / 朝貢 / 借道 | | 1 | per §7 | per design | ⏸ |
| — | `return_city` 歸還城池 | | — | — | trust +25/威望+10 | ⏸ |

**Implemented command shapes — the LLM contract (ADR-012):**

| action | `target_id` | params / args / text |
|---|---|---|
| `send_message` | recipient seat | `text` = the message |
| `propose_treaty` | recipient seat | `args.treaty_type` ∈ `nap｜alliance｜truce`; `args.duration` (optional seasons) |
| `respond_treaty` | **proposer** seat | `args.response` ∈ `accept｜reject｜counter` (+ `args.treaty_type`/`duration` when countering) |
| `gift` | recipient seat | `param_a` = gold amount |
| `declare_war` | seat | — |
| `break_treaty` | seat | `args.treaty_type` |

**Lifecycle ✅ (ADR-012):** propose → pending (visible to the two parties) → accept / reject / counter → at
rollover unanswered offers **lapse** while accepted ones are **ratified** with
`ExpirySeason = season + duration − 1`; treaties past that season expire. A ratified 停戰 ends the war.
偷襲/背盟 stay derived from state at execution time (ADR-010 #7); 守約's +15/+5 is not implemented.

**Timing ✅ (ADR-010 #5):** treaties take effect **next season** — proposals resolve at season rollover.
Consequently 求和 (sue for peace) is the 停戰 proposal path, not a direct peace action (OPEN_QUESTIONS 16).

## 6. 諜報 Espionage — ⏸ ALL deferred post-MVP (needs Spy model + fog integration)

派間諜 (needs 諜報1) · 偵察 200金 · 竊取軍報 400金 · 散播謠言 500金 (民心−10) ·
偽造軍報 600金 (諜報3) · 挑撥離間 800金 (信任−10) · 收買將領 1000金+ · 暗殺 1500金 (諜報4) ·
巡查 0金 (+15% 暴露) / 清查 300金 (嫌疑−30). Exposure formula §8.3.
**But:** fog-of-war *observation filtering* (cities ±3格, armies ±2格, §8.1) is 🎯 MVP —
it is observation-side, not an action, and the thesis needs it.

## 7. 研究 Research ✅ (P7, ADR-014) — design §9 四條線 × 5 級

One action, four lines. `target_id` is unused; `args.line` is required.

| id | 中文 | CP | Cost | Preconditions | Effect | Event | Status |
|---|---|---|---|---|---|---|---|
| R1 | `research` | 研究 | **1 (TBD)** | `args.line` ∈ `agri｜military｜espionage｜governance` (農政/兵法/諜報/吏治) | `ResearchPoints ≥` the level's price; `level < 5` | level +1, points deducted immediately (20/40/70/110/160, LOCKED design §9); a level's effects are read at the point of use | `research` **owner-only** (`line`, `level`, `cost`, `research_points`) | ✅ |

**Income ✅:** `ResearchPoints += 15` (TBD, design's 「約 15 點／季」) at every season rollover, ×1.25 with
吏治 L4 學堂 → 19/season. No building income: **學堂 the building (+3/季, design §4.2) is deferred with D8 建築**.

**Level effects — implemented vs deferred (design §9 table).** A level with a deferred effect is still
**purchasable**: it is progress up the line and unlocks what comes after it.

| Line | L1 | L2 | L3 | L4 | L5 |
|---|---|---|---|---|---|
| 農政 agri | ✅ harvest ×1.10 | ⏸ 水利 (drought halving — no 天時 events) | ⏸ 屯田 (D9 自給 30%) | ⏸ 常平倉 (food cap — no cap modelled) | ✅ all harvests ×1.25 (stacks with L1) |
| 兵法 military | ✅ 訓練 cap 5→6 (design reads 訓練 +1; implemented as the cap, see ADR-014) | ⏸ 騎兵 (no unit types) | ⏸ 器械 (no siege weapons) | ✅ 戰力 ×1.15, faction-side | ⏸ 統帥 (no generals) |
| 諜報 espionage | ⏸ 細作 | ⏸ 反諜 | ⏸ 偽報 | ⏸ 諜網 | ⏸ 無間 | (all five gate the espionage phase, §6) |
| 吏治 governance | ✅ tax ×1.10 | ✅ 民心 +5/季 per city | ✅ 叛變機率 −50% (`RevoltChanceMult = 0.5`, synced at rollover) | ✅ research +25% (→19/season) | ✅ 威望 +10 one-time, on purchase |

**Timing (ADR-014):** point-of-use effects (harvest, tax, combat, training cap, the 民心 grant) apply as
soon as the level exists — a level bought before `EndSeason()` already pays that season. The two derived
values — 吏治 L3's `RevoltChanceMult` and the L4 income bonus — are settled at the season rollover, so they
take effect from the next season (the rule treaties follow, ADR-010 #5).

## 8. 君主特權 / control switch

- `abdicate`/`regency` (禪讓/託孤, 0 CP): **this IS the FYP control switch** — a protocol-level
  operation (HandoverBundle, CONTROLLER_PROTOCOL §3), not a normal action. 🎯 MVP.
- 稱帝 (城≥30, 威望≥70 → 天命 victory route), 結義 (信任≥80→95), 招降敵將, 斬將, 大赦： ⏸ all need General/Prestige depth.

## 8b. 壓力與後果 Pressure & consequences (P6) — engine-side, no new actions

> Rules, not orders: every seat (human / LLM / scripted) is subject to them identically and cannot opt
> out. All resolve deterministically from the engine's seeded RNG at `BeginSeason()` / `EndSeason()`.
> Sources: design §3 (天時), §4.1 + §10.2 (民心/民變), §5.1 + §6.3 + §10.1 (補給/斷補/嚴冬), §13 (勝利).

| System | Rule | Numbers (`BalanceConfig`) | Event | Status |
|---|---|---|---|---|
| 天時 seasons | `(Season−1) % 4` → 春·夏·秋·冬; the multiplier scales **every** city's harvest at rollover | 春 `SpringFoodYieldMult` **0.7** · 夏 **1.0** · 秋 **2.0** · 冬 **0.3** — all LOCKED (design §3 table) | — (`SeasonReport.SeasonName`, `season_start.season_name`, observation `season_name`) | ✅ |
| 嚴冬 | 行軍 budget ×0.7 (enforced in `march` validation); 逃兵 +5% troops | `WinterMarchBudgetMult` **0.7** (LOCKED, §10.1 嚴冬); `WinterDesertionExtraTroopLoss` **0.05** (LOCKED, §10.1); winter budget = floor(6 × 0.7) = **4** | — | ✅ |
| 民變 revolt | each owned city with 民心 < threshold rolls `(25 − 民心) × 2% × FactionState.RevoltChanceMult`; on revolt the city's **harvest and tax are zero** for that tick, garrison −10%, 民心 +15 after venting (≤100) | `RevoltRiskMoraleThreshold` **25** (LOCKED, §4.1); `RevoltChancePerMoralePoint` **0.02** 待決 (the 2%/point is invented); `RevoltGarrisonLossPct` **0.10** 待決 (design silent); `RevoltMoraleGainAfterVenting` **15** 待決 | `revolt` **public** (`city_id`, `garrison_lost`, `morale`) | ✅ |
| 逃兵 army supply | each army eats `Troops × FoodPerGarrisonTroop` per season: the city it stands on pays first, then the faction's other cities **proportionally**; a pool that cannot cover the whole ration feeds nobody and the army deserts — 兵力 −10% (冬 −15%), 士氣 −15 | `FoodPerGarrisonTroop` **0.1** (existing 待決); `DesertionTroopLossPct` **0.10** (LOCKED, §6.3 斷補); `DesertionMoraleLoss` **15** 待決 (`cause=no_food`) | `desertion` **owner-only** (`army_id`, `cause`, `troops_lost`, `troops`, `morale`) | ✅ |
| 一統天下 victory | when **every** city on the map shares one non-empty owner (neutral cities must be taken too), `GameState.WinnerSeatId` is set and every later `Submit()` returns `game over` | — | `game_over` **public** (`winner_seat_id`, `reason=unification`) | ✅ (design §13; 稱霸諸侯 / 受天明命 / 時間到 ⏸) |

**Locked simplifications (ADR-013).** 補給 0–100, distance-from-home decay and per-army supply state are
**not** modelled — faction food coverage stands in for the supply line, so "far from home" is expressed as
"the faction is out of food", and nothing is consumed on a failed ration. A revolt is a **one-tick** event:
no ownership change, no multi-season rebellion, and the city keeps producing again next season (民心 +15
usually makes it eligible to produce at all). Seasons are computed from the absolute season number, so a
game always opens in 春 and always has the same season sequence for a given seed.

## 9. State fields to add (MVP)

| Model | Add | Status |
|---|---|---|
| `CityState` | `民心 (0-100, start 60 待決)`, `城防 0-5`, `Training 0-5`, `ReclaimPct` | ✅ defaults in `BalanceConfig`, 民心 clamps 0–100 |
| `FactionState` | `威望 (0-100, start 50 待決)` | ✅ clamped, written by gifts/attacks/treaties |
| `FactionState` | `RevoltChanceMult` (default 1.0) | ✅ P6 §8b — read by `RollRevolt`; **P7 吏治 3 syncs it to 0.5** at rollover (ADR-014) |
| `DiplomacyState` (new) | `Trust[from][to] ∈ −100..+100` (start 0 待決), `Treaties` list, `WarWith` set, `Proposals` list | ✅ trust is directional and clamped; one pending offer per pair; expiry enforced at rollover |
| `ArmyState` | `Morale (0-100, start 60 待決)` | ✅ P2: battle losses, 潰散 at 0, seasonal regrowth |
| `GameState` | `WinnerSeatId` (null while live) | ✅ P6 §8b — set by `CheckVictory()`, blocks `Submit()`, exposed in the observation |
| `FactionState` | `ResearchPoints` (int) + `Research` (4 lines → level 0-5, keys `agri/military/espionage/governance`) | ✅ P7 §7 — income at rollover, levels bought by `research`; **private to the seat** |
| observation | `GameEngine.Observe(seatId)` → `Observation` (cities ±3, armies ±2, **absent** outside; filtered event log) | ✅ P4, ADR-011; P6 adds `season_name` + `winner_seat_id`; P7 adds `research_points` + `research` (own levels only) |
| 待決 consequence | 民心 effect: **LOCKED** — 民心<30 → city yields −50% (else the number is dead weight for LLM reasoning) | ✅ applied at season rollover; the design's second threshold (民心<25 → 民變) landed in P6 as §8b — both numbers stand (ADR-013, OPEN_QUESTIONS 19) |

## 10. Decisions — LOCKED 2026-10-08 (recorded as ADR-010; team may supersede)

1. CP per season: **5** (design doc value; demo's 3 superseded).
2. `farm` → renamed **`reclaim`**, +15% yield per use, cap +60% (D3); 屯田 deferred (D9).
3. MVP tier locked: **D1-D7, M1-M3, P1-P6, fog observations, control switch.** Espionage,
   research, buildings, generals, supply → phase 2.
4. 攻城 MVP: **assault-only**, single resolution on the season it's launched.
5. Treaty timing: **takes effect next season** (proposals resolve at season rollover).
6. 民心 consequence: **民心<30 → city yields −50%** (LOCKED, see §9).
7. 偷襲/背盟: **derived, not separate actions** — attacking without a declared war or
   attacking an ally applies reputation/trust penalties computed from state at execution time.

> **P6 additions (2026-10-11, ADR-013 — supersedes #3's "supply → phase 2" insofar as supply is now
> modelled as a food ration, and #6's single 民心 threshold insofar as 民心<25 民變 (§8b) is implemented
> alongside it):**
> 8. `FactionState.RevoltChanceMult` (default **1.0**) is the revolt-chance hook; P7 吏治 3 sets **0.5**.
> 9. Seasonal fact: harvest 春 ×0.7 / 夏 ×1.0 / 秋 ×2.0 / 冬 ×0.3 (LOCKED, design §3); 冬 march budget
>    **4** (`floor(6 × 0.7)`) and 冬 desertion **+5%** (LOCKED, design §10.1 嚴冬).
> 10. Victory MVP: **一統天下 only** (every city, neutrals included, under one seat). 稱霸諸侯 / 受天明命 /
>     時間到 stay ⏸ (OPEN_QUESTIONS 10/11).

> **P7 additions (2026-10-12, ADR-014 — supersedes #3's "research → phase 2": the four lines are live,
> espionage levels are purchasable but their effects wait for §6):**
> 11. Research income **15/season** (TBD — the design says 「約 15 點／季」), ×1.25 → **19** with 吏治 L4.
> 12. `research` costs **1 號令** (TBD — design ch.11 does not price it) + the level's points
>     (20/40/70/110/160, LOCKED §9), one level per command, max level 5 per line.
> 13. Research events are **owner-only**: a tech tree is a seat's secret. The observation carries the
>     seat's own `research_points`/`research` and nobody else's.
> 14. 兵法 L1 (design: 「練兵（訓練 +1）」) is implemented as **訓練 cap 5→6** for that faction, not as a
>     free training level — the reading the P7 brief locked; flagged for the team.
