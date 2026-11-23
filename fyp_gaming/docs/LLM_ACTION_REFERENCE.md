# LuanShi — Complete Action Reference for the LLM

Every action in the game, verified against the engine (`ActionCommand.cs`, all `Do*` handlers in
`GameEngine.cs`, `BalanceConfig.cs`, `UnitTypes.cs`). All numbers are the standard `BalanceConfig`
profile.

**LLM availability column.** The LLM parser (`LlmActionParser`) accepts only the base vocabulary
(7 domestic orders + `march` + `end_turn`), plus opt-in vocabularies gated by config flags:
`enable_attack` (attack_army, attack_city), `enable_research` (research), `enable_diplomacy`
(send_message, propose_treaty, respond_treaty). Everything else is engine-only: available to the
UI and the scripted AI, but a model reply naming it is rejected by the parser.

## Turn frame (shared by all actions)

- One turn = one season. Each faction gets **5 command points (CP)** per season.
- Default order cost = **1 CP**; `attack_city` = **2 CP**; `send_message` / `propose_treaty` /
  `respond_treaty` = **0 CP**; `end_turn` is free.
- A rejected order costs nothing (no CP, no resources). The engine revalidates everything;
  prompt text is advisory only.

---

## A. Domestic orders (`target_id` = own city id, 1 CP each)

### 1. `levy_tax` — 徵稅 · LLM: always
- **What it does:** Instantly adds gold to the city's treasury, paid for with popular support.
- **Equation:** `gold += 200`, `morale −= 5` (morale clamps 0–100).
- **Used for:** Emergency gold for recruiting, gifts, or spy missions.
- **Outcome:** `tax_levied` event (public). Careful: morale < 30 halves that city's food and gold
  yields; morale < 25 risks revolt.
- **Example:** `{"type":"levy_tax","target_id":"city-a"}` — your capital sits at 80 morale and you
  need 200 gold for a mission this season.

### 2. `lighten_labor` — 輕徭 · LLM: always
- **What it does:** Spends city gold to buy back morale.
- **Equation:** `gold −= 100` (fails if `gold < 100`), `morale += 8`.
- **Used for:** Undoing `levy_tax` damage, or lifting a city back over the morale-30 yield
  threshold before the harvest resolves.
- **Example:** `{"type":"lighten_labor","target_id":"city-a"}` — after two tax levies morale fell
  to 27, which would halve yields; this restores it to 35.

### 3. `reclaim` — 開墾 · LLM: always
- **What it does:** Permanently raises the city's food-yield percentage. Once per city per season;
  cumulative cap **+60%** (+15 points per use).
- **Equation (seasonal harvest, resolved at rollover):**
  `harvest = CityBaseFoodYield × (1 + reclaimPct/100) × yieldFactor × seasonMult × researchFoodMult`
  where `yieldFactor = 0.5 if morale < 30 else 1`, `seasonMult = 0.7 spring / 1.0 summer /
  2.0 autumn / 0.3 winter`, `researchFoodMult = 1.10 at Agri≥1, ×1.25 more at Agri 5`.
- **Used for:** Long-term food security; best started early so the bonus compounds into autumn
  (×2) harvests.
- **Example:** `{"type":"reclaim","target_id":"city-b"}` — spring, season 1: raising city-b from
  +0% to +15% makes every future harvest 15% larger.

### 4. `recruit` — 募兵 · LLM: always (`args.unit_type` restricted to `"infantry"` for the model)
- **What it does:** Converts gold and population into garrison troops. Engine unit types:
  `infantry` (free) / `archer` (free) / `cavalry` (needs Military 2) / `siege` (needs Military 3).
- **Equation:** `gold −= cost(unit_type)`, `population −= 1000`, `garrison += 1000`.
  Cost per 1,000 troops: infantry 100, archer 150, cavalry 300, siege 500.
- **Constraint:** fails if the garrison already holds a *different* unit type (empty it or match
  it first).
