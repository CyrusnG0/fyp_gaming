using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [RES-1] Opt-in research orders: the strict parser, the seat-safe catalogue and the prompt identity.
// Builder-level only — no provider, no runner. Everything here passes with the research switch off or
// on, because every API keeps its old signature and the disabled outputs are byte-identical.

internal static partial class Harness
{
    private static void RunLlmResearchAction()
    {
        Section("[RES-1] research parser, catalogue and prompt identity");
        ResParserAccepts();
        ResParserRejects();
        ResParserFeatureOff();
        ResCatalogueCandidates();
        ResCatalogueBoundaries();
        ResCatalogueFeatureOff();
        ResPrivacy();
    }

    private static void ResCheck(bool ok, string name) => Check(ok, "[RES-1] " + name);

    // ---- fixtures -----------------------------------------------------------

    /// <summary>seat-a owns city-a (0,0) and army-a-1 (1,1); seat-b owns the far hidden city.</summary>
    private static GameEngine ResWorld(int seed, int points = 100)
    {
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        engine.State.FindFaction("seat-a").ResearchPoints = points;
        return engine;
    }

    private static string ResJson(string line)
        => "{\"type\":\"research\",\"args\":{\"line\":\"" + line + "\"}}";

    private static ActionOption ResOption(List<ActionOption> options, string line)
        => options.FirstOrDefault(o => o != null && o.Type == ActionType.Research
            && o.Args != null && o.Args.TryGetValue("line", out string value) && value == line);

    private static string ResPromptText(Observation observation, AttackView attacks = null,
        bool research = true)
        => ActionCatalogue.ToPromptText(ActionCatalogue.Build(observation, attacks, research));

    // ---- parser ------------------------------------------------------------

    private static void ResParserAccepts()
    {
        foreach (string line in ResearchLine.All)
        {
            ParseResult parsed = LlmActionParser.Parse(ResJson(line), false, true);
            ResCheck(parsed.Ok && parsed.Action.Type == ActionType.Research
               && parsed.Action.TargetId == null && !parsed.Action.EndTurn
               && parsed.Action.Args != null && parsed.Action.Args.Count == 1
               && parsed.Action.Args["line"] == line,
                "the exact targetless order parses for line " + line + " (" + (parsed.Error ?? "ok") + ")");
        }

        string withText = "{\"type\":\"research\",\"args\":{\"line\":\"agri\"},"
            + "\"reason\":\"food first\",\"summary\":\"I will study agriculture.\"}";
        ParseResult full = LlmActionParser.Parse(withText, false, true);
        ResCheck(full.Ok && full.Action.Reason == "food first"
           && full.Action.Summary == "I will study agriculture.",
            "reason and summary stay optional research data");

        ParseResult fenced = LlmActionParser.Parse(
            "```json\n{\"type\":\"research\",\"args\":{\"line\":\"military\"}}\n```", false, true);
        ResCheck(fenced.Ok && fenced.Action.Args["line"] == ResearchLine.Military,
            "the existing fence unwrapping applies unchanged");

        ActionCommand command = LlmActionParser.ToCommand(full.Action);
        ResCheck(command != null && command.Type == ActionType.Research && command.TargetId == null
           && command.ParamA == 0 && command.ParamB == 0
           && command.Args != null && command.Args["line"] == "agri"
           && command.ActorSeatId == null && command.ActionId == null,
            "the host builds the command from args and binds nothing else");

        ResCheck(LlmActionParser.ResearchTypes.Count == 1
           && LlmActionParser.ResearchTypes.Contains(ActionType.Research)
           && !LlmActionParser.SupportedTypes.Contains(ActionType.Research),
            "research is advertised as an opt-in type, not as part of the demo vocabulary");
    }

    private static void ResParserRejects()
    {
        var rejected = new List<KeyValuePair<string, string>>
        {
            Case("{\"type\":\"research\",\"target_id\":\"city-a\",\"args\":{\"line\":\"agri\"}}", "invalid field"),
            Case("{\"type\":\"research\",\"target_id\":null,\"args\":{\"line\":\"agri\"}}", "invalid field"),
            Case("{\"type\":\"research\",\"target_id\":\"\",\"args\":{\"line\":\"agri\"}}", "invalid field"),
            Case("{\"type\":\"research\",\"param_a\":0,\"args\":{\"line\":\"agri\"}}", "invalid field"),
            Case("{\"type\":\"research\",\"param_b\":3,\"args\":{\"line\":\"agri\"}}", "invalid field"),
            Case("{\"type\":\"research\"}", "line is required"),
            Case("{\"type\":\"research\",\"args\":{}}", "line is required"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"navy\"}}", "unsupported research line"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"AGRI\"}}", "unsupported research line"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"agri\",\"duration\":3}}", "unsupported argument"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"agri\",\"unit_type\":\"infantry\"}}", "unsupported argument"),
            Case("{\"type\":\"research\",\"args\":null}", "invalid field type"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"agri\"},\"actor_seat_id\":\"seat-b\"}", "forbidden field"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"agri\"},\"token\":\"t\"}", "forbidden field"),
            Case("{\"type\":\"research\",\"args\":{\"line\":\"agri\"},\"foo\":1}", "unknown field"),
        };
        int wrong = rejected.Count(pair => LlmActionParser.Parse(pair.Key, false, true).Ok
            || LlmActionParser.Parse(pair.Key, false, true).Error != pair.Value);
        ResCheck(wrong == 0, "all " + rejected.Count + " malformed research replies are rejected with a "
            + "typed error (" + wrong + " wrong)");

        ParseResult notOptedIn = LlmActionParser.Parse(ResJson("agri"), true, false);
        ResCheck(!notOptedIn.Ok && notOptedIn.Error == "unsupported action type",
            "research stays an unsupported action type while the flag is off");
        ResCheck(!LlmActionParser.Parse(ResJson("agri")).Ok,
            "the one-argument parse keeps the demo vocabulary");

        ParseResult both = LlmActionParser.Parse(ResJson("governance"), true, true);
        ResCheck(both.Ok && both.Action.Args["line"] == ResearchLine.Governance,
            "research composes with the attack vocabulary in the same parse call");
    }

