// KingdomDemo (partial) — the persistent HUD: faction bar, tab column, End-turn control and status strip.

using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        /// <summary>
        /// Top bar: faction / controller / season / remaining orders, then the faction totals on a second
        /// line. The controller word is derived from the observation's FactionView, so the handover to an
        /// LLM or scripted seat is visible without exposing any extra state. In spectator mode the same
        /// two lines carry the pilot context instead of a single seat's totals.
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
            string detail;
            if (spectatorMode)
            {
                header = SpectatorHeaderLine();
                detail = $"Domestic management only  |  Orders {orders}/{BalanceConfig.CommandPointsPerSeason}";
                if (agentFakeMode) detail += "  |  FAKE";
            }
            else
            {
                header =
                    $"{fac.Name} - {ControllerLabel(controller)}  |  " +
                    $"Season {engine.State.Season} ({BalanceConfig.SeasonName(engine.State.Season)})  |  " +
                    $"Orders {orders}/{BalanceConfig.CommandPointsPerSeason}";
                detail =
                    $"Food {food}  |  Gold {gold}  |  Population {population}  |  Prestige {prestige}";
            }

            var top = new Rect(TopBarX, TopBarY, TopBarWidth, TopBarHeight);
            uiRects.Add(top);
            DrawBackdrop(top);

            var headerStyle = new GUIStyle(uiTitle) { wordWrap = false };
            for (int size = 21; size >= 14; size -= 2)
            {
                headerStyle.fontSize = size;
                if (headerStyle.CalcSize(new GUIContent(header)).x <= TopBarWidth - 26f) break;
            }
            GUI.Label(new Rect(top.x + 12f, top.y + TopBarTitleRowY, top.width - 24f, TopBarTitleRowHeight),
                      EllipsizeToWidth(headerStyle, header, top.width - 24f), headerStyle);
            GUI.Label(new Rect(top.x + 12f, top.y + TopBarTotalsRowY, top.width - 24f, TopBarTotalsRowHeight),
                      EllipsizeToWidth(uiSmall, detail, top.width - 24f), uiSmall);
        }

        /// <summary>
        /// Spectator context line. Completed/Paused never say "Active: -": they name the season count and
        /// the faction whose filtered view the map is showing.
        /// </summary>
        string SpectatorHeaderLine()
        {
            SpectatorSession session = spectatorSession;
            string viewed = ViewedFactionNameFromObservation();
            if (session == null)
                return "LLM vs LLM pilot - unavailable  |  viewing " + viewed;

            int played = session.SeasonsPlayed;
            switch (session.State)
            {
                case SpectatorState.Running:
                    return "LLM vs LLM pilot  |  " +
                           $"Season {session.CurrentSeason}/{session.SeasonsPlanned}  |  Active: {viewed} (LLM)";
                case SpectatorState.Paused:
                    string reason = string.IsNullOrEmpty(session.PauseReason)
                        ? "no reason given" : session.PauseReason;
                    return $"Pilot paused - {reason} - viewing {viewed}";
                case SpectatorState.Completed:
                    return $"Pilot completed - {played} seasons - viewing {viewed}";
                case SpectatorState.Failed:
                    return $"Pilot failed - {played} seasons - viewing {viewed}";
                default:
                    return "LLM vs LLM pilot - starting  |  viewing " + viewed;
            }
        }

        /// <summary>The concept tabs. They are view switches, but order buttons live inside them, so the
        /// whole column is frozen (and visibly disabled) while a controller owns the turn.</summary>
        void DrawTabColumn()
        {
            PrepareUiTheme();
            // A spectator may browse every tab (read-only); order buttons inside stay locked by uiInputLocked.
            bool interactive = spectatorMode ? !reportShowing : (!uiInputLocked && !reportShowing);

            for (int i = 0; i < Tabs.GetLength(0); i++)
            {
                var rect = new Rect(TabColumnX, TabTop + i * TabStep, TabColumnWidth, TabHeight);
                uiRects.Add(rect);
                bool active = activeTab == Tabs[i, 0];
                // A locked tab is an opaque disabled plate, never a translucent button showing the map.
                var style = !interactive
                    ? (active ? uiTabActiveLocked : uiTabLocked)
                    : (active ? uiTabActive : uiTab);
                if (GUI.Button(rect, Tabs[i, 1], style) && interactive) ShowTab(Tabs[i, 0]);
            }
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
            if (GUI.Button(rect, label, interactive ? uiButton : uiButtonDisabled) && interactive)
            {
                locationChoiceOpen = false;
                EndTurn();
            }

            if (hotseat)
                GUI.Label(new Rect(EndTurnX, EndTurnY + EndTurnHeight + 2f, EndTurnWidth, 14f),
                          EllipsizeToWidth(uiSmall, $"Turn: {fac.Name}", EndTurnWidth), uiSmall);
        }

        /// <summary>
        /// Bottom status strip (46 design px): one row with the status message on the left and the lock
        /// reason/controls hint on the right. The recent command feed is a slim second row, drawn only
        /// when there is a feed; it no longer reserves empty height over the map.
        /// </summary>
        void DrawStatusBar()
        {
            PrepareUiTheme();
            var bar = new Rect(StatusBarX, StatusBarY, StatusBarWidth, StatusBarHeight);
            uiRects.Add(bar);
            DrawBackdrop(bar);

            float hintLeft = Mathf.Max(bar.x + 12f, bar.x + bar.width - 12f - StatusHintWidth);
            GUI.Label(new Rect(bar.x + 12f, bar.y + StatusMessageRowY,
                               Mathf.Max(200f, hintLeft - bar.x - 24f), StatusMessageRowHeight),
                      EllipsizeToWidth(uiBody, statusMsg, Mathf.Max(200f, hintLeft - bar.x - 24f)), uiBody);
            if (uiInputLocked)
                GUI.Label(new Rect(hintLeft, bar.y + StatusMessageRowY,
                                   StatusHintWidth, StatusMessageRowHeight),
                          EllipsizeToWidth(uiReasonRight, "Orders locked: " + LockedReason(), StatusHintWidth),
                          uiReasonRight);
            else
                GUI.Label(new Rect(hintLeft, bar.y + StatusMessageRowY,
                                   StatusHintWidth, StatusMessageRowHeight),
                          EllipsizeToWidth(uiHint, ControlsHint, StatusHintWidth), uiHint);

            if (jsonFeed.Count == 0) return;
            int first = Mathf.Max(0, jsonFeed.Count - StatusFeedLines);
            for (int i = first; i < jsonFeed.Count; i++)
            {
                float y = bar.y + StatusFeedRowY + (i - first) * StatusFeedLineHeight;
                GUI.Label(new Rect(bar.x + 12f, y, bar.width - 24f, StatusFeedLineHeight),
                          EllipsizeToWidth(uiMonoTiny, jsonFeed[i], bar.width - 24f), uiMonoTiny);
            }
        }
    }
}
