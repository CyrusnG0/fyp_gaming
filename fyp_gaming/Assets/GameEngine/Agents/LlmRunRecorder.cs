using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Receives one already-serialized JSON line per model request attempt. Implementations must be
    /// local-only sinks; the runner is responsible for never putting the API key in the line.
    /// </summary>
    public interface ILlmRunRecorder
    {
        /// <summary>
        /// Record one line under an explicit run id. Passing the id per call (rather than through
        /// mutable binding state) keeps two runners that share one recorder on separate files.
        /// </summary>
        void Record(string runId, string jsonLine);
    }

    /// <summary>
    /// Local JSONL recorder: one file per run id in the host-supplied directory. Writing is
    /// best-effort so a disk problem cannot break a turn; <see cref="LastWriteError"/> is a safe
    /// diagnostic that never includes file contents or the API key.
    /// </summary>
    public sealed class LlmRunRecorder : ILlmRunRecorder
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly object _gate = new object();
        private string _runId;

        public string DirectoryPath { get; }
        public string RunId { get { lock (_gate) return _runId; } }
        public string LastWriteError { get; private set; }

        public LlmRunRecorder(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("a run directory is required", nameof(directory));
            DirectoryPath = Path.GetFullPath(directory);
        }

        /// <summary>
        /// Bind the recorder's display <see cref="FilePath"/> to a runner's id. Recording itself is
        /// always explicit per <see cref="Record(string,string)"/> call, so sharing a recorder does
        /// not route lines into the wrong run file.
        /// </summary>
        internal void UseRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return;
            lock (_gate)
            {
                if (_runId == null) _runId = runId;
            }
        }

        public string FilePath
        {
            get
            {
                lock (_gate)
                {
                    if (_runId == null)
                        _runId = "run-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-"
                               + Guid.NewGuid().ToString("N").Substring(0, 8);
                    return Path.Combine(DirectoryPath, SafeName(_runId) + ".jsonl");
                }
            }
        }

        public void Record(string runId, string jsonLine)
        {
            if (jsonLine == null) return;
            if (string.IsNullOrWhiteSpace(runId))
            {
                LastWriteError = "run record could not be written";
                return;
            }
            try
            {
                string path = Path.Combine(DirectoryPath, SafeName(runId) + ".jsonl");
                lock (_gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(path, jsonLine + "\n", Utf8NoBom);
                    if (_runId == null) _runId = runId;
                    LastWriteError = null;
                }
            }
            catch (Exception)
            {
                // Best effort: recording must never abort a turn or echo document contents.
                LastWriteError = "run record could not be written";
            }
        }

        private static string SafeName(string value)
        {
            var sb = new StringBuilder(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "run" : sb.ToString();
        }
    }

    // Recording helpers kept beside the recorder so each source file stays small. They are part of
    // LlmTurnRunner's main-thread turn implementation and use only already-captured values.
    public sealed partial class LlmTurnRunner
    {
        private void TrackUsage(LlmResponse response)
        {
            if (response == null) return;
            if (!response.InputTokens.HasValue || !response.OutputTokens.HasValue)
            {
                _inputUnknown = true;
                _outputUnknown = true;
                _costKnown = false;
                return;
            }

            _totalInput = (_totalInput ?? 0) + response.InputTokens.Value;
            _totalOutput = (_totalOutput ?? 0) + response.OutputTokens.Value;
            if (_config.UsdPerMTokIn.HasValue && _config.UsdPerMTokOut.HasValue)
            {
                _cost += response.InputTokens.Value / 1000000.0 * _config.UsdPerMTokIn.Value
                       + response.OutputTokens.Value / 1000000.0 * _config.UsdPerMTokOut.Value;
            }
            else
            {
                _costKnown = false;
            }
        }

        private bool TurnTimeExpired()
        {
            int seconds = Math.Max(0, _config.MaxTurnSeconds);
            return _clock != null && _clock.Elapsed.TotalSeconds >= seconds;
        }

        private bool RequestTimedOut(InFlight flight)
            => _config.RequestTimeoutMs > 0 && ElapsedMs(flight) >= _config.RequestTimeoutMs;

        private int ElapsedMs(InFlight flight)
        {
            if (_clock == null) return 0;
            long elapsed = _clock.ElapsedMilliseconds - flight.StartedMs;
            return elapsed < 0 ? 0 : (int)Math.Min(int.MaxValue, elapsed);
        }

        private int RemainingRequests()
            => Math.Max(0, _config.MaxRequestsPerTurn - RequestsThisTurn);

        private int RemainingActions()
            => Math.Max(0, _config.MaxActionsPerTurn - ActionsThisTurn);

        private void AddHistory(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _history.Add(Limit(Redact(line), 240));
            while (_history.Count > MaxRecentHistory) _history.RemoveAt(0);
        }

        private static string HistoryLine(LlmAction action, string result)
        {
            string target = string.IsNullOrEmpty(action.TargetId) ? "" : " " + action.TargetId;
            return action.Type + target + " -> " + result;
        }

        private static string ErrorText(LlmResponse response)
            => string.IsNullOrEmpty(response.ErrorDetail)
                ? response.Error.ToString()
                : response.Error + ": " + response.ErrorDetail;

        private string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string key = _config.ApiKeyValue;
            return string.IsNullOrEmpty(key) ? value : value.Replace(key, "[redacted]");
        }

        private static string Limit(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static string Hash(string text)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private void WriteRecord(InFlight flight, LlmResponse response, bool parseOk,
            string commandJson, bool? resultOk, string resultError)
        {
            if (_recorder == null) return;
            try
            {
                var parts = new List<string>(30);
                parts.Add(Str("run_id", _runId));
                parts.Add(Num("game_seed", _seed));
                parts.Add(Num("seed", _seed));
                parts.Add(Str("provider", _transport.Provider));
                parts.Add(Str("model", _transport.Model));
                // The version/hash actually sent for this request (a non-planning host stays on
                // demo-slice-3 even while the default planning prompt is demo-slice-4).
                parts.Add(Str("prompt_version", flight.PromptVersion ?? PromptBuilder.ActivePromptVersion));
                parts.Add(Str("schema_hash", flight.SchemaHash ?? PromptBuilder.SchemaHash()));
                parts.Add(Str("seat_id", _seatId));
                parts.Add(Num("turn_token", _turnToken));
                parts.Add(Num("season", flight.Season));
                parts.Add(Str("observation_hash", flight.ObservationHash));
                parts.Add(Str("observation_json", flight.ObservationJson));
                parts.Add(Num("request_index", flight.RequestIndex));
                parts.Add(Num("latency_ms", response == null ? 0 : response.LatencyMs));
                parts.Add(nullableInt("input_tokens", response?.InputTokens));
                parts.Add(nullableInt("output_tokens", response?.OutputTokens));
                parts.Add(Str("raw_model_text", Limit(Redact(response?.Text), 4000)));
                parts.Add(Bool("parse_ok", parseOk));
                parts.Add(Str("command_json", commandJson));
                parts.Add(nullableBool("result_ok", resultOk));
                parts.Add(Str("result_error", Redact(resultError)));
                parts.Add(Str("state_after", State.ToString()));
                parts.Add(Str("error_kind", response == null
                    ? LlmErrorKind.Network.ToString() : response.Error.ToString()));
                parts.Add(Str("error_detail", Redact(response?.ErrorDetail)));
                parts.Add(Str("provider_request_id", response?.ProviderRequestId));
                parts.Add(nullableDouble("cost_usd", CostUsd));
                parts.Add(Num("requests_this_turn", RequestsThisTurn));
                parts.Add(Num("actions_this_turn", ActionsThisTurn));
                parts.Add(Num("invalid_this_turn", InvalidThisTurn));
                parts.Add(Num("total_requests", TotalRequests));
                _recorder.Record(_runId, "{" + string.Join(",", parts) + "}");
            }
            catch (Exception)
            {
                // A recorder fault must never mutate or abort a turn.
            }
        }

        private static string Str(string key, string value)
            => "\"" + key + "\":" + MiniJson.Str(value);
        private static string Num(string key, int value)
            => "\"" + key + "\":" + MiniJson.Num(value);
        private static string Bool(string key, bool value)
            => "\"" + key + "\":" + (value ? "true" : "false");
        private static string nullableInt(string key, int? value)
            => "\"" + key + "\":" + (value.HasValue ? MiniJson.Num(value.Value) : "null");
        private static string nullableBool(string key, bool? value)
            => "\"" + key + "\":" + (value.HasValue ? (value.Value ? "true" : "false") : "null");
        // StrictJson accepts integers only; costs are therefore written as an invariant decimal string
        // (or null when unknown) so the whole record line remains strictly parseable.
        private static string nullableDouble(string key, double? value)
            => "\"" + key + "\":" + (value.HasValue
                ? MiniJson.Str(value.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture))
                : "null");
    }
}