    private static KeyValuePair<string, string> Case(string json, string error)
        => new KeyValuePair<string, string>(json, error);

    private static void ResParserFeatureOff()
    {
        // The un-opted vocabulary and shapes are untouched.
        ResCheck(LlmActionParser.Parse("{\"type\":\"march\",\"target_id\":\"army-a-1\",\"param_a\":1,\"param_b\":2}").Ok,
            "a march without coordinates-opt-in still parses");
        ResCheck(!LlmActionParser.Parse("{\"type\":\"march\",\"target_id\":\"army-a-1\"}").Ok,
            "the march coordinate requirement is unchanged");
        ResCheck(LlmActionParser.Parse("{\"type\":\"end_turn\",\"target_id\":null}").Ok,
            "end_turn still accepts an omitted or null target");
        ResCheck(!LlmActionParser.Parse("{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":1,\"param_b\":1}").Ok,
            "attack orders still need their own opt-in");
        ParseResult recruit = LlmActionParser.Parse(
            "{\"type\":\"recruit\",\"target_id\":\"city-a\",\"args\":{\"unit_type\":\"infantry\"}}");
        ResCheck(recruit.Ok && recruit.Action.Args["unit_type"] == UnitTypes.Infantry,
            "recruit args are unchanged");
        ResCheck(LlmActionParser.SupportedTypes.Count == 9
           && LlmActionParser.AttackTypes.Count == 2,
            "the published vocabularies did not grow");
    }

    // ---- catalogue ---------------------------------------------------------

    private static void ResCatalogueCandidates()
    {
        var engine = ResWorld(9701);
        Observation observation = engine.Observe("seat-a");
        List<ActionOption> options = ActionCatalogue.Build(observation, null, true);

        var research = options.Where(o => o != null && o.Type == ActionType.Research).ToList();
        ResCheck(research.Count == 4
           && research.Select(o => o.Args["line"]).SequenceEqual(ResearchLine.All),
            "one research candidate per line, in schema order");
        ResCheck(research.All(o => o.TargetId == null && !o.ParamA.HasValue && !o.ParamB.HasValue),
            "research candidates carry no target and no coordinates");
        ResCheck(research.All(o => o.CommandPointCost == BalanceConfig.CommandPointCost(ActionType.Research)
               && o.LikelyAffordable),
            "each candidate states the command-point price the engine will charge");

        ActionOption agri = ResOption(options, ResearchLine.Agri);
        int expected = BalanceConfig.ResearchLevelCost(1);
        ResCheck(agri != null && agri.Label.Contains("Agriculture") && agri.Label.Contains("level 1")
           && agri.Label.Contains(expected.ToString()),
            "the label names the line, the next level and its point price");

        int firstResearch = options.FindIndex(o => o.Type == ActionType.Research);
        int lastDomestic = options.FindLastIndex(o => o.Type != ActionType.Research
            && o.Type != "end_turn" && o.Type != ActionType.AttackArmy && o.Type != ActionType.AttackCity);
        int endTurn = options.FindIndex(o => o.Type == "end_turn");
        ResCheck(firstResearch > lastDomestic && firstResearch < endTurn,
            "research sits after the domestic blocks and before end_turn");
    }

