using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// What the engine's seat-filtered observation showed one seat about the OTHER seats' cities and armies at the moment
    /// one request was dispatched. Keyed by the strings that would appear in a prompt (city id, city name, army id);
    /// the value is "visible", "scouted" (a retained scouting report) or "hidden".
    /// </summary>
    public sealed class DispatchWitness
    {
        public const string Visible = "visible", Scouted = "scouted", Hidden = "hidden";
        public readonly Dictionary<string, string> Tokens = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Result of scanning every dispatched request of one arm against its own witness. See <see cref="BatchPrivacy.Scope"/>.</summary>
    public sealed class PrivacyEvidence
    {
        [JsonPropertyName("scope")] public string ScopeNote { get; set; } = BatchPrivacy.Scope;
        [JsonPropertyName("requests")] public int Requests { get; set; }
        [JsonPropertyName("requests_without_witness")] public int RequestsWithoutWitness { get; set; }
        /// <summary>Sum over requests of how many tokens were hidden from that seat at that dispatch (each one was searched for in the prompt).</summary>
        [JsonPropertyName("hidden_token_checks")] public int HiddenTokenChecks { get; set; }
        /// <summary>A token hidden at dispatch that no earlier (or this) prompt to the seat actually contained while it was visible or scouted, yet appears in this request's prompt.</summary>
        [JsonPropertyName("leaks")] public List<string> Leaks { get; set; } = new List<string>();
        /// <summary>A token hidden now whose text was delivered to the same seat in an earlier prompt while it was visible or scouted: retained knowledge, counted not judged.</summary>
        [JsonPropertyName("retained_known_mentions")] public int RetainedKnownMentions { get; set; }
    }

    public static class BatchPrivacy
    {
        public const string Scope =
            "Per request: every other-seat city id, city name and army id that the engine's seat-filtered Observe(seat) at that "
            + "dispatch showed neither as visible nor as a scouting report is searched for as plain text in that request's system "
            + "and user prompt. A token is 'delivered' to a seat only when a prompt sent to that seat contained its text while the "
            + "observation had it visible or scouted; being visible in an observation alone is not delivery. Proves absence of "
            + "those exact strings only. Does not prove the observation filter itself is right (Observe is the oracle), nor covers numbers, "
            + "treaty or event text, paraphrases, or neutral cities. A hidden token whose text was delivered earlier is retained "
            + "knowledge, not a leak; delivery is judged on that seat's prompt text only, so it cannot show that the model used or remembered it; whether its retained wording is accurate or stale is not judged here.";

        /// <summary>The seat's view of every other seat's entities right now, from the same Observe(seat) the production host calls.</summary>
        public static DispatchWitness Witness(GameEngine engine, string seat)
        {
            Observation obs = engine.Observe(seat);
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CityView c in obs.Cities) seen[c.Id] = DispatchWitness.Visible;
            foreach (ScoutedCityView c in obs.ScoutedCities) if (!seen.ContainsKey(c.CityId)) seen[c.CityId] = DispatchWitness.Scouted;
            foreach (ArmyView a in obs.Armies) seen[a.Id] = DispatchWitness.Visible;
            var witness = new DispatchWitness();
            foreach (CityState c in engine.State.Cities)
            {
                if (string.IsNullOrEmpty(c.OwnerSeatId) || c.OwnerSeatId == seat) continue;
                string status = seen.TryGetValue(c.Id, out string s) ? s : DispatchWitness.Hidden;
                witness.Tokens[c.Id] = status;
                if (!string.IsNullOrEmpty(c.Name)) witness.Tokens[c.Name] = status;
            }
            foreach (ArmyState a in engine.State.Armies)
                if (a.OwnerSeatId != seat) witness.Tokens[a.Id] = seen.TryGetValue(a.Id, out string s) ? s : DispatchWitness.Hidden;
            return witness;
        }

        /// <summary>Scans captures in dispatch order. A token counts as delivered to a seat once a prompt to that seat contained its text while it was visible or scouted.</summary>
        public static PrivacyEvidence Scan(IEnumerable<ExchangeCapture> captures)
        {
            var e = new PrivacyEvidence();
            var known = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (ExchangeCapture c in captures)
            {
                e.Requests++;
                if (c.Witness == null) { e.RequestsWithoutWitness++; continue; }
                if (!known.TryGetValue(c.Seat, out HashSet<string> seatKnown)) known[c.Seat] = seatKnown = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> t in c.Witness.Tokens)
                    if (t.Value != DispatchWitness.Hidden && Contains(c, t.Key)) seatKnown.Add(t.Key);   // observation alone is not delivery
                foreach (KeyValuePair<string, string> t in c.Witness.Tokens)
                {
                    if (t.Value != DispatchWitness.Hidden) continue;
                    e.HiddenTokenChecks++;
                    if (!Contains(c, t.Key)) continue;
                    if (seatKnown.Contains(t.Key)) e.RetainedKnownMentions++;
                    else e.Leaks.Add(c.Seat + " request " + c.SeatRequest + " shows '" + t.Key + "', hidden at dispatch and not delivered to this seat in any earlier prompt");
                }
            }
            return e;
        }

        private static bool Contains(ExchangeCapture c, string token) => (c.System ?? "").Contains(token) || (c.User ?? "").Contains(token);
    }
}
