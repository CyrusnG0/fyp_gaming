using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Provider boundary from design section 4. Adding a real provider must be a new implementation
    /// of this interface, never a change to the engine, runner, prompt builder or transport.
    /// </summary>
    public interface IScenarioProvider
    {
        string Kind { get; }          // "fake" | "openai-compatible"
        string Model { get; }
        bool IsLive { get; }
        ILlmTransport CreateTransport(CsProviderContext context);
    }

    public sealed class CsProviderContext
    {
        public string RunDirectory { get; init; }
        public string ScenarioId { get; init; }
        public int Seed { get; init; }
        public LlmConfig Config { get; init; }
        public Func<string> ActiveSeatId { get; init; }            // session.ActiveSeatId
        public IReadOnlyList<CsScriptStep> Script { get; init; }   // study seat's fake script
        public IReadOnlyDictionary<string, IReadOnlyList<CsScriptStep>> Scripts { get; init; }
    }

    /// <summary>
    /// Offline fake provider. It makes no network call and reads no credential. Its replies are
    /// fixtures labelled <c>source=fake_scripted</c>; they are plumbing evidence, not model behaviour.
    /// </summary>
    public sealed class CsFakeScenarioProvider : IScenarioProvider
    {
        public CsFakeScenarioProvider(string model)
        {
            Model = string.IsNullOrWhiteSpace(model) ? "fake-model" : model;
        }

        public string Kind => "fake";
        public string Model { get; }
        public bool IsLive => false;

        public ILlmTransport CreateTransport(CsProviderContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var scripts = new Dictionary<string, List<CsScriptStep>>(StringComparer.Ordinal);
            if (context.Scripts != null)
                foreach (KeyValuePair<string, IReadOnlyList<CsScriptStep>> kv in context.Scripts)
                    scripts[kv.Key] = kv.Value == null ? new List<CsScriptStep>() : new List<CsScriptStep>(kv.Value);
            if (scripts.Count == 0 && context.Script != null && context.ActiveSeatId != null)
                scripts["__active__"] = new List<CsScriptStep>(context.Script);
            return new CsSeatTransport(Model, context.ActiveSeatId, scripts);
        }
    }

    /// <summary>
    /// Selection gate. A live kind is PREPARED and offline-tested, but it is only returned when
    /// the caller explicitly allows it; otherwise the refusal is loud. There is no silent
    /// live-to-fake fallback and an unknown kind always throws.
    /// </summary>
    public static class CsProviderFactory
    {
        public const string LiveRefusal =
            "provider kind 'openai-compatible' is refused without --allow-live: a live run is "
            + "PREPARED and offline-tested, but this build has NOT exercised or validated a network "
            + "provider and makes no live call unless the explicit flag is passed. No environment "
            + "value or credential is read at this point.";

        public static IScenarioProvider Create(string kind, string model, bool allowLive = false)
        {
            if (string.Equals(kind, "fake", StringComparison.Ordinal))
                return new CsFakeScenarioProvider(model);
            if (string.Equals(kind, "openai-compatible", StringComparison.Ordinal))
            {
                if (!allowLive) throw new CsLiveRefusedException(LiveRefusal);
                return new CsLiveScenarioProvider(model);
            }
            throw new NotSupportedException("unknown provider kind '" + kind
                + "'; accepted kinds are 'fake' and 'openai-compatible'");
        }

        /// <summary>
        /// Exact provider-status text. It must remain honest: fake is available; the live kind is
        /// prepared and offline-tested but NOT exercised against a network. Reading this constant
        /// never reads an environment variable.
        /// </summary>
        public static readonly string StatusText =
            "LuanShi controlled scenarios - provider status\n" +
            "fake: AVAILABLE. No network call, no credential read. Replies are labelled fixtures\n" +
            "  (source=fake_scripted) with usage_source=fake_deterministic. They are plumbing\n" +
            "  evidence, never model behaviour, and no test treats a scripted choice as a pass.\n" +
            "openai-compatible: PREPARED and offline-tested; NOT exercised or validated against a\n" +
            "  network. Selection returns CsLiveScenarioProvider only with --allow-live; its\n" +
            "  transport is new OpenAiChatTransport(context.Config), where the same LlmConfig the\n" +
            "  runner uses comes from LlmConfig.Load(). No engine, runner, prompt or transport edit\n" +
            "  is needed. The live kind reads these environment variable NAMES (values are never\n" +
            "  printed): LUANSHI_LLM_API_KEY, LUANSHI_LLM_BASE_URL, LUANSHI_LLM_MODEL.\n" +
            "  A live run also requires run_ceilings.max_output_tokens (1.." +
            CsLimits.MaxOutputTokensCeiling + "). This build makes no live call and no paid call\n" +
            "  unless --allow-live is passed, and does not claim live validation or real-provider\n" +
            "  readiness.\n" +
            "Limits: " + CsLimits.LimitsNote + "\n" +
            "No credential is read or printed by this command.";
    }
}
