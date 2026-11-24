using System;

namespace LuanShi.Engine
{
    public static partial class ReplayLoader
    {
        /// <summary>Upper bound on the provider reasoning text kept per request for the debug view.</summary>
        public const int MaxProviderReasoningChars = 24000;

        /// <summary>
        /// The provider's reasoning stream from a recorded chat-completion body
        /// (choices[0].message.reasoning_content), or null when the body is absent, truncated, not
        /// parseable, or carries none. Never throws.
        /// </summary>
        public static string ExtractProviderReasoning(string rawBody)
        {
            if (string.IsNullOrEmpty(rawBody)) return null;
            if (!StrictJson.TryParseLenientNumbers(rawBody, out JsonNode root, out _) || !(root is JsonObject obj)) return null;
            if (!obj.TryGet("choices", out JsonNode choices) || !(choices is JsonArray list) || list.Items.Count == 0) return null;
            if (!(list.Items[0] is JsonObject first) || !first.TryGet("message", out JsonNode message)
                || !(message is JsonObject msg)) return null;
            // The same two field names the live transport reads for the reasoning-character count.
            string text = GetString(msg, "reasoning_content", null);
            if (string.IsNullOrEmpty(text)) text = GetString(msg, "reasoning", null);
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length <= MaxProviderReasoningChars ? text : text.Substring(0, MaxProviderReasoningChars) + " [truncated]";
        }
    }
}
