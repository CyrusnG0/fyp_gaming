using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LuanShi.Engine
{
    public static class ReplayLoader
    {
        public const string VerifiedDemoRunId = "run-20261109082434809-51b7ebd5";

        private sealed class ExchangeRecord
        {
            public string ExchangeId;
            public string UserPrompt;
            public string ContentText;
            public string Explanation;
        }

        public static bool TryResolveRunFiles(string runId, out string summaryPath, out string jsonlPath, out string exchangesPath, out string error)
            => TryResolveRunFiles(runId, null, out summaryPath, out jsonlPath, out exchangesPath, out error);

        public static bool TryResolveRunFiles(string runId, string searchRoot, out string summaryPath, out string jsonlPath, out string exchangesPath, out string error)
        {
            summaryPath = null; jsonlPath = null; exchangesPath = null; error = null;

            if (string.IsNullOrWhiteSpace(runId))
            {
                error = "Run ID is required.";
                return false;
            }

            string cleanRunId = runId.Trim();
            if (!string.Equals(cleanRunId, VerifiedDemoRunId, StringComparison.Ordinal))
            {
                error = $"Offline replay is restricted to verified demo run '{VerifiedDemoRunId}'; got '{cleanRunId}'.";
                return false;
            }

            var searchDirs = new List<string>();
            bool rootSpecified = !string.IsNullOrEmpty(searchRoot);
            if (rootSpecified)
            {
                string fullRoot = Path.GetFullPath(searchRoot);
                if (Directory.Exists(fullRoot)) searchDirs.Add(fullRoot);
                string rootRuns = Path.Combine(fullRoot, "runs");
                if (Directory.Exists(rootRuns)) searchDirs.Add(rootRuns);
            }
            else
            {
                string cur = Directory.GetCurrentDirectory();
                searchDirs.Add(Path.Combine(cur, "runs"));
                searchDirs.Add(cur);
                searchDirs.Add(Path.Combine(cur, "..", "runs"));
                searchDirs.Add(Path.Combine(cur, "..", "..", "runs"));
                searchDirs.Add(Path.Combine(cur, ".kimi-code", "worktrees", "offline-replay", "runs"));
                searchDirs.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs"));
            }

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                string s = null, j = null, e = null;
                string sum1 = Path.Combine(dir, $"pilot-{cleanRunId}-summary.json");
                string sum2 = Path.Combine(dir, $"{cleanRunId}-summary.json");
                if (File.Exists(sum1)) s = sum1;
                else if (File.Exists(sum2)) s = sum2;

                string jsonFile = Path.Combine(dir, $"{cleanRunId}.jsonl");
                if (File.Exists(jsonFile)) j = jsonFile;

                string exchFile = Path.Combine(dir, $"{cleanRunId}-exchanges.ndjson");
                if (File.Exists(exchFile)) e = exchFile;

                if (s != null && j != null && e != null)
                {
                    summaryPath = Path.GetFullPath(s);
                    jsonlPath = Path.GetFullPath(j);
                    exchangesPath = Path.GetFullPath(e);
                    return true;
                }

                if (s != null || j != null || e != null || rootSpecified)
                {
                    var missing = new List<string>();
                    if (s == null) missing.Add("missing summary");
                    if (j == null) missing.Add("missing jsonl");
                    if (e == null) missing.Add("missing exchanges");
                    error = $"Directory '{dir}' has incomplete files for run '{cleanRunId}': {string.Join(", ", missing)}.";
                    return false;
                }
            }

            error = $"Could not resolve verified run files for '{cleanRunId}' in searched paths.";
            return false;
        }

        public static ReplayBundle LoadByRunId(string runId, string searchRoot = null)
        {
            if (!TryResolveRunFiles(runId, searchRoot, out string s, out string j, out string e, out string err))
                throw new FileNotFoundException(err);
            return Load(s, j, e, runId);
        }

        public static ReplayBundle Load(string summaryPath, string jsonlPath, string exchangesPath, string expectedRunId = VerifiedDemoRunId)
        {
            if (!File.Exists(summaryPath)) throw new FileNotFoundException("summary file missing: " + summaryPath);
            if (!File.Exists(jsonlPath)) throw new FileNotFoundException("jsonl file missing: " + jsonlPath);
            if (!File.Exists(exchangesPath)) throw new FileNotFoundException("exchanges file missing: " + exchangesPath);

            var summary = LoadSummary(summaryPath);
            if (string.IsNullOrEmpty(summary.RunId) || !string.Equals(summary.RunId, VerifiedDemoRunId, StringComparison.Ordinal))
                throw new InvalidDataException($"Summary run_id '{summary.RunId}' does not match verified demo run '{VerifiedDemoRunId}'.");
            if (expectedRunId != null && !string.Equals(summary.RunId, expectedRunId, StringComparison.Ordinal))
                throw new InvalidDataException($"Summary run_id '{summary.RunId}' does not match expected run_id '{expectedRunId}'.");

            var exchanges = LoadExchanges(exchangesPath, summary.RunId);
            var requests = LoadRequests(jsonlPath, exchanges, summary.RunId, summary.Seed);

            if (summary.TotalRequests <= 0 || requests.Count != summary.TotalRequests)
                throw new InvalidDataException($"Total requests mismatch: summary specifies {summary.TotalRequests}, but jsonl loaded {requests.Count}.");

            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                string key = (req.RequestIndex > 0 && !string.IsNullOrEmpty(req.RunId) && !string.IsNullOrEmpty(req.SeatId) && req.Season > 0)
                    ? $"{req.RunId}:{req.SeatId}:{req.Season}:{req.RequestIndex}"
                    : null;
                if (key == null || !exchanges.TryGetValue(key, out ExchangeRecord ex) || string.IsNullOrEmpty(ex.UserPrompt) || string.IsNullOrEmpty(ex.ContentText))
                    throw new InvalidDataException($"Missing or incomplete exchange record for request {key ?? "absent-request-index"}.");
            }

            return new ReplayBundle {
                Summary = summary, Requests = requests,
                SummaryPath = summaryPath, JsonlPath = jsonlPath, ExchangesPath = exchangesPath
            };
        }

        private static Dictionary<string, ExchangeRecord> LoadExchanges(string path, string expectedRunId)
        {
            var map = new Dictionary<string, ExchangeRecord>(StringComparer.Ordinal);
            using (var reader = new StreamReader(path))
            {
                string line;
                int lineNum = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!StrictJson.TryParseLenientNumbers(line, out JsonNode root, out string err) || !(root is JsonObject obj))
                        throw new InvalidDataException($"Failed to parse exchange JSON line {lineNum}: {err}");

                    string runId = GetString(obj, "run_id");
                    if (!string.Equals(runId, expectedRunId, StringComparison.Ordinal))
                        throw new InvalidDataException($"Exchange record at line {lineNum} has run_id '{runId}' (expected '{expectedRunId}').");

                    int reqIdx = GetInt(obj, "request_index", -1);
                    string seatId = GetString(obj, "seat_id");
                    int season = GetInt(obj, "season", -1);
                    // Match key exact: run_id + seat_id + season + request_index.
                    // If request index is absent (<= 0), record cannot match an action.
                    string key = (reqIdx > 0 && !string.IsNullOrEmpty(runId) && !string.IsNullOrEmpty(seatId) && season > 0)
                        ? $"{runId}:{seatId}:{season}:{reqIdx}"
                        : null;
                    if (key == null) continue;

                    if (!map.TryGetValue(key, out ExchangeRecord rec)) { rec = new ExchangeRecord(); map[key] = rec; }
                    string exId = GetString(obj, "exchange_id");
                    if (!string.IsNullOrEmpty(exId)) rec.ExchangeId = exId;

                    string evt = GetString(obj, "exchange_event");
                    if (evt == "request" && obj.TryGet("request", out JsonNode reqNode) && reqNode is JsonObject reqObj)
                        rec.UserPrompt = GetString(reqObj, "user");
                    else if (evt == "result" && obj.TryGet("response", out JsonNode resNode) && resNode is JsonObject resObj)
                    {
                        string ct = GetString(resObj, "content_text");
                        rec.ContentText = ct;
                        if (!string.IsNullOrEmpty(ct) && StrictJson.TryParse(ct, out JsonNode ctNode, out _) && ctNode is JsonObject ctObj)
                        {
                            string summary = GetString(ctObj, "summary", null);
                            string reason = GetString(ctObj, "reason", null);
                            if (!string.IsNullOrEmpty(summary)) rec.Explanation = summary;
                            else if (!string.IsNullOrEmpty(reason)) rec.Explanation = reason;
                            else rec.Explanation = "unknown";
                        }
                        else
                        {
                            rec.Explanation = "unknown";
                        }
                    }
                }
            }
            return map;
        }

        private static ReplaySummaryData LoadSummary(string path)
        {
            string text = File.ReadAllText(path);
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode root, out string err) || !(root is JsonObject obj))
                throw new InvalidDataException("Invalid summary JSON: " + err);

            var data = new ReplaySummaryData {
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
                            {
                                var missing = new List<string>();
                                if (!scObj.TryGet("seat_id", out var sidNode) || !(sidNode is JsonString)) missing.Add("seat_id");
                                if (!scObj.TryGet("faction", out var facNode) || !(facNode is JsonString)) missing.Add("faction");
                                if (!scObj.TryGet("gold", out var gNode) || !(gNode is JsonInt)) missing.Add("gold");
                                if (!scObj.TryGet("food", out var fNode) || !(fNode is JsonInt)) missing.Add("food");
                                if (!scObj.TryGet("population", out var pNode) || !(pNode is JsonInt)) missing.Add("population");
                                if (!scObj.TryGet("garrison", out var garNode) || !(garNode is JsonInt)) missing.Add("garrison");
                                if (!scObj.TryGet("cities", out var cNode) || !(cNode is JsonInt)) missing.Add("cities");

                                list.Add(new ReplaySeatScore {
                                    SeatId = GetString(scObj, "seat_id"), Faction = GetString(scObj, "faction"),
                                    Gold = GetInt(scObj, "gold"), Food = GetInt(scObj, "food"),
                                    Population = GetInt(scObj, "population"), Garrison = GetInt(scObj, "garrison"),
                                    Cities = GetInt(scObj, "cities"),
                                    MissingFields = missing.Count > 0 ? missing : null
                                });
                            }
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

        private static List<ReplayRequestRecord> LoadRequests(string path, Dictionary<string, ExchangeRecord> exchanges, string expectedRunId, int expectedSeed)
        {
            var list = new List<ReplayRequestRecord>();
            using (var reader = new StreamReader(path))
            {
                string line;
                int lineNum = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!StrictJson.TryParseLenientNumbers(line, out JsonNode root, out string err) || !(root is JsonObject obj))
                        throw new InvalidDataException($"Failed to parse request JSON line {lineNum}: {err}");

                    string runId = GetString(obj, "run_id");
                    if (!string.Equals(runId, expectedRunId, StringComparison.Ordinal))
                        throw new InvalidDataException($"Request record at line {lineNum} has run_id '{runId}' (expected '{expectedRunId}').");

                    int seed = GetInt(obj, "seed", GetInt(obj, "game_seed"));
                    if (seed != expectedSeed)
                        throw new InvalidDataException($"Request record at line {lineNum} has seed {seed} (expected {expectedSeed}).");

                    bool parseOk = GetBool(obj, "parse_ok");
                    string raw = GetString(obj, "raw_model_text");
                    bool isEnd = false;
                    if (parseOk && !string.IsNullOrEmpty(raw))
                    {
                        var p = LlmActionParser.Parse(raw);
                        isEnd = p.Ok && p.Action != null && p.Action.EndTurn;
                    }
                    bool resultOk = obj.TryGet("result_ok", out JsonNode roNode) && roNode is JsonBool rob ? rob.Value : (isEnd && parseOk);

                    var rec = new ReplayRequestRecord {
                        RunId = runId, Seed = seed, Provider = GetString(obj, "provider"), Model = GetString(obj, "model"),
                        PromptVersion = GetString(obj, "prompt_version"), SchemaHash = GetString(obj, "schema_hash"),
                        SeatId = GetString(obj, "seat_id"), TurnToken = GetInt(obj, "turn_token"), Season = GetInt(obj, "season"),
                        ObservationHash = GetString(obj, "observation_hash"), ObservationJson = GetRawOrString(obj, "observation_json"),
                        RequestIndex = GetInt(obj, "request_index"), TotalRequests = GetInt(obj, "total_requests"),
                        RawModelText = raw, ParseOk = parseOk,
                        CommandJson = GetRawOrString(obj, "command_json"), ResultOk = resultOk,
                        ResultError = GetString(obj, "result_error"), StateAfter = GetString(obj, "state_after"),
                        FinishReason = GetString(obj, "finish_reason"), InputTokens = GetInt(obj, "input_tokens"),
                        OutputTokens = GetInt(obj, "output_tokens"), ReasoningTokens = GetInt(obj, "reasoning_tokens"),
                        TotalTokens = GetInt(obj, "total_tokens"), ProviderRequestId = GetString(obj, "provider_request_id")
                    };

                    string key = (rec.RequestIndex > 0 && !string.IsNullOrEmpty(rec.RunId) && !string.IsNullOrEmpty(rec.SeatId) && rec.Season > 0)
                        ? $"{rec.RunId}:{rec.SeatId}:{rec.Season}:{rec.RequestIndex}"
                        : null;
                    if (key != null && exchanges.TryGetValue(key, out ExchangeRecord ex))
                    {
                        rec.ExchangeId = ex.ExchangeId; rec.UserPrompt = ex.UserPrompt;
                        rec.PromptExcerpt = ExtractPromptExcerpt(ex.UserPrompt);
                        rec.ResponseContentText = ex.ContentText ?? rec.RawModelText;
                        rec.RecordedExplanation = !string.IsNullOrEmpty(ex.Explanation) ? ex.Explanation : "unknown";
                    }
                    else
                    {
                        rec.ResponseContentText = rec.RawModelText;
                        rec.RecordedExplanation = null;
                    }
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

        private static string GetString(JsonObject obj, string key, string fallback = "") => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonString s ? s.Value : fallback;
        private static string GetRawOrString(JsonObject obj, string key) => obj != null && obj.TryGet(key, out JsonNode n) ? (n is JsonString s ? s.Value : n.ToString()) : "";
        private static int GetInt(JsonObject obj, string key, int fallback = 0) => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonInt i ? (int)i.Value : fallback;
        private static long GetLong(JsonObject obj, string key, long fallback = 0) => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonInt i ? i.Value : fallback;
        private static bool GetBool(JsonObject obj, string key, bool fallback = false) => obj != null && obj.TryGet(key, out JsonNode n) && n is JsonBool b ? b.Value : fallback;
    }
}
