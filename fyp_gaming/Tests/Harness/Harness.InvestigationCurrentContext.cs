using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunInvestigationCurrentContext()
    {
        Section("[new] research/diplomacy manual lookup current context");

        var engine = DiplomacyScenario();
        FactionState own = engine.State.FindFaction("seat-a");
        own.ResearchPoints = 73;
        own.Research[ResearchLine.Agri] = 1;
        own.Research[ResearchLine.Military] = 2;
        own.Research[ResearchLine.Espionage] = 5;
        own.Research[ResearchLine.Governance] = 0;
        engine.State.Diplomacy.SetTrust("seat-a", "seat-b", 17);
        engine.State.Diplomacy.SetTrust("seat-b", "seat-a", -8);
        engine.State.Diplomacy.DeclareWar("seat-a", "seat-c");
        engine.State.Diplomacy.Treaties.Add(new TreatyRecord
        {
            Type = TreatyType.Nap, FactionA = "seat-a", FactionB = "seat-b",
            ExpirySeason = engine.State.Season + 2,
        });
        engine.State.Diplomacy.Proposals.Add(new TreatyProposal
        {
            ProposalId = "inbound-a", FromSeatId = "seat-b", ToSeatId = "seat-a",
            Type = TreatyType.Alliance, DurationSeasons = 4,
            ProposedSeason = engine.State.Season - 1, Text = "private proposal prose should not appear",
        });
        engine.State.Diplomacy.Proposals.Add(new TreatyProposal
        {
            ProposalId = "outbound-a", FromSeatId = "seat-a", ToSeatId = "seat-c",
            Type = TreatyType.Truce, DurationSeasons = 2,
            ProposedSeason = engine.State.Season, Text = "another private proposal",
        });
        engine.State.Diplomacy.Proposals.Add(new TreatyProposal
        {
            ProposalId = "nonparty", FromSeatId = "seat-b", ToSeatId = "seat-c",
            Type = TreatyType.Nap, DurationSeasons = 5,
            ProposedSeason = engine.State.Season, Text = "must stay private",
        });

        Observation observation = engine.Observe("seat-a");
        // Defend the helper even if a malformed host accidentally supplies non-party view records.
        observation.Treaties.Add(new TreatyView
        {
            Type = TreatyType.Alliance, FactionA = "seat-b", FactionB = "seat-c", ExpirySeason = 99,
        });
        observation.Proposals.Add(new ProposalView
        {
            ProposalId = "forged-nonparty", FromSeatId = "seat-b", ToSeatId = "seat-c",
            Type = TreatyType.Nap, DurationSeasons = 3, ProposedSeason = observation.Season,
            Text = "hidden nonparty text",
        });
        var service = new InvestigationService(engine.State.GameId, "seat-a", observation, null);
        string stateBefore = own.ResearchPoints + ":" + own.ResearchLevel(ResearchLine.Agri)
            + ":" + engine.State.Diplomacy.GetTrust("seat-a", "seat-b")
            + ":" + engine.State.Diplomacy.Treaties.Count
            + ":" + engine.State.Diplomacy.Proposals.Count;
        string observationBefore = observation.ToJson();

        ManualLookupResult research = service.ManualLookup("tech");
        Check(research.Ok && research.TopicId == "research" && research.CurrentContext != null
              && research.CurrentContext.Kind == "research" && research.CurrentContext.ResearchPoints == 73
              && research.CurrentContext.Research.Count == ResearchLine.All.Length,
            "[new] exact research alias returns structured own-seat current context");
        Check(research.CurrentContext.Research[0].Line == ResearchLine.Agri
              && research.CurrentContext.Research[0].Level == 1
              && research.CurrentContext.Research[0].NextLevelCost == BalanceConfig.ResearchLevelCost(2)
              && research.CurrentContext.Research[2].Level == BalanceConfig.ResearchMaxLevel
              && research.CurrentContext.Research[2].NextLevelCost == null,
            "[new] research context reports configured next cost and null at maximum level");
        Check(research.CurrentContext.Scope == "current_observation_data_not_action_availability"
              && !research.LlmActionExposed,
            "[new] research context is explicitly data and retains the not-action-exposed caveat");

        ManualLookupResult diplomacy = service.ManualLookup("treaties");
        Check(diplomacy.Ok && diplomacy.TopicId == "diplomacy" && diplomacy.CurrentContext != null
              && diplomacy.CurrentContext.Kind == "diplomacy",
            "[new] exact diplomacy alias returns current diplomacy context");
        Check(diplomacy.CurrentContext.Relations.Count == 2
              && diplomacy.CurrentContext.Relations[0].SeatId == "seat-b"
              && diplomacy.CurrentContext.Relations[0].MyTrust == 17
              && diplomacy.CurrentContext.Relations[0].TheirTrust == -8
              && diplomacy.CurrentContext.Relations[1].AtWar,
            "[new] diplomacy preserves deterministic directional trust and war facts");
        Check(diplomacy.CurrentContext.Treaties.Count == 1
              && diplomacy.CurrentContext.Treaties[0].PartnerSeatId == "seat-b"
              && diplomacy.CurrentContext.Treaties[0].ExpirySeason == engine.State.Season + 2
              && diplomacy.CurrentContext.Treaties[0].SeasonsRemainingIncludingCurrent == 3,
            "[new] only party treaties are included and expiry timing is inclusive");
        Check(diplomacy.CurrentContext.Proposals.Count == 2
              && diplomacy.CurrentContext.Proposals[0].Direction == "inbound"
              && diplomacy.CurrentContext.Proposals[0].SeasonsSinceProposed == 1
              && diplomacy.CurrentContext.Proposals[1].Direction == "outbound"
              && diplomacy.CurrentContext.Proposals[1].CounterpartySeatId == "seat-c",
            "[new] pending proposals show only party direction and timing");
        Check(!diplomacy.ToJson().Contains("private proposal prose")
              && !diplomacy.ToJson().Contains("forged-nonparty")
              && !diplomacy.ToJson().Contains("nonparty"),
            "[new] proposal prose and non-party records are omitted");

        ManualLookupResult partial = service.ManualLookup("research l");
        Check(partial.Ok && partial.TopicId == "research" && partial.CurrentContext == null,
            "[new] a partial topic match does not receive context enrichment");
        Check(InvestigationParses(research.ToJson()) && InvestigationParses(diplomacy.ToJson()),
            "[new] structured context responses remain valid JSON");

        ManualLookupResult wrongSeat = new InvestigationService(engine.State.GameId, "seat-b", observation, null)
            .ManualLookup("research");
        ManualLookupResult wrongGame = new InvestigationService("other-game", "seat-a", observation, null)
            .ManualLookup("diplomacy");
        Check(!wrongSeat.Ok && wrongSeat.CurrentContext == null
              && !wrongGame.Ok && wrongGame.CurrentContext == null,
            "[new] wrong-seat and wrong-match requests fail closed without current context");

        var smallOptions = InvestigationOptions.Defaults();
        smallOptions.MaxResponseChars = 512;
        var bounded = new InvestigationService(engine.State.GameId, "seat-a", observation, null, smallOptions)
            .ManualLookup("research");
        Check(bounded.Ok && bounded.Truncated && bounded.CurrentContext == null && bounded.Facts.Count > 0
              && bounded.ToJson().Length <= smallOptions.MaxResponseChars
              && InvestigationParses(bounded.ToJson()),
            "[new] tight cap drops optional context with truncation while preserving rule answer JSON");

        Observation bystander = engine.Observe("seat-c");
        ManualLookupResult bystanderDiplomacy = new InvestigationService(engine.State.GameId, "seat-c", bystander, null)
            .ManualLookup("diplomacy");
        Check(bystanderDiplomacy.CurrentContext.Treaties.Count == 0
              && bystanderDiplomacy.CurrentContext.Proposals.Count == 2
              && bystanderDiplomacy.CurrentContext.Proposals[0].CounterpartySeatId == "seat-b"
              && bystanderDiplomacy.CurrentContext.Proposals[0].DurationSeasons == 5
              && bystanderDiplomacy.CurrentContext.Proposals[1].CounterpartySeatId == "seat-a",
            "[new] engine-filtered bystander context contains only the two offers addressed to it");
        string stateAfter = own.ResearchPoints + ":" + own.ResearchLevel(ResearchLine.Agri)
            + ":" + engine.State.Diplomacy.GetTrust("seat-a", "seat-b")
            + ":" + engine.State.Diplomacy.Treaties.Count
            + ":" + engine.State.Diplomacy.Proposals.Count;
        Check(stateAfter == stateBefore && observation.ToJson() == observationBefore,
            "[new] current-context lookup leaves canonical state and detached observation unchanged");
    }
}
