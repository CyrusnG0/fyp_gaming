using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in research order prompt material. <c>research</c> is a domestic-scope order with no
    /// target, so its addendum is appended to whatever system prompt the other opt-ins produced: it
    /// composes with manual_lookup, attacks, personas and deliberation instead of replacing them.
    /// Kept in its own partial so the disabled prompt text, versions, schema hash and user prompt
    /// stay byte-identical when <c>researchOrdersEnabled</c> is false.
    /// </summary>
    public static partial class PromptBuilder
    {
        /// <summary>Distinct identity for the opt-in research orders.</summary>
        public const string ResearchPromptVersion = "demo-slice-8-research-1";

        /// <summary>
        /// Distinct identity for research combined with production deliberation, so a run record can
        /// never confuse the two opt-in bundles.
        /// </summary>
        public const string DeliberationResearchPromptVersion =
            "demo-slice-8-deliberation-research-1";

        /// <summary>
        /// Appended to the system prompt when research orders are enabled. Every number comes from
        /// <see cref="BalanceConfig"/>, and the current levels, points and per-line prices are read
        /// from the seat's own observation and candidate list, so this text repeats no mutable value.
        /// Levels whose systems do not exist yet say "deferred" rather than promising an effect.
        /// </summary>
        public static string ResearchAddendum()
        {
            int maxLevel = BalanceConfig.ResearchMaxLevel;
            int trainingBase = BalanceConfig.TrainingCap(0);
            int trainingResearched = BalanceConfig.TrainingCap(1);
            int orderCost = BalanceConfig.CommandPointCost(ActionType.Research);
            return "\n\nRESEARCH ORDERS (opt-in; the engine is authoritative):\n" +
                   "- research advances one line by one level. The order is exactly " +
                   "{\"type\":\"research\",\"args\":{\"line\":\"<line>\"}}. For research only, this " +
                   "overrides the base OUTPUT CONTRACT's target_id requirement and recruit-only args " +
                   "rule: omit target_id entirely (target_id:null is invalid), omit param_a and param_b, " +
                   "and args contains only line. No target or coordinate is accepted.\n" +
                   "- line is one of agri (Agriculture), military (Military), espionage " +
                   "(Intelligence), governance (Administration).\n" +
                   "- Your own research_points and research levels are fields of your observation. " +
                   "Research is private: no other faction sees your levels or points, and you see none " +
                   "of theirs.\n" +
                   "- CANDIDATE OPTIONS lists every line you can order right now: each entry's label " +
                   "names its next level and research-point price, and its cost field is the " +
                   "command-point price. A line missing from that list is already at level " +
                   Number(maxLevel) + ", short of points, or you have no command points left. Do not " +
                   "order a line that is not listed.\n" +
                   "- The order is refused when the line is already at level " + maxLevel +
                   " or your research points are below that level's price. A rejected order costs " +
                   "nothing.\n" +
                   "- Research points arrive at every season rollover; governance level 4 raises that " +
                   "income from the next rollover.\n" +
                   "- What each level does today (a deferred level still counts as progress toward the " +
                   "next one):\n" +
                   "  agri 1: your cities' harvest +" + Percent(BalanceConfig.ResearchAgriFoodBonus) + ".\n" +
                   "  agri 2: DEFERRED (water works would halve drought damage; this demo has no " +
                   "drought events).\n" +
                   "  agri 3: DEFERRED (military farms would feed garrisons; the farm-garrison order is " +
                   "not implemented).\n" +
                   "  agri 4: DEFERRED (state granaries would raise a food cap; no food cap is " +
                   "modelled).\n" +
                   "  agri 5: all of your harvests +" + Percent(BalanceConfig.ResearchAgriL5FoodBonus) +
                   " on top of level 1.\n" +
                   "  military 1: training cap +" +
                   Number(BalanceConfig.ResearchMilTrainingCapBonus) + " (" + trainingBase + " becomes " +
                   trainingResearched + ").\n" +
                   "  military 2: cavalry can be recruited.\n" +
                   "  military 3: siege engines can be recruited, and a siege army waives the assault " +
                   "troop ratio.\n" +
                   "  military 4: your armies fight at +" +
                   Percent(BalanceConfig.ResearchMilCombatBonus) + " strength.\n" +
                   "  military 5: DEFERRED (no generals).\n" +
                   EspionageEffects() +
                   "  governance 1: your city tax +" + Percent(BalanceConfig.ResearchGovTaxBonus) + ".\n" +
                   "  governance 2: +" + Number(BalanceConfig.ResearchGovMoralePerSeason) +
                   " morale per season in every one of your cities.\n" +
                   "  governance 3: revolt chance x" + Decimal(BalanceConfig.ResearchGovRevoltMult) +
                   ", settled at the next rollover.\n" +
                   "  governance 4: research income +" + Percent(BalanceConfig.ResearchGovIncomeBonus) +
                   ", settled at the next rollover.\n" +
                   "  governance 5: +" + Number(BalanceConfig.ResearchGovPrestigeL5) +
                   " prestige once, when the level is bought.\n" +
                   "- research costs " + Number(orderCost) + " command point" +
                   (orderCost == 1 ? "" : "s") +
                   " and uses one request like any other order; end_turn is unchanged.";
        }

        /// <summary>Design §9's forged-reports level. A level index, not a tunable balance value.</summary>
        private const int ForgedReportsLevel = 3;

        /// <summary>
        /// The espionage levels as the engine actually implements them. <see cref="BalanceConfig.SpySuccessChance"/>
        /// gives every level the same per-level success bonus, so levels 3-5 are not simply "deferred":
        /// only their design-only extras are, and the shared bonus still applies. Unlock gates and the
        /// exact-report level are read from <see cref="BalanceConfig"/>; 4 and 5 are design §9 levels
        /// whose effects have no engine constant.
        /// </summary>
        private static string EspionageEffects()
        {
            int scoutGate = BalanceConfig.ScoutResearchLevel;
            int rumorGate = BalanceConfig.RumorResearchLevel;
            int sowGate = BalanceConfig.SowDistrustResearchLevel;
            int exactGate = BalanceConfig.ScoutExactAccuracyLevel;

            string text = "  espionage (every level, including 3-5): spy-mission success +"
                + PercentagePoints(BalanceConfig.SpySuccessPerLevel)
                + ", rolled by scout, spread rumour and sow distrust and capped at certainty.\n";
            if (scoutGate == rumorGate)
            {
                text += "  espionage " + Number(scoutGate)
                    + ": the scout and spread-rumour missions unlock.\n";
            }
            else
            {
                text += "  espionage " + Number(scoutGate) + ": the scout missions unlock.\n" +
                        "  espionage " + Number(rumorGate) + ": the spread-rumour missions unlock.\n";
            }
            text += "  espionage " + Number(sowGate)
                + ": the sow-distrust mission unlocks (the design's extra exposure bonus is DEFERRED).\n";
            text += "  espionage " + Number(exactGate)
                + ": scout reports become exact numbers instead of the few/some/many band";
            text += exactGate == ForgedReportsLevel
                ? " (forged reports are DEFERRED: they need per-seat poisoned intelligence).\n"
                : ".\n  espionage " + Number(ForgedReportsLevel)
                    + ": forged reports are DEFERRED (they need per-seat poisoned intelligence).\n";
            text += "  espionage 4: the design's spy-network agent slots are DEFERRED (no spy agents " +
                    "exist); the per-level success bonus still applies.\n";
            text += "  espionage 5: the design's counter-intelligence effect is DEFERRED; the per-level " +
                    "success bonus still applies.\n";
            return text;
        }

        /// <summary>
        /// Research wins over the attack/persona/inquiry identities, and research with deliberation
        /// keeps a third identity of its own. With <paramref name="researchOrdersEnabled"/> false this
        /// is exactly the deliberation-aware selection.
        /// </summary>
        public static string VersionFor(bool planning, bool inquiry, bool personaEnabled, bool attack,
            bool deliberation, bool researchOrdersEnabled)
        {
            if (_activeVersion == LegacyPromptVersion) return LegacyPromptVersion;
            if (_activeVersion == DomesticPromptVersion) return DomesticPromptVersion;
            if (!researchOrdersEnabled)
                return VersionFor(planning, inquiry, personaEnabled, attack, deliberation);
            return deliberation ? DeliberationResearchPromptVersion : ResearchPromptVersion;
        }

        public static string VersionFor(bool planning, bool inquiry, LlmPersona persona, bool attack,
            bool deliberation, bool researchOrdersEnabled)
            => VersionFor(planning, inquiry, persona != null, attack, deliberation,
                researchOrdersEnabled);

        /// <summary>
        /// Order: the existing blocks (base rules, manual_lookup, attacks, deliberation, persona),
        /// then the research rules. With <paramref name="researchOrdersEnabled"/> false the bytes are
        /// unchanged.
        /// </summary>
        public static string SystemPromptFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool researchOrdersEnabled)
        {
            if (!researchOrdersEnabled)
                return SystemPromptFor(planning, inquiry, persona, attack, deliberation);
            if (_activeVersion == LegacyPromptVersion) return LegacySystem;
            if (_activeVersion == DomesticPromptVersion) return DomesticSystem;
            return SystemPromptFor(planning, inquiry, persona, attack, deliberation) + ResearchAddendum();
        }

        public static string SchemaHashFor(bool planning, bool inquiry, LlmPersona persona, bool attack,
            bool deliberation, bool researchOrdersEnabled)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                SystemPromptFor(planning, inquiry, persona, attack, deliberation,
                    researchOrdersEnabled));
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);

            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string CombinedVersionFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool rich, bool research)
        {
            if (!research) return RichVersionFor(planning, inquiry, persona, attack, deliberation, rich);
            if (!rich) return VersionFor(planning, inquiry, persona, attack, deliberation, true);
            return "demo-slice-8-rich-research-1";
        }

        public static string CombinedSystemPromptFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool rich, bool research)
        {
            if (!research) return RichSystemPromptFor(planning, inquiry, persona, attack, deliberation, rich);
            if (!rich) return SystemPromptFor(planning, inquiry, persona, attack, deliberation, true);
            return RichSystemPromptFor(planning, inquiry, persona, attack, deliberation, true) + ResearchAddendum();
        }

        public static string CombinedSchemaHashFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool rich, bool research)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(CombinedSystemPromptFor(planning, inquiry, persona,
                attack, deliberation, rich, research));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Domestic (non-planning) user prompt with the research identity. With
        /// <paramref name="researchOrdersEnabled"/> false it is the existing 9-argument prompt.
        /// </summary>
        public static string UserPrompt(Observation observation, IReadOnlyList<ActionOption> options,
            IReadOnlyList<string> recentHistory, int requestsLeft, int actionsLeft,
            IReadOnlyList<LlmMemory> memories, string inquiryResult, bool inquiryEnabled,
            LlmPersona persona, bool researchOrdersEnabled)
        {
            if (!researchOrdersEnabled)
                return UserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                    memories, inquiryResult, inquiryEnabled, persona);

            string version = VersionFor(false, inquiryEnabled, persona, false, false, true);
            return BuildUserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                version, null, memories, inquiryResult, persona);
        }

        /// <summary>
        /// Planning/deliberation user prompt with the research identity. With
        /// <paramref name="researchOrdersEnabled"/> false it delegates to the deliberation-aware
        /// builder unchanged, so the prompt bytes and version are identical for every existing flag
        /// combination. When enabled the only content difference is the version line: the research
        /// rules and candidates already live in the system prompt and the option list. The size
        /// policy matches the deliberation builder but counts the research system prompt, so the
        /// addendum is included in the budget.
        /// </summary>
        public static string UserPrompt(Observation observation, IReadOnlyList<ActionOption> options,
            IReadOnlyList<string> recentHistory, int requestsLeft, int actionsLeft,
            MovementView movement, IReadOnlyList<LlmMemory> memories, string inquiryResult,
            bool inquiryEnabled, LlmPersona persona, bool attackEnabled, AttackView attacks,
            string strategicNote, IReadOnlyList<string> retainedInquiryResults,
            bool deliberationEnabled, bool researchOrdersEnabled)
        {
            if (!researchOrdersEnabled)
                return UserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                    movement, memories, inquiryResult, inquiryEnabled, persona, attackEnabled, attacks,
                    strategicNote, retainedInquiryResults, deliberationEnabled);

            string version = VersionFor(true, inquiryEnabled, persona, attackEnabled,
                deliberationEnabled, true);
            int systemLength = SystemPromptFor(true, inquiryEnabled, persona, attackEnabled,
                deliberationEnabled, true).Length;
            string attackNotice = attackEnabled ? ActionCatalogue.AttackNotice(attacks) : null;
            string note = deliberationEnabled ? strategicNote : null;
            IReadOnlyList<string> retained = deliberationEnabled ? retainedInquiryResults : null;

            string Render(string movementSection, IReadOnlyList<string> keptResults, string keptNote)
                => BuildUserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                    version, movementSection, memories, inquiryResult, persona, attackNotice,
                    keptResults, keptNote);
            bool Fits(string text) => systemLength + text.Length <= MaxPromptChars;

            if (movement == null)
            {
                string withSupplements = Render(null, retained, note);
                if (Fits(withSupplements)) return withSupplements;
                string noRetained = Render(null, null, note);
                if (Fits(noRetained)) return noRetained;
                return Render(null, null, null);
            }

            string full = Render(MovementOptionsText(movement), retained, note);
            if (Fits(full)) return full;

            // Same deterministic order as the deliberation builder: earlier retained results first,
            // then the note, then movement options per army and finally whole armies.
            string withoutRetained = Render(MovementOptionsText(movement), null, note);
            if (Fits(withoutRetained)) return withoutRetained;
            string withoutSupplements = Render(MovementOptionsText(movement), null, null);
            if (Fits(withoutSupplements)) return withoutSupplements;

            int max = 0;
            if (movement.Armies != null)
                foreach (ArmyMovePlan plan in movement.Armies)
                    if (plan != null && plan.Options != null && plan.Options.Count > max)
                        max = plan.Options.Count;

            for (int limit = max - 1; limit >= 0; limit--)
            {
                string candidate = Render(MovementOptionsText(movement, limit), null, note);
                if (Fits(candidate)) return candidate;
            }

            int totalArmies = movement.Armies == null ? 0 : movement.Armies.Count;
            for (int armies = totalArmies - 1; armies >= 0; armies--)
            {
                string candidate = Render(MovementOptionsText(movement, 1, armies), null, note);
                if (Fits(candidate)) return candidate;
            }
            for (int armies = totalArmies - 1; armies >= 0; armies--)
            {
                string candidate = Render(MovementOptionsText(movement, 0, armies), null, note);
                if (Fits(candidate)) return candidate;
            }
            return Render(MovementOptionsText(movement, 0, 0), null, note);
        }

        private static string Percent(float ratio)
            => Number((int)Math.Round(ratio * 100f)) + "%";

        private static string PercentagePoints(float ratio)
            => Number((int)Math.Round(ratio * 100f)) + " percentage points";

        private static string Decimal(float value)
            => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
