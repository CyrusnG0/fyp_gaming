using System.Threading;
using System.Threading.Tasks;

namespace LuanShi.Engine
{
    /// <summary>Stable failure classes for the runner and the run record. None means success.</summary>
    public enum LlmErrorKind
    {
        None,
        Timeout,
        RateLimited,
        Auth,
        Server,
        Network,
        Malformed,
        Cancelled,
        BudgetExhausted,
    }

    /// <summary>One provider-neutral chat request. The host builds it on the game thread.</summary>
    public sealed class LlmRequest
    {
        public string System;
        public string User;
        public int MaxOutputTokens;
        public int TimeoutMs;

        public LlmRequest() { }

        public LlmRequest(string system, string user, int maxOutputTokens, int timeoutMs)
        {
            System = system;
            User = user;
            MaxOutputTokens = maxOutputTokens;
            TimeoutMs = timeoutMs;
        }
    }

    /// <summary>
    /// Provider-neutral response. ErrorDetail is deliberately short and never contains the API key
    /// or a response-body echo; null token counts mean the provider did not report usage.
    /// </summary>
    public sealed class LlmResponse
    {
        public bool Ok;
        public string Text;
        public LlmErrorKind Error;
        public string ErrorDetail;
        public int LatencyMs;
        public int? InputTokens;
        public int? OutputTokens;
        public string ProviderRequestId;

        public static LlmResponse Success(string text, int latencyMs, int? inputTokens,
            int? outputTokens, string providerRequestId)
        {
            return new LlmResponse
            {
                Ok = true,
                Text = text ?? "",
                Error = LlmErrorKind.None,
                LatencyMs = latencyMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ProviderRequestId = providerRequestId,
            };
        }

        public static LlmResponse Failure(LlmErrorKind error, string detail, int latencyMs = 0)
        {
            return new LlmResponse
            {
                Ok = false,
                Error = error == LlmErrorKind.None ? LlmErrorKind.Network : error,
                ErrorDetail = Shorten(detail),
                LatencyMs = latencyMs,
            };
        }

        internal static string Shorten(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return null;
            string one = detail.Replace('\r', ' ').Replace('\n', ' ');
            return one.Length <= 200 ? one : one.Substring(0, 200);
        }
    }

    /// <summary>
    /// The only provider-I/O boundary. SendAsync may run on a background task; it must not touch
    /// the game engine, the seat observation or any Unity object.
    /// </summary>
    public interface ILlmTransport
    {
        string Provider { get; }
        string Model { get; }
        Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct);
    }
}
