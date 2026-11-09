using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal sealed class StubServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _body;
    private readonly int _status;
    private readonly Task _serve;
    private int _count;
    private string _authorization;
    private string _requestBody;

    public string BaseUrl { get; }
    public int RequestCount => Volatile.Read(ref _count);
    public string LastAuthorization => Volatile.Read(ref _authorization);
    public string LastRequestBody => Volatile.Read(ref _requestBody);

    public StubServer(string body, int status = 200)
    {
        _body = body ?? "";
        _status = status;
        int port = FreePort();
        BaseUrl = "http://127.0.0.1:" + port;
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _serve = Task.Run(HandleAsync);
    }

    private async Task HandleAsync()
    {
        while (true)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            try
            {
                using (var reader = new StreamReader(context.Request.InputStream,
                    context.Request.ContentEncoding ?? Encoding.UTF8))
                    Volatile.Write(ref _requestBody, await reader.ReadToEndAsync());
                Volatile.Write(ref _authorization, context.Request.Headers["Authorization"]);
                Interlocked.Increment(ref _count);

                byte[] payload = Encoding.UTF8.GetBytes(_body);
                HttpListenerResponse response = context.Response;
                response.StatusCode = _status;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = payload.Length;
                await response.OutputStream.WriteAsync(payload, 0, payload.Length);
                response.Close();
            }
            catch (Exception)
            {
                try { context.Response.Abort(); } catch (Exception) { }
            }
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

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
