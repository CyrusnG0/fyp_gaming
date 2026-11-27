using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LuanShi.Engine;

// [RES-1b] The research prompt: identity, composition with the other opt-ins, and the size budget.
// Builder-level only. Every disabled combination must stay byte-identical to the existing prompt.

internal static partial class Harness
{
    private static void RunLlmResearchPrompt()
    {
        Section("[RES-1b] research prompt identity, composition and size budget");
        ResPromptIdentity();
        ResPromptEffectAccuracy();
        ResPromptComposition();
        ResPromptSizeBudget();
    }

    // ---- prompt identity ---------------------------------------------------

    private static void ResPromptIdentity()
    {
        string planning = PromptBuilder.SystemPromptFor(true);
        string withResearch = PromptBuilder.SystemPromptFor(true, false, null, false, false, true);
        ResCheck(PromptBuilder.SystemPromptFor(true, false, null, false, false, false) == planning,
            "research off is byte-identical to the existing planning prompt");
        ResCheck(withResearch == planning + PromptBuilder.ResearchAddendum()
           && withResearch.Length == planning.Length + PromptBuilder.ResearchAddendum().Length,
            "research on appends exactly the addendum to the unchanged base prompt");
        byte[] digest;
        using (SHA256 sha = SHA256.Create())
            digest = sha.ComputeHash(Encoding.UTF8.GetBytes(withResearch));
        string expectedHash = string.Concat(digest.Take(8)
            .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        ResCheck(PromptBuilder.SchemaHashFor(true, false, null, false, false, true) == expectedHash
           && PromptBuilder.SchemaHashFor(true, false, null, false, false, false)
               == PromptBuilder.SchemaHashFor(true, false, null, false, false),
            "the schema hash hashes the research system prompt and stays unchanged with research off");

        string domestic = PromptBuilder.SystemPromptFor(false, false, null, false, false, true);
        ResCheck(domestic == PromptBuilder.DomesticSystemPrompt() + PromptBuilder.ResearchAddendum(),
            "a non-planning host gets the domestic prompt plus the research addendum");

        var versions = new HashSet<string>(StringComparer.Ordinal)
        {
            PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, false, false),
            PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, true, false),
            PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, false, true),
            PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, true, true),
        };
        ResCheck(versions.Count == 4, "the four deliberation/research combinations have distinct identities");
        ResCheck(PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, false, true)
               == PromptBuilder.ResearchPromptVersion
           && PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, true, true)
               == PromptBuilder.DeliberationResearchPromptVersion
           && PromptBuilder.VersionFor(true, false, (LlmPersona)null, false, true, false)
               == PromptBuilder.DeliberationPromptVersion,
            "each combination returns its documented version constant");

        var addendum = PromptBuilder.ResearchAddendum();
        ResCheck(addendum.Contains("{\"type\":\"research\",\"args\":{\"line\":\"<line>\"}}")
           && addendum.Contains("omit target_id entirely (target_id:null is invalid)")
           && addendum.Contains("omit param_a and param_b")
           && addendum.Contains("recruit-only args rule") && addendum.Contains("args contains only line"),
            "the addendum states the exact command shape and overrides the base target/args rules");
        bool linesNamed = ResearchLine.All.All(line => addendum.Contains(line));
        bool labelsNamed = addendum.Contains("Agriculture") && addendum.Contains("Military")
            && addendum.Contains("Intelligence") && addendum.Contains("Administration");
        ResCheck(linesNamed && labelsNamed, "all four lines and their display names are listed");
        ResCheck(addendum.Contains("research_points") && addendum.Contains("CANDIDATE OPTIONS")
           && addendum.Contains("command-point price") && addendum.Contains("season rollover"),
            "the addendum points at the seat's own points, the priced candidates and the income timing");
        ResCheck(addendum.Contains("SCOUTING") && addendum.Contains("\"type\":\"scout\"")
           && addendum.Contains("enemy or neutral city") && addendum.Contains("never your own city"),
            "the addendum tells the model it can scout known enemy or neutral cities");

        bool deferred = addendum.Contains("agri 2: DEFERRED") && addendum.Contains("agri 3: DEFERRED")
            && addendum.Contains("agri 4: DEFERRED") && addendum.Contains("military 5: DEFERRED");
        ResCheck(deferred, "the unimplemented agri and military levels are labelled DEFERRED");
        bool implemented = addendum.Contains("harvest +10%") && addendum.Contains("training cap +1")
            && addendum.Contains("cavalry can be recruited") && addendum.Contains("siege engines")
            && addendum.Contains("city tax +10%") && addendum.Contains("revolt chance x0.5")
            && addendum.Contains("research income +25%") && addendum.Contains("prestige once");
        ResCheck(implemented, "the implemented effects are stated with authoritative values");
    }

    /// <summary>
    /// The effect table must match what the engine implements today. The espionage line in particular
    /// carries a real per-level success bonus (BalanceConfig.SpySuccessChance) and a real exact-report
    /// level, so 3-5 cannot be described as simply "deferred".
    /// </summary>
    private static void ResPromptEffectAccuracy()
    {
        var addendum = PromptBuilder.ResearchAddendum();
        int orderCost = BalanceConfig.CommandPointCost(ActionType.Research);
        int perLevel = (int)Math.Round(BalanceConfig.SpySuccessPerLevel * 100f);
        int agriPercent = (int)Math.Round(BalanceConfig.ResearchAgriFoodBonus * 100f);
        int agriL5Percent = (int)Math.Round(BalanceConfig.ResearchAgriL5FoodBonus * 100f);
        int combatPercent = (int)Math.Round(BalanceConfig.ResearchMilCombatBonus * 100f);
        int taxPercent = (int)Math.Round(BalanceConfig.ResearchGovTaxBonus * 100f);
        int incomePercent = (int)Math.Round(BalanceConfig.ResearchGovIncomeBonus * 100f);
        string revolt = BalanceConfig.ResearchGovRevoltMult.ToString("0.##", CultureInfo.InvariantCulture);

        ResCheck(addendum.Contains("- research costs " + orderCost + " command point"
                   + (orderCost == 1 ? "" : "s") + " and uses one request")
               && !addendum.Contains("one command point"),
            "the command-point price comes from BalanceConfig with correct singular/plural wording");

        ResCheck(addendum.Contains("espionage (every level, including 3-5): spy-mission success +"
                   + perLevel + " percentage points")
               && addendum.Contains("scout, spread rumour and sow distrust")
               && addendum.Contains("capped at certainty"),
            "every espionage level states the implemented success bonus instead of 'deferred'");

        ResCheck(addendum.Contains("espionage " + BalanceConfig.SowDistrustResearchLevel
                   + ": the sow-distrust mission unlocks")
               && addendum.Contains("extra exposure bonus is DEFERRED"),
            "the sow-distrust gate is read from BalanceConfig and its extra effect stays deferred");

        ResCheck(addendum.Contains("espionage " + BalanceConfig.ScoutExactAccuracyLevel
                   + ": scout reports become exact numbers")
               && addendum.Contains("few/some/many band")
               && addendum.Contains("forged reports are DEFERRED"),
            "espionage 3 states its real exact-report effect, with forged reports deferred");

        ResCheck(addendum.Contains("espionage 4: the design's spy-network agent slots are DEFERRED")
               && addendum.Contains("espionage 5: the design's counter-intelligence effect is DEFERRED")
               && addendum.Contains("the per-level success bonus still applies"),
            "espionage 4 and 5 keep the shared success bonus and promise no missing subsystem");

        ResCheck(!addendum.Contains("espionage 3: DEFERRED")
               && !addendum.Contains("espionage 4: DEFERRED")
               && !addendum.Contains("espionage 5: DEFERRED"),
            "the old 'espionage N is deferred' misdescription is gone");

        ResCheck(addendum.Contains("your cities' harvest +" + agriPercent + "%")
               && addendum.Contains("all of your harvests +" + agriL5Percent + "%")
               && addendum.Contains("training cap +" + BalanceConfig.ResearchMilTrainingCapBonus)
               && addendum.Contains("your armies fight at +" + combatPercent + "%")
               && addendum.Contains("your city tax +" + taxPercent + "%")
               && addendum.Contains("morale per season in every one of your cities")
               && addendum.Contains("revolt chance x" + revolt)
               && addendum.Contains("research income +" + incomePercent + "%"),
            "agri, military and governance wording still matches the implemented effects");
    }

    private static void ResPromptComposition()
    {
        var engine = ResWorld(9709);
        Observation observation = engine.Observe("seat-a");
        AttackView attacks = AttackPlanner.Build(engine, "seat-a");
        LlmPersona persona = LlmPersona.WeiMild;

        string attackOnly = PromptBuilder.SystemPromptFor(true, false, persona, true, false, false);
        string attackResearch = PromptBuilder.SystemPromptFor(true, false, persona, true, false, true);
        ResCheck(attackResearch == attackOnly + PromptBuilder.ResearchAddendum()
           && attackResearch.Contains("ATTACK AND CAPTURE")
           && attackResearch.Contains("FACTION PERSONA")
           && attackResearch.Contains("RESEARCH ORDERS"),
            "research composes with attacks and personas without disturbing their blocks");

        string deliberation = PromptBuilder.SystemPromptFor(true, false, persona, true, true, false);
        string both = PromptBuilder.SystemPromptFor(true, false, persona, true, true, true);
        ResCheck(both == deliberation + PromptBuilder.ResearchAddendum()
           && both.Contains("OPTIONAL DELIBERATION")
           && both.Contains("RESEARCH ORDERS")
           && both.IndexOf("OPTIONAL DELIBERATION", StringComparison.Ordinal)
               < both.IndexOf("RESEARCH ORDERS", StringComparison.Ordinal),
            "research composes with deliberation and stays the last rules block");

        string inquiry = PromptBuilder.SystemPromptFor(true, true, null, false, false, true);
        ResCheck(inquiry.Contains("manual_lookup") && inquiry.Contains("RESEARCH ORDERS"),
            "research composes with the manual_lookup addendum");

        // User prompt: the research identity is the only change, and only the version line differs.
        List<ActionOption> researchOptions = ActionCatalogue.Build(observation, attacks, true);
        string plain = PromptBuilder.UserPrompt(observation, researchOptions, new List<string>(), 6, 5,
            (MovementView)null, null, null, false, persona, true, attacks, null, null, false);
        string research = PromptBuilder.UserPrompt(observation, researchOptions, new List<string>(), 6,
            5, (MovementView)null, null, null, false, persona, true, attacks, null, null, false, true);
        string expectedVersion = "Prompt version: " + PromptBuilder.ResearchPromptVersion + "\n";
        string plainVersion = "Prompt version: " + PromptBuilder.AttackPromptVersion + "\n";
        ResCheck(plain.Contains(plainVersion)
           && research.Contains(expectedVersion)
           && research.Replace(expectedVersion, plainVersion) == plain,
            "the research user prompt differs from the attack prompt only in the version line");

        string deliberationResearch = PromptBuilder.UserPrompt(observation,
            ActionCatalogue.Build(observation, attacks, true), new List<string>(), 6, 5,
            (MovementView)null, null, null, false, persona, true, attacks, "note", null, true, true);
        ResCheck(deliberationResearch.Contains("Prompt version: "
               + PromptBuilder.DeliberationResearchPromptVersion + "\n")
           && deliberationResearch.Contains("STRATEGIC NOTE"),
            "deliberation plus research keeps its own version and the note block");

        string off = PromptBuilder.UserPrompt(observation, researchOptions, new List<string>(), 6, 5,
            (MovementView)null, null, null, false, persona, true, attacks, null, null, false, false);
        ResCheck(off == plain, "the disabled research wrapper returns the existing prompt bytes");

        string domesticPlain = PromptBuilder.UserPrompt(observation, researchOptions, new List<string>(),
            6, 5, null, null, false, persona);
        string domesticResearch = PromptBuilder.UserPrompt(observation, researchOptions,
            new List<string>(), 6, 5, null, null, false, persona, true);
        ResCheck(domesticResearch.Contains("Prompt version: " + PromptBuilder.ResearchPromptVersion + "\n")
           && domesticResearch.Replace("Prompt version: " + PromptBuilder.ResearchPromptVersion + "\n",
                "Prompt version: " + PromptBuilder.PersonaPromptVersion + "\n") == domesticPlain,
            "the domestic research prompt keeps the persona version line as its only difference");
    }

    private static void ResPromptSizeBudget()
    {
        var engine = ResWorld(9710);
        Observation observation = engine.Observe("seat-a");
        List<ActionOption> options = ActionCatalogue.Build(observation, null, true);
        var movement = MovementPlanner.Build(engine, "seat-a");
        var note = "NOTEPAD " + new string('n', 120);
        int systemPlain = PromptBuilder.SystemPromptFor(true, false, null, false, true, false).Length;
        int systemResearch = PromptBuilder.SystemPromptFor(true, false, null, false, true, true).Length;

        // Pad one retained result so the plain prompt only just fits its own budget.
        var one = new List<string> { "RETAINEDPAD" };
        string probe = PromptBuilder.UserPrompt(observation, options, new List<string>(), 6, 5,
            movement, null, null, false, null, false, null, note, one, true);
        int room = PromptBuilder.MaxPromptChars - (systemPlain + probe.Length) - 1;
        ResCheck(room > PromptBuilder.ResearchAddendum().Length,
            "the fixture leaves room to show the addendum changes the budget");
        var retained = new List<string> { "RETAINEDPAD" + new string('r', Math.Max(0, room)) };

        string plain = PromptBuilder.UserPrompt(observation, options, new List<string>(), 6, 5,
            movement, null, null, false, null, false, null, note, retained, true);
        string research = PromptBuilder.UserPrompt(observation, options, new List<string>(), 6, 5,
            movement, null, null, false, null, false, null, note, retained, true, true);

        ResCheck(plain.Length + systemPlain <= PromptBuilder.MaxPromptChars
           && plain.Contains("RETAINEDPAD"),
            "the same fixture fits without the addendum and keeps the retained result");
        ResCheck(research.Length + systemResearch <= PromptBuilder.MaxPromptChars,
            "with research the prompt still fits the cap including the addendum");
        ResCheck(!research.Contains("RETAINEDPAD") && research.Contains("NOTEPAD"),
            "the addendum is counted, so the retained result is dropped before the note");
    }
}
