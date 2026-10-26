using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmRunnerHttp()
    {
        Section("LLM HTTP transport: loopback OpenAI-compatible path [DEMO-B2]");
        if (!LlmHttpProbe(out string why))
        {
            Console.WriteLine("  SKIP  System.Net.HttpListener unavailable: " + why);
            return;
        }

        const string success =
            "{\"id\":\"chatcmpl-test\",\"choices\":[{\"message\":{\"role\":\"assistant\","
            + "\"content\":\"{\\\"type\\\":\\\"end_turn\\\"}\"}}],"
            + "\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7}}";

        LlmHttpStub stub;
        LlmResponse response = LlmHttpOnce(200, success, 0, 5000, out stub);
        Check(response != null && response.Ok && response.Text == "{\"type\":\"end_turn\"}"
              && response.InputTokens == 11 && response.OutputTokens == 7
              && response.ProviderRequestId == "chatcmpl-test" && response.LatencyMs >= 0,
            "200 success parses content and usage tokens");
        LlmHttpCapture captured = stub.Captured;
        Check(captured != null && captured.Method == "POST"
              && captured.Path.EndsWith("/chat/completions", StringComparison.Ordinal),
            "POSTs exactly to {base_url}/chat/completions");
        Check(captured != null && captured.Authorization == "Bearer " + LlmFakeKey,
            "Bearer key is sent to the provider and is not logged");
        Check(captured != null && captured.Body.Contains("\"model\":\"http-model\"")
              && captured.Body.Contains("\"max_tokens\":123")
              && captured.Body.Contains("\"messages\":[{\"role\":\"system\"")
              && captured.Body.Contains("{\"role\":\"user\"")
              && captured.Body.Contains("\"response_format\":{\"type\":\"json_object\"}"),
            "request body carries model, system/user messages, max_tokens and JSON mode");
        stub.Dispose();

        response = LlmHttpOnce(401, "{\"error\":\"bad " + LlmFakeKey + "\"}", 0, 5000, out stub);
        Check(response != null && !response.Ok && response.Error == LlmErrorKind.Auth
              && !response.ErrorDetail.Contains(LlmFakeKey)
              && response.ErrorDetail.Length <= 200,
            "HTTP 401 => Auth with a short body-free detail");
        stub.Dispose();

        response = LlmHttpOnce(429, "{\"error\":\"slow down\"}", 0, 5000, out stub);
        Check(response != null && !response.Ok && response.Error == LlmErrorKind.RateLimited,
            "HTTP 429 => RateLimited");
        stub.Dispose();

        response = LlmHttpOnce(500, "{\"error\":\"boom\"}", 0, 5000, out stub);
        Check(response != null && !response.Ok && response.Error == LlmErrorKind.Server,
            "HTTP 500 => Server");
        stub.Dispose();

        response = LlmHttpOnce(200, "not-json", 0, 5000, out stub);
        Check(response != null && !response.Ok && response.Error == LlmErrorKind.Malformed,
            "malformed success body => Malformed");
        stub.Dispose();

        response = LlmHttpOnce(200, success, 400, 60, out stub);
        Check(response != null && !response.Ok && response.Error == LlmErrorKind.Timeout,
            "slow provider response => Timeout");
        stub.Dispose();

        Check(!success.Contains(LlmFakeKey), "HTTP stub fixture itself contains no key");
    }

    private static LlmResponse LlmHttpOnce(int status, string body, int delayMs, int timeoutMs,
        out LlmHttpStub stub, string location = null)
    {
        stub = new LlmHttpStub(status, body, delayMs, location);
        try
        {
            using (new LlmEnvScope(LlmFakeKey, "http-model", stub.BaseUrl))
            {
                var config = LlmConfig.LoadFromFile(null);
                config.MaxOutputTokens = 123;
                var transport = new OpenAiChatTransport(config);
                var request = new LlmRequest("SYSTEM-CONTENT", "USER-CONTENT", 123, timeoutMs);
                LlmResponse response = transport.SendAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();
                stub.WaitServed(4000);
                return response;
            }
        }
        finally
        {
            stub.WaitServed(4000);
        }
    }

    private static bool LlmHttpProbe(out string reason)
    {
        reason = null;
        HttpListener listener = null;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + LlmFreePort() + "/");
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
        finally
        {
            if (listener != null)
            {
                try { listener.Close(); } catch (Exception) { }
            }
        }
    }

    private static int LlmFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class LlmHttpCapture
    {
        public string Method;
        public string Path;
        public string Authorization;
        public string Body;
    }

    private sealed class LlmHttpStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly int _status;
        private readonly string _body;
        private readonly int _delayMs;
        private readonly string _location;
        private readonly Task _serve;
        private int _requests;

        public string BaseUrl { get; }
        public int RequestCount => Volatile.Read(ref _requests);
        public LlmHttpCapture Captured { get; private set; }
        public string Error { get; private set; }

        public LlmHttpStub(int status, string body, int delayMs, string location = null)
        {
            int port = LlmFreePort();
            _status = status;
            _body = body;
            _delayMs = delayMs;
            _location = location;
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
                Interlocked.Increment(ref _requests);
                string body;
                using (var reader = new StreamReader(context.Request.InputStream,
                    context.Request.ContentEncoding ?? Encoding.UTF8))
                    body = await reader.ReadToEndAsync();

                Captured = new LlmHttpCapture
                {
                    Method = context.Request.HttpMethod,
                    Path = context.Request.Url.AbsolutePath,
                    Authorization = context.Request.Headers["Authorization"],
                    Body = body,
                };

                if (_delayMs > 0) await Task.Delay(_delayMs);
                if (!string.IsNullOrEmpty(_location))
                    context.Response.Headers["Location"] = _location;
                byte[] bytes = Encoding.UTF8.GetBytes(_body ?? "");
                context.Response.StatusCode = _status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                context.Response.Close();
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
    }
}
