using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    // ---- [DEMO-B5] provider-envelope decoding ---------------------------------

    private static void RunLlmHardeningB5()
    {
        RunB5ParserEscapes();
        RunB5HttpDecoding();
        RunB5MalformedDiagnostic();
    }

    private static void RunB5ParserEscapes()
    {
        Section("LLM provider envelope: escaped controls, UTF-8, large reasoning [DEMO-B5]");

        const string pretty = "{\n  \"type\": \"end_turn\"\n}";
        string reasoning = "analysis line 1\nline 2\r\n\tindented 漢字\n"
            + new string('漢', 6000) + new string('r', 12000);
        string body = B5Envelope(pretty, reasoning);

        Check(StrictJson.TryParseLenientNumbers(body, out JsonNode node, out string parseError)
              && node is JsonObject obj
              && obj.TryGet("choices", out JsonNode choicesNode)
              && choicesNode is JsonArray choices && choices.Count == 1
              && choices.Items[0] is JsonObject choice
              && choice.TryGet("message", out JsonNode messageNode)
              && messageNode is JsonObject message
              && message.TryGet("content", out JsonNode contentNode)
              && contentNode is JsonString content && content.Value == pretty,
            "[DEMO-B5] lenient provider parse accepts escaped CR/LF/TAB and multi-byte UTF-8"
            + (parseError == null ? "" : " (error: " + parseError + ")"));

        Check(reasoning.Length > 16384
              && StrictJson.TryParseLenientNumbers(body, out _, out _),
            "[DEMO-B5] a reasoning_content string larger than 16 KB is accepted");

        string escapedControls = "{\"x\":\"a\\u0000b\\u001Fc\\n\\t\\r\\b\\f\"}";
        Check(StrictJson.TryParseLenientNumbers(escapedControls, out JsonNode escapedNode, out _)
              && escapedNode is JsonObject escapedObj
              && escapedObj.TryGet("x", out JsonNode escapedValue)
              && escapedValue is JsonString escaped
              && escaped.Value == "a\0bc\n\t\r\b\f",
            "[DEMO-B5] escaped JSON control characters are decoded, including \\u0000");

        string rawControl = "{\"x\":\"a" + (char)0x1F + "b\"}";
        Check(!StrictJson.TryParseLenientNumbers(rawControl, out _, out string rawError)
              && rawError == "control character",
            "[DEMO-B5] a raw (unescaped) control character is still rejected");
    }

    private static void RunB5HttpDecoding()
    {
        Section("LLM HTTP transport: gzip, chunked, UTF-8, large reasoning [DEMO-B5]");
        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        const string pretty = "{\n  \"type\": \"reclaim\",\n  \"target_id\": \"city-a\"\n}";
        string reasoning = new string('思', 7000) + "\n" + new string('r', 12000);
        string body = B5Envelope(pretty, reasoning);

        B5HttpStub gzip = null;
        try
        {
            gzip = new B5HttpStub(body, gzip: true, chunked: true);
            LlmResponse response = B5Send(gzip);
            Check(response != null && response.Ok && response.Text == pretty
                  && response.InputTokens == 123 && response.OutputTokens == 456
                  && response.ProviderRequestId == "chatcmpl-b5",
                "[DEMO-B5] gzip + chunked provider response decodes to the original content");
            Check(gzip.Captured != null
                  && gzip.Captured.AcceptEncoding != null
                  && gzip.Captured.AcceptEncoding.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0,
                "[DEMO-B5] the transport advertises gzip/deflate via Accept-Encoding");
        }
        finally
        {
            if (gzip != null) gzip.Dispose();
        }

        B5HttpStub chunked = null;
        try
        {
            chunked = new B5HttpStub(body, gzip: false, chunked: true);
            LlmResponse response = B5Send(chunked);
            Check(response != null && response.Ok && response.Text == pretty,
                "[DEMO-B5] an uncompressed chunked response is read to its final zero-length chunk");
        }
        finally
        {
            if (chunked != null) chunked.Dispose();
        }
    }

    private static void RunB5MalformedDiagnostic()
    {
        Section("LLM HTTP transport: safe malformed-body diagnostic [DEMO-B5]");
        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        string malformed = "{\"error\":\"sk-test-DO-NOT-LEAK-B2-7f31c9 "
            + "Bearer abc.def.ghi\",\"choices\":[}";
        B5HttpStub stub = null;
        try
        {
            stub = new B5HttpStub(malformed, gzip: true, chunked: true);
            LlmResponse response = B5Send(stub);
            string detail = response == null ? null : response.ErrorDetail;
            Check(response != null && !response.Ok && response.Error == LlmErrorKind.Malformed
                  && detail != null && detail.Contains("http=200")
                  && detail.Contains("ct=application/json")
                  && detail.Contains("ce=gzip")
                  && detail.Contains("n=")
                  && detail.Contains("step=")
                  && detail.Contains("head="),
                "[DEMO-B5] malformed JSON details include status, content type/encoding, size and parser step");
            Check(detail != null && !detail.Contains(LlmFakeKey)
                  && !detail.Contains("Bearer") && !detail.Contains("Authorization")
                  && detail.Length <= 200,
                "[DEMO-B5] malformed-body details redact sk-/Bearer text and stay short");
        }
        finally
        {
            if (stub != null) stub.Dispose();
        }
    }

    private static string B5Envelope(string content, string reasoning)
    {
        var sb = new StringBuilder(512 + reasoning.Length + content.Length);
        sb.Append("{\"id\":\"chatcmpl-b5\",\"object\":\"chat.completion\",\"created\":1700000000,");
        sb.Append("\"model\":\"deepseek-reasoner\",\"choices\":[{\"index\":0,\"message\":{");
        sb.Append("\"role\":\"assistant\",\"content\":").Append(MiniJson.Str(content));
        sb.Append(",\"reasoning_content\":").Append(MiniJson.Str(reasoning));
        sb.Append("},\"logprobs\":null,\"finish_reason\":\"stop\"}],");
        sb.Append("\"usage\":{\"prompt_tokens\":123,\"completion_tokens\":456,\"total_tokens\":579,");
        sb.Append("\"prompt_cache_hit_tokens\":0,\"prompt_cache_miss_tokens\":123,");
        sb.Append("\"prompt_tokens_details\":{\"cached_tokens\":0,\"cost\":0.00000012},");
        sb.Append("\"completion_tokens_details\":{\"reasoning_tokens\":321,");
        sb.Append("\"cost\":0.00000034,\"rate\":1.5e-7}},");
        sb.Append("\"system_fingerprint\":null}");
        return sb.ToString();
    }

    private static LlmResponse B5Send(B5HttpStub stub)
    {
        var config = LlmKeyedConfig();
        config.BaseUrl = stub.BaseUrl;
        var transport = new OpenAiChatTransport(config);
        var request = new LlmRequest("SYSTEM", "USER", 123, 5000);
        LlmResponse response = transport.SendAsync(request, CancellationToken.None)
            .GetAwaiter().GetResult();
        stub.WaitServed(5000);
        return response;
    }

    private sealed class B5HttpCapture
    {
        public string Authorization;
        public string AcceptEncoding;
    }

    private sealed class B5HttpStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _payload;
        private readonly bool _gzip;
        private readonly bool _chunked;
        private readonly Task _serve;

        public string BaseUrl { get; }
        public B5HttpCapture Captured { get; private set; }
        public string Error { get; private set; }

        public B5HttpStub(string body, bool gzip, bool chunked)
        {
            _gzip = gzip;
            _chunked = chunked;
            byte[] raw = Encoding.UTF8.GetBytes(body ?? "");
            _payload = gzip ? B5Gzip(raw) : raw;

            int port = LlmFreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _serve = Task.Run(HandleAsync);
        }

        private async Task HandleAsync()
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                using (var reader = new StreamReader(context.Request.InputStream,
                    context.Request.ContentEncoding ?? Encoding.UTF8))
                    await reader.ReadToEndAsync();

                Captured = new B5HttpCapture
                {
                    Authorization = context.Request.Headers["Authorization"],
                    AcceptEncoding = context.Request.Headers["Accept-Encoding"],
                };

                HttpListenerResponse response = context.Response;
                response.StatusCode = 200;
                response.ContentType = "application/json; charset=utf-8";
                if (_gzip) response.Headers["Content-Encoding"] = "gzip";
                if (_chunked)
                {
                    response.SendChunked = true;
                }
                else
                {
                    response.ContentLength64 = _payload.Length;
                }

                // Deliberately tiny writes: chunk boundaries do not align with UTF-8 characters.
                const int chunk = 7;
                for (int offset = 0; offset < _payload.Length; offset += chunk)
                {
                    int count = Math.Min(chunk, _payload.Length - offset);
                    await response.OutputStream.WriteAsync(_payload, offset, count);
                    await response.OutputStream.FlushAsync();
                }
                response.Close();
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name;
            }
        }

        public void WaitServed(int milliseconds)
        {
            try { _serve.Wait(milliseconds); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch (Exception) { }
            try { _listener.Close(); } catch (Exception) { }
            WaitServed(1000);
        }

        private static byte[] B5Gzip(byte[] raw)
        {
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress, true))
                    gzip.Write(raw, 0, raw.Length);
                return memory.ToArray();
            }
        }
    }
}
