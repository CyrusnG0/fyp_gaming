using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in per-country strategic persona prompt material. Kept in its own partial so the legacy
    /// prompt file stays small and the disabled text, version and hash remain exactly as before.
    /// </summary>
    public static partial class PromptBuilder
    {
        /// <summary>
        /// Distinct identity for the opt-in persona loop. It is selected whenever an advisory persona
        /// is active and no harness-level override is pinned.
        /// </summary>
        public const string PersonaPromptVersion = "demo-slice-5-persona";

        /// <summary>
        /// Appended to the system prompt when a persona is active.
        /// Advisory only; rules, observations and validation are unchanged.
        /// </summary>
        public static string PersonaAddendum(LlmPersona persona)
        {
            if (persona == null) return "";
            return "\n\nFACTION PERSONA (advisory goals and style only; rules, visibility and validation are unchanged):\n" +
                   "Persona ID: " + CleanLine(persona.Id) + "\n" +
                   persona.PromptText;
        }

        public static string VersionFor(bool planning, bool inquiry, bool personaEnabled)
        {
            if (_activeVersion == LegacyPromptVersion) return LegacyPromptVersion;
            if (_activeVersion == DomesticPromptVersion) return DomesticPromptVersion;
            if (personaEnabled || _activeVersion == PersonaPromptVersion) return PersonaPromptVersion;
            if (inquiry) return InquiryPromptVersion;
            return planning ? PromptVersion : DomesticPromptVersion;
        }

        public static string VersionFor(bool planning, bool inquiry, LlmPersona persona)
            => VersionFor(planning, inquiry, persona != null);

        public static string SystemPromptFor(bool planning, bool inquiry, LlmPersona persona)
        {
            if (_activeVersion == LegacyPromptVersion) return LegacySystem;
            if (_activeVersion == DomesticPromptVersion) return DomesticSystem;
            string text = planning ? System : DomesticSystem;
            if (inquiry) text += InquiryAddendum;
            if (persona != null) text += PersonaAddendum(persona);
            return text;
        }

        public static string SystemPromptFor(bool planning, LlmConfig config, string factionKey)
        {
            bool inquiry = config != null && config.EnableManualLookup;
            LlmPersona persona = LlmPersona.ResolveIfEnabled(config, factionKey);
            return SystemPromptFor(planning, inquiry, persona);
        }

        public static string SchemaHashFor(bool planning, bool inquiry, LlmPersona persona)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(SystemPromptFor(planning, inquiry, persona));
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);

            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string UserPrompt(Observation observation, IReadOnlyList<ActionOption> options,
            IReadOnlyList<string> recentHistory, int requestsLeft, int actionsLeft,
            LlmPersona persona)
            => UserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft, null, null, false, persona);

        public static string UserPrompt(Observation observation, IReadOnlyList<ActionOption> options,
            IReadOnlyList<string> recentHistory, int requestsLeft, int actionsLeft,
            MovementView movement, LlmPersona persona)
            => UserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft, movement, null, null, false, persona);
    }
}
