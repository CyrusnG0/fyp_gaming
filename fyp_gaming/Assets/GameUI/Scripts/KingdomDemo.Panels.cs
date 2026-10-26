// KingdomDemo (partial) — the IMGUI shell and shared panel helpers.
//
// City/army order panels live in KingdomDemo.SelectionPanels.cs; the docked agent details live in
// KingdomDemo.AgentPanel.cs. This file keeps the per-frame OnGUI order and the small shared helpers.

using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------------ UI

        void OnGUI()
        {
            uiRects.Clear();
            float s = Screen.width / DesignWidth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            PrepareUiTheme();

            if (!ready)
            {
                GUI.Label(new Rect(20, 20, 900, 40), "KingdomDemo init failed: " + initError, uiTitle);
                return;
            }

            // The spectator loop must never be blocked by a court report or a hotseat handover.
            if (spectatorMode) DismissSpectatorModals();

            // Between human seats the whole screen is replaced by the handover blocker: the previous
            // player must not see the next player's map, panels or report (hotseat rule).
            if (handoverShowing)
            {
                DrawHandoverOverlay(uiTitle, uiBody);
                return;
            }

            if (engine == null || obs == null || string.IsNullOrEmpty(currentSeat))
            {
                GUI.Label(new Rect(20, 20, 1100, 40),
                    "KingdomDemo is waiting for game state to finish initializing.", uiTitle);
                return;
            }

            var fac = engine.State.FindFaction(currentSeat);
            if (fac == null)
            {
                GUI.Label(new Rect(20, 20, 1100, 40),
                    "KingdomDemo cannot find the current seat: " + currentSeat, uiTitle);
                return;
            }

            // 一統天下 (design §13): the game is decided — the land is one, there is nothing left to order.
            if (engine.State.WinnerSeatId != null)
            {
                DrawVictoryBanner(uiTitle, uiBody);
                return;
            }

            bool hotseat = SeatRotation.HumanSeats(engine.State).Count > 1;
            DrawTopBar(fac);
            DrawTabColumn();
            DrawEndTurnButton(fac, hotseat);

            // Observation-authorized name tags are drawn early so every panel and modal sits above them.
            DrawCityLabels();
            DrawSelectedArmyLabel();

            // selection + location popup (map view only; the court report is a modal over it)
            if (activeTab == "map")
            {
                if (!reportShowing)
                {
                    if (spectatorMode)
                    {
                        // No order panels: the spectator cannot select or command a seat.
                        DrawSpectatorPanel();
                    }
                    else
                    {
                        if (selCity != null) DrawCityPanel(selCity, uiBody, uiButton);
                        else if (selArmy != null) DrawArmyPanel(selArmy, uiBody, uiButton);
                        DrawLocationChoice(uiBody, uiButton);
                    }
                }
            }

            DrawStatusBar();

            // A locked LLM turn may never expose a tab body. ShowTab also refuses to switch, but the
            // draw path keeps the invariant even if some other code changes activeTab.
            bool tabBodyAllowed = !(uiInputLocked && !spectatorMode);
            if (activeTab != "map" && !reportShowing && tabBodyAllowed)
                DrawTabPanel(uiBody, uiButton, uiTitle, uiMono);
            if (reportShowing) DrawReport(uiBody, uiButton, uiTitle);

            // HUD overlay, not the map-tab branch: the Cancel button must stay reachable even if a
            // tab was open when the agent turn began. DrawAgentPanel adds/removes its uiRects entry.
            if (!spectatorMode && !reportShowing && agentHasSeat)
                DrawAgentPanel(AgentPanelArea());

            if (!reportShowing) DrawAgentChat();

            HandleClicks();

            // Tooltips are drawn last, above every panel, and never influence the click test above.
            DrawTooltip();
        }

        void PlannedLabel(Rect r, string text, GUIStyle style)
        {
            bool prev = GUI.enabled;
            GUI.enabled = false;      // grayed out: designed, not yet implemented
            GUI.Label(r, text, style);
            GUI.enabled = prev;
        }

        string OwnerName(CityView city)
        {
            if (city.IsNeutral) return "Neutral";
            var f = engine.State.FindFaction(city.OwnerSeatId);
            return f != null ? f.Name : "?";
        }
    }
}
