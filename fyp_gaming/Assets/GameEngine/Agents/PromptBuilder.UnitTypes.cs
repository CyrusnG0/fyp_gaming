using System;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in unit-type recruitment text (<see cref="LlmConfig.EnableUnitTypes"/>). The block states
    /// what the four recruitable types cost and require in this match's balance profile, and what the
    /// existing rules say they do. It never says which type to recruit. Every number is read from
    /// <see cref="UnitTypes"/>, <see cref="BalanceConfig"/> or the profile, so the text cannot drift
    /// from the validator. It adds no city, army or opponent data. With the flag off none of this runs
    /// and the prompt text, version and hash are unchanged.
    /// </summary>
    public static partial class PromptBuilder
    {
        /// <summary>Sits after the assault-text suffix and before the garrison-conversion suffix.</summary>
        public const string UnitTypesVersionSuffix = "+unit-types-2";

        /// <summary>First unit-types identity. Its prompt still carried the base infantry-only recruit
        /// sentences, so new runs never use it; recorded requests that do are still read as unit-types.</summary>
        public const string LegacyUnitTypesVersionSuffix = "+unit-types-1";

        internal static bool EndsWithUnitTypesSuffix(string version, out int suffixLength)
        {
            suffixLength = 0;
            if (string.IsNullOrEmpty(version)) return false;
            if (version.EndsWith(UnitTypesVersionSuffix, StringComparison.Ordinal)) suffixLength = UnitTypesVersionSuffix.Length;
            else if (version.EndsWith(LegacyUnitTypesVersionSuffix, StringComparison.Ordinal)) suffixLength = LegacyUnitTypesVersionSuffix.Length;
            return suffixLength > 0;
        }

        public static bool IsUnitTypesPromptVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            if (IsGarrisonConversionPromptVersion(version))
                version = version.Substring(0, version.Length - (version.EndsWith(GarrisonConversionVersionSuffix, StringComparison.Ordinal)
                    ? GarrisonConversionVersionSuffix.Length : LegacyGarrisonConversionVersionSuffix.Length));
            return EndsWithUnitTypesSuffix(version, out _);
        }

        private const string BaseRecruitOrderText = "recruit (args.unit_type must be \"infantry\")";
        private const string BaseRecruitArgsText = "only recruit accepts it, with unit_type = \"infantry\".";

        /// <summary>
        /// The complete unit-types system prompt: the base text with its two infantry-only recruit
        /// statements replaced (not contradicted by a later override), then the UNIT TYPES block.
        /// Called after any action-reference block, which was built with the same flag.
        /// </summary>
        public static string ApplyUnitTypes(string system, BalanceProfile profile, bool attack,
            bool garrisonConversion, bool assaultRatioEnforced)
        {
            system = (system ?? "")
                .Replace(BaseRecruitOrderText, "recruit (args.unit_type: infantry, archer, cavalry or siege; see UNIT TYPES)")
                .Replace(BaseRecruitArgsText, "only recruit accepts it, with unit_type as listed under UNIT TYPES.");
            return system + UnitTypesAddendum(profile, attack, garrisonConversion, assaultRatioEnforced);
        }

        public static string UnitTypesAddendum(BalanceProfile profile, bool attack, bool garrisonConversion,
            bool assaultRatioEnforced)
        {
            if (profile == null) profile = BalanceProfile.Standard;
            int troops = BalanceConfig.RecruitTroopGain;
            var sb = new StringBuilder(2048);
            sb.Append("\n\nUNIT TYPES (opt-in; values are for balance profile \"").Append(profile.Key)
              .Append("\"; the engine is authoritative):\n");
            sb.Append("- recruit accepts args.unit_type \"infantry\", \"archer\", \"cavalry\" or \"siege\". For recruit " +
                      "only. If args is omitted the engine recruits infantry. Any other value is rejected.\n");
            sb.Append("- Each recruit order costs ").Append(Number(BalanceConfig.CommandPointCost(ActionType.Recruit)))
              .Append(" command point(s) and ").Append(Number(BalanceConfig.RecruitPopCost))
              .Append(" population, and adds ").Append(Number(troops))
              .Append(" troops to that city's garrison. Per type, for one order:\n");
            foreach (string type in UnitTypes.All)
            {
                int gate = UnitTypes.RequiredMilitaryLevel(type);
                sb.Append("  - ").Append(type).Append(": ").Append(Number(profile.RecruitGoldCost(type, troops)))
                  .Append(" gold; ").Append(gate > 0 ? "needs Military research level " + Number(gate) : "no research needed")
                  .Append("; ").Append(Number(troops)).Append(" troops eat ")
                  .Append(Decimal(profile.TroopFoodUpkeep(troops, type))).Append(" food per season; route cost allowance ")
                  .Append(Number(UnitTypes.MovePoints(type))).Append(" per season (before the winter multiplier).\n");
            }
            sb.Append("- A recruit order for a type whose research level you have not reached is rejected; your research " +
                      "levels are fields of your observation. CANDIDATE OPTIONS lists one recruit entry per type per city " +
                      "and marks which ones look affordable now; the engine revalidates every order.\n");
            sb.Append("- A city's garrison holds one unit type. While it has troops, recruit accepts only that type; an empty " +
                      "garrison takes the recruited type. ");
            sb.Append(garrisonConversion
                ? "convert_garrison turns garrison troops into a field army of the garrison's type, and an army only " +
                  "merges with troops of the same type.\n"
                : "This match has no order that turns a garrison into a field army.\n");
            if (attack) AppendUnitCombatFacts(sb, assaultRatioEnforced);
            return sb.ToString();
        }

        private static void AppendUnitCombatFacts(StringBuilder sb, bool assaultRatioEnforced)
        {
            // Field battle: the engine multiplies each side's own strength by its own factor, so the same
            // pair of types is stated from both sides. Siege never takes part in the counter cycle.
            sb.Append("- Field battle (attack_army): each side's strength is multiplied by its own type factor, so a matchup affects both armies. ")
              .Append("infantry vs archer: infantry x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Archer)))
              .Append(", archer x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Archer, UnitTypes.Infantry)))
              .Append("; cavalry vs infantry: cavalry x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Cavalry, UnitTypes.Infantry)))
              .Append(", infantry x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Cavalry)))
              .Append("; archer vs cavalry: archer x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Archer, UnitTypes.Cavalry)))
              .Append(", cavalry x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Cavalry, UnitTypes.Archer)))
              .Append("; same type x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Infantry)))
              .Append(" each. Siege is x").Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Siege, UnitTypes.Infantry)))
              .Append(" for itself in any field battle, attacking or defending, and an army fighting siege is x")
              .Append(Decimal(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Siege))).Append(".\n");
            sb.Append("- Other type effects: an attacking cavalry army also gets x").Append(Decimal(BalanceConfig.CavalryPlainsChargeBonus))
              .Append(" when the army it attacks stands on plains (the defender gets no charge bonus); infantry, attacking or defending, takes x")
              .Append(Decimal(BalanceConfig.InfantryLossTakenMult)).Append(" losses in field battles; archers may attack an army up to ")
              .Append(Number(UnitTypes.AttackRange(UnitTypes.Archer))).Append(" hexes away (other types ")
              .Append(Number(UnitTypes.AttackRange(UnitTypes.Infantry))).Append("); cavalry may march ")
              .Append(Number(BalanceConfig.CavalryMarchesPerSeason)).Append(" times per season.\n");
            sb.Append("- City assault (attack_city): a siege attacker is x").Append(Decimal(BalanceConfig.SiegeVsCityBonus))
              .Append(" against the city's defense; no other attacking type has a type factor there. A garrison of archers defends at x")
              .Append(Decimal(BalanceConfig.ArcherSiegeDefenseBonus)).Append("; no other garrison type has one.");
            if (assaultRatioEnforced)
                sb.Append(" A siege army is exempt from the minimum troop ratio for assaulting a city.");
            sb.Append('\n');
        }
    }
}
