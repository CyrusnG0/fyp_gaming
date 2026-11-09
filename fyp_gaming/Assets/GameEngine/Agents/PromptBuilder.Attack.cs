using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in attack and capture prompt material. Kept in its own partial so the legacy prompt text,
    /// versions and hashes stay exactly as before when <see cref="LlmConfig.EnableAttack"/> is off.
    /// The candidate list itself is rendered by <see cref="ActionCatalogue"/>; this partial only owns
    /// the rules summary and the prompt identity.
    /// </summary>
    public static partial class PromptBuilder
    {
        /// <summary>Distinct identity for the opt-in attack loop (planning prompt + attack rules).</summary>
        public const string AttackPromptVersion = "demo-slice-6-attack-1";

        /// <summary>
        /// Appended to the planning system prompt when attacks are enabled. The numbers come from
        /// <see cref="BalanceConfig"/>, so the text cannot drift from the validator. It states no
        /// odds: combat is resolved by the engine only.
        /// </summary>
        public static string AttackAddendum()
        {
            string field = BalanceConfig.CommandPointCost(ActionType.AttackArmy).ToString(CultureInfo.InvariantCulture);
            string city = BalanceConfig.CommandPointCost(ActionType.AttackCity).ToString(CultureInfo.InvariantCulture);
            string ratio = BalanceConfig.AssaultTroopRatio.ToString(CultureInfo.InvariantCulture);
            string archer = BalanceConfig.ArcherAttackRange.ToString(CultureInfo.InvariantCulture);
            return "\n\nATTACK AND CAPTURE (opt-in; the engine is authoritative):\n" +
                   "- attack_army starts a field battle. target_id is your own army id; param_a and param_b are the x and z of an enemy army listed in CANDIDATE OPTIONS. It costs " + field + " command point(s). The target must be adjacent to the attacking army (archers reach " + archer + " hexes).\n" +
                   "- attack_city assaults an adjacent enemy city, resolved within the same season. target_id is your own army id; param_a and param_b are the x and z of a city listed in CANDIDATE OPTIONS. It costs " + city + " command points and needs at least " + ratio + " times the defending troops unless the army is a siege army.\n" +
                   "- Neutral cities and unowned watch towers are captured by march, not by attack_city. An enemy city cannot be entered by march.\n" +
                   "- Only attack a target that appears as an attack_army or attack_city option, with its listed coordinates. Do not invent a target.\n" +
                   "- The engine decides every battle. Results are not guaranteed: your army can lose troops and morale, be routed or be destroyed, and a routed army cannot attack. Compare troop numbers before attacking.\n" +
                   "- Attacking a faction you hold a treaty with breaks the treaty and costs trust and prestige.\n" +
                   "- A rejected attack may be reported as \"" + FeedbackPolicy.ArmyMoraleBroken + "\" or \"" + FeedbackPolicy.AssaultRatioTooLow + "\". Other rejections are reported generically.\n" +
                   "- For attack_army and attack_city, param_a and param_b are required integers copied from a listed option.";
        }

        public static string VersionFor(bool planning, bool inquiry, LlmPersona persona, bool attack)
        {
            if (_activeVersion == LegacyPromptVersion) return LegacyPromptVersion;
            if (_activeVersion == DomesticPromptVersion) return DomesticPromptVersion;
            if (attack && planning) return AttackPromptVersion;
            return VersionFor(planning, inquiry, persona);
        }

        /// <summary>
        /// Order: base planning rules, manual_lookup, attack rules, then the persona (so the advisory
        /// persona remains the last block). With <paramref name="attack"/> false this is byte-identical
        /// to the three-argument form.
        /// </summary>
        public static string SystemPromptFor(bool planning, bool inquiry, LlmPersona persona, bool attack)
        {
            if (_activeVersion == LegacyPromptVersion) return LegacySystem;
            if (_activeVersion == DomesticPromptVersion) return DomesticSystem;
            string text = planning ? System : DomesticSystem;
            if (inquiry) text += InquiryAddendum;
            if (attack && planning) text += AttackAddendum();
            if (persona != null) text += PersonaAddendum(persona);
            return text;
        }

        public static string SchemaHashFor(bool planning, bool inquiry, LlmPersona persona, bool attack)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(SystemPromptFor(planning, inquiry, persona, attack));
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);

            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
