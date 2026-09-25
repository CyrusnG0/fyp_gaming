# CURRENT_STATE.md

> Day-to-day truth of where the project stands. **Update this file at the end of every work session.**
> Last updated: 2026-09-25

## Status summary

**Phase: 0→1 — Scaffolding done; strategic demo map built; starting the kingdom layer.** TBTK3 imported, URP-converted, verified. `LuanShi_Demo` scene: old-China 2D fake-3D strategic map. **TBTK's role is settled: scaffolding for grid/pathfinding/camera only — not the battle gameplay (ADR-007).**

## Done

- Game design document v1.0 (`doc/LuanShi-Game-Design-v1.md`) — full design: seasons, resources, combat, diplomacy/trust, espionage, research, victory conditions.
- Design & implementation report v1.0 → **v1.1** (`doc/LuanShi-Design-Implementation-Report-v1.1.docx`):
  - v1.1 change: **architecture switched from web stack (React/FastAPI/PostgreSQL) to Unity-native** (Unity 6 URP 2D, pure C# authoritative engine, hot-seat multiplayer, JSON snapshot + event log persistence). See `docs/DECISIONS.md` ADR-001.
- Repo initialized for multi-session work: `AGENTS.md`, `docs/` (this file, FEATURES, DECISIONS, CONTROLLER_PROTOCOL, OPEN_QUESTIONS).
- Controller switch protocol specified (`docs/CONTROLLER_PROTOCOL.md`) — the FYP research core.
- Unity project exists: `fyp_gaming/`, Unity 6000.6.2f1, URP 2D template, otherwise empty (default SampleScene only).
- Unity MCP configured (`.kimi-code/mcp.json`) and **verified working** (2026-09-25) — tools register only in sessions created after workspace trust.
- **TBTK 3.1.5 imported** into `fyp_gaming/Assets/TBTK/` (2026-09-25). Compiles clean. `Demo_Classic.unity` verified end-to-end via MCP: hex grid generates at runtime, UnitPerTurn mode, 2 factions × 5 units, cover obstacles, collectibles, HP overlays, turn-order UI, AI-vs-AI battle ran 12 rounds to a GAME OVER screen (Faction2 won).
- Project settings change (uncommitted): `activeInputHandler` set to **Both** (TBTK uses legacy `UnityEngine.Input`; project was Input System only). **Requires Unity editor restart to take effect** — until then, play mode spams `InvalidOperationException` input errors and mouse/keyboard don't reach TBTK.
- **TBTK materials converted to URP** (2026-09-25, scripted): 75 materials Standard→URP/Lit (`_MainTex`→`_BaseMap`, transparent via `_Mode`; particles→URP Particles/Unlit; `Custom/BlendColorsOverlayTexture`→TBTK's own URP shadergraph). Demo scenes render correctly under URP now.
- **`LuanShi_Demo.unity` strategic map built + verified in play mode** (`Assets/GameUI/Scenes/`): 26×17 hex grid (`GridGenerator`), painted terrain — river (unwalkable), 2 mountain clusters (unwalkable, impassable), 3 forest clusters (cost 2) — 3 cities (Wei red west / Shu blue east / neutral south), 4 banner-armies per faction, 3/4 camera (pitch 45, zoom 8–45, pan limits). Fog of war, unit deployment, collectibles all disabled.
- **Art direction: 2D fake-3D old-China (ADR-008).** Painted tile textures (plains/water) on hexes + billboard sprites for mountains/forests/cities/army-banners (`BillboardSprite.cs` keeps 45° pitch; banners tinted per faction). Placeholder art generated procedurally by `Assets/GameUI/Art/_make_sprites.py` (PIL, Python 3.13) — regenerate with better art anytime.

## In progress

- (nothing actively in progress)

## Known integration issues (TBTK ↔ this project)

1. ~~URP vs Built-in shaders~~ — **resolved 2026-09-25**: TBTK materials converted to URP (see Done).
2. **Input handling**: see `activeInputHandler` note above; editor restart pending.
3. TBTK prefabs are old serialization format (harmless "version 5 below minimum 6" warnings on import; re-save upgrades them).
4. TBTK demo scene opens with harmless "Parameter 'Selected' does not exist" warnings (old UI serialization). These warnings also make `Unity_RunCommand` report "failure" — judge success by the `[Log]` lines.
5. **`GridManager.Init` calls `CombineMeshes.Combine()`** if the component exists on the Grid object — it merges all node meshes into one single-material mesh, destroying per-node terrain colors at runtime. Removed from `LuanShi_Demo`'s Grid object (its MeshRenderer disabled too). If terrain renders all-one-color, check this first.
6. **Template junk**: TBTK's New-Scene template spawns collectible crates at runtime via `CollectibleManager` — disabled in LuanShi_Demo (`generateInGame=false, spawnChance=0, activeItemLimit=0`). Its 64 template obstacle blocks were deleted from GridItems.
7. **Sprite draw order**: billboard prop sprites need positive `sortingOrder` (we use `500 - z*10`); with negative orders some get overdrawn by TBTK overlay objects (observed: far-side mountain sprites invisible until bumped).

## TBTK quick facts (from inspection)

- Demo scenes: `Demo_Classic`, `Demo_XCom`, `Demo_JRPG`, `Demo_Persistent_*`, `Demo__Menu` under `Assets/TBTK/DemoNScenes/`.
- Turn modes: UnitPerTurn (demo default) and FactionPerTurn. AI hooks: `UnitManager.EndTurn_*` routes non-playable units/factions to `TBTK.AI.MoveUnit/MoveFaction`; proper end-turn API is `GameControl.EndTurn()` (guarded), not `TurnControl.EndTurn()`.
- Runtime DB assets in `Assets/TBTK/Resources/DB_TBTK/` (units, abilities, damage table, perks, collectibles, global settings).
- Systems available: hex+square grids, A* pathfinding, fog of war (`GameControl.EnableFogOfWar()`), cover system, abilities/faction abilities, perks, collectibles, counter-attack, AP costs, deployment phase, full UI scaffold (HUD, unit info, tooltips, game over), camera control.
- Full doc: `Assets/TBTK/TBTK_3_Documentation.pdf`.

## Next actions (priority order)

1. **Restart Unity editor** (applies activeInputHandler=Both), then open `LuanShi_Demo` and confirm clicking/dragging works.
2. ~~Team decision: TBTK's role~~ — **resolved 2026-09-25 (ADR-007)**: scaffolding for grid/pathfinding/camera only; URP conversion done.
3. **Resolve open decisions** blocking Phase 0 — see `docs/OPEN_QUESTIONS.md` (MVP seat count, map sizes, turn-order rule, LLM provider).
4. **Professor demo (current push)**: one-season loop on the LuanShi_Demo map — city panel (population/food/gold), 2–3 orders (屯田 farm, 徵兵 raise troops, march an army banner along a hex path), "end season" → resolution + 朝報 morning report + 史官 event log. Placeholder numbers OK; all orders must flow through one `ActionCommand` schema so an LLM seat can later submit the same commands.
5. **Scaffold `GameEngine/` assembly**: canonical state model (Game/Seat/Nation/City/Army/General), `ActionCommand` DTOs, `GameEngine.SubmitAction()` skeleton with validation pipeline.
6. **Implement controller interfaces** per `docs/CONTROLLER_PROTOCOL.md` (`IPlayerController`, HumanController stub, ScriptedController stub, LLMController stub) + event log writer.
7. **Text-mode season prototype** (headless engine test that plays one Option B season with scripted AI) — proves the loop before any UI.
8. First MVP map (open-plains map) as data, not code.

## Blockers

- None technical right now. Waiting on team decisions in `docs/OPEN_QUESTIONS.md` (seat count, LLM provider/budget, map dimensions, TBTK role).

## Notes for the next agent

- Read `AGENTS.md` first, then this file, then `docs/DECISIONS.md` and `docs/CONTROLLER_PROTOCOL.md`.
- The formal report docx lags `docs/`; when they conflict on architecture, **v1.1 docx + docs/ win** (v1 docx is superseded).
- Game design detail (numbers, tables, action list) is in `doc/LuanShi-Game-Design-v1.md`; treat MVP scope per report §14: 4 seats, 2 maps, 15–20 seasons, subset of the 41 actions.
- Unity MCP: works, but `Unity_RunCommand` has a 60 s client timeout — long operations (package import) keep running server-side after the timeout; poll state instead of retrying blindly. `GetInstanceID()` is compile-error in Unity 6000.6; avoid it in RunCommand snippets.
