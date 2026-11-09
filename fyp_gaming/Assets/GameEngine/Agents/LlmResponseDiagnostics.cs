using System;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>How the provider's <c>message.content</c> looked after reception.</summary>
    public enum LlmContentState
    {
        /// <summary>No successful provider response, or no content field to classify.</summary>
        Unknown,
        /// <summary>Content contains something other than whitespace.</summary>
        Present,
        /// <summary>Content is an empty string or null.</summary>
        Empty,
        /// <summary>Content contains only whitespace/newlines.</summary>
        WhitespaceOnly,
    }

    /// <summary>
    /// Read-only classification of one received response. This is deliberately separate from the
    /// action parser: it never executes, repairs or reinterprets model text. It exists so the run
    /// record and the empty-response decision can use provider evidence (finish reason, output
    /// budget and reasoning usage) instead of guessing from the text alone.
    /// </summary>
    public static class LlmResponseDiagnostics
    {
        public static LlmContentState ContentState(LlmResponse response)
        {
            if (response == null || !response.Ok || response.Text == null)
                return LlmContentState.Unknown;
            if (response.Text.Length == 0)
                return LlmContentState.Empty;
            return response.Text.Trim().Length == 0
                ? LlmContentState.WhitespaceOnly
                : LlmContentState.Present;
        }

        public static bool ContentIsEmpty(LlmResponse response)
        {
            LlmContentState state = ContentState(response);
            return state == LlmContentState.Empty || state == LlmContentState.WhitespaceOnly;
        }

        public static bool FinishReasonIsLength(LlmResponse response)
            => response != null
               && string.Equals(response.FinishReason, "length", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the provider either said "length" or reported exactly the requested completion
        /// budget. Both are evidence that the answer, if any, was cut off.
        /// </summary>
        public static bool ReachedOutputBudget(LlmResponse response, int maxOutputTokens)
        {
            if (response == null) return false;
            if (FinishReasonIsLength(response)) return true;
            return maxOutputTokens > 0 && response.OutputTokens.HasValue
                && response.OutputTokens.Value >= maxOutputTokens;
        }

        /// <summary>
        /// Evidence that a separate reasoning/thinking stream consumed the completion budget:
        /// the budget was reached, the answer is empty, and the provider reported reasoning usage.
        /// This is a provider-accounting signal, not a description of the model's internal thought.
        /// </summary>
        public static bool ReasoningConsumedBudget(LlmResponse response, int maxOutputTokens)
        {
            if (!ContentIsEmpty(response) || !ReachedOutputBudget(response, maxOutputTokens))
                return false;
            return (response.ReasoningTokens.HasValue && response.ReasoningTokens.Value > 0)
                || (response.ReasoningContentChars.HasValue && response.ReasoningContentChars.Value > 0);
        }

        /// <summary>
        /// True when retrying at the same output cap cannot be expected to produce an answer: the
        /// provider hit the length limit and returned no content. The runner stops instead of
        /// spending another request on the same doomed cap.
        /// </summary>
        public static bool IsBudgetExhaustedEmpty(LlmResponse response, int maxOutputTokens)
            => ContentIsEmpty(response) && ReachedOutputBudget(response, maxOutputTokens);

        /// <summary>
        /// Short, safe diagnostic for logs and research records. It reports metadata only and never
        /// echoes model or game text. Unknown values are omitted rather than guessed.
        /// </summary>
        public static string Describe(LlmResponse response, int maxOutputTokens)
        {
            if (response == null) return "no response";
            if (!response.Ok)
                return "provider error " + response.Error
                    + (string.IsNullOrEmpty(response.ErrorDetail) ? "" : ": " + response.ErrorDetail);

            var sb = new StringBuilder(160);
            switch (ContentState(response))
            {
                case LlmContentState.Empty: sb.Append("empty response"); break;
                case LlmContentState.WhitespaceOnly: sb.Append("whitespace-only response"); break;
                default: sb.Append("response"); break;
            }

            sb.Append(" (finish_reason=");
            // Do not truncate arbitrary provider text into a diagnostic: truncating before
            // redaction could leave an unrecognisable fragment of a credential.
            sb.Append(string.IsNullOrEmpty(response.FinishReason) ? "unknown"
                : FinishReasonIsLength(response) ? "length"
                : string.Equals(response.FinishReason, "stop", StringComparison.OrdinalIgnoreCase)
                    ? "stop" : "other");
            sb.Append(", completion_tokens=");
            if (response.OutputTokens.HasValue)
            {
                sb.Append(response.OutputTokens.Value);
                sb.Append('/');
                sb.Append(maxOutputTokens > 0 ? maxOutputTokens.ToString() : "unknown");
            }
            else
            {
                sb.Append("unknown");
            }
            if (response.ReasoningTokens.HasValue)
                sb.Append(", reasoning_tokens=").Append(response.ReasoningTokens.Value);
            if (response.ReasoningContentChars.HasValue)
                sb.Append(", reasoning_content_chars=").Append(response.ReasoningContentChars.Value);
            sb.Append(')');
            return sb.Length <= 240 ? sb.ToString() : sb.ToString(0, 240);
        }

        /// <summary>
        /// Redacts credentials before captured text is written to local evidence. The API key is
        /// supplied explicitly; common bearer/sk- token shapes are also removed defensively. This
        /// does not make arbitrary captured game/model text trusted: it is still stored as data.
        /// </summary>
        public static string RedactForCapture(string text, string apiKey)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string redacted = text;
            if (!string.IsNullOrEmpty(apiKey))
                redacted = redacted.Replace(apiKey, "[redacted]");
            redacted = RedactToken(redacted, "sk-", false);
            redacted = RedactToken(redacted, "Bearer", true);
            return redacted;
        }

        private static string SafeField(string value)
        {
            var sb = new StringBuilder(Math.Min(value.Length, 24));
            foreach (char c in value)
            {
                if (sb.Length >= 24) break;
                sb.Append(c < ' ' || c > '~' ? '?' : c);
            }
            return sb.Length == 0 ? "unknown" : sb.ToString();
        }

        private static string RedactToken(string text, string marker, bool skipWhitespace)
        {
            int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
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
                index = text.IndexOf(marker, index + replacement.Length, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }

        private static bool IsSecretTokenChar(char c)
            => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'
               || c == '~' || c == '+' || c == '/' || c == '=';
    }
}
