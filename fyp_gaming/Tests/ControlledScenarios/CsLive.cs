using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// A live run was requested without the explicit library/CLI gate. The message names the gate
    /// and contains no environment value or credential.
    /// </summary>
    public sealed class CsLiveRefusedException : Exception
    {
        public CsLiveRefusedException(string message) : base(message) { }
    }

    /// <summary>
    /// The live environment is absent or invalid. Messages name variables only; they never carry
    /// a value, a key or a provider URL.
    /// </summary>
    public sealed class CsLiveConfigException : Exception
    {
        public CsLiveConfigException(string message) : base(message) { }
    }

    /// <summary>Named live-only limits. The note is the fixed, non-negotiable reporting text.</summary>
    public static class CsLimits
    {
        public const int MaxOutputTokensCeiling = 16384;

        public const string LimitsNote =
            "Ceilings are request-count, wall-time and output-token limits only. They are NOT a "
            + "dollar or token-cost cap; usage and cost may be unknown or incomplete.";
    }

    /// <summary>Result of reading the process environment for a live run.</summary>
    public sealed class CsLiveEnvironmentResult
    {
        public string BaseUrl { get; internal set; }
        public string Model { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Reads and validates the three live variables. Only variable NAMES appear in diagnostics.
    /// The API key is never returned here; <see cref="LlmConfig.Load"/> is the only path that
    /// carries it to <see cref="OpenAiChatTransport"/>.
    /// </summary>
    public static class CsLiveEnvironment
    {
        public static readonly string[] EnvVarNames =
        {
            "LUANSHI_LLM_API_KEY", "LUANSHI_LLM_BASE_URL", "LUANSHI_LLM_MODEL",
        };

        /// <summary>Keys shorter than this are ignored for exact matching, so a tiny value cannot
        /// mangle ordinary text or reject every path. Empty values are always ignored.</summary>
        internal const int MinimumCredentialLength = 8;

        /// <summary>Fixed, value-free message for a configured key that cannot be redacted safely.</summary>
        internal const string KeyTooShortError =
            "LUANSHI_LLM_API_KEY is too short to be redacted safely (value not shown)";

        public static CsLiveEnvironmentResult Read(Func<string, string> getenv = null)
        {
            getenv ??= Environment.GetEnvironmentVariable;
            var result = new CsLiveEnvironmentResult();
            string key = Value(getenv, "LUANSHI_LLM_API_KEY");
            string baseUrl = Value(getenv, "LUANSHI_LLM_BASE_URL");
            string model = Value(getenv, "LUANSHI_LLM_MODEL");

            if (string.IsNullOrWhiteSpace(key)) result.Errors.Add("LUANSHI_LLM_API_KEY is not set");
            else if (key.Trim().Length < MinimumCredentialLength) result.Errors.Add(KeyTooShortError);
            if (string.IsNullOrWhiteSpace(model)) result.Errors.Add("LUANSHI_LLM_MODEL is not set");
            else result.Model = model.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
                result.Errors.Add("LUANSHI_LLM_BASE_URL is not set");
            else
            {
                string trimmed = baseUrl.Trim();
                string display = DisplayBaseUrl(trimmed);
                if (display == null)
                    result.Errors.Add("LUANSHI_LLM_BASE_URL must be an absolute https URL with no "
                        + "userinfo, query or fragment");
                else if (HasUnsafePath(trimmed, key))
                    result.Errors.Add("LUANSHI_LLM_BASE_URL must not carry a credential in its "
                        + "path (value not shown)");
                else result.BaseUrl = trimmed;
            }
            return result;
        }

        /// <summary>
        /// Rejects a configured but too-short key on live paths that build their own config without
        /// calling <see cref="Read"/> (for example a live provider override in RunScenario). An
        /// absent key is not rejected here; only a configured value that cannot be redacted.
        /// </summary>
        public static void ValidateConfiguredKey(Func<string, string> getenv = null)
        {
            getenv ??= Environment.GetEnvironmentVariable;
            string key = Value(getenv, "LUANSHI_LLM_API_KEY");
            if (!string.IsNullOrWhiteSpace(key) && key.Trim().Length < MinimumCredentialLength)
                throw new CsLiveConfigException(KeyTooShortError);
        }

        /// <summary>
        /// Resolves the manifest model. The literal "from-env" takes LUANSHI_LLM_MODEL; anything
        /// else must match it exactly. A mismatch is an error and never a silent override.
        /// </summary>
        public static string ResolveModel(string manifestModel, string environmentModel)
        {
            if (string.Equals(manifestModel, "from-env", StringComparison.Ordinal))
                return environmentModel;
            if (!string.Equals(manifestModel, environmentModel, StringComparison.Ordinal))
                throw new CsLiveConfigException(
                    "provider.model must be 'from-env' or exactly match LUANSHI_LLM_MODEL "
                    + "(model values are not printed)");
            return manifestModel;
        }

        /// <summary>
        /// scheme://host[:port] plus a path ONLY when every path segment is short, plain and not
        /// secret-like; otherwise scheme://host[:port]/... User info, query and fragment are never
        /// displayed, so a credential, token-like segment or query parameter cannot leak into
        /// evidence, settings or reports. The original value is never returned in an error.
        /// </summary>
        public static string DisplayBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri)) return null;
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)) return null;
            string root = uri.Scheme + "://" + uri.Authority;
            string path = uri.AbsolutePath;
            if (string.IsNullOrEmpty(path) || path == "/") return root + (path ?? "");
            return PathIsSafe(path) ? root + path : root + "/...";
        }

        /// <summary>
        /// True when a URL's path carries a credential: the configured key itself, a token-like
        /// segment (sk-..., key=..., user:pass, or a long plain token) or any over-long plain
        /// segment. Messages never echo the URL or the key.
        /// </summary>
        private static bool HasUnsafePath(string baseUrl, string key)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri)) return false;
            string path = uri.AbsolutePath ?? "";
            if (PathCarriesCredential(path, key)) return true;
            foreach (string segment in path.Split('/'))
                if (IsSecretLikeSegment(segment)) return true;
            return false;
        }

        /// <summary>
        /// True when the raw key, its percent-encoded form (either hex case) or the key recovered
        /// by one or two unescape passes appears anywhere in the path or a single segment. This
        /// catches a key smuggled through percent encoding or double encoding.
        /// </summary>
        private static bool PathCarriesCredential(string path, string key)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(key)) return false;
            bool plainKey = key.Length >= MinimumCredentialLength;
            if (plainKey && path.IndexOf(key, StringComparison.Ordinal) >= 0) return true;
            string encoded = Uri.EscapeDataString(key);
            if (ContainsPercentForm(path, encoded)) return true;
            string once = SafeUnescape(path), twice = SafeUnescape(once);
            if (plainKey && (once.IndexOf(key, StringComparison.Ordinal) >= 0
                    || twice.IndexOf(key, StringComparison.Ordinal) >= 0)) return true;
            if (ContainsPercentForm(once, encoded) || ContainsPercentForm(twice, encoded)) return true;
            foreach (string segment in path.Split('/'))
            {
                string first = SafeUnescape(segment), second = SafeUnescape(first);
                if (plainKey && (first.IndexOf(key, StringComparison.Ordinal) >= 0
                        || second.IndexOf(key, StringComparison.Ordinal) >= 0)) return true;
                if (ContainsPercentForm(segment, encoded)
                    || ContainsPercentForm(first, encoded)
                    || ContainsPercentForm(second, encoded)) return true;
            }
            return false;
        }

        /// <summary>Encoded comparison that tolerates upper- or lower-case percent hex digits.</summary>
        private static bool ContainsPercentForm(string value, string encoded)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(encoded)) return false;
            return value.IndexOf(encoded, StringComparison.Ordinal) >= 0
                || value.IndexOf(encoded, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SafeUnescape(string value)
        {
            try { return Uri.UnescapeDataString(value); }
            catch (Exception) { return value; }
        }

        private static bool PathIsSafe(string path)
        {
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0) continue;
                // Any percent-encoded byte could hide a key or an unprintable payload. Displaying
                // "%" would also expose an encoded credential, so it is never safe.
                if (segment.IndexOf('%') >= 0) return false;
                if (segment.Length >= 24 || IsSecretLikeSegment(segment)) return false;
                foreach (char c in segment)
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) return false;
            }
            return true;
        }

        private static bool IsSecretLikeSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return false;
            if (segment.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)) return true;
            if (segment.IndexOf("key=", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (segment.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (segment.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (segment.IndexOf(':') >= 0) return true;            // user:pass
            if (segment.Length >= 24)
            {
                bool plain = true;
                foreach (char c in segment)
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) { plain = false; break; }
                if (plain) return true;
            }
            return false;
        }

        private static string Value(Func<string, string> getenv, string name)
        {
            try { return getenv(name); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Live provider boundary. Construction reads no environment and makes no network call;
    /// <see cref="CreateTransport"/> returns the production transport built from the SAME
    /// <see cref="LlmConfig"/> the runner uses, which is how the key reaches it without ever
    /// being copied into evidence.
    /// </summary>
    public sealed class CsLiveScenarioProvider : IScenarioProvider
    {
        public CsLiveScenarioProvider(string model)
        {
            Model = model;
        }

        public string Kind => "openai-compatible";
        public string Model { get; }
        public bool IsLive => true;

        public ILlmTransport CreateTransport(CsProviderContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Config == null) throw new ArgumentException("live provider needs a config", nameof(context));
            return new OpenAiChatTransport(context.Config);
        }
    }
}
