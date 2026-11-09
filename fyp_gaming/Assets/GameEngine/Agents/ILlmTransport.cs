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
    ///
    /// <see cref="RawResponse"/> is the exact body received from the provider (when one was read).
    /// It may contain untrusted model text and must only be written to local, ignored run evidence;
    /// it is never part of ErrorDetail, Status or any model-facing feedback.
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
        /// <summary>Provider-reported reasoning/thinking tokens, or null when not supplied.</summary>
        public int? ReasoningTokens;
        /// <summary>Provider-reported total tokens, or null when not supplied.</summary>
        public int? TotalTokens;
        /// <summary>Provider finish reason ("stop", "length", ...), or null when not supplied.</summary>
        public string FinishReason;
        /// <summary>Length of a separate message.reasoning_content field, or null when absent.</summary>
        public int? ReasoningContentChars;
        /// <summary>HTTP status when a provider response was received; null for local failures.</summary>
        public int? HttpStatus;
        /// <summary>Exact received body. Untrusted; local capture only, never model feedback.</summary>
        public string RawResponse;
        public string ProviderRequestId;

        public static LlmResponse Success(string text, int latencyMs, int? inputTokens,
            int? outputTokens, string providerRequestId, string finishReason = null,
            int? reasoningTokens = null, int? totalTokens = null,
            int? reasoningContentChars = null, string rawResponse = null, int? httpStatus = null)
        {
            return new LlmResponse
            {
                Ok = true,
                Text = text ?? "",
                Error = LlmErrorKind.None,
                LatencyMs = latencyMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ReasoningTokens = reasoningTokens,
                TotalTokens = totalTokens,
                FinishReason = finishReason,
                ReasoningContentChars = reasoningContentChars,
                HttpStatus = httpStatus,
                RawResponse = rawResponse,
                ProviderRequestId = providerRequestId,
            };
        }

        public static LlmResponse Failure(LlmErrorKind error, string detail, int latencyMs = 0,
            string rawResponse = null, int? httpStatus = null)
        {
            return new LlmResponse
            {
                Ok = false,
                Error = error == LlmErrorKind.None ? LlmErrorKind.Network : error,
                ErrorDetail = Shorten(detail),
                LatencyMs = latencyMs,
                HttpStatus = httpStatus,
                RawResponse = rawResponse,
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
