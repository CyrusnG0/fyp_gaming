using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Clean seam for a future research/diplomacy **context** workstream. The provider is trusted
    /// host code, receives only an already seat-filtered observation, and returns text that the
    /// caller must label untrusted. This interface does not add a game action, a tool name or a
    /// rule: it cannot expose anything the engine has not already made visible to that seat.
    ///
    /// A provider must be deterministic, seat-filtered, and must not read canonical state,
    /// the filesystem, the network or another seat's memory.
    /// </summary>
    public interface ILlmDeliberationContextProvider
    {
        /// <summary>Stable, short identifier shown in evidence (never secrets or file paths).</summary>
        string ProviderId { get; }

        /// <summary>
        /// Bounded untrusted context for the active seat, or null when there is nothing to add.
        /// Callers may apply <see cref="LlmDeliberationExtensions.BoundContext"/> before display.
        /// </summary>
        string BuildUntrustedContext(Observation observation, IReadOnlyList<LlmMemory> memories);
    }

    /// <summary>
    /// Documented extension rules for the Codex/Luna research and diplomacy workstream. The
    /// production runner deliberately contains no research or diplomacy mechanics and no model
    /// action vocabulary beyond the engine's existing commands.
    ///
    /// To add a future **read-only tool**, the owning workstream must (in its own files):
    /// 1. add the stable tool name and its bounded fields to the strict request contract/parser;
    /// 2. add one read-only service method over the seat-filtered observation; and
    /// 3. add one dispatch case to <see cref="InvestigationDispatcher"/>.
    /// The production deliberation path then dispatches it automatically: it never trusts a
    /// model-supplied identity, budget or permission and it already bounds the serialized result.
    ///
    /// To add a future **game action**, the owning workstream must add it to the engine validator,
    /// <see cref="ActionCatalogue"/> and <see cref="LlmActionParser"/> first. The runner will not
    /// advertise or accept a command the engine does not have.
    /// </summary>
    public static class LlmDeliberationExtensions
    {
        /// <summary>Per-provider character cap for optional external context.</summary>
        public const int MaxContextCharsPerProvider = 2000;

        /// <summary>Hard cap on the number of context providers a host may register.</summary>
        public const int MaxContextProviders = 4;

        /// <summary>
        /// Bounds one provider's context for display: control characters become spaces and the
        /// text is clipped to <see cref="MaxContextCharsPerProvider"/>. The result is still data,
        /// never instructions.
        /// </summary>
        public static string BoundContext(string providerId, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new StringBuilder(Math.Min(text.Length, MaxContextCharsPerProvider));
            foreach (char c in text)
            {
                if (sb.Length >= MaxContextCharsPerProvider) break;
                sb.Append(char.IsControl(c) ? ' ' : c);
            }
            string clean = sb.ToString().Trim();
            if (clean.Length == 0) return null;
            string id = string.IsNullOrEmpty(providerId) ? "context" : providerId.Trim();
            return "[" + id + "] " + clean;
        }
    }
}
