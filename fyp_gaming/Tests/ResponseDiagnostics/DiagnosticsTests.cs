using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class DiagnosticsTests
{
    // ---- 1: normal response telemetry and exact capture ---------------------

    private static void RunNormalTelemetry()
    {
        Section("telemetry: normal response records finish reason and reasoning usage");
        const string content = "{\"type\":\"end_turn\"}";
        string body = Envelope(content, "stop", true, 11, 7, 3, 18, "brief analysis");
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            var direct = SendOnce(new OpenAiChatTransport(config));
            Check(direct != null && direct.Ok && direct.Text == content
                  && direct.HttpStatus == 200 && direct.RawResponse == body,
                "transport returns content and the exact raw provider body");
            Check(direct.FinishReason == "stop" && direct.ReasoningTokens == 3
                  && direct.TotalTokens == 18
                  && direct.ReasoningContentChars == "brief analysis".Length,
                "finish_reason, reasoning tokens, total tokens and reasoning_content length are parsed");
            Check(stub.LastAuthorization == "Bearer " + FakeKey,
                "stub observed the expected bearer header (proves the key never needs capture)");

            var run = RunTurn(new OpenAiChatTransport(config), config, "normal");
            try
            {
                Check(run.Runner.State == RunnerState.Finished
                      && run.Runner.EndReason == EndReason.ModelEndedTurn,
                    "normal end_turn still finishes the turn");
                var records = RunRecords(run.Dir);
                Check(records.Count == 1, "one run record for one provider request");
                JsonObject record = records.Count == 1 ? records[0] : null;
                Check(S(record, "finish_reason") == "stop"
                      && I(record, "reasoning_tokens") == 3
                      && I(record, "total_tokens") == 18
                      && I(record, "input_tokens") == 11
                      && I(record, "output_tokens") == 7,
                    "run record carries finish reason and input/output/reasoning usage");
                Check(I(record, "content_chars") > 0 && B(record, "content_trimmed_empty") == false,
                    "run record distinguishes a non-empty answer from an empty one");

                var exchange = ExchangeRecords(run.Dir);
                JsonObject requestLine = FindEvent(exchange, "request");
                JsonObject resultLine = FindEvent(exchange, "result");
                Check(requestLine != null && resultLine != null
                      && S(requestLine, "exchange_id") == S(resultLine, "exchange_id"),
                    "request and result sidecar lines share run id and request index");
                JsonObject request = Child(requestLine, "request");
                Check(S(request, "system") != null
                      && S(request, "system").StartsWith("You are the governor")
                      && S(request, "user") != null
                      && S(request, "user").Contains("City A"),
                    "sidecar stores the exact dispatched system/user prompt");
                Check(PromptMatchesWire(stub.LastRequestBody, request),
                    "sidecar prompt is byte-identical to the request body on the wire");
                JsonObject response = Child(resultLine, "response");
                Check(S(response, "raw_body") == body
                      && S(response, "content_text") == content
                      && I(response, "reasoning_content_chars") == "brief analysis".Length,
                    "sidecar stores the full received provider response");
                Check(!ExchangeText(run.Dir).Contains(FakeKey),
                    "sidecar never contains the API key");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    // ---- 2: the recorded empty-reply signature ------------------------------

    private static void RunEmptyReasoningBudget()
    {
        Section("regression: empty content at the output cap is not retried blindly");
        string body = Envelope("", "length", true, 100, 3000, 3000, 3100,
            new string('r', 5000));
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            // More retries are allowed than the runner should use: evidence says the same cap
            // already consumed the whole budget, so a repair request cannot help.
            config.MaxInvalidPerTurn = 5;
            var run = RunTurn(new OpenAiChatTransport(config), config, "empty-reasoning");
            try
            {
                Check(run.Runner.RequestsThisTurn == 1 && stub.RequestCount == 1,
                    "a budget-exhausted empty answer stops after one provider request");
                Check(run.Runner.State == RunnerState.Finished
                      && run.Runner.EndReason == EndReason.InvalidResponseLimit,
                    "turn stops in the existing counted invalid-response category");
                Check(run.Runner.FailureReason != null
                      && run.Runner.FailureReason.Contains("reasoning")
                      && run.Runner.EndReasonText.Contains("reasoning"),
                    "stop reason and display text record that provider reasoning consumed the budget");
                Check(run.Host.EndTurnCalls == 1, "the host still receives exactly one EndTurn");

                var records = RunRecords(run.Dir);
                JsonObject record = records.Count == 1 ? records[0] : null;
                Check(S(record, "finish_reason") == "length"
                      && I(record, "output_tokens") == 3000
                      && I(record, "reasoning_tokens") == 3000
                      && I(record, "content_chars") == 0
                      && B(record, "content_trimmed_empty") == true,
                    "run record preserves the empty/truncated/reasoning evidence");
                Check(S(record, "result_error") != null
                      && S(record, "result_error").Contains("empty response")
                      && S(record, "result_error").Contains("finish_reason=length")
                      && S(record, "result_error").Contains("reasoning_tokens=3000"),
                    "recorded diagnostic names the empty response and provider accounting");

                var exchange = ExchangeRecords(run.Dir);
                JsonObject response = Child(FindEvent(exchange, "result"), "response");
                Check(S(response, "raw_body") == body
                      && B(response, "content_trimmed_empty") == true
                      && I(response, "reasoning_tokens") == 3000,
                    "sidecar keeps the full reasoner envelope for later inspection");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    // ---- 3: whitespace-only content under JSON mode --------------------------

    private static void RunWhitespaceOnlyBudget()
    {
        Section("regression: whitespace-only content at the cap is classified and stopped");
        const string whitespace = "\n\t  \r\n";
        string body = Envelope(whitespace, "length", true, 90, 3000, 0, 3090, null);
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            var run = RunTurn(new OpenAiChatTransport(config), config, "empty-whitespace");
            try
            {
                Check(run.Runner.RequestsThisTurn == 1
                      && run.Runner.EndReason == EndReason.InvalidResponseLimit,
                    "whitespace-only content at the cap stops instead of retrying");
                Check(run.Runner.FailureReason != null
                      && run.Runner.FailureReason.Contains("truncated")
                      && run.Runner.EndReasonText.Contains("truncated"),
                    "stop reason does not claim provider reasoning when none was reported");
                var record = RunRecords(run.Dir).Count == 1 ? RunRecords(run.Dir)[0] : null;
                Check(I(record, "content_chars") == whitespace.Length
                      && B(record, "content_trimmed_empty") == true
                      && I(record, "reasoning_tokens") == 0,
                    "run record shows whitespace-only content with zero reasoning tokens");
                Check(S(record, "result_error") != null
                      && S(record, "result_error").Contains("whitespace-only"),
                    "diagnostic explicitly names whitespace-only content");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    // ---- 4: truncated but non-empty content keeps the repair path -----------

    private static void RunTruncatedNonEmpty()
    {
        Section("regression: truncated non-empty content is recorded, repair path preserved");
        const string partial = "{\"type\":\"end_turn\"";
        string body = Envelope(partial, "length", true, 80, 3000, 0, 3080, null);
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            config.MaxInvalidPerTurn = 1;
            var run = RunTurn(new OpenAiChatTransport(config), config, "truncated");
            try
            {
                Check(run.Runner.RequestsThisTurn == 1
                      && run.Runner.EndReason == EndReason.InvalidResponseLimit,
                    "non-empty truncation keeps the existing invalid-response limit path");
                var record = RunRecords(run.Dir).Count == 1 ? RunRecords(run.Dir)[0] : null;
                Check(S(record, "finish_reason") == "length"
                      && I(record, "content_chars") == partial.Length
                      && B(record, "content_trimmed_empty") == false
                      && S(record, "result_error") != null
                      && !S(record, "result_error").Contains("empty response"),
                    "truncated non-empty content is not misreported as an empty response");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    // ---- 5: missing usage stays unknown -------------------------------------

    private static void RunMissingUsage()
    {
        Section("telemetry: missing provider usage is recorded as null, never zero");
        string body = Envelope("{\"type\":\"end_turn\"}", "stop", false);
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            var run = RunTurn(new OpenAiChatTransport(config), config, "missing-usage");
            try
            {
                var records = RunRecords(run.Dir);
                JsonObject record = records.Count == 1 ? records[0] : null;
                Check(IsNull(record, "input_tokens") && IsNull(record, "output_tokens")
                      && IsNull(record, "reasoning_tokens") && IsNull(record, "total_tokens"),
                    "missing input/output/reasoning/total usage is explicitly null");
                Check(S(record, "finish_reason") == "stop"
                      && run.Runner.TotalInputTokens == null && run.Runner.TotalOutputTokens == null,
                    "finish reason still parses and runner totals stay unknown");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    // ---- 6: HTTP failure body is captured (redacted) ------------------------

    private static void RunHttpFailureCapture()
    {
        Section("capture: HTTP failure records the provider body without credentials");
        string body = "{\"error\":\"boom " + FakeKey + " Bearer " + BearerSecret + "\"}";
        using (var stub = new StubServer(body, 500))
        {
            var config = Config(stub.BaseUrl, 3000);
            var direct = SendOnce(new OpenAiChatTransport(config));
            Check(direct != null && !direct.Ok && direct.Error == LlmErrorKind.Server
                  && direct.HttpStatus == 500 && direct.RawResponse == body,
                "non-2xx provider response keeps status and raw body");
            Check(direct.ErrorDetail == "provider error (HTTP 500)",
                "short error detail stays credential-free");

            var run = RunTurn(new OpenAiChatTransport(config), config, "http-failure");
            try
            {
                var record = RunRecords(run.Dir).Count == 1 ? RunRecords(run.Dir)[0] : null;
                Check(S(record, "error_kind") == "Server" && I(record, "http_status") == 500,
                    "run record carries the HTTP failure class and status");
                var response = Child(FindEvent(ExchangeRecords(run.Dir), "result"), "response");
                Check(S(response, "raw_body") != null
                      && S(response, "raw_body").Contains("[redacted]"),
                    "sidecar stores the failure body with credentials redacted");
                string evidence = ExchangeText(run.Dir)
                    + string.Join("\n", File.ReadAllText(Path.Combine(run.Dir,
                        Path.GetFileName(run.Recorder.FilePath))));
                Check(!evidence.Contains(FakeKey) && !evidence.Contains(BearerSecret)
                      && !evidence.Contains("Authorization"),
                    "neither run record nor sidecar contains the key, bearer token or header");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    // ---- 7: transport start failure still captures the dispatched prompt ----

    private sealed class ThrowingTransport : ILlmTransport
    {
        public string Provider => "throwing";
        public string Model => "throwing-model";
        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            => throw new InvalidOperationException("transport exploded " + FakeKey);
    }

    private static void RunDispatchFailureCapture()
    {
        Section("capture: transport start failure keeps the prompt and a linked failure line");
        var config = Config("http://127.0.0.1:1", 3000);
        var run = RunTurn(new ThrowingTransport(), config, "dispatch-failure");
        try
        {
            Check(run.Runner.State == RunnerState.Failed
                  && run.Runner.EndReason == EndReason.TransportError,
                "transport start failure fails the turn with the transport reason");
            Check(RunRecords(run.Dir).Count == 0,
                "no provider result means no provider run record");
            var exchange = ExchangeRecords(run.Dir);
            Check(exchange.Count == 2
                  && FindEvent(exchange, "request") != null
                  && FindEvent(exchange, "result") != null,
                "sidecar keeps both the exact request and a linked failure result");
            Check(S(Child(FindEvent(exchange, "result"), "outcome"), "dispatch_error")
                  == "transport failed to start",
                "failure result names the dispatch failure");
            Check(!ExchangeText(run.Dir).Contains(FakeKey),
                "the exception text (which contained the key) is not captured");
        }
        finally { DeleteDir(run.Dir); }
    }

    // ---- 7b: timeouts and cancellation still produce linked request records --

    private sealed class NeverTransport : ILlmTransport
    {
        private readonly TaskCompletionSource<LlmResponse> _never =
            new TaskCompletionSource<LlmResponse>();
        public string Provider => "never";
        public string Model => "never-model";
        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct) => _never.Task;
    }

    private static void RunTimeoutAndCancelRecords()
    {
        Section("capture: timeout and cancel record unknown provider fields");
        var config = Config("http://127.0.0.1:1", 3000);
        config.RequestTimeoutMs = 20;
        config.MaxTurnSeconds = 120;
        config.MaxRequestsPerTurn = 2;
        var run = RunTurn(new NeverTransport(), config, "timeout");
        try
        {
            Check(run.Runner.State == RunnerState.Failed
                  && run.Runner.EndReason == EndReason.RequestTimeout
                  && run.Runner.RequestsThisTurn == 1,
                "an in-flight timeout fails the turn exactly once");
            var records = RunRecords(run.Dir);
            JsonObject record = records.Count == 1 ? records[0] : null;
            Check(S(record, "error_kind") == "Timeout"
                  && IsNull(record, "finish_reason")
                  && IsNull(record, "input_tokens")
                  && IsNull(record, "output_tokens")
                  && IsNull(record, "reasoning_tokens"),
                "timed-out request records unknown finish reason and usage as null");
            var exchange = ExchangeRecords(run.Dir);
            Check(exchange.Count == 2
                  && FindEvent(exchange, "request") != null
                  && FindEvent(exchange, "result") != null,
                "timed-out request still has a linked request/result sidecar pair");
            JsonObject response = Child(FindEvent(exchange, "result"), "response");
            Check(response != null && B(response, "ok") == false
                  && S(response, "error_kind") == "Timeout"
                  && S(response, "finish_reason") == null,
                "timed-out sidecar names the transport timeout without a provider answer");
        }
        finally { DeleteDir(run.Dir); }

        string dir = TempDir("cancel");
        try
        {
            var cancelConfig = Config("http://127.0.0.1:1", 3000);
            cancelConfig.MaxRequestsPerTurn = 2;
            var recorder = new LlmRunRecorder(dir);
            var runner = new LlmTurnRunner(new NeverTransport(), cancelConfig, recorder);
            runner.Begin(new TestHost(FixtureObservation()), 1, "seat-a", 8);
            runner.Cancel("handover test");

            Check(runner.State == RunnerState.Cancelled
                  && runner.EndReason == EndReason.Cancelled,
                "host cancellation remains a cancellation");
            var records = RunRecords(dir);
            JsonObject record = records.Count == 1 ? records[0] : null;
            Check(S(record, "error_kind") == "Cancelled"
                  && S(record, "state_after") == RunnerState.Cancelled.ToString()
                  && IsNull(record, "finish_reason"),
                "cancelled request records the cancellation and unknown provider fields");
            Check(ExchangeRecords(dir).Count == 2,
                "cancelled request keeps its exact prompt and a linked result line");
        }
        finally { DeleteDir(dir); }
    }

    // ---- 8: capture redaction of prompt and model text ----------------------

    private static void RunSecretRedaction()
    {
        Section("capture: prompt and model text are redacted before they reach ignored evidence");
        string content = "{\"type\":\"end_turn\",\"reason\":\"" + FakeKey
            + " Bearer " + BearerSecret + "\"}";
        string body = Envelope(content, "stop", true, 10, 5, 0, 15, null);
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            var run = RunTurn(new OpenAiChatTransport(config), config, "redaction",
                " " + FakeKey);
            try
            {
                string exchange = ExchangeText(run.Dir);
                string runText = File.ReadAllText(run.Recorder.FilePath);
                Check(!exchange.Contains(FakeKey) && !exchange.Contains(BearerSecret)
                      && exchange.Contains("[redacted]"),
                    "sidecar redacts the key from the prompt and the key/bearer token from the response");
                Check(!runText.Contains(FakeKey) && !runText.Contains(BearerSecret),
                    "run record redacts key and bearer-shaped credentials in model text");
                Check(!exchange.Contains("Authorization") && !runText.Contains("Authorization"),
                    "no authorization header is ever written to evidence");
                var request = Child(FindEvent(ExchangeRecords(run.Dir), "request"), "request");
                Check(S(request, "user") != null && S(request, "user").Contains("City A"),
                    "the exact prompt is still present apart from the redacted secret");
            }
            finally { DeleteDir(run.Dir); }
        }
    }

    private static bool PromptMatchesWire(string wireBody, JsonObject request)
    {
        if (wireBody == null || request == null) return false;
        // The request body carries temperature 0.2, which the strict model parser intentionally
        // rejects; the provider-envelope parser is the correct one for request inspection.
        if (!StrictJson.TryParseLenientNumbers(wireBody, out JsonNode node, out _)
            || !(node is JsonObject root))
            return false;
        JsonArray messages = Array(root, "messages");
        if (messages == null || messages.Count < 2) return false;
        var system = messages.Items[0] as JsonObject;
        var user = messages.Items[1] as JsonObject;
        return system != null && user != null
            && S(system, "content") == S(request, "system")
            && S(user, "content") == S(request, "user")
            && I(root, "max_tokens") == I(request, "max_output_tokens");
    }

    private static JsonObject FindEvent(List<JsonObject> records, string eventName)
    {
        foreach (JsonObject record in records)
            if (S(record, "exchange_event") == eventName) return record;
        return null;
    }
}
