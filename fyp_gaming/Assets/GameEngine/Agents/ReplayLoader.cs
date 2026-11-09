using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LuanShi.Engine
{
    public static class ReplayLoader
    {
        private sealed class ExchangeRecord
        {
            public string ExchangeId;
            public string UserPrompt;
            public string ContentText;
        }

        public static bool TryResolveRunFiles(string runIdOrDir, out string summaryPath, out string jsonlPath, out string exchangesPath, out string error)
        {
            summaryPath = null; jsonlPath = null; exchangesPath = null; error = null;
            string target = string.IsNullOrEmpty(runIdOrDir) ? "51b7ebd5" : runIdOrDir.Trim();
            if (target.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.GetDirectoryName(target);
                target = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
            }

            var searchDirs = new List<string>();
            if (Directory.Exists(target)) searchDirs.Add(Path.GetFullPath(target));
            string cur = Directory.GetCurrentDirectory();
            searchDirs.Add(Path.Combine(cur, "runs"));
            searchDirs.Add(cur);
            searchDirs.Add(Path.Combine(cur, "..", "runs"));
            searchDirs.Add(Path.Combine(cur, "..", "..", "runs"));
            searchDirs.Add(Path.Combine(cur, "..", "..", "..", "runs"));
            searchDirs.Add(Path.Combine(cur, ".kimi-code", "worktrees", "offline-replay", "runs"));
            searchDirs.Add(Path.Combine(cur, ".kimi-code", "worktrees", "demo-integration-prep", "runs"));
            searchDirs.Add(Path.Combine(cur, "..", "demo-integration-prep", "runs"));
            searchDirs.Add(Path.Combine(cur, "..", "..", "demo-integration-prep", "runs"));
            searchDirs.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs"));

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                string s = null, j = null, e = null;
                string[] files = Directory.GetFiles(dir);

                foreach (string f in files)
                {
                    string name = Path.GetFileName(f);
                    bool match = target.Length < 3 || name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (name.EndsWith("-summary.json", StringComparison.OrdinalIgnoreCase) && match) s = f;
                    else if (name.EndsWith("-exchanges.ndjson", StringComparison.OrdinalIgnoreCase) && match) e = f;
                    else if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) &&
                             !name.EndsWith("-exchanges.ndjson", StringComparison.OrdinalIgnoreCase) && match) j = f;
                }
                if (s == null || j == null || e == null)
                {
                    foreach (string f in files)
                    {
                        string name = Path.GetFileName(f);
                        if (name.EndsWith("-summary.json", StringComparison.OrdinalIgnoreCase)) s = f;
                        else if (name.EndsWith("-exchanges.ndjson", StringComparison.OrdinalIgnoreCase)) e = f;
                        else if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) &&
                                 !name.EndsWith("-exchanges.ndjson", StringComparison.OrdinalIgnoreCase)) j = f;
                    }
                }
                if (s != null && j != null && e != null)
                {
                    summaryPath = Path.GetFullPath(s);
                    jsonlPath = Path.GetFullPath(j);
                    exchangesPath = Path.GetFullPath(e);
                    return true;
                }
            }
            error = $"Could not resolve replay files for '{runIdOrDir}' in searched paths.";
            return false;
        }

        public static ReplayBundle LoadByRunId(string runId, string searchRoot = null)
        {
            string start = string.IsNullOrEmpty(searchRoot) ? runId : searchRoot;
            if (!TryResolveRunFiles(start, out string s, out string j, out string e, out string err))
                throw new FileNotFoundException(err);
            return Load(s, j, e);
        }

        public static ReplayBundle Load(string summaryPath, string jsonlPath, string exchangesPath)
        {
            if (!File.Exists(summaryPath)) throw new FileNotFoundException("summary file missing", summaryPath);
            if (!File.Exists(jsonlPath)) throw new FileNotFoundException("jsonl file missing", jsonlPath);
            if (!File.Exists(exchangesPath)) throw new FileNotFoundException("exchanges file missing", exchangesPath);

            var exchanges = LoadExchanges(exchangesPath);
            var summary = LoadSummary(summaryPath);
            var requests = LoadRequests(jsonlPath, exchanges);

            return new ReplayBundle
            {
                Summary = summary, Requests = requests,
                SummaryPath = summaryPath, JsonlPath = jsonlPath, ExchangesPath = exchangesPath
            };
        }

        private static Dictionary<string, ExchangeRecord> LoadExchanges(string path)
        {
            var map = new Dictionary<string, ExchangeRecord>(StringComparer.Ordinal);
            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!StrictJson.TryParseLenientNumbers(line, out JsonNode root, out _) || !(root is JsonObject obj)) continue;

                    string runId = GetString(obj, "run_id");
                    int turnToken = GetInt(obj, "turn_token");
                    int reqIndex = GetInt(obj, "request_index");
                    string key = $"{runId}:{turnToken}:{reqIndex}";

                    if (!map.TryGetValue(key, out ExchangeRecord rec)) { rec = new ExchangeRecord(); map[key] = rec; }
                    string exId = GetString(obj, "exchange_id");
                    if (!string.IsNullOrEmpty(exId)) rec.ExchangeId = exId;

                    string evt = GetString(obj, "exchange_event");
                    if (evt == "request" && obj.TryGet("request", out JsonNode reqNode) && reqNode is JsonObject reqObj)
                        rec.UserPrompt = GetString(reqObj, "user");
                    else if (evt == "result" && obj.TryGet("response", out JsonNode resNode) && resNode is JsonObject resObj)
                        rec.ContentText = GetString(resObj, "content_text");
                }
            }
            return map;
        }

        private static ReplaySummaryData LoadSummary(string path)
        {
            string text = File.ReadAllText(path);
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode root, out string err) || !(root is JsonObject obj))
                throw new FormatException("Invalid summary JSON: " + err);

            var data = new ReplaySummaryData
            {
                RunId = GetString(obj, "run_id"), Seed = GetInt(obj, "seed"),
                ManualLookupEnabled = GetBool(obj, "manual_lookup_enabled"),
                SeasonsPlanned = GetInt(obj, "seasons_planned"), SeasonsPlayed = GetInt(obj, "seasons_played"),
                StopReason = GetString(obj, "stop_reason"), State = GetString(obj, "state"),
                Provider = GetString(obj, "provider"), Model = GetString(obj, "model"),
                PromptVersion = GetString(obj, "prompt_version"), SchemaHash = GetString(obj, "schema_hash"),
                ConfigRedacted = GetString(obj, "config_redacted"), StartUtc = GetString(obj, "start_utc"),
                EndUtc = GetString(obj, "end_utc"), TotalRequests = GetInt(obj, "requests"),
                InputTokens = GetLong(obj, "input_tokens"), OutputTokens = GetLong(obj, "output_tokens"),
            };

            if (obj.TryGet("balance_profile", out JsonNode bpNode))
            {
                if (bpNode is JsonString bpStr) data.BalanceProfileKey = bpStr.Value;
                else if (bpNode is JsonObject bpObj) data.BalanceProfileKey = GetString(bpObj, "profile");
            }
            if (obj.TryGet("totals", out JsonNode totalsNode) && totalsNode is JsonObject tObj)
            {
                data.TotalActions = GetInt(tObj, "actions");
                data.TotalAccepted = GetInt(tObj, "accepted");
                data.TotalRejected = GetInt(tObj, "rejected");
                data.TotalVoluntaryEndTurns = GetInt(tObj, "voluntary_end_turns");
            }

            var scoreboards = new List<ReplaySeasonScoreboard>();
            if (obj.TryGet("seasons", out JsonNode seasonsNode) && seasonsNode is JsonArray sArr)
            {
                foreach (JsonNode sNode in sArr.Items)
                {
                    if (!(sNode is JsonObject sObj)) continue;
                    int seasonNum = GetInt(sObj, "season");
                    var list = new List<ReplaySeatScore>();
                    if (sObj.TryGet("scoreboard", out JsonNode sbNode) && sbNode is JsonArray sbArr)
                        foreach (JsonNode scNode in sbArr.Items)
                            if (scNode is JsonObject scObj)
                                list.Add(new ReplaySeatScore {
                                    SeatId = GetString(scObj, "seat_id"), Faction = GetString(scObj, "faction"),
                                    Gold = GetInt(scObj, "gold"), Food = GetInt(scObj, "food"),
                                    Population = GetInt(scObj, "population"), Garrison = GetInt(scObj, "garrison"),
                                    Cities = GetInt(scObj, "cities")
                                });
                    scoreboards.Add(new ReplaySeasonScoreboard { Season = seasonNum, Scores = list });
                }
            }
            data.Scoreboards = scoreboards;

            var turns = new List<ReplayTurnSummary>();
            if (obj.TryGet("turns", out JsonNode turnsNode) && turnsNode is JsonArray tArr)
            {
                foreach (JsonNode tNode in tArr.Items)
                    if (tNode is JsonObject turnObj)
                        turns.Add(new ReplayTurnSummary {
                            Season = GetInt(turnObj, "season"), SeatId = GetString(turnObj, "seat_id"),
                            Faction = GetString(turnObj, "faction"), Requests = GetInt(turnObj, "requests"),
                            Actions = GetInt(turnObj, "actions"), Accepted = GetInt(turnObj, "accepted"),
                            Rejected = GetInt(turnObj, "rejected"), Invalid = GetInt(turnObj, "invalid"),
                            LastSummary = GetString(turnObj, "last_summary"), PersonaId = GetString(turnObj, "persona_id"),
                            LastCommandJson = GetString(turnObj, "last_command_json"), LastResult = GetString(turnObj, "last_result"),
                            EndedBy = GetString(turnObj, "ended_by"), EndReason = GetString(turnObj, "end_reason"),
                            EndReasonText = GetString(turnObj, "end_reason_text"),
                        });
            }
            data.Turns = turns;
            return data;
        }

        private static List<ReplayRequestRecord> LoadRequests(string path, Dictionary<string, ExchangeRecord> exchanges)
        {
            var list = new List<ReplayRequestRecord>();
            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!StrictJson.TryParseLenientNumbers(line, out JsonNode root, out _) || !(root is JsonObject obj)) continue;

                    var rec = new ReplayRequestRecord
                    {
                        RunId = GetString(obj, "run_id"),
                        Seed = GetInt(obj, "seed", GetInt(obj, "game_seed")),
                        Provider = GetString(obj, "provider"), Model = GetString(obj, "model"),
                        PromptVersion = GetString(obj, "prompt_version"), SchemaHash = GetString(obj, "schema_hash"),
                        SeatId = GetString(obj, "seat_id"), TurnToken = GetInt(obj, "turn_token"), Season = GetInt(obj, "season"),
                        ObservationHash = GetString(obj, "observation_hash"), ObservationJson = GetRawOrString(obj, "observation_json"),
                        RequestIndex = GetInt(obj, "request_index"), TotalRequests = GetInt(obj, "total_requests"),
                        RawModelText = GetString(obj, "raw_model_text"), ParseOk = GetBool(obj, "parse_ok"),
                        CommandJson = GetRawOrString(obj, "command_json"), ResultOk = GetBool(obj, "result_ok"),
                        ResultError = GetString(obj, "result_error"), StateAfter = GetString(obj, "state_after"),
                        FinishReason = GetString(obj, "finish_reason"), InputTokens = GetInt(obj, "input_tokens"),
                        OutputTokens = GetInt(obj, "output_tokens"), ReasoningTokens = GetInt(obj, "reasoning_tokens"),
                        TotalTokens = GetInt(obj, "total_tokens"), ProviderRequestId = GetString(obj, "provider_request_id")
                    };

                    string key = $"{rec.RunId}:{rec.TurnToken}:{rec.RequestIndex}";
                    if (exchanges.TryGetValue(key, out ExchangeRecord ex))
                    {
                        rec.ExchangeId = ex.ExchangeId; rec.UserPrompt = ex.UserPrompt;
                        rec.PromptExcerpt = ExtractPromptExcerpt(ex.UserPrompt);
                        rec.ResponseContentText = ex.ContentText ?? rec.RawModelText;
                    }
                    else { rec.ResponseContentText = rec.RawModelText; }
                    list.Add(rec);
                }
            }
            return list;
        }

        public static string ExtractPromptExcerpt(string userPrompt)
        {
            if (string.IsNullOrEmpty(userPrompt)) return "";
            string sanitized = Regex.Replace(userPrompt, @"(api_key|password|secret|bearer)\s*[=:]\s*[\w\-\.]+", "$1=<redacted>", RegexOptions.IgnoreCase);
            int start = sanitized.IndexOf("TURN CONTEXT", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = 0;
            int len = Math.Min(600, sanitized.Length - start);
            string excerpt = sanitized.Substring(start, len).Trim();
            if (start + len < sanitized.Length) excerpt += " ...";
            return excerpt;
        }

        private static string GetString(JsonObject obj, string key, string fallback = "")
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonString s ? s.Value : fallback;

        private static string GetRawOrString(JsonObject obj, string key)
        {
            if (obj != null && obj.TryGet(key, out JsonNode n))
            {
                if (n is JsonString s) return s.Value;
                if (n is JsonObject o) return o.ToString();
            }
            return "";
        }

        private static int GetInt(JsonObject obj, string key, int fallback = 0)
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonInt i ? (int)i.Value : fallback;

        private static long GetLong(JsonObject obj, string key, long fallback = 0)
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonInt i ? i.Value : fallback;

        private static bool GetBool(JsonObject obj, string key, bool fallback = false)
            => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonBool b ? b.Value : fallback;
    }
}
