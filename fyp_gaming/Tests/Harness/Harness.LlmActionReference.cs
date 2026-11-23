using System;
using System.IO;
using System.Linq;
using LuanShi.Engine;

// [ACT-REF] Full action reference prompt material (opt-in, default off).
//
// The reference documents every order the model may emit, with the engine's equations and a
// one-line example, plus the season-resolution context. These checks prove the disabled path is
// byte-identical to the existing prompt, the enabled path advertises only parser vocabulary, and
// the run identity composes with the other opt-ins.

internal static partial class Harness
{
    private static void RunLlmActionReference()
    {
        Section("[ACT-REF] full action reference prompt material (opt-in)");
        ActionReferenceOffIsByteIdentical();
        ActionReferenceContent();
        ActionReferenceIdentity();
        ActionReferenceConfigAndRunner();
    }

    private static void RefCheck(bool ok, string name) => Check(ok, "[ACT-REF] " + name);

    // ---- 1: disabled path is byte-identical ---------------------------------

    private static void ActionReferenceOffIsByteIdentical()
    {
        RefCheck(PromptBuilder.CombinedSystemPromptFor(true, false, null, false, false, false, false,
                     false, false, false)
                 == PromptBuilder.CombinedSystemPromptFor(true, false, null, false, false, false, false,
                     false, false),
            "the disabled reference leaves the base system prompt byte-identical");
        RefCheck(PromptBuilder.CombinedSystemPromptFor(true, false, null, true, false, false, false,
                     false, false, false)
                 == PromptBuilder.CombinedSystemPromptFor(true, false, null, true, false, false, false,
                     false, false),
            "the disabled reference leaves the attack system prompt byte-identical");
        RefCheck(PromptBuilder.CombinedVersionFor(true, false, null, true, false, false, false, false,
                     false, false)
                 == PromptBuilder.CombinedVersionFor(true, false, null, true, false, false, false, false,
                     false),
            "the disabled reference leaves the attack identity unchanged");
        RefCheck(PromptBuilder.CombinedSchemaHashFor(true, false, null, true, false, false, false,
                     false, false, false)
                 == PromptBuilder.CombinedSchemaHashFor(true, false, null, true, false, false, false,
                     false, false),
            "the disabled reference leaves the attack schema hash unchanged");
    }

    // ---- 2: enabled text is complete and honest ----------------------------

    private static void ActionReferenceContent()
    {
        string sys = PromptBuilder.CombinedSystemPromptFor(true, false, null, false, false, false,
            false, false, false, true);

        RefCheck(sys.Contains("ACTION REFERENCE (every order in this build"),
            "the enabled system prompt carries the reference header");
        RefCheck(sys.Contains("levy_tax") && sys.Contains("lighten_labor") && sys.Contains("reclaim")
                 && sys.Contains("recruit") && sys.Contains("train") && sys.Contains("fortify")
                 && sys.Contains("relief") && sys.Contains("march") && sys.Contains("end_turn"),
            "every always-available order is named");
        RefCheck(sys.Contains("attack_army") && sys.Contains("attack_city")
                 && sys.Contains("research") && sys.Contains("send_message"),
            "the opt-in orders are named without claiming they are always available");
        RefCheck(sys.Contains("NOT available in this build: convert_garrison, garrison_army, gift, "
                              + "declare_war, break_treaty, scout, rumor, sow_distrust"),
            "engine-only orders are explicitly marked unavailable");
        RefCheck(sys.Contains("gold +" + BalanceConfig.TaxLevyAmount)
                 && sys.Contains("morale -" + BalanceConfig.TaxLevyMoraleCost)
                 && sys.Contains("cap +" + BalanceConfig.ReclaimPctCap + "%")
                 && sys.Contains("population become +" + BalanceConfig.RecruitTroopGain),
            "the domestic numbers come from BalanceConfig");
        RefCheck(sys.Contains("20/40/70/110/160"),
            "the research level prices match ResearchLevelCosts");
        RefCheck(sys.Contains("There is no minimum troop ratio"),
            "the assault reference states the removed ratio rule is gone");
        RefCheck(!sys.Contains("3:1") && !sys.Contains("assault ratio is"),
            "no stale 3:1 assault wording is reintroduced");
        RefCheck(sys.Contains("Spring x0.7") && sys.Contains("Winter x0.3")
                 && sys.Contains("halves its food and gold yields"),
            "the season-resolution context carries the harvest and morale rules");
        RefCheck(sys.Contains("garrison_strength = garrison x") && sys.Contains("strength = troops x"),
            "the combat equations are present");
        RefCheck(sys.Contains("e.g. {\"type\":\"levy_tax\"")
                 && sys.Contains("e.g. {\"type\":\"march\"")
                 && sys.Contains("e.g. {\"type\":\"attack_city\"")
                 && sys.Contains("e.g. {\"type\":\"end_turn\""),
            "each order group shows an example reply");
    }

