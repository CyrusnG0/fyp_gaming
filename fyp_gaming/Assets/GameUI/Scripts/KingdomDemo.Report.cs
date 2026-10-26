// KingdomDemo (partial) — court report, handover and victory screens, and report text helpers.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ---- Battle report: read the last battle back out of the seat's filtered history (design §6.4) --

        /// <summary>
        /// The most recent battle this seat was told about, read from its filtered event projection —
        /// never from the canonical log (LS-003). The seat that fought keeps the full report; a seat that
        /// only witnessed the news sees the same public summary the court report shows.
        /// </summary>
        string LastBattleSummary()
        {
            if (obs == null) return null;
            var records = obs.Events;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].EventType == "battle_field") return FieldSummary(records[i]);
                if (records[i].EventType == "battle_siege") return SiegeSummary(records[i]);
            }
            return null;
        }

        string FieldSummary(EventView rec)
                => $"Field battle, {rec.Payload["rounds"]} rounds: {SeatShort(rec.Payload["attacker_seat_id"])}" +
                    $" ({UnitTypeIn(rec, "attacker_unit_type")}) {rec.Payload["attacker_troops_before"]}→{rec.Payload["attacker_troops_after"]} vs. " +
               $"{SeatShort(rec.Payload["defender_seat_id"])}" +
                    $" ({UnitTypeIn(rec, "defender_unit_type")}) {rec.Payload["defender_troops_before"]}→{rec.Payload["defender_troops_after"]} {OutcomeLabel(rec.Payload["outcome"])}";

        string SiegeSummary(EventView rec)
                => $"Siege of {CityName(rec.Payload["city_id"])} for {rec.Payload["rounds"]} rounds: " +
                    $"attacker ({UnitTypeIn(rec, "attacker_unit_type")}) {rec.Payload["troops_before"]}→{rec.Payload["troops_after"]}, " +
                    $"garrison ({UnitTypeIn(rec, "garrison_unit_type")}) {rec.Payload["garrison_before"]}→{rec.Payload["garrison_after"]} {OutcomeLabel(rec.Payload["outcome"])}";

        static string UnitTypeIn(EventView rec, string key)
            => rec.Payload.TryGetValue(key, out string type) ? UnitTypeName(type) : "?";

        string CityName(string cityId) => ReportText.CityLabel(obs, cityId);

        static string OutcomeLabel(string outcome)
        {
            switch (outcome)
            {
                case "defender_destroyed": return "Defender destroyed";
                case "attacker_destroyed": return "Attacker destroyed";
                case "mutual_destruction": return "Mutual destruction";
                case "defender_routed":    return "Defender routed";
                case "attacker_routed":    return "Attacker routed";
                case "mutual_rout":        return "Both sides routed";
                case "defender_holds":     return "Defender holds";
                case "captured":           return "City captured";
                case "city_held":          return "City held";
                case "assault_failed":     return "Assault failed";
                default:                   return outcome;
            }
        }

        /// <summary>
        /// The court report is the reader's own ministry report: public news plus only the city lines this seat
        /// can see (the engine keeps the canonical report; the observation does the filtering).
        /// </summary>
        void BuildReport(SeasonReport report)
        {
            reportLines.Clear();
            reportLines.Add($"-- Season {report.Season} ({report.SeasonName}) court report ({engine.State.FindFaction(currentSeat)?.Name}) --");
            reportLines.Add("");

            foreach (var rec in obs.Events)
            {
                if (rec.Season != report.Season) continue;
                if (rec.EventType == "city_captured")
                {
                    reportLines.Add(ReportText.CaptureLine(obs, rec));
                }
                else if (rec.EventType == "battle_field")
                    reportLines.Add("Battle report: " + FieldSummary(rec));
                else if (rec.EventType == "battle_siege")
                    reportLines.Add("Battle report: " + SiegeSummary(rec));
                else if (rec.EventType == "war_declared")
                    reportLines.Add($"Diplomacy: {SeatShort(rec.Payload["attacker_seat_id"])} declared war on " +
                                    $"{SeatShort(rec.Payload["defender_seat_id"])}");
                else if (rec.EventType == "treaty_broken")
                    reportLines.Add($"Diplomacy: {SeatShort(rec.Payload["breaker_seat_id"])} broke the " +
                                    $"{TreatyLabel(rec.Payload["treaty_type"])} treaty with {SeatShort(rec.Payload["counterpart_seat_id"])}");
                else if (rec.EventType == "message_sent")
                    reportLines.Add($"Diplomacy: message from {SeatShort(rec.ActorSeatId)} — {rec.Payload["text"]}");
            }

            foreach (var kv in report.CityLines)
                if (obs.FindCity(kv.Key) != null) reportLines.Add(kv.Value);

            reportLines.Add("");
            reportLines.Add($"Historian: {CountSeasonEvents(report.Season)} events recorded this season.");

            // Player-facing export only (LS-014): the seat's own filtered history, never the canonical log.
            try
            {
                HistoryExport.Write(Application.persistentDataPath, obs);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"History export failed: {e.Message}");
            }
        }

        int CountSeasonEvents(int season)
        {
            if (obs == null) return 0;
            int n = 0;
            foreach (var rec in obs.Events) if (rec.Season == season) n++;
            return n;
        }

        void CloseReport()
        {
            reportShowing = false;
            reportScroll = Vector2.zero;
            statusMsg = SeatTurnPrompt();
        }

        /// <summary>
        /// Full-screen opaque blocker between seats (hotseat rule): whoever holds the keyboard sees a black
        /// screen and a prompt, and presses a key or clicks to take the chair. The fill is drawn in *screen*
        /// space, not the 1600×900 design space — a scaled rect leaves the last pixels of a non-16:9 window
        /// showing the previous player's map. OnGUI renders above every camera, so an opaque full-screen
        /// fill is enough to hide the scene as well as the panels, and the early return in OnGUI means
        /// nothing else is drawn at all while this is up.
        /// </summary>
        void DrawHandoverOverlay(GUIStyle title, GUIStyle body)
        {
            var e = Event.current;
            var centered = new GUIStyle(title) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            var centeredBody = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
            centered.normal.textColor = ParchmentPanel;
            centeredBody.normal.textColor = ParchmentPanel;

            if (e.type == EventType.KeyDown || e.type == EventType.MouseDown)
            {
                handoverShowing = false;
                statusMsg = SeatTurnPrompt();
                e.Use();
                if (pendingReport) { pendingReport = false; reportShowing = true; }
                return;
            }

            FillScreen(InkScreen);

            var fac = engine.State.FindFaction(currentSeat);
            GUI.Label(new Rect(0, 330, 1600, 50),
                $"Season {engine.State.Season} · {fac?.Name} ({currentSeat})", centered);
            GUI.Label(new Rect(0, 400, 1600, 40),
                $"Pass to seat {fac?.Name} ({currentSeat}) — click to take the throne", centeredBody);
            GUI.Label(new Rect(0, 460, 1600, 40), "Nothing of the previous player's screen is shown.", centeredBody);
        }

        /// <summary>
        /// Fills the whole game view in *screen* pixels, whatever the window aspect is. Callers run inside
        /// OnGUI's design-space matrix, so the matrix is reset for the fill and restored afterwards.
        /// </summary>
        void FillScreen(Color color)
        {
            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// 一統天下 (design §13): the whole screen becomes the result. Order buttons, panels and map clicks
        /// are unreachable — the engine refuses further commands anyway (Submit → "game over").
        /// </summary>
        void DrawVictoryBanner(GUIStyle title, GUIStyle body)
        {
            var centered = new GUIStyle(title) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            var centeredBody = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
            centered.normal.textColor = ParchmentPanel;
            centeredBody.normal.textColor = ParchmentPanel;

            FillScreen(InkScreen);

            var winner = engine.State.FindFaction(engine.State.WinnerSeatId);
            GUI.Label(new Rect(0, 380, 1600, 60), $"{winner?.Name} unifies the land — game over", centered);
            GUI.Label(new Rect(0, 450, 1600, 40), "Unification · all cities under one banner", centeredBody);
        }

        void DrawReport(GUIStyle body, GUIStyle btn, GUIStyle title)
        {
            // The panel hugs its wrapped text (capped) instead of a fixed 800x420 box, and scrolls when
            // a busy season exceeds the cap. Every line wraps inside ReportBodyWidth.
            var sb = new StringBuilder();
            for (int i = 1; i < reportLines.Count; i++) sb.AppendLine(reportLines[i]);
            string reportBody = sb.ToString();
            float contentHeight = uiBodyWrap.CalcHeight(new GUIContent(reportBody), ReportBodyWidth) + 4f;
            float textHeight = Mathf.Min(ReportTextMaxHeight, Mathf.Max(20f, contentHeight));

            var p = new Rect(ReportPanelX, ReportPanelY, ReportPanelWidth,
                             ReportPanelBaseHeight + textHeight);
            uiRects.Add(p);
            DrawBackdrop(p, CurrentSeatAccent());
            string reportTitle = reportLines.Count > 0 ? reportLines[0] : "";
            GUI.Label(new Rect(p.x + 20, p.y + 12, ReportTitleWidth, 30),
                      EllipsizeToWidth(title, reportTitle, ReportTitleWidth), title);

            var view = new Rect(p.x + 20, p.y + 50, ReportTitleWidth, textHeight);
            // scroll only when the cap actually bites, and without a permanently visible scrollbar
            if (contentHeight > textHeight + 0.5f)
            {
                TryWheelScroll(p, view, contentHeight, ref reportScroll);
                reportScroll = GUI.BeginScrollView(view, reportScroll,
                                                   new Rect(0, 0, ReportBodyWidth, contentHeight), false, false);
                GUI.Label(new Rect(0, 0, ReportBodyWidth, contentHeight), reportBody, uiBodyWrap);
                GUI.EndScrollView();
            }
            else
            {
                GUI.Label(new Rect(view.x, view.y, ReportTitleWidth, Mathf.Max(textHeight, contentHeight) + 8f),
                          reportBody, uiBodyWrap);
            }

            float continueX = p.x + (ReportPanelWidth - ReportContinueWidth) * 0.5f;
            if (GUI.Button(new Rect(continueX, p.y + 50 + textHeight + 14,
                                    ReportContinueWidth, ReportContinueHeight), "Continue", btn))
                CloseReport();
        }

        static string TreatyLabel(string type)
        {
            switch (type)
            {
                case TreatyType.Nap: return "Non-aggression pact";
                case TreatyType.Alliance: return "Alliance";
                case TreatyType.Truce: return "Truce";
                default: return type;
            }
        }

        string SeatShort(string seatId)
        {
            if (seatId == null) return "—";
            var f = engine.State.FindFaction(seatId);
            return f != null ? f.Name : seatId;
        }

        string PayloadText(IReadOnlyDictionary<string, string> payload)
        {
            if (payload == null || payload.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in payload)
            {
                if (sb.Length > 0) sb.Append(' ');
                string value = (kv.Key == "unit_type" || kv.Key == "garrison_type")
                    ? UnitTypeName(kv.Value) : kv.Value;
                sb.Append(kv.Key).Append('=').Append(value);
            }
            return sb.ToString();
        }

        string PhaseLabel(string phase)
        {
            if (phase == "player_orders") return "Orders";
            if (phase == "resolution") return "Resolution";
            return phase;
        }
    }
}
