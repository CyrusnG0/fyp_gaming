using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Writes a local, secret-free pilot summary:
    /// <c>runs/pilot-&lt;runId&gt;-summary.json</c> under a host-supplied directory.
    /// The file contains only configuration that has already passed through
    /// <see cref="LlmConfig.Redacted"/>; the API key is additionally scrubbed from every string.
    /// </summary>
    public sealed class SpectatorSummary
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly string _directory;

        public string DirectoryPath => _directory;

        public SpectatorSummary(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("a summary directory is required", nameof(directory));
            _directory = Path.GetFullPath(directory);
        }

        public string Write(SpectatorSession session, LlmConfig config, ILlmTransport transport)
            => Write(session, config, transport, session == null ? null : session.Runner);

        public string Write(SpectatorSession session, LlmConfig config, ILlmTransport transport,
            LlmTurnRunner runner)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (runner == null) runner = session.Runner;
            if (runner == null) throw new ArgumentException("a runner is required", nameof(runner));

            string runId = runner.RunId;
            string json = BuildJson(session, config, transport, runner);
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory,
                "pilot-" + SafeName(runId) + "-summary.json");
            File.WriteAllText(path, json, Utf8NoBom);
            return path;
        }

        public static string Write(string directory, SpectatorSession session, LlmConfig config,
            ILlmTransport transport)
            => new SpectatorSummary(directory).Write(session, config, transport);

        public static string Write(string directory, SpectatorSession session, LlmConfig config,
            ILlmTransport transport, LlmTurnRunner runner)
            => new SpectatorSummary(directory).Write(session, config, transport, runner);

        private static string BuildJson(SpectatorSession session, LlmConfig config,
            ILlmTransport transport, LlmTurnRunner runner)
        {
            string runId = runner.RunId;
            string provider = transport != null ? transport.Provider : (config != null ? config.Provider : null);
            string model = transport != null ? transport.Model : (config != null ? config.Model : null);
            string redacted = config != null ? config.Redacted() : "unknown";
            long? input = TokenTotal(runner.TotalInputTokens);
            long? output = TokenTotal(runner.TotalOutputTokens);
            double? cost = runner.CostUsd;
            DateTime end = session.EndedUtc ?? DateTime.UtcNow;

            var sb = new StringBuilder(4096);
            sb.Append('{');
            Field(sb, "run_id", runId, config, false);
            sb.Append(",\"run_ids\":[").Append(Str(runId, config)).Append(']');
            sb.Append(",\"seed\":").Append(session.Seed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seasons_planned\":")
              .Append(session.SeasonsPlanned.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seasons_played\":")
              .Append(session.SeasonsPlayed.ToString(CultureInfo.InvariantCulture));
            Field(sb, "stop_reason", session.StopReason ?? session.State.ToString(), config, true);
            Field(sb, "state", session.State.ToString(), config, true);
            Field(sb, "provider", provider, config, true);
            Field(sb, "model", model, config, true);
            Field(sb, "prompt_version", PromptBuilder.PromptVersion, config, true);
            Field(sb, "schema_hash", PromptBuilder.SchemaHash(), config, true);
            Field(sb, "config_redacted", redacted, config, true);
            Field(sb, "start_utc", Utc(session.StartedUtc), config, true);
            Field(sb, "end_utc", Utc(end), config, true);
            sb.Append(",\"requests\":").Append(runner.TotalRequests.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"input_tokens\":").Append(NullableLong(input));
            sb.Append(",\"output_tokens\":").Append(NullableLong(output));
            sb.Append(",\"cost_usd\":").Append(Cost(cost));

            sb.Append(",\"totals\":{");
            sb.Append("\"requests\":").Append(runner.TotalRequests.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"actions\":").Append(session.TotalActions.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"accepted\":").Append(session.TotalAccepted.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rejected\":").Append(session.TotalRejected.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"input_tokens\":").Append(NullableLong(input));
            sb.Append(",\"output_tokens\":").Append(NullableLong(output));
            sb.Append(",\"cost_usd\":").Append(Cost(cost));
            sb.Append('}');

            AppendSeasons(sb, session, config);
            AppendTurns(sb, session, config);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendSeasons(StringBuilder sb, SpectatorSession session, LlmConfig config)
        {
            var seasons = new List<int>();
            foreach (SpectatorTurnRecord record in session.TurnRecords)
                if (!seasons.Contains(record.Season)) seasons.Add(record.Season);
            seasons.Sort();

            sb.Append(",\"seasons\":[");
            for (int s = 0; s < seasons.Count; s++)
            {
                if (s > 0) sb.Append(',');
                int season = seasons[s];
                sb.Append("{\"season\":").Append(season.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"seats\":[");
                var seats = new List<string>();
                foreach (SpectatorTurnRecord record in session.TurnRecords)
                    if (record.Season == season && !seats.Contains(record.SeatId))
                        seats.Add(record.SeatId);

                for (int t = 0; t < seats.Count; t++)
                {
                    if (t > 0) sb.Append(',');
                    AppendSeat(sb, session, season, seats[t], config);
                }
                sb.Append("]}");
            }
            sb.Append(']');
        }

        private static void AppendSeat(StringBuilder sb, SpectatorSession session, int season,
            string seatId, LlmConfig config)
        {
            int requests = 0, actions = 0, accepted = 0, rejected = 0;
            var commands = new List<string>();
            var results = new List<string>();
            var endedBy = new List<string>();
            string faction = null;

            foreach (SpectatorTurnRecord record in session.TurnRecords)
            {
                if (record.Season != season || !string.Equals(record.SeatId, seatId,
                        StringComparison.Ordinal)) continue;
                requests += record.Requests;
                actions += record.Actions;
                accepted += record.Accepted;
                rejected += record.Rejected;
                if (faction == null) faction = record.FactionName;
                if (!string.IsNullOrEmpty(record.LastCommandJson)) commands.Add(record.LastCommandJson);
                if (!string.IsNullOrEmpty(record.LastResult)) results.Add(record.LastResult);
                if (!string.IsNullOrEmpty(record.EndedBy)) endedBy.Add(record.EndedBy);
            }

            sb.Append('{');
            Field(sb, "seat_id", seatId, config, false);
            Field(sb, "faction", faction, config, true);
            sb.Append(",\"requests\":").Append(requests.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"actions\":").Append(actions.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"accepted\":").Append(accepted.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rejected\":").Append(rejected.ToString(CultureInfo.InvariantCulture));
            AppendStrings(sb, "commands", commands, config);
            AppendStrings(sb, "results", results, config);
            AppendStrings(sb, "ended_by", endedBy, config);
            sb.Append('}');
        }

        private static void AppendTurns(StringBuilder sb, SpectatorSession session, LlmConfig config)
        {
            sb.Append(",\"turns\":[");
            for (int i = 0; i < session.TurnRecords.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SpectatorTurnRecord record = session.TurnRecords[i];
                sb.Append('{');
                sb.Append("\"season\":").Append(record.Season.ToString(CultureInfo.InvariantCulture));
                Field(sb, "seat_id", record.SeatId, config, true);
                Field(sb, "faction", record.FactionName, config, true);
                sb.Append(",\"requests\":").Append(record.Requests.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"actions\":").Append(record.Actions.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"accepted\":").Append(record.Accepted.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"rejected\":").Append(record.Rejected.ToString(CultureInfo.InvariantCulture));
                Field(sb, "last_summary", record.LastSummary, config, true);
                Field(sb, "last_command_json", record.LastCommandJson, config, true);
                Field(sb, "last_result", record.LastResult, config, true);
                Field(sb, "ended_by", record.EndedBy, config, true);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void AppendStrings(StringBuilder sb, string key, List<string> values,
            LlmConfig config)
        {
            sb.Append(",\"").Append(key).Append("\":[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Str(values[i], config));
            }
            sb.Append(']');
        }

        private static void Field(StringBuilder sb, string key, string value, LlmConfig config,
            bool comma)
        {
            if (comma) sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(Str(value, config));
        }

        private static string Str(string value, LlmConfig config)
        {
            if (value == null) return "null";
            string key = config == null ? null : config.ApiKeyValue;
            if (!string.IsNullOrEmpty(key)) value = value.Replace(key, "[redacted]");
            return MiniJson.Str(value);
        }

        private static string NullableLong(long? value)
            => value.HasValue && value.Value != 0
                ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";

        private static long? TokenTotal(long? value)
            => value.HasValue && value.Value != 0 ? value : (long?)null;

        private static string Cost(double? value)
            => value.HasValue
                ? MiniJson.Str(value.Value.ToString("0.########", CultureInfo.InvariantCulture))
                : "null";

        private static string Utc(DateTime value)
            => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        private static string SafeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "run";
            var sb = new StringBuilder(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "run" : sb.ToString();
        }
    }
}
