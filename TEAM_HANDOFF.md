# LuanShi teammate handoff

12 November 2026 · Shared code baseline `8c879fe` · Unity 6000.6.2f1

LuanShi is a Unity turn-based strategy FYP. Each LLM receives a view filtered by fog of war and submits JSON actions to the same authoritative engine used by other controllers. The architect approves design and merges. Coordinate task ownership and scope changes in the group chat.

Our milestone is a real two-LLM match in which both can make informed decisions, manage food, fight and capture, exchange diplomacy through the game, and choose research. Give them incentives and a clear victory condition. A real battle is required for the battle milestone. Diplomacy and research each need a separate scripted contract test and a real opportunity in the match. Record any declined opportunity.

## Start here

1. Fetch `origin/main`. The coordinator's local remote-tracking ref was `8c879fe` when this handoff was written. If main has advanced, post the new SHA in the group chat and confirm the task baseline before starting.
2. Claim one task card and create your own branch. Use `codex/team-integration` for work intended for the combined build, or current main for a clean baseline comparison. Follow the [integration README](fyp_gaming/README.md). Open the inner `fyp_gaming/` folder in Unity. Work only in the files listed in your card. Read those files and the dependencies named in the card. Stop and post the exact file and reason if you need a scope expansion.
3. Use your own API key via `LUANSHI_LLM_API_KEY`. Put local settings in `fyp_gaming/llm.local.json`. Commit only sanitized profile templates. Keep keys, local configs, raw exchanges and run data out of Git. The coordinator can share approved redacted evidence privately.
4. Keep commits on your branch. Send the commit SHA and evidence in the group chat. The integration owner coordinates sharing, pushing and merging. Never edit another person's branch or files.

From the repository root, the baseline checks are:

```sh
bash fyp_gaming/Tests/run-tests.sh
bash fyp_gaming/Tests/run-audits.sh
dotnet run --project fyp_gaming/Tests/ResponseDiagnostics/ResponseDiagnostics.csproj -c Release
```

## What exists and who owns the unfinished work

| Status | Reference | Next action |
| --- | --- | --- |
| Shared baseline | `8c879fe` | Unity spectator matches, legal movement and domestic actions, per-faction personas, easy-research balance, optional manual lookup, diagnostics and run recording. Earlier integration branches for the provider diagnostics, personas and balance are already represented in main. Start from main. |
| Combat candidate | `kimi/attack-pilot-integration` at `5fbf0ef` | Contains the earlier `codex/attack-capture` work and its follow-up fixes. Worker reported harness, audits and UI compile passing. Both Unity fake battle attempts stopped before dispatch. Integration owner must verify field battle and city assault in Unity before a paid combat match. |
| Manual expansion candidate | `kimi/manual-topics` at `e5922a0` | Review and integrate with the combat branch. Broader manual coverage is distinct from granting an LLM the ability to issue an action. |
| Offline replay prototype | `kimi/offline-replay` at `7b71ef6`, plus uncommitted work in its worktree | Integration owner preserves the dirty work and owns replay and its summary. Current loader accepts the original five-season demo only. An earlier revision matched that run's 44 observation hashes under Unity Mono CLI. Broaden run selection and validate the five- and 20-season recordings, including the timeout and empty-response cases, before claiming general replay support. |
| Deliberation foundation | `DeliberationBudget*.cs` on main | Ledger exists. Runner wiring, extra inquiry routes and strategic-note behavior need design and implementation. Manual lookup is the only currently wired inquiry. |
| Diplomacy and research | `fyp_gaming/Assets/GameEngine/ActionCommand.cs`, `GameEngine.cs` | Existing engine actions need explicit LLM-facing contracts, legal options, visibility rules and end-to-end tests. The baseline match provides no evidence of either feature working through the LLM. |

The integration branch combines the combat, expanded manual and replay candidates listed above. Use its README for branch setup and the pending verification for each candidate.

### Recorded baseline to beat

20 seasons, seed `12345`, easy-research, DeepSeek `deepseek-flash`, Wei mild vs Shu aggressive. Run ID `run-20261110043837835-9516d2fc`. Across 129 requests, the engine accepted 107 actions with zero rejections. Shu made 54 marches and Wei made zero. Both used zero manual lookups. Wei ended with 19 food and 4,883 gold. Shu ended with 0 food and 4,103 gold. The game continued through three request timeouts and one empty reply. The empty reply was Shu season 7 request 3, with finish reason `length` and all 8,000 reported completion tokens spent on provider-reported reasoning. Total reported usage was 807,576 input and 202,684 output tokens. Timeout usage and actual dollar cost are unknown.

Use this run to trace food and movement. March frequency alone does not measure expansion. Ending food alone does not identify which expenses caused the shortage.

## Task cards

### 1. Provider bring-up and JSON contract

