using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// What one arm's dispatched requests show about the PRODUCTION spatial context: how many carried the production
    /// addendum and section, the prompt version and schema hash the runner recorded, the spatial truncation the
    /// production builder reported per request, and a privacy scan against the seat's own fog of war.
    /// Everything is read from the requests the fake provider actually received, never from a scripted expectation.
    /// </summary>
    public sealed class SpatialEvidence
    {
        /// <summary>off | standard | rich, from the dispatched LlmConfig.SpatialContext.</summary>
        [JsonPropertyName("level")] public string Level { get; set; }
        [JsonPropertyName("requests")] public int Requests { get; set; }
        [JsonPropertyName("system_with_spatial_addendum")] public int SystemWithAddendum { get; set; }
        [JsonPropertyName("user_with_spatial_section")] public int UserWithSection { get; set; }
        [JsonPropertyName("prompt_versions")] public List<string> PromptVersions { get; set; } = new List<string>();
        [JsonPropertyName("schema_hashes")] public List<string> SchemaHashes { get; set; } = new List<string>();
        [JsonPropertyName("truncation_reports")] public int TruncationReports { get; set; }
        [JsonPropertyName("truncated_requests")] public int TruncatedRequests { get; set; }
        [JsonPropertyName("truncation_reasons")] public List<string> TruncationReasons { get; set; } = new List<string>();
        /// <summary>Total items the production builder dropped, by kind, summed over requests.</summary>
        [JsonPropertyName("dropped")] public SortedDictionary<string, int> Dropped { get; set; } = new SortedDictionary<string, int>();
        [JsonPropertyName("max_total_chars")] public int MaxTotalChars { get; set; }
        /// <summary>Other-seat ids and city names hidden from the seat at the start and at the end of the match.</summary>
        [JsonPropertyName("hidden_ids_checked")] public int HiddenIdsChecked { get; set; }
        [JsonPropertyName("hidden_id_leaks")] public List<string> HiddenIdLeaks { get; set; } = new List<string>();
        /// <summary>Digest of the whole canonical world, hidden parts included (researcher-side only).</summary>
        [JsonPropertyName("world_start_digest")] public string WorldStartDigest { get; set; }
        [JsonPropertyName("world_final_digest")] public string WorldFinalDigest { get; set; }

        /// <summary>"seat#nth request of that seat" -> observation hash, from the run records.</summary>
        [JsonIgnore] public Dictionary<string, string> ObservationHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        [JsonIgnore] public HashSet<string> HiddenIds = new HashSet<string>(StringComparer.Ordinal);
    }

    public static class BatchSpatial
    {
        private static readonly string[] DropKeys =
            { "own_dropped", "visible_dropped", "stale_dropped", "objectives_dropped", "frontiers_dropped",
              "visible_rows_dropped", "move_options_dropped", "armies_dropped" };

        /// <summary>Other seats' cities and armies the given seat cannot see right now: their ids and city names.</summary>
        public static HashSet<string> HiddenFrom(GameEngine engine, string seat)
        {
            Observation obs = engine.Observe(seat);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityView c in obs.Cities) seen.Add(c.Id);
            foreach (ScoutedCityView c in obs.ScoutedCities) seen.Add(c.CityId);
            foreach (ArmyView a in obs.Armies) seen.Add(a.Id);
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityState c in engine.State.Cities)
                if (!string.IsNullOrEmpty(c.OwnerSeatId) && c.OwnerSeatId != seat && !seen.Contains(c.Id)) { hidden.Add(c.Id); hidden.Add(c.Name); }
            foreach (ArmyState a in engine.State.Armies)
                if (a.OwnerSeatId != seat && !seen.Contains(a.Id)) hidden.Add(a.Id);
            return hidden;
        }

        /// <summary>
        /// Builds the evidence for one finished arm. <paramref name="hiddenBySeat"/> holds, per seat, the ids hidden from it
        /// at both ends of the match; any of them in that seat's dispatched prompt is reported as a leak.
        /// </summary>
        public static SpatialEvidence Collect(string level, List<ExchangeCapture> captures, string exchangesPath, string runPath,
            Dictionary<string, HashSet<string>> hiddenBySeat, string startDigest, string finalDigest)
        {
            var e = new SpatialEvidence { Level = level, Requests = captures.Count, WorldStartDigest = startDigest, WorldFinalDigest = finalDigest };
            string addendum = PromptBuilder.SpatialContextAddendum();
            foreach (ExchangeCapture c in captures)
            {
                if ((c.System ?? "").Contains(addendum)) e.SystemWithAddendum++;
                if ((c.User ?? "").Contains(PromptBuilder.SpatialHeader)) e.UserWithSection++;
                if (hiddenBySeat != null && hiddenBySeat.TryGetValue(c.Seat, out HashSet<string> hidden))
                    foreach (string id in hidden)
                        if ((c.System ?? "").Contains(id) || (c.User ?? "").Contains(id))
                            e.HiddenIdLeaks.Add(c.Seat + " request " + c.SeatRequest + " shows hidden '" + id + "'");
            }
            if (hiddenBySeat != null) foreach (HashSet<string> h in hiddenBySeat.Values) e.HiddenIdsChecked += h.Count;
            foreach (JsonElement x in Lines(exchangesPath))
            {
                if (MatchAuditor.Str(x, "exchange_event") != "request") continue;
                AddUnique(e.PromptVersions, MatchAuditor.Str(x, "prompt_version"));
                AddUnique(e.SchemaHashes, MatchAuditor.Str(x, "schema_hash"));
                if (!x.TryGetProperty("spatial_truncation", out JsonElement t) || t.ValueKind != JsonValueKind.Object) continue;
                e.TruncationReports++;
                if (MatchAuditor.Bool(t, "truncated") == true) { e.TruncatedRequests++; AddUnique(e.TruncationReasons, MatchAuditor.Str(t, "reason")); }
                e.MaxTotalChars = Math.Max(e.MaxTotalChars, (int)(MatchAuditor.Num(t, "total_chars") ?? 0));
                foreach (string key in DropKeys)
                {
                    long dropped = MatchAuditor.Num(t, key) ?? 0;
                    e.Dropped.TryGetValue(key, out int sum);
                    e.Dropped[key] = sum + (int)dropped;
                }
            }
            var perSeat = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonElement r in Lines(runPath))
            {
                string seat = MatchAuditor.Str(r, "seat_id") ?? "?";
                perSeat.TryGetValue(seat, out int nth);
                perSeat[seat] = ++nth;
                e.ObservationHashes[seat + "#" + nth] = MatchAuditor.Str(r, "observation_hash");
            }
            return e;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (value != null && !list.Contains(value)) list.Add(value);
        }

        private static IEnumerable<JsonElement> Lines(string path)
        {
            if (path == null || !File.Exists(path)) yield break;
            foreach (string line in File.ReadAllLines(path))
                if (line.Trim().Length > 0) yield return JsonDocument.Parse(line).RootElement.Clone();
        }

        /// <summary>
        /// The matched-pair verdict for a spatial_context experiment, from the dispatched prompts. Every problem is a reason
        /// the pair cannot be read as "same game, only the production spatial context differs".
        /// </summary>
        public static List<string> CheckPair(MatchOutcome control, MatchOutcome treatment)
        {
            var p = new List<string>();
            SpatialEvidence c = control.Spatial, t = treatment.Spatial;
            if (c == null || t == null) { p.Add("an arm has no spatial evidence (it did not run)"); return p; }
            if (c.Level != "off") p.Add("control spatial level is '" + c.Level + "', expected off");
            if (t.Level == "off") p.Add("treatment spatial level is off");
            if (c.Requests == 0 || t.Requests == 0) { p.Add("an arm dispatched no request"); return p; }
            if (c.SystemWithAddendum != 0 || c.UserWithSection != 0)
                p.Add("control prompts carry spatial material (" + c.SystemWithAddendum + " system, " + c.UserWithSection + " user)");
            if (t.SystemWithAddendum != t.Requests || t.UserWithSection != t.Requests)
                p.Add("treatment prompts lack spatial material: " + t.SystemWithAddendum + " of " + t.Requests + " system prompts, "
                      + t.UserWithSection + " of " + t.Requests + " user prompts carry it");
            string addendum = PromptBuilder.SpatialContextAddendum();
            var left = new Dictionary<string, ExchangeCapture>(StringComparer.Ordinal);
            foreach (ExchangeCapture x in control.Captures) left[x.Seat + "#" + x.SeatRequest] = x;
            int aligned = 0, addendumOnly = 0, sameObservation = 0, observationDiffers = 0;
            foreach (ExchangeCapture x in treatment.Captures)
            {
                string key = x.Seat + "#" + x.SeatRequest;
                if (!left.TryGetValue(key, out ExchangeCapture other)) continue;
                aligned++;
                if (x.System == other.System + addendum) addendumOnly++;
                c.ObservationHashes.TryGetValue(key, out string a);
                t.ObservationHashes.TryGetValue(key, out string b);
                if (a != null && a == b) sameObservation++; else observationDiffers++;
            }
            if (aligned != control.Captures.Count || aligned != treatment.Captures.Count)
                p.Add("the arms' requests do not align one to one (" + aligned + " aligned, " + control.Captures.Count + " control, " + treatment.Captures.Count + " treatment)");
            if (addendumOnly != aligned) p.Add("treatment system prompt is not 'control system prompt + production spatial addendum' in " + (aligned - addendumOnly) + " of " + aligned + " aligned requests");
            if (observationDiffers > 0) p.Add("the seat observation differs between the arms in " + observationDiffers + " of " + aligned + " aligned requests");
            if (c.PromptVersions.Count == 0 || t.PromptVersions.Count == 0 || c.PromptVersions.Exists(t.PromptVersions.Contains))
                p.Add("prompt versions do not differ between the arms (control " + string.Join(",", c.PromptVersions) + "; treatment " + string.Join(",", t.PromptVersions) + ")");
            else if (!t.PromptVersions.TrueForAll(v => v.Contains("spatial")) || c.PromptVersions.Exists(v => v.Contains("spatial")))
                p.Add("only the treatment's prompt versions should name spatial (control " + string.Join(",", c.PromptVersions) + "; treatment " + string.Join(",", t.PromptVersions) + ")");
            if (c.SchemaHashes.Count == 0 || t.SchemaHashes.Count == 0 || c.SchemaHashes.Exists(t.SchemaHashes.Contains))
                p.Add("schema hashes do not differ between the arms");
            if (c.TruncationReports != 0) p.Add("control recorded " + c.TruncationReports + " spatial truncation report(s) with spatial context off");
            if (t.TruncationReports != t.Requests) p.Add("treatment recorded " + t.TruncationReports + " spatial truncation report(s) for " + t.Requests + " requests");
            if (c.WorldStartDigest == null || c.WorldStartDigest != t.WorldStartDigest) p.Add("the arms did not start from the same canonical world");
            foreach (string leak in c.HiddenIdLeaks) p.Add("control leak: " + leak);
            foreach (string leak in t.HiddenIdLeaks) p.Add("treatment leak: " + leak);
            return p;
        }

        public static string Summary(SpatialEvidence s)
            => "level " + s.Level + "; " + s.SystemWithAddendum + "/" + s.Requests + " system prompts carry the production addendum, "
               + s.UserWithSection + "/" + s.Requests + " user prompts the spatial section; prompt version " + string.Join(",", s.PromptVersions)
               + "; schema " + string.Join(",", s.SchemaHashes) + "; truncation reports " + s.TruncationReports + ", truncated " + s.TruncatedRequests
               + (s.TruncationReasons.Count > 0 ? " (" + string.Join(",", s.TruncationReasons) + ")" : "") + "; hidden ids checked " + s.HiddenIdsChecked
               + ", leaks " + s.HiddenIdLeaks.Count;
    }
}
