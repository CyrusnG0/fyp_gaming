using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuanShi.ExperimentBatch
{
    public sealed class TurnAudit
    {
        [JsonPropertyName("season")] public int Season { get; set; }
        [JsonPropertyName("seat")] public string Seat { get; set; }
        [JsonPropertyName("requests_in_records")] public int RequestsInRecords { get; set; }
        [JsonPropertyName("requests_in_summary")] public int RequestsInSummary { get; set; }
        [JsonPropertyName("orders_in_records")] public List<string> OrdersInRecords { get; set; } = new List<string>();
        [JsonPropertyName("accepted_in_records")] public int AcceptedInRecords { get; set; }
        [JsonPropertyName("accepted_in_summary")] public int AcceptedInSummary { get; set; }
        [JsonPropertyName("summary_last_command")] public string SummaryLastCommand { get; set; }
        /// <summary>Orders the summary's single last-command field cannot show for this turn.</summary>
        [JsonPropertyName("orders_not_visible_in_summary_commands")] public int OrdersNotVisibleInSummaryCommands { get; set; }
    }

    /// <summary>Totals recomputed from the per-request records and compared with the summary and the runner.</summary>
    public sealed class MatchAudit
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("mismatches")] public List<string> Mismatches { get; set; } = new List<string>();
        [JsonPropertyName("counts")] public SortedDictionary<string, long?> Counts { get; set; } = new SortedDictionary<string, long?>();
        [JsonPropertyName("turns")] public List<TurnAudit> Turns { get; set; } = new List<TurnAudit>();
        [JsonPropertyName("truncation")] public List<string> Truncation { get; set; } = new List<string>();
        [JsonPropertyName("unknown")] public List<string> Unknown { get; set; } = new List<string>();
    }

    /// <summary>
    /// Does not trust the summary: every figure is rebuilt from the per-request run records, and the
    /// recorder's exchange sidecar and the batch's own prompt capture are compared with each other.
    /// </summary>
    public static class MatchAuditor
    {
        public static MatchAudit Audit(string dir, MatchOutcome outcome, BatchManifest m)
        {
            var a = new MatchAudit();
            // A match the runner refused before any request has no run or exchange file: that is zero records, not a crash.
            List<JsonElement> records = outcome.Files.TryGetValue("run_jsonl", out FileRef runFile) ? ReadLines(Path.Combine(dir, runFile.Path)) : new List<JsonElement>();
            List<JsonElement> exchanges = outcome.Files.TryGetValue("exchanges", out FileRef exchangeFile) ? ReadLines(Path.Combine(dir, exchangeFile.Path)) : new List<JsonElement>();
            JsonElement summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, outcome.Files["summary"].Path))).RootElement;
            JsonElement totals = summary.GetProperty("totals");
            // The summary states its headline figures twice (top level and totals); they must agree with each other.
            foreach (string key in new[] { "requests", "input_tokens", "output_tokens" })
                if (Num(summary, key) != Num(totals, key)) a.Mismatches.Add("summary is inconsistent with itself: top-level " + key + " " + Show(Num(summary, key)) + " vs totals " + Show(Num(totals, key)));

            int exchangeRequests = 0, exchangeResults = 0;
            var exchangeRequestHashes = new List<string[]>();
            foreach (JsonElement x in exchanges)
            {
                string kind = Str(x, "exchange_event");
                if (kind == "request")
                {
                    exchangeRequests++;
                    JsonElement req = x.GetProperty("request");
                    exchangeRequestHashes.Add(new[] { BatchPlan.Sha256(Str(req, "system")), BatchPlan.Sha256(Str(req, "user")) });
                }
                else if (kind == "result") exchangeResults++;
            }

            CompareRequests(a, outcome, records, exchangeRequests, exchangeResults, exchangeRequestHashes, totals);
            CompareTokens(a, outcome, records, totals);
            CompareOrdersAndTurns(a, records, summary, totals);
            Report(a, records);
            a.Ok = a.Mismatches.Count == 0;
            return a;
        }

        private static void CompareRequests(MatchAudit a, MatchOutcome o, List<JsonElement> records, int exReq, int exRes,
            List<string[]> exchangeHashes, JsonElement totals)
        {
            a.Counts["run_records"] = records.Count;
            a.Counts["exchange_requests"] = exReq;
            a.Counts["exchange_results"] = exRes;
            a.Counts["prompts_captured"] = o.Captures.Count;
            a.Counts["runner_total_requests"] = o.Requests;
            a.Counts["summary_requests"] = totals.GetProperty("requests").GetInt32();
            foreach (KeyValuePair<string, long?> c in a.Counts)
                if (c.Value != records.Count) a.Mismatches.Add("request count: " + c.Key + "=" + c.Value + " but " + records.Count + " per-request records");
            for (int i = 0; i < Math.Min(exchangeHashes.Count, o.Captures.Count); i++)
            {
                ExchangeCapture cap = o.Captures[i];
                if (exchangeHashes[i][0] != BatchPlan.Sha256(cap.System) || exchangeHashes[i][1] != BatchPlan.Sha256(cap.User))
                    a.Mismatches.Add("prompt " + (i + 1) + ": the recorder's exchange differs from the batch's own capture");
            }
        }

        private static void CompareTokens(MatchAudit a, MatchOutcome o, List<JsonElement> records, JsonElement totals)
        {
            long input = 0, output = 0;
            int unknown = 0;
            foreach (JsonElement r in records)
            {
                long? i = Num(r, "input_tokens"), p = Num(r, "output_tokens");
                if (!i.HasValue || !p.HasValue) { unknown++; continue; }
                input += i.Value; output += p.Value;
            }
            long? inputTotal = unknown == 0 ? input : (long?)null, outputTotal = unknown == 0 ? output : (long?)null;
            a.Counts["records_without_usage"] = unknown;
            a.Counts["input_tokens_from_records"] = inputTotal;
            a.Counts["output_tokens_from_records"] = outputTotal;
            a.Counts["input_tokens_summary"] = Num(totals, "input_tokens");
            a.Counts["output_tokens_summary"] = Num(totals, "output_tokens");
            if (Num(totals, "input_tokens") != inputTotal) a.Mismatches.Add("input tokens: summary " + Show(Num(totals, "input_tokens")) + " vs records " + Show(inputTotal));
            if (Num(totals, "output_tokens") != outputTotal) a.Mismatches.Add("output tokens: summary " + Show(Num(totals, "output_tokens")) + " vs records " + Show(outputTotal));
            if (o.InputTokens != inputTotal) a.Mismatches.Add("input tokens: runner " + Show(o.InputTokens) + " vs records " + Show(inputTotal));
            if (unknown > 0) a.Unknown.Add("token usage unknown for " + unknown + " of " + records.Count + " requests; match totals are unknown, not zero");
        }

        private static void CompareOrdersAndTurns(MatchAudit a, List<JsonElement> records, JsonElement summary, JsonElement totals)
        {
            var groups = new List<TurnAudit>();
            var byToken = new Dictionary<long, TurnAudit>();
            int accepted = 0, rejected = 0;
            foreach (JsonElement r in records)
            {
                long token = Num(r, "turn_token") ?? -1;
                if (!byToken.TryGetValue(token, out TurnAudit t))
                {
                    t = new TurnAudit { Season = (int)(Num(r, "season") ?? 0), Seat = Str(r, "seat_id") };
                    byToken[token] = t; groups.Add(t);
                }
                t.RequestsInRecords++;
                string command = Str(r, "command_json");
                bool? ok = Bool(r, "result_ok");
                string type = command == null ? null : CommandType(command);
                if (type == null || type == "end_turn" || !ok.HasValue) continue;
                t.OrdersInRecords.Add(type + (ok.Value ? ":accepted" : ":rejected"));
                if (ok.Value) { t.AcceptedInRecords++; accepted++; } else rejected++;
            }
            a.Counts["orders_from_records"] = accepted + rejected;
            a.Counts["accepted_from_records"] = accepted;
            a.Counts["rejected_from_records"] = rejected;
            a.Counts["accepted_summary"] = Num(totals, "accepted");
            a.Counts["rejected_summary"] = Num(totals, "rejected");
            if (Num(totals, "accepted") != accepted) a.Mismatches.Add("accepted orders: summary " + Num(totals, "accepted") + " vs records " + accepted);
            if (Num(totals, "rejected") != rejected) a.Mismatches.Add("rejected orders: summary " + Num(totals, "rejected") + " vs records " + rejected);

            JsonElement turns = summary.GetProperty("turns");
            int index = 0;
            foreach (JsonElement s in turns.EnumerateArray())
            {
                int requests = (int)(Num(s, "requests") ?? 0);
                string last = Str(s, "last_command_json");
                TurnAudit t = null;
                if (requests > 0 && index < groups.Count) t = groups[index++];
                if (t == null)
                {
                    if (requests > 0) a.Mismatches.Add("summary turn s" + Num(s, "season") + " " + Str(s, "seat_id") + " reports " + requests + " requests but has no records");
                    continue;
                }
                t.RequestsInSummary = requests;
                t.AcceptedInSummary = (int)(Num(s, "accepted") ?? 0);
                t.SummaryLastCommand = last;
                // The summary keeps ONE command per turn (the last), so every other order is invisible there.
                t.OrdersNotVisibleInSummaryCommands = Math.Max(0, t.OrdersInRecords.Count - (last != null && CommandType(last) != "end_turn" ? 1 : 0));
                if (t.RequestsInRecords != t.RequestsInSummary) a.Mismatches.Add("turn s" + t.Season + " " + t.Seat + ": records " + t.RequestsInRecords + " requests, summary " + t.RequestsInSummary);
                if (t.AcceptedInRecords != t.AcceptedInSummary) a.Mismatches.Add("turn s" + t.Season + " " + t.Seat + ": records " + t.AcceptedInRecords + " accepted, summary " + t.AcceptedInSummary);
            }
            if (index != groups.Count) a.Mismatches.Add("record turns (" + groups.Count + ") not all present in the summary (" + index + ")");
            a.Turns = groups;
            int hidden = 0;
            foreach (TurnAudit t in groups) hidden += t.OrdersNotVisibleInSummaryCommands;
            if (hidden > 0)
                a.Unknown.Add(hidden + " accepted/rejected order(s) are not visible in the summary's per-turn command list (it keeps only the last command); totals were taken from the per-request records");
        }

        private static void Report(MatchAudit a, List<JsonElement> records)
        {
            int length = 0, empty = 0, maxOut = 0, noCost = 0, noReasoning = 0;
            var cut = new SortedDictionary<string, int[]>();   // section -> {requests cut, min shown, max shown, min available, max available}
            foreach (JsonElement r in records)
            {
                if (Str(r, "finish_reason") == "length") length++;
                if (Bool(r, "content_trimmed_empty") == true) empty++;
                long? outTokens = Num(r, "output_tokens"), cap = Num(r, "max_output_tokens");
                if (outTokens.HasValue && cap.HasValue && outTokens.Value >= cap.Value) maxOut++;
                if (r.TryGetProperty("cost_usd", out JsonElement c) && c.ValueKind == JsonValueKind.Null) noCost++;
                if (!Num(r, "reasoning_tokens").HasValue) noReasoning++;
                if (r.TryGetProperty("context_report", out JsonElement report) && report.ValueKind == JsonValueKind.Object
                    && report.TryGetProperty("sections", out JsonElement sections))
                    foreach (JsonElement s in sections.EnumerateArray())
                    {
                        if (Bool(s, "truncated") != true) continue;
                        string name = Str(s, "name") ?? "?";
                        int shown = (int)(Num(s, "included") ?? 0), avail = (int)(Num(s, "available") ?? 0);
                        if (!cut.TryGetValue(name, out int[] stat)) cut[name] = stat = new[] { 0, int.MaxValue, 0, int.MaxValue, 0 };
                        stat[0]++; stat[1] = Math.Min(stat[1], shown); stat[2] = Math.Max(stat[2], shown); stat[3] = Math.Min(stat[3], avail); stat[4] = Math.Max(stat[4], avail);
                    }
            }
            foreach (KeyValuePair<string, int[]> kv in cut)
                a.Truncation.Add("'" + kv.Key + "' was cut in " + kv.Value[0] + " of " + records.Count + " requests (showed " + kv.Value[1] + "-" + kv.Value[2]
                                 + " of " + kv.Value[3] + "-" + kv.Value[4] + " available)");
            a.Counts["finish_reason_length"] = length;
            a.Counts["empty_replies"] = empty;
            a.Counts["replies_at_output_bound"] = maxOut;
            if (length > 0) a.Truncation.Add(length + " reply(ies) ended with finish_reason=length (model output truncated)");
            if (empty > 0) a.Truncation.Add(empty + " empty reply(ies)");
            if (noCost > 0) a.Unknown.Add("cost_usd unknown in " + noCost + " of " + records.Count + " records");
            if (noReasoning > 0) a.Unknown.Add("reasoning_tokens not reported in " + noReasoning + " of " + records.Count + " records");
        }

        private static string CommandType(string commandJson)
        {
            try
            {
                using (JsonDocument d = JsonDocument.Parse(commandJson)) return Str(d.RootElement, "type");
            }
            catch (JsonException) { return null; }
        }

        private static List<JsonElement> ReadLines(string path)
        {
            var list = new List<JsonElement>();
            foreach (string line in File.ReadAllLines(path))
                if (line.Trim().Length > 0) list.Add(JsonDocument.Parse(line).RootElement.Clone());
            return list;
        }

        internal static string Str(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        internal static long? Num(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : (long?)null;

        internal static bool? Bool(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v)
               && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null;

        private static string Show(long? v) => v.HasValue ? v.Value.ToString() : "unknown";
    }
}
