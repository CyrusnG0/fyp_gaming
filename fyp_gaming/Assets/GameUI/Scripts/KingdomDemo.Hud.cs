// KingdomDemo (partial) — the persistent HUD: faction bar, tab column, End-turn control and status strip.

using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        /// <summary>
        /// Top bar: faction / controller / season / remaining orders, then the faction totals on a second
        /// line. The controller word is derived from the engine's ControllerType, so the handover to an
        /// LLM or scripted seat is visible without exposing any extra state.
        /// </summary>
        void DrawTopBar(FactionState fac)
        {
            PrepareUiTheme();

            int food = 0, gold = 0, population = 0;
            if (obs != null)
                foreach (var city in obs.CitiesOf(currentSeat))
                {
                    food += city.Food;
                    gold += city.Gold;
                    population += city.Population;
                }

            int orders = obs != null ? obs.CommandPoints : fac.CommandPoints;
            int prestige = obs != null ? obs.Prestige : fac.Prestige;
            string controller = fac.Controller;
            if (obs != null)
            {
                var view = obs.Faction(currentSeat);
                if (view != null) controller = view.Controller;
            }

            string header;
            if (spectatorMode)
            {
                int season = spectatorSession != null ? spectatorSession.CurrentSeason : engine.State.Season;
                int planned = spectatorSession != null ? spectatorSession.SeasonsPlanned
                    : Mathf.Max(0, spectatorSeasons);
                string active = spectatorSession != null && !string.IsNullOrEmpty(spectatorSession.ActiveFactionName)
                    ? spectatorSession.ActiveFactionName : "-";
                header = "LLM vs LLM pilot (domestic)  |  " +
                         $"Season {season}/{planned}  |  Active: {active} (LLM)  |  " +
                         $"Orders {orders}/{BalanceConfig.CommandPointsPerSeason}";
                if (agentFakeMode) header += "  |  FAKE";
            }
            else
            {
                header =
                    $"{fac.Name} - {ControllerLabel(controller)}  |  " +
                    $"Season {engine.State.Season} ({BalanceConfig.SeasonName(engine.State.Season)})  |  " +
                    $"Orders {orders}/{BalanceConfig.CommandPointsPerSeason}";
            }
            string totals =
                $"Faction totals: Food {food} | Gold {gold} | Population {population} | Prestige {prestige}";

            var top = new Rect(TopBarX, TopBarY, TopBarWidth, TopBarHeight);
            uiRects.Add(top);
            DrawBackdrop(top);

            var headerStyle = new GUIStyle(uiTitle) { wordWrap = false };
            for (int size = 21; size >= 14; size -= 2)
            {
                headerStyle.fontSize = size;
                if (headerStyle.CalcSize(new GUIContent(header)).x <= TopBarWidth - 26f) break;
            }
            GUI.Label(new Rect(top.x + 12f, top.y + 4f, top.width - 24f, 28f), header, headerStyle);
            GUI.Label(new Rect(top.x + 12f, top.y + 32f, top.width - 24f, 20f), totals, uiSmall);
        }

        /// <summary>The concept tabs. They are view switches, but order buttons live inside them, so the
        /// whole column is frozen (and visibly disabled) while a controller owns the turn.</summary>
        void DrawTabColumn()
        {
            PrepareUiTheme();
            // A spectator may browse every tab (read-only); order buttons inside stay locked by uiInputLocked.
            bool interactive = spectatorMode ? !reportShowing : (!uiInputLocked && !reportShowing);

            var previous = GUI.enabled;
            GUI.enabled = interactive;
            for (int i = 0; i < Tabs.GetLength(0); i++)
            {
                var rect = new Rect(TabColumnX, TabTop + i * TabStep, TabColumnWidth, TabHeight);
                uiRects.Add(rect);
                var style = activeTab == Tabs[i, 0] ? uiTabActive : uiTab;
                if (GUI.Button(rect, Tabs[i, 1], style)) ShowTab(Tabs[i, 0]);
            }
            GUI.enabled = previous;
        }

        /// <summary>Prominent human turn control on the right; disabled and relabelled while locked.</summary>
        void DrawEndTurnButton(FactionState fac, bool hotseat)
        {
            PrepareUiTheme();
            if (spectatorMode) { DrawSpectatorTurnControl(); return; }
            var rect = new Rect(EndTurnX, EndTurnY, EndTurnWidth, EndTurnHeight);
            uiRects.Add(rect);

            bool interactive = !uiInputLocked && !reportShowing;
            string label = uiInputLocked ? "Waiting..." : hotseat ? $"End {fac.Name}'s turn" : "End turn";

            var previous = GUI.enabled;
            GUI.enabled = interactive;
            if (GUI.Button(rect, label, uiButton))
            {
                locationChoiceOpen = false;
                EndTurn();
            }
            GUI.enabled = previous;

            if (hotseat)
                GUI.Label(new Rect(EndTurnX, EndTurnY + 46f, EndTurnWidth, 18f),
                          $"Turn: {fac.Name}", uiSmall);
        }

        /// <summary>
        /// Bottom status strip: the existing one-line status message, a permanent controls legend and the
        /// recent command JSON. Keeping the legend here makes the map's interaction model discoverable
        /// without opening a separate help screen.
        /// </summary>
        void DrawStatusBar()
        {
            PrepareUiTheme();
            var bar = new Rect(StatusBarX, StatusBarY, StatusBarWidth, StatusBarHeight);
            uiRects.Add(bar);
            DrawBackdrop(bar);

            GUI.Label(new Rect(bar.x + 12f, bar.y + 6f, bar.width - 24f, 22f), statusMsg, uiBody);
            GUI.Label(new Rect(bar.x + 12f, bar.y + 29f, 850f, 20f),
                spectatorMode
                    ? "Spectator mode - orders locked - stop/resume/extend in the pilot panel"
                    : ControlsHint, uiHint);
            if (uiInputLocked)
                GUI.Label(new Rect(bar.x + 872f, bar.y + 28f, bar.width - 884f, 36f),
                          "Orders locked: " + LockedReason(), uiReasonWrap);

            int shown = uiInputLocked ? 2 : 3;
            int first = Mathf.Max(0, jsonFeed.Count - shown);
            float feedTop = bar.y + (uiInputLocked ? 64f : 50f);
            for (int i = first; i < jsonFeed.Count; i++)
                GUI.Label(new Rect(bar.x + 12f, feedTop + (i - first) * 15f, bar.width - 24f, 15f),
                          jsonFeed[i], uiMono);
        }
    }
}
