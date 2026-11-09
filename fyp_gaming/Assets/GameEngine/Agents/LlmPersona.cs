using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Optional turn-host capability that binds the acting seat's trusted faction key or name
    /// (for example "Shu" or "Wei") from authoritative match configuration so the runner can
    /// resolve a per-country <see cref="LlmPersona"/> when <see cref="LlmConfig.EnablePersonas"/>
    /// is enabled.
    /// </summary>
    public interface IPersonaHost : ITurnHost
    {
        string FactionKey { get; }
    }

    /// <summary>
    /// Per-country advisory persona for LLM governors.
    /// Provides stylistic direction without altering authoritative game rules, fog-of-war,
    /// or command validation boundaries.
    /// </summary>
    public sealed class LlmPersona
    {
        public const string ShuPersonaId = "shu-aggressive";
        public const string WeiPersonaId = "wei-mild";
        public const string DefaultPersonaId = "default-balanced";

        public const string ShuPromptText =
            "You are an ambitious, aggressive ruler. Prioritise expanding territory, " +
            "marching armies toward neutral and enemy cities, and seizing opportunities even " +
            "at some risk. Do not sit idle when a legal march can improve your position.";

        public const string WeiPromptText =
            "You are a cautious, defensive ruler. Prioritise food security, fortification, " +
            "troop training and steady growth. Avoid risky marches unless your food and " +
            "defences are secure.";

        public const string DefaultPromptText =
            "You are a balanced, pragmatic ruler. Weigh food security, city defence, troop " +
            "readiness and legal expansion according to your current observation and command budget.";

        public static readonly LlmPersona ShuAggressive =
            new LlmPersona(ShuPersonaId, "shu", "aggressive", ShuPromptText, false);

        public static readonly LlmPersona WeiMild =
            new LlmPersona(WeiPersonaId, "wei", "mild", WeiPromptText, false);

        public static readonly LlmPersona DefaultBalanced =
            new LlmPersona(DefaultPersonaId, "default", "balanced", DefaultPromptText, true);

        public string Id { get; }
        public string FactionKey { get; }
        public string Style { get; }
        public string PromptText { get; }
        public bool IsDefault { get; }

        public LlmPersona(string id, string factionKey, string style, string promptText, bool isDefault)
        {
            Id = id ?? "";
            FactionKey = factionKey ?? "";
            Style = style ?? "";
            PromptText = promptText ?? "";
            IsDefault = isDefault;
        }

        /// <summary>
        /// Resolves a known persona from a faction name, seat key, or persona style/id.
        /// Unknown or undefined factions safely default to <see cref="DefaultBalanced"/>.
        /// </summary>
        public static LlmPersona ForKey(string factionOrPersonaKey)
        {
            if (string.IsNullOrWhiteSpace(factionOrPersonaKey))
                return DefaultBalanced;

            string key = factionOrPersonaKey.Trim().ToLowerInvariant();
            if (key.StartsWith("seat-", StringComparison.Ordinal))
                key = key.Substring("seat-".Length).Trim();
            else if (key.StartsWith("faction-", StringComparison.Ordinal))
                key = key.Substring("faction-".Length).Trim();

            switch (key)
            {
                case "shu":
                case "蜀":
                case "shu-aggressive":
                case "aggressive":
                    return ShuAggressive;

                case "wei":
                case "魏":
                case "wei-mild":
                case "mild":
                    return WeiMild;

                case "default":
                case "default-balanced":
                case "balanced":
                    return DefaultBalanced;

                default:
                    return DefaultBalanced;
            }
        }

        /// <summary>
        /// Resolves a persona with optional configuration overrides.
        /// </summary>
        public static LlmPersona Resolve(string factionOrSeatKey, LlmConfig config = null)
        {
            if (config != null && !string.IsNullOrWhiteSpace(factionOrSeatKey)
                && config.TryGetPersonaOverride(factionOrSeatKey, out string overrideKey)
                && !string.IsNullOrWhiteSpace(overrideKey))
            {
                LlmPersona fromOverride = ForKey(overrideKey);
                if (!fromOverride.IsDefault || IsExplicitDefaultKey(overrideKey))
                    return fromOverride;
                return ForKey(factionOrSeatKey);
            }
            return ForKey(factionOrSeatKey);
        }

        /// <summary>
        /// Resolves a persona only when <see cref="LlmConfig.EnablePersonas"/> is true.
        /// Returns null when personas are disabled.
        /// </summary>
        public static LlmPersona ResolveIfEnabled(LlmConfig config, string factionOrSeatKey)
        {
            if (config == null || !config.EnablePersonas)
                return null;
            return Resolve(factionOrSeatKey, config);
        }

        /// <summary>
        /// Binds the acting seat's trusted faction to a persona for a turn.
        /// Evaluates config overrides, authoritative <see cref="IPersonaHost.FactionKey"/>,
        /// and seat-verified observation attributes in priority order.
        /// </summary>
        public static LlmPersona ResolveForTurn(LlmConfig config, ITurnHost host, Observation observation, string trustedSeatId)
        {
            if (config == null || !config.EnablePersonas)
                return null;

            if (!string.IsNullOrEmpty(trustedSeatId)
                && config.TryGetPersonaOverride(trustedSeatId, out string seatOverride)
                && !string.IsNullOrWhiteSpace(seatOverride))
            {
                LlmPersona selected = ForKey(seatOverride);
                if (!selected.IsDefault || IsExplicitDefaultKey(seatOverride))
                    return selected;
            }

            string factionKey = null;
            if (host is IPersonaHost personaHost && !string.IsNullOrWhiteSpace(personaHost.FactionKey))
            {
                factionKey = personaHost.FactionKey;
            }
            else if (observation != null && !string.IsNullOrEmpty(trustedSeatId)
                     && string.Equals(observation.SeatId, trustedSeatId, StringComparison.Ordinal))
            {
                factionKey = observation.Faction(trustedSeatId)?.Name;
            }

            return Resolve(factionKey, config);
        }

        private static bool IsExplicitDefaultKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            string k = key.Trim().ToLowerInvariant();
            if (k.StartsWith("seat-", StringComparison.Ordinal))
                k = k.Substring("seat-".Length).Trim();
            else if (k.StartsWith("faction-", StringComparison.Ordinal))
                k = k.Substring("faction-".Length).Trim();
            return k == "default" || k == "default-balanced" || k == "balanced";
        }
    }
}