- **Used for:** Building defensive garrisons; feeding `convert_garrison` to create field armies.
- **Example:** `{"type":"recruit","target_id":"city-a","args":{"unit_type":"infantry"}}` — city-a
  has 300 gold and 10,000 population; afterwards: 200 gold, 9,000 pop, +1,000 infantry garrison.

### 5. `convert_garrison` — garrison → field army · LLM: **engine/UI only**
- **What it does:** Moves `param_a` troops out of a city's garrison. If your **same-type** army
  already stands on the city hex, the troops merge into it; otherwise a **new army** is formed on
  the city hex (named after the city, inherits the garrison's unit type).
- **Constraints:** `1 ≤ param_a ≤ garrison`; fails if another faction's army occupies the hex, or
  the standing army's type differs from the garrison's. Troops that already marched this season
  stay movement-spent after the transfer.
- **Used for:** Turning defensive stockpiles into an attack force — the only way to create new
  field armies.
- **Example:** `{"type":"convert_garrison","target_id":"city-a","param_a":1500}` — city-a's
  2,000-man infantry garrison becomes a 500-man garrison plus a new 1,500-man field army.

### 6. `garrison_army` — field army → garrison · LLM: **engine/UI only**
- **What it does:** Transfers `param_a` troops from your army into the garrison of the own city it
  stands on. If the army is emptied it disbands (`army_destroyed`, cause `garrisoned`). Empty
  garrisons adopt the army's unit type; same-type rule applies otherwise.
- **Used for:** Reinforcing a city about to be assaulted, or retiring a damaged army's survivors
  into the walls.
- **Example:** `{"type":"garrison_army","target_id":"army-a-1","param_a":400}` — your battered
  400-man army at the capital dissolves into the garrison before the enemy siege host arrives.

### 7. `train` — 練兵 · LLM: always
- **What it does:** City training +1, up to the cap `5 + 1 if Military ≥ 1` (i.e. 6 with Military
  research 1).
- **Equation (effect in combat):** every strength calculation includes `× (1 + training × 0.10)` —
  training 5 is +50% strength for that city's garrison, and a faction's armies use the faction's
  best city training.
- **Used for:** Force multiplier for both defense (garrison) and offense (armies).
- **Example:** `{"type":"train","target_id":"city-a"}` — taking the capital's training from 2 to 3
  raises every combat strength derived from it by another 10%.

### 8. `fortify` — 修城 · LLM: always
- **What it does:** City defense (城防) +1, max 5.
- **Equation (effect in assaults):** `garrison_strength × (1 + defense × 0.15)` — defense 5 adds
  +75% to wall strength.
- **Used for:** Hardening your capital and border cities against `attack_city`.
- **Example:** `{"type":"fortify","target_id":"city-a"}` — enemy armies are massing nearby; each
  level adds 15% to the garrison's effective strength.

### 9. `relief` — 賑災 · LLM: always
- **What it does:** Spends city food to restore morale.
- **Equation:** `food −= 300` (fails if `food < 300`), `morale += 15`.
- **Used for:** Recovering from revolt risk when gold is scarce but food is plentiful (e.g. right
  after an autumn harvest).
- **Example:** `{"type":"relief","target_id":"city-b"}` — city-b is at 20 morale after a famine;
  relief brings it to 35, above both the revolt-risk and yield-penalty lines.

---

## B. Military orders

### 10. `march` — 行軍 · LLM: always (planning hosts; 1 CP)
- **What it does:** Moves one of your armies to a destination hex (`target_id` = own army id,
  `param_a`/`param_b` = destination x/z). Automatically **captures neutral cities and
  unowned/enemy watch towers** on the destination. Cannot enter an enemy-owned city (that is what
  `attack_city` is for).
- **Equations:**
  - Season movement budget: `budget = MovePoints(type) × (0.7 in winter, else 1)` (integer
    floor). MovePoints: infantry/archer **6**, cavalry **8**, siege **3** — so a winter siege army
    has budget 2.
  - Path cost = sum of the costs of every hex entered (plains cost 1, forest cost 2;
    mountains/rivers unwalkable). The engine finds the cheapest path; the order fails if the
    cheapest cost > budget.
- **Constraints:** destination must be walkable, **within your sight** (own city ±3 hexes, own
  army ±2, own watchtower ±5), unoccupied by any army, and not the army's current hex. One march
  per army per season — **except cavalry: two marches**, each costing 1 CP with its own full
  budget.
- **Outcome:** `army_moved` event with cost and march count; `city_captured` /
  `watch_tower_captured` (both public) when applicable; capturing a city triggers the victory
  check.
- **Example:** `{"type":"march","target_id":"army-a-1","param_a":4,"param_b":2}` — your cavalry
  army dashes onto neutral city-n (first march), then uses its second march order to threaten the
  enemy border.

### 11. `attack_army` — 野戰 field battle · LLM: with `enable_attack` (1 CP)
- **What it does:** Your army attacks an enemy army at `param_a`/`param_b`. Range: 1 hex for
  infantry/cavalry/siege, **2 for archers**. Resolved immediately, max **5 rounds**.
- **Equations:**
  - `attacker_strength = troops × (1 + training × 0.10) × (0.5 + morale/200) × research_mult ×
    type_mult`
  - `research_mult = 1.15 if your Military research ≥ 4, else 1.0`
  - `type_mult` (attacker): counter cycle — infantry > archer, cavalry > infantry, archer >
    cavalry: winner ×1.3, loser ×0.8; siege armies ×0.5 in field battles; **cavalry attacking a
    defender standing on plains gets an extra ×1.3 plains charge** (the defender never does).
  - Each round: `losses_taken = enemy_strength × 0.08`, varied ±15%, `× 0.85 for infantry`
    (durable), capped at current troops. Morale drops ~1 point per 1% of that side's troops lost
    that round.
- **Outcomes:** troops = 0 → army destroyed; morale = 0 → rout (extra −30% of remaining troops,
  immediate retreat to nearest owned city with a free hex; destroyed instead if none exists or the
  loss kills it); after 5 rounds with no break → **defender holds** (counts as defender win).
  Winner: +10 army morale, faction **+3 prestige**. Mutual destruction/rout: no winner.
- **Reputation (applies to both attack orders):** no war declared → sneak attack: victim's trust
  in you −40, prestige −12, and war begins. Attacking an ally/NAP partner breaks the treaty:
  victim trust −50, prestige −15, **all bystanders −15**. Breaking a truce: −30/−10. Attacking
  after `declare_war`: no extra penalty.
- **Example:** `{"type":"attack_army","target_id":"army-a-1","param_a":2,"param_b":1}` — your
  2,000 cavalry at high morale charges a 500-man infantry army on plains: type ×1.3 × charge ×1.3
  ≈ 1.69× strength multiplier.

### 12. `attack_city` — 攻城 assault · LLM: with `enable_attack` (2 CP)
- **What it does:** Your army storms an adjacent (same hex or next-hex) **enemy-owned** city. Any
  defender-owned army standing on the city hex **merges into the garrison first**, then the battle
  runs: max 5 rounds; the garrison has no morale track, only the attacking army can rout.
- **Equations:**
  - `garrison_strength = garrison_troops × (1 + city_training × 0.10) × (1 + city_defense × 0.15)
    × research_mult × garrison_type_mult` (`garrison_type_mult = 1.25 for archer garrisons,
    else 1.0`)
  - `attacker_strength = troops × (1 + training × 0.10) × (0.5 + morale/200) × research_mult ×
    siege_mult` (`siege_mult = 1.3 for siege armies, 1.0 otherwise`)
  - Per round: `army_losses = garrison_strength × 0.08 (±15%)`; `garrison_losses =
    attacker_strength × 0.08 (±15%)`.
  - **There is no minimum troop-ratio rule** — any army may assault any city; the odds come purely
    from the strength equations.
- **Outcomes:** garrison reaches 0 → **city captured**: ownership flips, garrison = 0, city morale
  = 0, capturer **+5 prestige**, victory check. Army destroyed or routed → city holds. 5 rounds
  with garrison alive → city holds. Neutral cities reject this order — march into them instead.
- **Example:** `{"type":"attack_city","target_id":"army-a-1","param_a":1,"param_b":2}` — your
  2,000-man siege host assaults a city with 500 garrison and defense 1: garrison strength ≈
  500 × 1.15 = 575 vs your ≈ 2,000 × (0.5+60/200) × 1.3 = 2,080 — the walls fall in ~2 rounds.

---

## C. Diplomacy orders (`target_id` = another faction's seat id)

### 13. `send_message` — 遣使 · LLM: with `enable_diplomacy` (0 CP, free)
- **What it does:** Delivers `text` to another seat; logged publicly. **Changes no game state** —
  pure communication.
- **Used for:** Negotiating treaties, coordinating attacks, deception.
- **Example:** `{"type":"send_message","target_id":"seat-b","text":"Join my war on seat-c and I
  will gift 2000 gold."}`

### 14. `propose_treaty` — 提出條約 · LLM: with `enable_diplomacy` (0 CP, free)
- **What it does:** Creates a pending offer. `args.treaty_type` = `nap` (non-aggression) /
  `alliance` / `truce` (ends a war once ratified); optional `args.duration` 1–12 seasons
  (default 6).
- **Outcome:** takes effect only when the other seat accepts **and** the season rolls over. Fails
  if that treaty is already in force or an offer is already pending.
- **Example:** `{"type":"propose_treaty","target_id":"seat-b","args":{"treaty_type":"nap",
  "duration":"6"}}`

### 15. `respond_treaty` — 回覆 · LLM: with `enable_diplomacy` (0 CP, free)
- **What it does:** Answers a pending offer from `target_id`. `args.response` = `accept`
  (ratified at the next season rollover) / `reject` (offer deleted). The engine also supports
  `counter` (replace with your own offer), but the LLM diplomacy slice does not expose it.
- **Example:** `{"type":"respond_treaty","target_id":"seat-b","args":{"response":"accept"}}`

### 16. `gift` — 贈禮 · LLM: **engine/UI only** (1 CP)
- **What it does:** Sends gold from your treasury to another seat to buy goodwill.
- **Equation:** `param_a` = gold amount, minimum 1000. `units = floor(gold / 1000)`; **their trust
  in you += 3 × units; your prestige += 1 × units**.
- **Example:** `{"type":"gift","target_id":"seat-b","param_a":2000}` — costs 2,000 gold; seat-b's
  trust in you +6, your prestige +2.

### 17. `declare_war` — 宣戰 · LLM: **engine/UI only** (1 CP)
- **What it does:** Formally opens war with `target_id`. **Prestige −5.** All your later attacks
  on that seat count as `declared_war` — no further trust/prestige penalties (vs sneak attack's
  −40 trust / −12 prestige).
- **Example:** `{"type":"declare_war","target_id":"seat-c"}` — declare this season, assault their
  capital next season with clean legitimacy.

### 18. `break_treaty` — 毀約 · LLM: **engine/UI only** (1 CP)
- **What it does:** Renounces an in-force treaty by act of state (`args.treaty_type` required).
- **Equation:** NAP/alliance: victim trust −50, prestige −15, every third party trust −15. Truce:
  victim trust −30, prestige −10, no bystander hit.
- **Used for:** Getting out of a treaty *without* attacking immediately (attacking a treaty
  partner applies the same penalties anyway, but also starts the war on the spot).
- **Example:** `{"type":"break_treaty","target_id":"seat-b","args":{"treaty_type":"nap"}}`

---

## D. Research

### 19. `research` — 研究 · LLM: with `enable_research` (1 CP + research points)
- **What it does:** Buys the next level of one line: `args.line` = `agri` / `military` /
  `espionage` / `governance`. Levels cost **20 / 40 / 70 / 110 / 160 points** (income: +15
  points/season, +25% → 19 with Governance 4). Secret: the event is owner-only. No `target_id`.
- **Effects per line:**
  - **agri:** L1 all harvests ×1.10; L5 a further ×1.25. L2–4 deferred (no effect).
  - **military:** L1 training cap +1 (→6); L2 unlocks cavalry; L3 unlocks siege engines; L4 all
    your combat strengths ×1.15; L5 deferred.
  - **espionage:** gates the three spy missions (L1 scout+rumor, L2 sow_distrust), raises mission
    success (+5%/level), L3+ gives exact scout numbers.
  - **governance:** L1 tax ×1.10; L2 +5 morale/season in every city; L3 revolt chance ×0.5; L4
    research income +25%; L5 one-time +10 prestige.
- **Example:** `{"type":"research","args":{"line":"military"}}` — with 70 points banked and
  Military at 1, this buys Military 2 and unlocks cavalry recruitment.

---

## E. Espionage — LLM: **engine/UI only** for all three

Mission frame: each mission costs 1 CP plus gold; the success roll is **70% + 5% per espionage
level**; gold is spent even on failure; failure exposes you (public `spy_exposed`, **prestige −8**,
each victim's trust in you −10).

### 20. `scout` — 偵察 (needs Espionage 1, 200 gold)
- **What it does:** Reveals a foreign/neutral city's troops and food in your observation for
  **this season and the next** — even after your armies move away.
- **Accuracy:** Espionage < 3 → bands only (troops: few < 1000, some < 3000, many ≥ 3000; food:
  few < 500, some < 2000, many ≥ 2000). Espionage ≥ 3 → exact numbers.
- **Example:** `{"type":"scout","target_id":"city-c-1"}` — before committing your siege host,
  check whether the enemy capital's garrison is 300 or 3,000.

### 21. `rumor` — 散播謠言 (needs Espionage 1, 500 gold)
- **What it does:** On success: target foreign city's **morale −10**. Deniable — the owner is told
  rumors circulate but not who sent them. Cannot target neutral cities.
- **Used for:** Pushing an enemy city under morale 30 (halved yields) or 25 (revolt risk) before
  you besiege it.
- **Example:** `{"type":"rumor","target_id":"city-c-1"}`

### 22. `sow_distrust` — 挑撥離間 (needs Espionage 2, 800 gold)
- **What it does:** `target_id` = seat A, `args.target_b` = seat B. On success: **trust between A
  and B −10 in both directions**; the victims see their trust move but never learn you paid for
  it.
- **Used for:** Breaking up an enemy alliance, or keeping two rivals hostile to each other.
- **Example:** `{"type":"sow_distrust","target_id":"seat-b","args":{"target_b":"seat-c"}}`

---

## F. Turn control

### 23. `end_turn` · LLM: always (free)
- **What it does:** Ends your turn immediately; unspent CP is discarded. Correct when remaining
  options don't help — never repeat pointless orders just to spend the budget.
- **Example:** `{"type":"end_turn","target_id":null}`

---

## Appendix — passive season resolution (context the actions play against)

- **City gold income:** `population × 0.01 × yieldFactor × (1.10 if Governance≥1)`.
- **City food:** `+ harvest (see reclaim) − (population × 0.02 + garrison upkeep)`. Upkeep per
  troop/season: infantry/archer 0.1, cavalry 0.2, siege 0.05.
- **Famine:** food < 0 → population −10%, food reset to 0.
- **Revolt:** morale < 25 → chance = (25 − morale) × 2% (×0.5 with Governance 3). A revolting city
  yields nothing that season, loses 10% of its garrison, then rebounds +15 morale.
- **Field armies eat from your faction pool** (the city they stand on pays first, the others cover
  the rest in proportion to their stores). If the pool can't cover the ration: **desertion −10%
  troops (−15% in winter), −15 morale**.
- **Army morale regrows +10/season up to 60.** City morale and prestige clamp to 0–100, trust to
  −100…+100.
- **Population grows +5% ±2% per season** when the city is not starving.
- **Victory (engine rule):** own every city on the map. The prompt's current temporary objective
  is capital capture.
