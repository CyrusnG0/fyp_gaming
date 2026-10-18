// KingdomDemo (partial) — court report, handover and victory screens, and report text helpers.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ---- Battle report: read the last battle back out of the log (design §6.4) ------

        string LastBattleSummary()
        {
            var records = engine.Log.Records;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].EventType == "battle_field") return FieldSummary(records[i]);
                if (records[i].EventType == "battle_siege") return SiegeSummary(records[i]);
            }
            return null;
        }

        string FieldSummary(EventRecord rec)
                => $"Field battle, {rec.Payload["rounds"]} rounds: {SeatShort(rec.Payload["attacker_seat_id"])}" +
                    $" {rec.Payload["attacker_troops_before"]}→{rec.Payload["attacker_troops_after"]} vs. " +
               $"{SeatShort(rec.Payload["defender_seat_id"])}" +
                    $" {rec.Payload["defender_troops_before"]}→{rec.Payload["defender_troops_after"]} {OutcomeLabel(rec.Payload["outcome"])}";

        string SiegeSummary(EventRecord rec)
                => $"Siege of {CityName(rec.Payload["city_id"])} for {rec.Payload["rounds"]} rounds: " +
                    $"attacker {rec.Payload["troops_before"]}→{rec.Payload["troops_after"]}, garrison {rec.Payload["garrison_before"]}→{rec.Payload["garrison_after"]} {OutcomeLabel(rec.Payload["outcome"])}";

        string CityName(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            return city != null ? city.Name : cityId;
        }

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
                    var city = engine.State.FindCity(rec.Payload["city_id"]);
                    var fac = engine.State.FindFaction(city.OwnerSeatId);
                    reportLines.Add($"* {fac.Name} army occupied {city.Name}, expanding its territory!");
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

            File.WriteAllText(Path.Combine(Application.persistentDataPath, "luanshi_log.jsonl"),
                              engine.Log.ToJsonLines());
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

            if (e.type == EventType.KeyDown || e.type == EventType.MouseDown)
            {
                handoverShowing = false;
                statusMsg = SeatTurnPrompt();
                e.Use();
                if (pendingReport) { pendingReport = false; reportShowing = true; }
                return;
            }

            FillScreen(Color.black);

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

            FillScreen(Color.black);

            var winner = engine.State.FindFaction(engine.State.WinnerSeatId);
            GUI.Label(new Rect(0, 380, 1600, 60), $"{winner?.Name} unifies the land — game over", centered);
            GUI.Label(new Rect(0, 450, 1600, 40), "一統天下 · all cities are one banner", centeredBody);
        }

        void DrawReport(GUIStyle body, GUIStyle btn, GUIStyle title)
        {
            // the panel hugs its lines (capped at the old 420-tall size) instead of a fixed 800×420,
            // and scrolls when a season was busier than the cap
            int lines = System.Math.Max(1, reportLines.Count - 1);
            float textHeight = System.Math.Min(ReportTextMaxHeight, lines * ReportLineHeight);
            var p = new Rect(400, 150, 800, 116f + textHeight);
            uiRects.Add(p);
            DrawBackdrop(p);
            GUI.Label(new Rect(p.x + 20, p.y + 12, 760, 30), reportLines.Count > 0 ? reportLines[0] : "", title);

            var sb = new StringBuilder();
            for (int i = 1; i < reportLines.Count; i++) sb.AppendLine(reportLines[i]);

            var view = new Rect(p.x + 20, p.y + 50, 760, textHeight);
            // scroll only when the cap actually bites, and without a permanently visible scrollbar
            if (lines * ReportLineHeight > textHeight)
            {
                float contentHeight = lines * ReportLineHeight + 4f;
                reportScroll = GUI.BeginScrollView(view, reportScroll,
                                                   new Rect(0, 0, 744, contentHeight), false, false);
                GUI.Label(new Rect(0, 0, 736, contentHeight), sb.ToString(), body);
                GUI.EndScrollView();
            }
            else
            {
                GUI.Label(new Rect(view.x, view.y, 760, textHeight + 8f), sb.ToString(), body);
            }

            if (GUI.Button(new Rect(p.x + 330, p.y + 50 + textHeight + 14, 140, 40), "Continue", btn)) CloseReport();
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

        string PayloadText(Dictionary<string, string> payload)
        {
            if (payload == null || payload.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in payload)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
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