- **Own:** new `provider-profiles/<provider>.example.json` and `provider-profiles/<provider>.md`, plus your ignored local config. Read `fyp_gaming/Assets/GameEngine/Agents/OpenAiChatTransport.cs`, `LlmConfig.cs`, `LlmActionParser.cs`, and `fyp_gaming/Tests/ResponseDiagnostics/`. The profile uses the existing config keys `provider`, `base_url`, `model`, `json_mode`, `max_output_tokens` and request caps. A transport, parser or diagnostics change needs separate ownership.
- **Run in order:** fake-provider JSON contract, up to three approved live probe requests, then a five-season fake rehearsal followed by a capped five-season real run. Use separate run IDs. Agree on the live request cap and any spend limit with the architect before calling the provider.
- **Pass:** show valid action JSON reaching the engine validator. Report provider and model, settings, valid and invalid counts, finish reasons, content presence, reported reasoning and token usage where available, timeouts and cost or unknown. Distinguish length-limited empty content, ordinary empty content, malformed JSON and transport failure. An empty response must never execute an action. The 8,000-token DeepSeek setting still produced one empty reply, so verify the new provider from its own telemetry.

### 2. Deliberation and decision context

- **Own initially:** new `handoff-proposals/deliberation.md`. Read `fyp_gaming/Assets/GameEngine/Agents/LlmTurnRunner*.cs`, `DeliberationBudget*.cs`, the investigation request handlers and prompt builders, plus the observation builder as needed. Submit a wiring plan and proposed prompt wording for approval before changing code.
- Give the model a bounded opportunity to ask for rules, inspect visible entities, read history or recall memory. These reads consume deliberation budget and zero game actions. Specify how deliberation ends with a legal action or a strategic note that is read back on that faction's next turn. Define request, time and note limits and the fallback for a failed or empty response. Notes are separate from canonical game state.
- Propose a compact fog-filtered map view with boundaries, own armies, known locations, and distance or direction to useful destinations. Include food inflow, consumption and forecast from card 3 where available. Show sample input and strict JSON output for action, inquiry and note. Include tests for seat isolation, hidden information, action counters and next-turn retrieval.

### 3. Food relief and upkeep

- **Own:** `fyp_gaming/Assets/GameEngine/BalanceConfig*.cs`, `BalanceProfile*.cs`, and new `handoff-proposals/economy.md`. Read the economy and upkeep paths in `GameEngine.cs` and the approved baseline evidence. Reconcile food by season from income, consumption, upkeep and other adjustments. Mark unknown quantities.
- Propose a gold-to-food `buy_food` action with conversion, availability, cap and action cost, and a second upkeep pass grounded in the 20-season numbers. Include proposed values, expected effects for both factions and test cases. Get architect review before merge.
- An actual new action needs command, engine validation and LLM contract changes outside this card. Ask for an explicit integration task before implementation. That task must test insufficient gold, repeated buying, correct food/gold deltas and unchanged default profiles.

### 4. Compact scenario and win conditions

- **Own:** new scenario/config data and `handoff-proposals/scenario.md`. Read `fyp_gaming/Assets/GameUI/Scripts/KingdomDemo.cs` initialization and `GameEngine.cs` victory logic as dependencies. Report any code needed for loading scenario data or scoring. The current demo places cities in its Unity initializer, and the engine's existing victory check is unification.
- Draft a compact map with a few cities per faction and neutral cities between them. Show that legal paths give both factions a chance to make contact within a few seasons. Propose capture-target victory or highest score at season 30, with explicit scoring, ties and timing. Include visible incentives for food relief, diplomacy and research. Models choose when to use the mechanics. Record refusals as data.

### 5. Match operations and analysis

- **Own:** `tools/match_stats.py` and new `handoff-proposals/match-operations.md`. The existing script is a local ignored helper. Request a sanitized copy from the integration owner and propose an approved tracked location if the team wants to ship it. Read the run summary schema and only the approved logs you need.
- Record scenario, seed, commit, provider/model, feature flags, and limits with each run. Report per-side action types, march destinations classified as neutral city or empty hex when the map evidence supports it, food curves, lookups, rejections, reasoning tokens and missing usage. Track distance to a known objective or repeated movement separately from the destination classification. After each match send the stats and one paragraph on observed differences.

## Integration order and completion

1. **In parallel:** teammate starts provider fake tests and prepares deliberation, food and scenario proposals. Integration owner preserves replay work, generalizes offline replay and its summaries, and verifies the combat candidate in Unity.
2. **After scope approval:** wire deliberate turns, context, food relief and the scenario. Assign separate command-surface cards for diplomacy and research. Each gets legal and illegal JSON cases, seat-scoped results, and an engine-state assertion. Verify diplomacy can convey a message or offer and a response through the game's own rules. Verify that research can be selected and produces its recorded effect.
3. **Before paid play:** use scripted responses to exercise the full path through Unity and the engine, including combat/capture, diplomacy, research and victory/tiebreak handling. Verify an empty reply never becomes an action and recorded outcomes can be analyzed offline. Then run the agreed capped two-LLM match and report the results with run IDs and replay verification.

After each work session post the branch and SHA, what you built, commands and actual outputs or redacted log excerpts, what you couldn't verify, and the exact blocker or scope decision needed. The architect reviews before any merge.
