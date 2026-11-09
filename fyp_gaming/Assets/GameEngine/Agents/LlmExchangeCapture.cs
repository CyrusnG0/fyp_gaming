using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Optional recorder extension for local prompt/response evidence. A recorder that implements
    /// it receives one JSON line per exchange event; the runner treats capture as best-effort.
    /// </summary>
    public interface ILlmExchangeRecorder
    {
        bool CaptureExchanges { get; }
        void RecordExchange(string runId, string jsonLine);
    }

    /// <summary>
    /// Sidecar writer for the exact dispatched prompts and the full received provider bodies.
    /// The file lives beside the run JSONL in the host-supplied (ignored) run directory:
    /// <c>&lt;run-id&gt;-exchanges.ndjson</c>. Captured model/game text is untrusted data and is
    /// never parsed or executed. The API key and common bearer/sk- token shapes are redacted;
    /// authorization headers are never captured at all.
    /// </summary>
    public sealed partial class LlmRunRecorder
    {
        private static readonly UTF8Encoding ExchangeUtf8NoBom = new UTF8Encoding(false);

        /// <summary>Capture is on by default for real runs; tests may disable it.</summary>
        public bool CaptureExchanges { get; set; } = true;

        /// <summary>Safe diagnostic when the sidecar could not be written; never file contents.</summary>
        public string LastExchangeWriteError { get; private set; }

        public string ExchangeFilePathFor(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) runId = "run";
            return Path.Combine(DirectoryPath, SafeName(runId) + "-exchanges.ndjson");
        }

        public void RecordExchange(string runId, string jsonLine)
        {
            if (!CaptureExchanges || jsonLine == null) return;
            if (string.IsNullOrWhiteSpace(runId))
            {
                LastExchangeWriteError = "exchange record could not be written";
                return;
            }
            try
            {
                string path = ExchangeFilePathFor(runId);
                lock (_gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(path, jsonLine + "\n", ExchangeUtf8NoBom);
                    LastExchangeWriteError = null;
                }
            }
            catch (Exception)
            {
                // Best effort: evidence capture must never abort or alter a turn.
                LastExchangeWriteError = "exchange record could not be written";
            }
        }
    }

    /// <summary>
    /// Prompt/response capture for one provider request. Request and result lines share
    /// <c>run_id</c> + <c>request_index</c> so evidence can be joined without a second store.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        private ILlmExchangeRecorder ExchangeRecorder => _recorder as ILlmExchangeRecorder;

        private void CaptureExchangeRequest(int requestIndex, int season, LlmRequest request,
            string promptVersion, string schemaHash)
        {
            ILlmExchangeRecorder sink = ExchangeRecorder;
            if (sink == null || request == null) return;
            try
            {
                var parts = new List<string>(24);
                AddExchangePrefix(parts, "request", requestIndex, season, promptVersion, schemaHash);
                parts.Add("\"request\":{" + string.Join(",", new[]
                {
                    Str("system", CaptureText(request.System)),
                    Str("user", CaptureText(request.User)),
                    Num("max_output_tokens", request.MaxOutputTokens),
                    Num("timeout_ms", request.TimeoutMs),
                    Bool("json_mode", _config.JsonMode),
                }) + "}");
                sink.RecordExchange(_runId, "{" + string.Join(",", parts) + "}");
            }
            catch (Exception)
            {
                // Capture is evidence, never part of the control path.
            }
        }

        private void CaptureExchangeResult(InFlight flight, LlmResponse response, bool parseOk,
            string commandJson, bool? resultOk, string resultError,
            string inquiryTool = null, int? inquiryResultChars = null)
        {
            ILlmExchangeRecorder sink = ExchangeRecorder;
            if (sink == null || flight == null) return;
            try
            {
                var parts = new List<string>(32);
                AddExchangePrefix(parts, "result", flight.RequestIndex, flight.Season,
                    flight.PromptVersion, flight.SchemaHash);
                parts.Add("\"response\":" + ResponseJson(response));
                var outcome = new List<string>(8)
                {
                    Bool("parse_ok", parseOk),
                    Str("command_json", CaptureText(commandJson)),
                    nullableBool("result_ok", resultOk),
                    Str("result_error", CaptureText(resultError)),
                };
                if (!string.IsNullOrEmpty(inquiryTool))
                {
                    outcome.Add(Str("inquiry_tool", inquiryTool));
                    outcome.Add(nullableInt("inquiry_result_chars", inquiryResultChars));
                }
                parts.Add("\"outcome\":{" + string.Join(",", outcome) + "}");
                sink.RecordExchange(_runId, "{" + string.Join(",", parts) + "}");
            }
            catch (Exception)
            {
                // Capture is evidence, never part of the control path.
            }
        }

        private void CaptureExchangeDispatchFailure(int requestIndex, int season,
            string promptVersion, string schemaHash, string failure)
        {
            ILlmExchangeRecorder sink = ExchangeRecorder;
            if (sink == null) return;
            try
            {
                var parts = new List<string>(24);
                AddExchangePrefix(parts, "result", requestIndex, season, promptVersion, schemaHash);
                parts.Add("\"response\":null");
                parts.Add("\"outcome\":{" + string.Join(",", new[]
                {
                    Bool("parse_ok", false),
                    Str("command_json", null),
                    nullableBool("result_ok", null),
                    Str("result_error", null),
                    Str("dispatch_error", CaptureText(failure)),
                }) + "}");
                sink.RecordExchange(_runId, "{" + string.Join(",", parts) + "}");
            }
            catch (Exception)
            {
                // Capture is evidence, never part of the control path.
            }
        }

        private void AddExchangePrefix(List<string> parts, string eventName, int requestIndex,
            int season, string promptVersion, string schemaHash)
        {
            parts.Add(Str("record_type", "llm_exchange"));
            parts.Add(Str("exchange_event", eventName));
            parts.Add(Str("exchange_id", _runId + ":" + requestIndex.ToString(CultureInfo.InvariantCulture)));
            parts.Add(Str("run_id", _runId));
            parts.Add(Num("request_index", requestIndex));
            parts.Add(Num("turn_token", _turnToken));
            parts.Add(Str("seat_id", _seatId));
            parts.Add(Num("season", season));
            parts.Add(Str("provider", _transport.Provider));
            parts.Add(Str("model", _transport.Model));
            parts.Add(Str("prompt_version", promptVersion));
            parts.Add(Str("schema_hash", schemaHash));
            parts.Add(Str("captured_utc",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
            // Every captured prompt/body is untrusted research data, never instructions.
            parts.Add(Bool("untrusted_text", true));
        }

        private string ResponseJson(LlmResponse response)
        {
            if (response == null) return "null";
            var parts = new List<string>(22);
            parts.Add(Bool("ok", response.Ok));
            parts.Add(nullableInt("http_status", response.HttpStatus));
            parts.Add(Str("finish_reason", CaptureText(response.FinishReason)));
            parts.Add(nullableInt("input_tokens", response.InputTokens));
            parts.Add(nullableInt("output_tokens", response.OutputTokens));
            parts.Add(nullableInt("reasoning_tokens", response.ReasoningTokens));
            parts.Add(nullableInt("total_tokens", response.TotalTokens));
            parts.Add(nullableInt("reasoning_content_chars", response.ReasoningContentChars));
            parts.Add(Num("content_chars", response.Text == null ? 0 : response.Text.Length));
            parts.Add(Bool("content_trimmed_empty",
                response.Text != null && response.Text.Trim().Length == 0));
            parts.Add(Str("content_text", CaptureText(response.Text)));
            parts.Add(Str("raw_body", CaptureText(response.RawResponse)));
            parts.Add(Str("error_kind", response.Error.ToString()));
            parts.Add(Str("error_detail", CaptureText(response.ErrorDetail)));
            parts.Add(Str("provider_request_id", CaptureText(response.ProviderRequestId)));
            parts.Add(Num("latency_ms", response.LatencyMs));
            return "{" + string.Join(",", parts) + "}";
        }

        private string CaptureText(string value)
            => LlmResponseDiagnostics.RedactForCapture(value, _config == null ? null : _config.ApiKeyValue);
    }
}