    // ---- 3: identity composes with the other opt-ins -----------------------

    private static void ActionReferenceIdentity()
    {
        RefCheck(PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false,
                     false, false, true) == PromptBuilder.ActionReferencePromptVersion,
            "alone, the reference claims its own prompt identity");
        RefCheck(PromptBuilder.CombinedVersionFor(true, false, null, true, false, false, false,
                     false, false, true) == PromptBuilder.AttackPromptVersion + "+actions",
            "with attacks on, the reference appends +actions instead of replacing the attack identity");
        RefCheck(PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false,
                     false, true, true) == PromptBuilder.DiplomacyPromptVersion + "+actions",
            "with diplomacy on, both identities compose");
        RefCheck(PromptBuilder.IsDiplomacyPromptVersion(
                     PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false,
                         false, true, true))
                 && PromptBuilder.IsDiplomacyPromptVersion("demo-slice-9-diplomacy-1+actions")
                 && PromptBuilder.IsDiplomacyPromptVersion("demo-slice-4+diplomacy+actions"),
            "replay still recognises a diplomacy run recorded with the reference enabled");
        RefCheck(PromptBuilder.CombinedVersionFor(false, false, null, false, false, false, false,
                     false, false, true) == PromptBuilder.DomesticPromptVersion
                 && PromptBuilder.CombinedSystemPromptFor(false, false, null, false, false, false,
                     false, false, false, true)
                    == PromptBuilder.CombinedSystemPromptFor(false, false, null, false, false, false,
                        false, false, false),
            "a domestic-only host keeps its prompt and version unchanged");

        string on = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false, false,
            false, false, true);
        string off = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false, false,
            false, false, false);
        RefCheck(on.Length == 16 && on != off
                 && on == PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false,
                     false, false, false, true),
            "the enabled schema hash is stable and differs from the disabled one");
    }

    // ---- 4: config key and runner integration ------------------------------

    private static void ActionReferenceConfigAndRunner()
    {
        string dir = LlmTempDir("action-ref");
        try
        {
            string path = Path.Combine(dir, "llm.local.json");
            File.WriteAllText(path, "{\"enable_action_reference\":true}");
            var enabled = LlmConfig.LoadFromFile(path);
            RefCheck(enabled.EnableActionReference
                     && enabled.Redacted().Contains("action_reference=true"),
                "the enable_action_reference key parses and appears in the redacted diagnostic");
            var disabled = new LlmConfig();
            RefCheck(!disabled.EnableActionReference
                     && !disabled.Redacted().Contains("action_reference"),
                "the reference is off by default and adds no diagnostic text");
        }
        finally { LlmDelete(dir); }

        var engine = MovementFixture(9601, 1, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, new LlmConfig
        {
            MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, EnableActionReference = true,
        }, null);
        runner.Begin(host, host.Token, "seat-a", 2);
        LlmDrain(runner);

        RefCheck(fake.CallCount == 1
                 && fake.Requests[0].System.Contains("ACTION REFERENCE (every order in this build"),
            "an enabled runner sends the reference in the system prompt");
        RefCheck(fake.Requests[0].User.Contains(
                     "Prompt version: " + PromptBuilder.ActionReferencePromptVersion),
            "the user prompt's identity line matches the run record");

        var engine2 = MovementFixture(9602, 1, UnitTypes.Infantry);
        var host2 = new LlmPlanningHost(engine2, "seat-a");
        var fake2 = new FakeLlmTransport();
        fake2.EnqueueSuccess(W2EndTurn());
        var runner2 = new LlmTurnRunner(fake2, new LlmConfig
        {
            MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2,
        }, null);
        runner2.Begin(host2, host2.Token, "seat-a", 2);
        LlmDrain(runner2);

        RefCheck(!fake2.Requests[0].System.Contains("ACTION REFERENCE")
                 && fake2.Requests[0].User.Contains("Prompt version: demo-slice-4"),
            "a default runner keeps the unchanged demo-slice-4 prompt");
    }
}
