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

        public static CsLiveEnvironmentResult Read(Func<string, string> getenv = null)
        {
            getenv ??= Environment.GetEnvironmentVariable;
            var result = new CsLiveEnvironmentResult();
            string key = Value(getenv, "LUANSHI_LLM_API_KEY");
            string baseUrl = Value(getenv, "LUANSHI_LLM_BASE_URL");
            string model = Value(getenv, "LUANSHI_LLM_MODEL");

            if (string.IsNullOrWhiteSpace(key)) result.Errors.Add("LUANSHI_LLM_API_KEY is not set");
            if (string.IsNullOrWhiteSpace(model)) result.Errors.Add("LUANSHI_LLM_MODEL is not set");
            else result.Model = model.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
                result.Errors.Add("LUANSHI_LLM_BASE_URL is not set");
            else
            {
                string display = DisplayBaseUrl(baseUrl.Trim());
                if (display == null)
                    result.Errors.Add("LUANSHI_LLM_BASE_URL must be an absolute https URL with no "
                        + "userinfo, query or fragment");
                else result.BaseUrl = baseUrl.Trim();
            }
            return result;
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
        /// scheme://host[:port]/path only. User info, query and fragment are never displayed, so a
        /// credential or query parameter cannot leak into evidence, settings or reports.
        /// </summary>
        public static string DisplayBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri)) return null;
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)) return null;
            return uri.Scheme + "://" + uri.Authority + uri.AbsolutePath;
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
