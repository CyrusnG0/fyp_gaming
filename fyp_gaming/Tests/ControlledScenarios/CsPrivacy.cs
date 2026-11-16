using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public sealed class CsPrivacyReport
    {
        public int Checked { get; set; }
        public int Leaks { get; set; }
        public List<string> Details { get; } = new List<string>();
        public bool? ObservationEmbedded { get; set; }
    }

    /// <summary>
    /// Fog/seat witness. For one dispatched prompt it answers: was this the acting seat's own
    /// observation, and did the prompt name any canonical entity that the acting seat could not
    /// see? Scope: ids only. It proves the prompt did not carry a hidden id; it cannot prove a
    /// paraphrase or a derived fact was absent.
    /// </summary>
    public static class CsPrivacy
    {
        public static CsPrivacyReport Scan(string system, string user, string actingSeat,
            Observation observation, GameState state)
        {
            var report = new CsPrivacyReport();
            string prompt = (system ?? "") + "\n" + (user ?? "");
            report.Checked = 1;
            if (observation == null)
            {
                report.Leaks++;
                report.Details.Add("observation unavailable");
                return report;
            }
            if (!string.Equals(observation.SeatId, actingSeat, StringComparison.Ordinal))
            {
                report.Leaks++;
                report.Details.Add("seat mismatch: acting=" + actingSeat + " observation=" + observation.SeatId);
                return report;
            }

            string bounded = PromptBuilder.BoundedObservationJson(observation);
            report.ObservationEmbedded = (user ?? "").Contains(bounded);
            if (report.ObservationEmbedded != true)
                report.Details.Add("act_observation_bytes_not_embedded");

            // An id may legitimately appear in the seat's own observation (including a public
            // event payload naming a now-hidden army). The witness is therefore: the prompt may
            // not name any entity id absent from THIS seat's observation. That is the fog boundary
            // the engine itself draws; the runner must not add another path.
            string observationText = observation.ToJson();
            foreach (CityState city in state.Cities)
                if (!observationText.Contains(city.Id) && prompt.Contains(city.Id))
                    Leak(report, "hidden city id " + city.Id);
            foreach (ArmyState army in state.Armies)
                if (!observationText.Contains(army.Id) && prompt.Contains(army.Id))
                    Leak(report, "hidden army id " + army.Id);
            foreach (WatchTowerState tower in state.WatchTowers)
                if (!observationText.Contains(tower.Id) && prompt.Contains(tower.Id))
                    Leak(report, "hidden tower id " + tower.Id);
            return report;
        }

        /// <summary>
        /// Prompt-time variant used by the runner: the observation JSON captured at dispatch and
        /// the scenario's own entity ids are enough to show that a hidden id did not reach the
        /// prompt, without re-reading a later canonical state.
        /// </summary>
        public static CsPrivacyReport ScanSetup(string system, string user, string actingSeat,
            string observationJson, IEnumerable<string> setupIds)
        {
            var report = new CsPrivacyReport { Checked = 1 };
            if (string.IsNullOrEmpty(observationJson))
            {
                report.Leaks++;
                report.Details.Add("observation unavailable");
                return report;
            }
            string prompt = (system ?? "") + "\n" + (user ?? "");
            string seatMarker = "\"seat_id\":" + Quote(actingSeat);
            if (observationJson.IndexOf(seatMarker, StringComparison.Ordinal) < 0)
            {
                report.Leaks++;
                report.Details.Add("seat mismatch: observation is not " + actingSeat);
            }
            report.ObservationEmbedded = (user ?? "").Contains(observationJson);
            if (report.ObservationEmbedded != true)
                report.Details.Add("act_observation_bytes_not_embedded");

            // Same witness as Scan: the prompt may not name an id absent from this observation.
            // Public event payloads in the observation are part of the engine's fog decision.
            foreach (string id in setupIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!observationJson.Contains(id) && prompt.Contains(id))
                    Leak(report, "hidden entity id " + id);
            }
            return report;
        }

        private static string Quote(string value)
            => value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static void Leak(CsPrivacyReport report, string detail)
        {
            report.Leaks++;
            report.Details.Add(detail);
        }
    }
}
