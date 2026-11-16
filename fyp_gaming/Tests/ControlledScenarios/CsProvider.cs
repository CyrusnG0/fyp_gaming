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
    /// Selection gate. This task accepts "fake" only: a live kind is refused loudly instead of
    /// silently falling back or making a paid call. See <see cref="StatusText"/> for the exact,
    /// smallest next step to add a live provider.
    /// </summary>
    public static class CsProviderFactory
    {
        public const string LiveRefusal =
            "provider kind 'openai-compatible' is refused: this build makes no live call and has not "
            + "exercised or validated a real provider. The smallest exact next step is documented by "
            + "'provider-status': add CsLiveScenarioProvider.CreateTransport returning "
            + "new OpenAiChatTransport(context.Config), require an explicit --allow-live, and select "
            + "provider.kind=\"openai-compatible\". No engine, runner, prompt or transport edit is needed.";

        public static IScenarioProvider Create(string kind, string model)
        {
            if (string.Equals(kind, "fake", StringComparison.Ordinal))
                return new CsFakeScenarioProvider(model);
            if (string.Equals(kind, "openai-compatible", StringComparison.Ordinal)
                || string.Equals(kind, "live", StringComparison.Ordinal))
                throw new NotSupportedException(LiveRefusal);
            throw new NotSupportedException("unknown provider kind '" + kind + "'; this build accepts only 'fake'");
        }

        /// <summary>
        /// Exact provider-status text. It must remain honest: fake is available; a real connection
        /// is possible via the public OpenAiChatTransport but is NOT exercised or validated here.
        /// </summary>
        public const string StatusText =
            "LuanShi controlled scenarios - provider status\n" +
            "fake: AVAILABLE. No network call, no credential read. Replies are labelled fixtures\n" +
            "  (source=fake_scripted) with usage_source=fake_deterministic. They are plumbing\n" +
            "  evidence, never model behaviour, and no test treats a scripted choice as a pass.\n" +
            "real provider: NOT exercised or validated here.\n" +
            "  A real provider connection is possible today via the public OpenAiChatTransport\n" +
            "  (already an ILlmTransport) with no production edit. Exact next step from design\n" +
            "  section 4: add CsLiveScenarioProvider.CreateTransport returning\n" +
            "  new OpenAiChatTransport(context.Config), constructed from LlmConfig.LoadFromFile(path)\n" +
            "  (or LlmConfig.Load()), which reads LUANSHI_LLM_API_KEY / LUANSHI_LLM_BASE_URL /\n" +
            "  LUANSHI_LLM_MODEL; require an explicit --allow-live; select\n" +
            "  provider.kind=\"openai-compatible\". No engine/runner/prompt/transport change is needed.\n" +
            "  This build makes no live call, performs no paid call, and does not claim live\n" +
            "  validation or real-provider readiness.\n" +
            "No credential is read or printed by this command.";
    }
}
