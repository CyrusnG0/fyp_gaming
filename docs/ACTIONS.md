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
| Move points per army per season | 6 (cavalry 8 ⏸) | terrain cost via Dijkstra; 冬 ×0.7 ⏸ |
| Once-per-season flags | per city （開墾…) / per army (marched) | reset in `BeginSeason()` |
| Event log | every accepted action emits an event with `visibility` (public / owner_and_observers / owner_only); rejections emit `action_rejected` (owner_only) | CONTROLLER_PROTOCOL §5 |
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
| M1 | `march` | 行軍 | 1/軍 | path cost ≤ 6, dest walkable, no army on the destination | move; neutral city → **capture** | `army_moved` (+`city_captured` public) | ✅ |
| M2 | `attack_army` | 野戰 | 1 | enemy army within 1 格 (`param_a/b` = its hex) | field battle (§6): ≤5 rounds, loss/round = enemy 戰力×8%; 士氣 0 → 潰散 +30% and retreat | `battle_field` public | ✅ |
| M3 | `attack_city` | 攻城 | 2 | adjacent to enemy city (`param_a/b` = its hex); 兵力 ≥**3:1** (no 器械 in MVP) | **assault only, single resolution** (ADR-010 #4): capture → ownership transfer, `駐軍 0`, `民心 0`, attacker +威望 | `battle_siege` public (+`city_captured`) | ✅ |
| M4 | `massacre` | 屠城 | 0 | just captured a city | instant pacify; `民心 −30` perm, `威望 −20`, all factions' trust −35 | `city_massacred` public | ⏸ (designed §4.3/§7.3, missing from ch.11) |
| — | `encamp` | 紮營 | 1 | — | 士氣+10, 補給+20 | | ⏸ needs supply |
| — | `ambush` | 設伏 | 1 | forest/hill | first-round dmg ×1.5, 敵士氣−20 | | ⏸ |
| — | `raid_supply` / `cut_supply` | 劫糧 / 斷補 | 1 | 騎兵 | 補給−40 / 逃兵10%/季 | | ⏸ needs cavalry + supply |
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

## 7. 研究 Research — ⏸ deferred (needs ResearchPoint model)

`research` (`args.line` ∈ {農政，兵法，諜報，吏治}, cost 20/40/70/110/160 點, ~15點/季).
No CP cost stated in design ❓. Unlocks D9 屯田 (農政3), espionage (諜報1/3/4), etc.

## 8. 君主特權 / control switch

- `abdicate`/`regency` (禪讓/託孤, 0 CP): **this IS the FYP control switch** — a protocol-level
  operation (HandoverBundle, CONTROLLER_PROTOCOL §3), not a normal action. 🎯 MVP.
- 稱帝 (城≥30, 威望≥70 → 天命 victory route), 結義 (信任≥80→95), 招降敵將, 斬將, 大赦： ⏸ all need General/Prestige depth.

## 9. State fields to add (MVP)

| Model | Add | Status |
|---|---|---|
| `CityState` | `民心 (0-100, start 60 待決)`, `城防 0-5`, `Training 0-5`, `ReclaimPct` | ✅ defaults in `BalanceConfig`, 民心 clamps 0–100 |
| `FactionState` | `威望 (0-100, start 50 待決)` | ✅ clamped, written by gifts/attacks/treaties |
| `DiplomacyState` (new) | `Trust[from][to] ∈ −100..+100` (start 0 待決), `Treaties` list, `WarWith` set, `Proposals` list | ✅ trust is directional and clamped; one pending offer per pair; expiry enforced at rollover |
| `ArmyState` | `Morale (0-100, start 60 待決)` | ✅ P2: battle losses, 潰散 at 0, seasonal regrowth |
| observation | `GameEngine.Observe(seatId)` → `Observation` (cities ±3, armies ±2, **absent** outside; filtered event log) | ✅ P4, ADR-011 |
| 待決 consequence | 民心 effect: **LOCKED** — 民心<30 → city yields −50% (else the number is dead weight for LLM reasoning) | ✅ applied at season rollover; 民變 (design's <25) not implemented — OPEN_QUESTIONS 19 |

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
