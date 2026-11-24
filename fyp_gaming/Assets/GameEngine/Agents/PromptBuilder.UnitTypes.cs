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
        public const string UnitTypesVersionSuffix = "+unit-types-1";

        public static bool IsUnitTypesPromptVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            if (IsGarrisonConversionPromptVersion(version))
                version = version.Substring(0, version.Length - (version.EndsWith(GarrisonConversionVersionSuffix, StringComparison.Ordinal)
                    ? GarrisonConversionVersionSuffix.Length : LegacyGarrisonConversionVersionSuffix.Length));
            return version.EndsWith(UnitTypesVersionSuffix, StringComparison.Ordinal);
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
                      "only, this overrides the base OUTPUT CONTRACT's infantry-only rule. If args is omitted the engine " +
                      "recruits infantry. Any other value is rejected.\n");
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
            sb.Append("- Unit rules in combat: infantry beats archer, cavalry beats infantry and archer beats cavalry " +
                      "(attacker's strength x").Append(Decimal(BalanceConfig.UnitCounterBonus))
              .Append(" against the type it beats, x").Append(Decimal(BalanceConfig.UnitCounterPenalty))
              .Append(" against the type that beats it). Siege is x").Append(Decimal(BalanceConfig.SiegeFieldPenalty))
              .Append(" in field battles and has no counter relation; against a city's defense siege is x")
              .Append(Decimal(BalanceConfig.SiegeVsCityBonus)).Append(". Archers may attack an army up to ")
              .Append(Number(UnitTypes.AttackRange(UnitTypes.Archer))).Append(" hexes away (other types ")
              .Append(Number(UnitTypes.AttackRange(UnitTypes.Infantry))).Append("), and an archer garrison defends its city at x")
              .Append(Decimal(BalanceConfig.ArcherSiegeDefenseBonus)).Append(". Cavalry may march ")
              .Append(Number(BalanceConfig.CavalryMarchesPerSeason)).Append(" times per season and attacks an army on plains at x")
              .Append(Decimal(BalanceConfig.CavalryPlainsChargeBonus)).Append(". Infantry takes x")
              .Append(Decimal(BalanceConfig.InfantryLossTakenMult)).Append(" losses in field battles.");
            if (assaultRatioEnforced)
                sb.Append(" A siege army is exempt from the minimum troop ratio for assaulting a city.");
            sb.Append('\n');
        }
    }
}
