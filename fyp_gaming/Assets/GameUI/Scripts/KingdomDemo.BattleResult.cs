using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        private EventView LatestBattleEvent()
        {
            if (obs == null) return null;
            for (int i = obs.Events.Count - 1; i >= 0; i--)
            {
                EventView record = obs.Events[i];
                if (record.EventType == "battle_field" || record.EventType == "battle_siege")
                    return record;
            }
            return null;
        }

        private void OpenBattleResultModal(EventView record)
        {
            if (record == null) return;
            battleResultEvent = record;
            battleResultShowing = true;
            selCity = null;
            selArmy = null;
            SafeClearArmyMoveTargets();
        }

        private void CloseBattleResultModal()
        {
            battleResultShowing = false;
            battleResultEvent = null;
            statusMsg = "Battle report closed";
        }

        private void DrawBattleResultModal(GUIStyle body, GUIStyle btn, GUIStyle title)
        {
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Escape))
            {
                CloseBattleResultModal();
                e.Use();
                return;
            }

            FillScreen(InkScreen);
            Rect panel = new Rect(340f, 130f, 920f, 640f);
            DrawBackdrop(panel, MutedRed);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 22f, panel.width - 56f, 34f),
                "Battle result", title);

            if (battleResultEvent == null)
            {
                GUI.Label(new Rect(panel.x + 28f, panel.y + 90f, panel.width - 56f, 30f),
                    "Battle details are unavailable.", body);
            }
            else
            {
                bool field = battleResultEvent.EventType == "battle_field";
                string location = field
                    ? "Field battle"
                    : "Siege of " + CityName(Value(battleResultEvent, "city_id", "?"));
                string attacker = SeatShort(Value(battleResultEvent, "attacker_seat_id", "?"));
                string defender = field
                    ? SeatShort(Value(battleResultEvent, "defender_seat_id", "?"))
                    : "Garrison";
                string rounds = Value(battleResultEvent, "rounds", "?");
                string outcome = OutcomeLabel(Value(battleResultEvent, "outcome", "Unknown"));
                string verdict = BattleVerdict(battleResultEvent);

                GUI.Label(new Rect(panel.x + 28f, panel.y + 86f, panel.width - 56f, 28f),
                    location + "  ·  " + rounds + " rounds  ·  " + outcome, body);
                GUI.Label(new Rect(panel.x + 28f, panel.y + 116f, panel.width - 56f, 34f),
                    verdict, BattleVerdictStyle(verdict, title));

                DrawCombatSide(panel, panel.y + 180f, attacker,
                    UnitTypeIn(battleResultEvent, field ? "attacker_unit_type" : "attacker_unit_type"),
                    Value(battleResultEvent, field ? "attacker_troops_before" : "troops_before", "?"),
                    Value(battleResultEvent, field ? "attacker_troops_after" : "troops_after", "?"),
                    body, title);
                DrawCombatSide(panel, panel.y + 340f, defender,
                    UnitTypeIn(battleResultEvent, field ? "defender_unit_type" : "garrison_unit_type"),
                    Value(battleResultEvent, field ? "defender_troops_before" : "garrison_before", "?"),
                    Value(battleResultEvent, field ? "defender_troops_after" : "garrison_after", "?"),
                    body, title);
            }

            GUI.Label(new Rect(panel.x + 28f, panel.yMax - 92f, panel.width - 56f, 24f),
                "Casualties are calculated from each side's before/after troop counts.", uiSmall);
            if (GUI.Button(new Rect(panel.x + panel.width - 188f, panel.yMax - 62f, 160f, 38f),
                           "Continue", btn))
                CloseBattleResultModal();
        }

        private void DrawCombatSide(Rect panel, float y, string side, string unitType,
            string before, string after, GUIStyle body, GUIStyle title)
        {
            GUI.Label(new Rect(panel.x + 28f, y, 250f, 28f), side, title);
            GUI.Label(new Rect(panel.x + 290f, y, 250f, 28f), "Unit: " + unitType, body);

            string casualty = CasualtyText(before, after);
            GUI.Label(new Rect(panel.x + 28f, y + 42f, panel.width - 56f, 28f),
                "Troops: " + before + " -> " + after + "    Casualties: " + casualty, body);
        }

        private static string Value(EventView record, string key, string fallback)
        {
            string value;
            return record != null && record.Payload.TryGetValue(key, out value)
                && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        private string BattleVerdict(EventView record)
        {
            string outcome = Value(record, "outcome", "");
            if (outcome == "mutual_rout" || outcome == "mutual_destruction")
                return "DRAW - Both sides were defeated";

            bool attackerWon = outcome == "captured"
                || outcome == "defender_destroyed"
                || outcome == "defender_routed";
            bool attackerLost = outcome == "attacker_destroyed"
                || outcome == "attacker_routed"
                || outcome == "assault_failed"
                || outcome == "city_held";

            string winnerSeat = Value(record, "winner_seat_id", "");
            if (!string.IsNullOrEmpty(winnerSeat))
            {
                if (winnerSeat == currentSeat) return "VICTORY";
                if (record.Payload.ContainsKey("attacker_seat_id")
                    && winnerSeat != currentSeat) return "DEFEAT";
            }

            if (record.Payload.ContainsKey("attacker_seat_id"))
            {
                if (record.Payload["attacker_seat_id"] == currentSeat)
                    return attackerWon ? "VICTORY" : attackerLost ? "DEFEAT" : "BATTLE COMPLETE";
                if (record.Payload.ContainsKey("defender_seat_id")
                    && record.Payload["defender_seat_id"] == currentSeat)
                    return attackerLost ? "VICTORY" : attackerWon ? "DEFEAT" : "BATTLE COMPLETE";
            }

            return attackerWon ? "ATTACKER VICTORY" : attackerLost ? "ATTACKER DEFEAT" : "BATTLE COMPLETE";
        }

        private static GUIStyle BattleVerdictStyle(string verdict, GUIStyle source)
        {
            var style = new GUIStyle(source) { fontSize = 24 };
            style.normal.textColor = verdict.Contains("VICTORY") ? new Color(0.12f, 0.48f, 0.16f)
                : verdict.Contains("DEFEAT") ? MutedRed : MutedInk;
            return style;
        }

        private static string CasualtyText(string before, string after)
        {
            int beforeCount;
            int afterCount;
            if (!int.TryParse(before, out beforeCount) || !int.TryParse(after, out afterCount))
                return "Unknown";
            return Math.Max(0, beforeCount - afterCount).ToString();
        }
    }
}
