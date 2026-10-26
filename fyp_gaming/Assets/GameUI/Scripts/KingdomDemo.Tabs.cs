// KingdomDemo (partial) — the Domestic / Diplomacy / Intel / Research / History tab panels.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------ concept tabs
        // Domestic / Diplomacy / Intelligence / Research are all live now;
        // History renders the real in-memory event log.

        /// <summary>Opens a concept panel by tab key. Same entry point the tab bar uses,
        /// so scripted demos and screenshot passes can drive the UI without a mouse.</summary>
        public void ShowTab(string key)
        {
            // Single-LLM mode freezes the whole tab layer during the agent's turn. Spectators may
            // browse read-only; their tab buttons stay available.
            if (uiInputLocked && !spectatorMode)
            {
                activeTab = "map";
                return;
            }
            activeTab = key;
        }

        // one scroll offset per concept tab, so switching tabs does not lose the reader's place
        private readonly Dictionary<string, Vector2> tabScrolls = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, float> tabContentHeights = new Dictionary<string, float>();
        private const float TabScrollStep = 24f;   // design-space pixels per wheel notch

        void DrawTabPanel(GUIStyle body, GUIStyle btn, GUIStyle title, GUIStyle mono)
        {
            if (uiInputLocked && !spectatorMode) return;   // single-LLM lock: no tab body at all
            float height = TabPanelHeight();
            // The side dock (agent/spectator) owns 960..1380, so a docked tab body stays left of it.
            float x = SideDockVisible() ? TabPanelDockedX : TabPanelX;
            var p = new Rect(x, TabPanelY, TabPanelWidth, height);
            uiRects.Add(p);
            DrawBackdrop(p, CurrentSeatAccent());
            GUI.Label(new Rect(p.x + 20, p.y + 12, TabPanelWidth - 40f, 30), TabTitle(), title);

            bool scrolls = activeTab == "domestic" || activeTab == "diplomacy"
                        || activeTab == "intel" || activeTab == "research";
            if (scrolls)
            {
                // A concept panel can outgrow the shell (diplomacy rows, intel missions, research lines):
                // it gets a scroll view, and Input.cs switches TBTK's camera zoom off while the pointer
                // is over a panel, so the wheel scrolls here instead of zooming the map underneath.
                var view = new Rect(p.x + 4, p.y + 52, TabPanelWidth - 8f, height - 52f - 56f);
                float contentHeight = tabContentHeights.TryGetValue(activeTab, out var measured)
                    ? measured : view.height;
                // Never below the view height: content that fits yields a zero scroll range (no phantom
                // scrolling into blank space), while real overflow keeps its exact measured range.
                contentHeight = Mathf.Max(contentHeight, view.height);
                tabScrolls.TryGetValue(activeTab, out var scroll);
                TryWheelScroll(p, view, contentHeight, ref scroll);

                // 776 leaves room for the vertical bar (16px) without ever triggering a horizontal one
                scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, TabPanelWidth - 24f, contentHeight), false, false);
                tabScrolls[activeTab] = scroll;

                // x - 4 and y - 52 cancel the view's own offsets, so a panel keeps the exact layout it
                // had outside the scroll view (its drawers still work in the old panel coordinates).
                var inner = new Rect(-4f, -52f, TabPanelWidth, contentHeight);
                float endY;
                switch (activeTab)
                {
                    case "domestic":  endY = DrawDomesticPanel(inner, body); break;
                    case "diplomacy": endY = DrawDiplomacyPanel(inner, body); break;
                    case "intel":     endY = DrawIntelPanel(inner, body, btn); break;
                    default:          endY = DrawResearchPanel(inner, body, btn); break;
                }
                GUI.EndScrollView();

                // measured this frame and used from the next one, so the scroll range hugs the content
                tabContentHeights[activeTab] = Mathf.Max(endY + 8f, view.height);
            }
            else
            {
                DrawHistorianPanel(p, body, mono);   // keeps its own content-sized scroll view
            }

            // pinned to the panel bottom, so a content-sized panel keeps its button inside
            if (GUI.Button(new Rect(p.x + 320, p.y + height - 56, 160, 40), "Close", btn)) activeTab = "map";
        }

        /// <summary>
        /// Wheel scrolling for a panel, handled here instead of relying on
        /// <see cref="GUI.BeginScrollView"/>'s internal hit test. The wheel is consumed over the whole
        /// panel (title band and margins included), the offset is clamped to the content range, and the
        /// event is marked used so the ScrollView cannot apply the same notch a second time. Returns true
        /// when it consumed the wheel.
        /// </summary>
        bool TryWheelScroll(Rect panel, Rect view, float contentHeight, ref Vector2 scroll)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.ScrollWheel) return false;
            UiPointerMapping.ToDesignSpaceFromTopLeft(e.mousePosition.x, e.mousePosition.y,
                Screen.width, out float mouseX, out float mouseY);
            if (!panel.Contains(new Vector2(mouseX, mouseY))) return false;

            float range = contentHeight - view.height;
            if (range <= 0f) return false;    // the content fits — there is nothing to scroll

            scroll.y = Mathf.Clamp(scroll.y + e.delta.y * TabScrollStep, 0f, range);
            e.Use();
            return true;
        }

        /// <summary>
        /// Draws body text at its real wrapped height instead of a guessed one. CalcHeight honours the
        /// style's wordWrap, so a footer that wraps to three lines reserves three lines and the panel's
        /// measured content height includes all of it — nothing bleeds under the Close button. The
        /// minimum keeps single-line rows at their old spacing.
        /// </summary>
        static float WrappedLabel(GUIStyle style, string text, float x, float y, float width, float minHeight)
        {
            float height = Mathf.Max(minHeight, style.CalcHeight(new GUIContent(text), width));
            GUI.Label(new Rect(x, y, width, height), text, style);
            return height;
        }

        /// <summary>
        /// Panel height per tab: the design previews keep the full shell; the historian chronicle hugs its
        /// contents (capped) so a two-record log does not open an 80%-empty box.
        /// </summary>
        float TabPanelHeight()
            => activeTab == "history" ? 178f + HistorianViewHeight() : TabPanelDefaultHeight;

        /// <summary>Height of the historian scroll view for the current seat: content-sized, capped.</summary>
        float HistorianViewHeight()
            => Mathf.Min(HistorianViewMaxHeight,
                         (obs != null ? obs.Events.Count : 1) * HistorianLineHeight + 8f);

        string TabTitle()
        {
            switch (activeTab)
            {
                case "domestic":  return "Domestic · Orders";
                case "diplomacy": return "Diplomacy · Orders";
                case "intel":     return "Intel · Orders";
                case "research":  return "Research · Orders";
                default:           return "History · Chronicle";
            }
        }

        float DrawDomesticPanel(Rect p, GUIStyle body)
        {
            var wrap = new GUIStyle(body) { wordWrap = true };
            float y = p.y + 52;
            y += WrappedLabel(wrap, "Domestic actions (design document chapter 11 · 10 total)",
                              p.x + 20, y, 760, 22) + 8;

            // Live actions mirror ACTIONS.md §3 and read their numbers from BalanceConfig,
            // so this panel cannot drift from what the engine actually charges.
            string[] live =
            {
                $"Tax      costs {OrderCost(ActionType.LevyTax)}  Gold +{BalanceConfig.TaxLevyAmount}, morale -{BalanceConfig.TaxLevyMoraleCost}",
                $"Lighten labor  costs {OrderCost(ActionType.LightenLabor)}  Gold -{BalanceConfig.LightenLaborGoldCost}, morale +{BalanceConfig.LightenLaborMoraleGain}",
                $"Reclaim  costs {OrderCost(ActionType.Reclaim)}  City food yield +{BalanceConfig.ReclaimPctPerUse}% (cap +{BalanceConfig.ReclaimPctCap}%)",
                $"Recruit  costs {OrderCost(ActionType.Recruit)}  Gold -{UnitTypes.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain)}/{UnitTypes.RecruitGoldCost(UnitTypes.Archer, BalanceConfig.RecruitTroopGain)}/{UnitTypes.RecruitGoldCost(UnitTypes.Cavalry, BalanceConfig.RecruitTroopGain)}/{UnitTypes.RecruitGoldCost(UnitTypes.Siege, BalanceConfig.RecruitTroopGain)} by type, population -{BalanceConfig.RecruitPopCost}, garrison +{BalanceConfig.RecruitTroopGain}",
                $"Train    costs {OrderCost(ActionType.Train)}  Training +1 (cap {MyTrainingCap()})",
                $"Fortify  costs {OrderCost(ActionType.Fortify)}  Defense +1 (cap {BalanceConfig.DefenseMax})",
                $"Relief   costs {OrderCost(ActionType.Relief)}  Food -{BalanceConfig.ReliefFoodCost}, morale +{BalanceConfig.ReliefMoraleGain}",
            };
            foreach (var row in live)
                y += WrappedLabel(wrap, "Available  " + row, p.x + 36, y, 744, 22) + 2;

            y += 10;
            string[] planned =
            {
                "Farm fields  costs 1 order  Requires agriculture 3: army supply +30%",
                "Build structure  costs 1 order  Requires gold and an empty slot (market / farm / barracks...)",
                "Move capital  costs 1 order  Requires another city: prestige -5, change capital",
            };
            foreach (var row in planned)
            {
                string text = "[Planned] " + row;
                float height = Mathf.Max(22f, wrap.CalcHeight(new GUIContent(text), 744f));
                PlannedLabel(new Rect(p.x + 36, y, 744, height), text, wrap);
                y += height + 2;
            }

            y += 10;
            string footer =
                $"Select one of our cities, then issue orders from its selection panel; unavailable buttons are grey and state their reason.\n" +
                $"Select one of our armies to start a field battle against an adjacent enemy ({OrderCost(ActionType.AttackArmy)}), or assault an enemy city ({OrderCost(ActionType.AttackCity)}, requiring a {BalanceConfig.AssaultTroopRatio}:1 troop ratio).";
            return y + WrappedLabel(wrap, footer, p.x + 20, y, 760, 44);
        }

        void DrawHistorianPanel(Rect p, GUIStyle body, GUIStyle mono)
        {
            // the chronicle is the seat's filtered view of the log (CONTROLLER_PROTOCOL §5)
            var records = obs != null ? obs.Events : new List<EventView>();
            string seatName = obs != null ? obs.Faction(currentSeat)?.Name : null;
            if (string.IsNullOrEmpty(seatName)) seatName = currentSeat;
            string chronicleTitle =
                $"Campaign chronicle (visible to {seatName}) — {records.Count} entries" +
                (obs != null ? $" (season {obs.Season} · {PhaseLabel(obs.Phase)})" : "");
            GUI.Label(new Rect(p.x + 20, p.y + 52, TabPanelWidth - 40f, 22),
                EllipsizeToWidth(body, chronicleTitle, TabPanelWidth - 40f), body);

            // hug the chronicle: the scroll view is as tall as its content (capped), and the
            // scrollbar only appears when the cap actually bites
            float viewHeight = HistorianViewHeight();
            float contentHeight = records.Count * HistorianLineHeight + 8f;
            var view = new Rect(p.x + 20, p.y + 80, 760, viewHeight);
            TryWheelScroll(p, view, contentHeight, ref historianScroll);
            historianScroll = GUI.BeginScrollView(view, historianScroll,
                                                  new Rect(0, 0, 744, contentHeight), false, false);
            for (int i = 0; i < records.Count; i++)
            {
                string line = HistorianLine(records[i]);
                GUI.Label(new Rect(4, 4 + i * HistorianLineHeight, TabPanelWidth - 64f, HistorianLineHeight),
                          EllipsizeToWidth(mono, line, TabPanelWidth - 64f), mono);
            }
            GUI.EndScrollView();

            int season = obs != null ? obs.Season : engine.State.Season;
            GUI.Label(new Rect(p.x + 20, p.y + 80 + viewHeight + 12, TabPanelWidth - 40f, 22),
                $"Historian: {CountSeasonEvents(season)} events recorded this season.", body);
        }

        string HistorianLine(EventView rec)
        {
            var sb = new StringBuilder();
            sb.Append(rec.Seq.ToString().PadLeft(2));
            sb.Append(" Season ").Append(rec.Season).Append(" ");
            sb.Append(rec.EventType);
            sb.Append("  ").Append(SeatShort(rec.ActorSeatId));
            string payload = PayloadText(rec.Payload);
            if (payload.Length > 0) sb.Append("  ").Append(payload);
            return sb.ToString();
        }
    }
}