    private static void ResCatalogueBoundaries()
    {
        var exact = ResWorld(9702, BalanceConfig.ResearchLevelCost(1));
        Observation atPrice = exact.Observe("seat-a");
        ResCheck(ActionCatalogue.Build(atPrice, null, true)
               .Count(o => o.Type == ActionType.Research) == 4,
            "a line priced exactly at the seat's points is advertised");

        var short1 = ResWorld(9703, BalanceConfig.ResearchLevelCost(1) - 1);
        Observation belowPrice = short1.Observe("seat-a");
        ResCheck(ActionCatalogue.Build(belowPrice, null, true)
               .All(o => o.Type != ActionType.Research),
            "one point short of every price advertises nothing");

        var mixed = ResWorld(9704, BalanceConfig.ResearchLevelCost(2));
        SetLevel(mixed, "seat-a", ResearchLine.Military, 1);
        SetLevel(mixed, "seat-a", ResearchLine.Espionage, 3);
        Observation oneLevel = mixed.Observe("seat-a");
        List<ActionOption> options = ActionCatalogue.Build(oneLevel, null, true);
        ResCheck(ResOption(options, ResearchLine.Military) != null
               && ResOption(options, ResearchLine.Military).Label.Contains("level 2")
               && ResOption(options, ResearchLine.Agri) != null,
            "an affordable line advertises its own next level at its own price");
        ResCheck(ResOption(options, ResearchLine.Espionage) == null
               && options.Count(o => o.Type == ActionType.Research) == 3,
            "a line whose next price the seat cannot pay is omitted");

        var maxed = ResWorld(9705, 500);
        foreach (string line in ResearchLine.All) SetLevel(maxed, "seat-a", line, BalanceConfig.ResearchMaxLevel);
        Observation atMax = maxed.Observe("seat-a");
        ResCheck(ActionCatalogue.Build(atMax, null, true).All(o => o.Type != ActionType.Research),
            "a maxed line is never advertised");

        var noCp = ResWorld(9706, 500);
        noCp.State.FindFaction("seat-a").CommandPoints = 0;
        Observation noOrders = noCp.Observe("seat-a");
        ResCheck(ActionCatalogue.Build(noOrders, null, true).All(o => o.Type != ActionType.Research),
            "no command points left advertises no research line");

        var zero = new GameEngine(9707);
        zero.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei" });
        zero.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Morale = 100,
        });
        Observation seatless = zero.Observe(null);
        ResCheck(ActionCatalogue.Build(seatless, null, true).All(o => o.Type != ActionType.Research),
            "a seatless observation advertises no research");
    }

    private static void ResCatalogueFeatureOff()
    {
        var engine = ResWorld(9708);
        Observation observation = engine.Observe("seat-a");
        AttackView attacks = AttackPlanner.Build(engine, "seat-a");
        string legacy = ActionCatalogue.ToPromptText(ActionCatalogue.Build(observation));
        string explicitOff = ActionCatalogue.ToPromptText(
            ActionCatalogue.Build(observation, null, false));
        string withAttacks = ActionCatalogue.ToPromptText(ActionCatalogue.Build(observation, attacks));
        string withAttacksOff = ActionCatalogue.ToPromptText(
            ActionCatalogue.Build(observation, attacks, false));
        ResCheck(legacy == explicitOff && withAttacks == withAttacksOff,
            "the feature-off catalogue is byte-identical to the existing overloads");
        ResCheck(!legacy.Contains("type=research") && !withAttacks.Contains("type=research"),
            "no research text appears while the flag is off");
        ResCheck(ActionCatalogue.Build(observation, attacks, true).Count
               == ActionCatalogue.Build(observation, attacks).Count + 4,
            "enabling the flag adds exactly the four affordable lines");
    }

    // ---- privacy -----------------------------------------------------------

    private static void ResPrivacy()
    {
        var engine = ResWorld(9711, 987654);
        SetLevel(engine, "seat-a", ResearchLine.Agri, 4);
        engine.State.FindFaction("seat-b").ResearchPoints = 100;
        Observation mine = engine.Observe("seat-a");
        Observation theirs = engine.Observe("seat-b");

        string mineText = ResPromptText(mine);
        string theirsText = ResPromptText(theirs);
        ResCheck(mineText.Contains("to level 5") && mineText.Contains("160"),
            "the owner sees its own next level and price");
        ResCheck(!theirsText.Contains("to level 5") && !theirsText.Contains("160"),
            "another seat never sees the owner's level or next price");
        ResCheck(theirsText.Contains("to level 1"), "the other seat still sees its own candidates");

        string theirsPrompt = PromptBuilder.UserPrompt(theirs,
            ActionCatalogue.Build(theirs, null, true), new List<string>(), 6, 5, (MovementView)null,
            null, null, false, null, false, null, null, null, false, true);
        ResCheck(!theirsPrompt.Contains("987654") && !theirsPrompt.Contains("to level 5"),
            "the other seat's prompt carries none of the owner's research points or levels");
        ResCheck(theirs.ResearchPoints != 987654 && theirs.ResearchLevel(ResearchLine.Agri) == 0,
            "the filtered observation itself carries only the reading seat's tech tree");

        var twin = ResWorld(9712, 987654);
        SetLevel(twin, "seat-a", ResearchLine.Agri, 3);
        SetLevel(twin, "seat-a", ResearchLine.Governance, 5);
        twin.State.FindFaction("seat-a").ResearchPoints = 3;
        twin.State.FindFaction("seat-b").ResearchPoints = 100;
        string fromTwin = ResPromptText(twin.Observe("seat-b"));
        ResCheck(fromTwin == theirsText,
            "a different hidden tech tree changes nothing in the other seat's candidates");
    }
}
