using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LuanShi.Engine
{
    /// <summary>
    /// OpenAI-compatible chat-completions transport (DeepSeek by default). Only this class performs
    /// provider I/O. The bearer key is read once at construction and never logged, serialized or
    /// included in an error detail.
    /// </summary>
    public sealed class OpenAiChatTransport : ILlmTransport
    {
        /// <summary>Provider response bodies larger than this are rejected without buffering them.</summary>
        public const int MaxResponseBytes = 1024 * 1024;

        private static readonly HttpClient Http = CreateClient();

        private readonly LlmConfig _config;
        private readonly string _apiKey;

        public string Provider => string.IsNullOrEmpty(_config.Provider) ? "deepseek" : _config.Provider;
        public string Model => _config.Model;

        public OpenAiChatTransport(LlmConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _apiKey = config.ApiKeyValue;
        }

        public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            var clock = Stopwatch.StartNew();
            if (req == null)
                return LlmResponse.Failure(LlmErrorKind.Network, "missing request");
            if (string.IsNullOrEmpty(_apiKey))
                return LlmResponse.Failure(LlmErrorKind.Auth, "api key is not configured");

            string endpoint = BuildEndpoint(_config.BaseUrl);
            if (endpoint == null)
            {
                return LlmResponse.Failure(LlmErrorKind.Network,
                    string.IsNullOrWhiteSpace(_config.BaseUrl)
                        ? "provider base url is not configured"
                        : "provider base url must use https");
            }

            int timeoutMs = req.TimeoutMs > 0 ? req.TimeoutMs : _config.RequestTimeoutMs;
            if (timeoutMs <= 0) timeoutMs = 25000;
            int maxTokens = req.MaxOutputTokens > 0 ? req.MaxOutputTokens : _config.MaxOutputTokens;
            if (maxTokens <= 0) maxTokens = 400;

            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (var message = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    cts.CancelAfter(timeoutMs);
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    message.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                    message.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
                    message.Content = new StringContent(BuildBody(req, maxTokens), Encoding.UTF8,
                        "application/json");

                    using (HttpResponseMessage response = await Http.SendAsync(message,
                        HttpCompletionOption.ResponseHeadersRead, cts.Token))
                    {
                        int latency = (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                        int status = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode)
                            return FailureForStatus(status, latency);

                        long? declared = response.Content.Headers.ContentLength;
                        if (declared.HasValue && declared.Value > MaxResponseBytes)
                        {
                            cts.Cancel();
                            return LlmResponse.Failure(LlmErrorKind.Malformed, "response too large", latency);
                        }

                        BodyRead body = await ReadResponseBodyAsync(response, cts.Token);
                        if (body == null || body.TooLarge)
                        {
                            cts.Cancel();
                            return LlmResponse.Failure(LlmErrorKind.Malformed, "response too large", latency);
                        }
                        return ParseSuccess(body, latency, response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                int latency = (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                if (ct.IsCancellationRequested)
                    return LlmResponse.Failure(LlmErrorKind.Cancelled, "request cancelled", latency);
                return LlmResponse.Failure(LlmErrorKind.Timeout, "request timed out", latency);
            }
            catch (HttpRequestException)
            {
                return LlmResponse.Failure(LlmErrorKind.Network, "network error",
                    (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds));
            }
            catch (Exception)
            {
                // Deliberately no exception text: it can contain request internals.
                return LlmResponse.Failure(LlmErrorKind.Network, "transport failure",
                    (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds));
            }
        }

        private string BuildBody(LlmRequest req, int maxTokens)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"model\":").Append(MiniJson.Str(_config.Model));
            sb.Append(",\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":").Append(MiniJson.Str(req.System ?? "")).Append('}');
            sb.Append(",{\"role\":\"user\",\"content\":").Append(MiniJson.Str(req.User ?? "")).Append('}');
            sb.Append(']');
            sb.Append(",\"temperature\":0.2");
            sb.Append(",\"max_tokens\":").Append(MiniJson.Num(maxTokens));
            sb.Append(",\"stream\":false");
            if (_config.JsonMode)
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildEndpoint(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            string trimmed = baseUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri)) return null;
            // The key travels in a header, so a plain-http provider would expose it in transit.
            // Loopback HTTP is permitted only so the offline harness can use a local listener.
            bool loopbackHttp = uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri);
            if (uri.Scheme != Uri.UriSchemeHttps && !loopbackHttp) return null;

            string endpoint = trimmed.EndsWith("/chat/completions", StringComparison.Ordinal)
                ? trimmed
                : trimmed + "/chat/completions";
            return Uri.TryCreate(endpoint, UriKind.Absolute, out _) ? endpoint : null;
        }

        private static bool IsLoopbackHost(Uri uri)
        {
            if (!uri.IsLoopback) return false;
            string host = uri.Host;
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "::1";
        }

        private sealed class BodyRead
        {
            public byte[] Bytes;
            public string Text;
            public int ByteCount;
            public bool TooLarge;
            public string DecodeError;
        }

        private static async Task<BodyRead> ReadResponseBodyAsync(HttpResponseMessage response,
            CancellationToken ct)
        {
            BodyRead wire;
            using (Stream stream = await response.Content.ReadAsStreamAsync())
                wire = await ReadBoundedBytesAsync(stream, ct).ConfigureAwait(false);
            if (wire.TooLarge) return wire;

            string encoding = ContentEncodingName(response);
            if (encoding == null || string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                return DecodeBody(wire);

            bool gzip = string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase);
            bool deflate = string.Equals(encoding, "deflate", StringComparison.OrdinalIgnoreCase);
            if (!gzip && !deflate)
            {
                BodyRead unsupported = DecodeBody(wire);
                unsupported.DecodeError = "unsupported content-encoding";
                return unsupported;
            }

            try
            {
                using (var input = new MemoryStream(wire.Bytes, false))
                using (Stream decompressor = gzip
                    ? (Stream)new GZipStream(input, CompressionMode.Decompress)
                    : new DeflateStream(input, CompressionMode.Decompress))
                {
                    BodyRead decoded = await ReadBoundedBytesAsync(decompressor, ct).ConfigureAwait(false);
                    return decoded.TooLarge ? decoded : DecodeBody(decoded);
                }
            }
            catch (InvalidDataException)
            {
                BodyRead invalid = DecodeBody(wire);
                invalid.DecodeError = "invalid gzip/deflate body";
                return invalid;
            }
        }

        private static async Task<BodyRead> ReadBoundedBytesAsync(Stream stream, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using (var memory = new MemoryStream())
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (memory.Length + read > MaxResponseBytes)
                        return new BodyRead { TooLarge = true };
                    memory.Write(buffer, 0, read);
                }
                byte[] bytes = memory.ToArray();
                return new BodyRead { Bytes = bytes, ByteCount = bytes.Length };
            }
        }

        private static BodyRead DecodeBody(BodyRead bytes)
        {
            return new BodyRead
            {
                Bytes = bytes.Bytes,
                ByteCount = bytes.ByteCount,
                Text = Encoding.UTF8.GetString(bytes.Bytes),
            };
        }

        private static string ContentEncodingName(HttpResponseMessage response)
        {
            var encodings = response.Content.Headers.ContentEncoding;
            if (encodings == null || encodings.Count == 0) return null;
            return string.Join(",", encodings);
        }

        private static LlmResponse FailureForStatus(int status, int latency)
        {
            if (status == 401 || status == 403)
                return LlmResponse.Failure(LlmErrorKind.Auth, "authentication failed (HTTP " + status + ")", latency);
            if (status == 429)
                return LlmResponse.Failure(LlmErrorKind.RateLimited, "rate limited (HTTP 429)", latency);
            if (status >= 500)
                return LlmResponse.Failure(LlmErrorKind.Server, "provider error (HTTP " + status + ")", latency);
            return LlmResponse.Failure(LlmErrorKind.Network, "provider rejected request (HTTP " + status + ")", latency);
        }

        private static LlmResponse ParseSuccess(BodyRead body, int latency, HttpResponseMessage response)
        {
            string text = body == null ? null : body.Text;
            int byteCount = body == null ? 0 : body.ByteCount;
            if (body != null && !string.IsNullOrEmpty(body.DecodeError))
                return LlmResponse.Failure(LlmErrorKind.Malformed,
                    MalformedDetail(response, byteCount, body.DecodeError, text), latency);
            if (string.IsNullOrEmpty(text))
                return LlmResponse.Failure(LlmErrorKind.Malformed,
                    MalformedDetail(response, byteCount, "empty body", text), latency);

            // Provider envelopes may carry floats/exponents in fields we ignore (usage details,
            // logprobs); model output itself is still parsed by the strict LlmActionParser.
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode root, out string parseError)
                || !(root is JsonObject obj))
            {
                string step = parseError;
                if (string.IsNullOrEmpty(step))
                    step = root == null ? "unexpected end" : "root not object";
                return LlmResponse.Failure(LlmErrorKind.Malformed,
                    MalformedDetail(response, byteCount, step, text), latency);
            }

            var choices = Field(obj, "choices") as JsonArray;
            var choice = choices != null && choices.Count > 0 ? choices.Items[0] as JsonObject : null;
            var message = choice == null ? null : Field(choice, "message") as JsonObject;
            var content = message == null ? null : Field(message, "content") as JsonString;
            if (content == null)
                return LlmResponse.Failure(LlmErrorKind.Malformed,
                    MalformedDetail(response, byteCount, "no message content", text), latency);

            int? inputTokens = null;
            int? outputTokens = null;
            var usage = Field(obj, "usage") as JsonObject;
            if (usage != null)
            {
                inputTokens = ReadToken(usage, "prompt_tokens");
                outputTokens = ReadToken(usage, "completion_tokens");
            }

            string requestId = null;
            var id = Field(obj, "id") as JsonString;
            if (id != null) requestId = id.Value;
            if (requestId == null && response != null
                && response.Headers.TryGetValues("x-request-id", out var ids))
            {
                foreach (string value in ids) { requestId = value; break; }
            }

            return LlmResponse.Success(content.Value, latency, inputTokens, outputTokens, requestId);
        }

        private static JsonNode Field(JsonObject obj, string key)
        {
            return obj.TryGet(key, out JsonNode value) ? value : null;
        }

        private static int? ReadToken(JsonObject usage, string key)
        {
            var number = Field(usage, key) as JsonInt;
            if (number == null || number.Value < 0 || number.Value > int.MaxValue) return null;
            return (int)number.Value;
        }

        /// <summary>
        /// Compact, model-safe malformed-body description. It reports provider metadata and a short
        /// body prefix only; request headers (and therefore the bearer key) are never included.
        /// </summary>
        private static string MalformedDetail(HttpResponseMessage response, int byteCount,
            string parseStep, string body)
        {
            int status = response == null ? 0 : (int)response.StatusCode;
            string contentType = response == null || response.Content.Headers.ContentType == null
                ? "-" : response.Content.Headers.ContentType.ToString();
            string contentEncoding = response == null ? "-" : ContentEncodingName(response);
            if (string.IsNullOrEmpty(contentEncoding)) contentEncoding = "identity";

            string detail = "http=" + status.ToString(CultureInfo.InvariantCulture)
                + ";ct=" + SafeField(contentType, 16)
                + ";ce=" + SafeField(contentEncoding, 12)
                + ";n=" + byteCount.ToString(CultureInfo.InvariantCulture)
                + ";step=" + SafeField(parseStep, 17)
                + ";head=" + SafeBodyHead(body);
            return detail.Length <= 200 ? detail : detail.Substring(0, 200);
        }

        private static string SafeField(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0) return "-";
            var sb = new StringBuilder(Math.Min(value.Length, maxLength));
            foreach (char c in value)
            {
                if (sb.Length >= maxLength) break;
                sb.Append(c < ' ' || c > '~' ? '?' : c);
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        private static string SafeBodyHead(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            int length = Math.Min(120, body.Length);
            if (length > 0 && char.IsHighSurrogate(body[length - 1])) length--;
            string head = body.Substring(0, length);
            head = RedactToken(head, "sk-", false);
            head = RedactToken(head, "Bearer", true);
            head = RedactToken(head, "bearer", true);
            if (head.Length > 120) head = head.Substring(0, 120);

            // The body may be malformed precisely because it contains raw control characters; keep
            // the diagnostic printable so it cannot inject lines into a log or run record.
            var printable = new StringBuilder(head.Length);
            foreach (char c in head) printable.Append(c < ' ' || c == (char)0x7F ? '?' : c);
            return printable.ToString();
        }

        private static string RedactToken(string text, string marker, bool skipWhitespace)
        {
            int index = text.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                int end = index + marker.Length;
                if (skipWhitespace)
                {
                    while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
                }
                while (end < text.Length && IsSecretTokenChar(text[end])) end++;
                const string replacement = "[redacted]";
                text = text.Substring(0, index) + replacement + text.Substring(end);
                index = text.IndexOf(marker, index + replacement.Length, StringComparison.Ordinal);
            }
            return text;
        }

        private static bool IsSecretTokenChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'
                || c == '~' || c == '+' || c == '/' || c == '=';
        }

        private static HttpClient CreateClient()
        {
            // Redirects are never followed (a 3xx is a transport failure), and the client carries no
            // default headers: the bearer key exists only on the individual request message.
            // Decompression is explicit in ReadResponseBodyAsync so the malformed-body diagnostic can
            // still report the wire Content-Encoding and the 1 MiB cap applies to decoded bytes.
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }
}
