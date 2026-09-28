# 《亂世》LuanShi — FYP

A turn-based Three Kingdoms strategy game where every seat (up to 8) can be controlled by a **human, an LLM, or a scripted AI** — and control can switch mid-game. Built in Unity 6 on TBTK3. Final-Year Project, 2-person team.

## Start here

- **`AGENTS.md`** — project orientation for agents and new contributors (read first)
- **`docs/CURRENT_STATE.md`** — what's done, what's next (updated every session)
- **`docs/CONTROLLER_PROTOCOL.md`** — the human↔LLM switch protocol (FYP research core)
- **`doc/LuanShi-Game-Design-v1.md`** — full game design
- **`doc/LuanShi-Design-Implementation-Report-v1.1.docx`** — architecture & implementation report

## Run the demo (teammate quickstart)

1. Clone this repo (no Git LFS needed).
2. In Unity Hub: **Add project from disk** → select the `fyp_gaming/` folder, open with **Unity 6000.6.2f1** (first import takes a few minutes; Library/ rebuilds locally).
3. Open scene `Assets/GameUI/Scenes/LuanShi_Demo.unity` and press **Play**.
4. Click 魏都 (blue city) → 屯田/徵兵; click the red banner army → click a hex to march; 結束季節 resolves the season and shows the 朝報. The 地圖/內政/外交/諜報/研究/史官 tabs preview the full design （史官 shows the real event log).

## Layout

| Path | What |
|---|---|
| `doc/` | Design & report documents (what we build) |
| `docs/` | Living working docs (where we are) |
| `docs/screenshots/` | Demo screenshots (latest UI state) |
| `fyp_gaming/` | Unity project (6000.6.2f1, URP 2D) — open this folder in Unity |
| `fyp_gaming/Assets/GameEngine/` | Pure C# authoritative game core (no Unity deps) |
| `fyp_gaming/Assets/GameUI/` | Scene, IMGUI demo UI, procedural 2D art |
| `fyp_gaming/Assets/TBTK/` | TBTK3 (third-party, grid/camera scaffolding; do not modify source) |

The TBTK3 `.unitypackage` is intentionally not in the repo — its content is already imported under `fyp_gaming/Assets/TBTK/`. Keep the repo **private**: it contains a paid asset.
