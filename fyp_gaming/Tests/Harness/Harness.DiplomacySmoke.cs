using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [new] Added during recovery: the historical harness never exercised diplomacy (P2–P6), so this file
// is a smoke of the proposal → ratification → betrayal path, not recovered coverage. Every assertion
// name carries the [new] prefix so the two kinds of coverage stay distinguishable in the output.

internal static partial class Harness
{
    private static void RunDiplomacySmoke()
    {
        Section("[new] diplomacy smoke (design §7.3–§7.4, ADR-010 #5: proposals, trust, war)");

        // ---- proposal gates
        {
            var e = DiplomacyScenario();
            var badType = e.Submit(TreatyOrder("seat-a", "seat-b", "vassal"));
            Check(!badType.Ok && badType.Error.Contains("unknown treaty_type"),
                  $"[new] an unknown treaty_type is refused ({badType.Error})");

            var self = e.Submit(TreatyOrder("seat-a", "seat-a", TreatyType.Nap));
            Check(!self.Ok && self.Error.Contains("your own seat"),
                  $"[new] a treaty with your own seat is refused ({self.Error})");

            var ok = e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Nap));
            Check(ok.Ok, $"[new] 互不侵犯 proposal is accepted as a pending offer ({ok.Error})");
            var proposed = Find(e.Log, "treaty_proposed");
            Check(proposed != null && proposed.Payload["treaty_type"] == TreatyType.Nap
               && proposed.Payload["duration"] == BalanceConfig.TreatyDurationSeasons.ToString(),
                  "[new] treaty_proposed carries the type and the default term");
            Check(proposed != null && proposed.Visibility != "public",
                  $"[new] the offer itself is not public ({proposed?.Visibility})");
            Check(e.State.Diplomacy.FindProposal("seat-a", "seat-b") != null, "[new] the offer is pending");
            Check(!e.State.Diplomacy.HasTreaty(TreatyType.Nap, "seat-a", "seat-b"),
                  "[new] a pending offer is not a treaty yet");

            var duplicate = e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Alliance));
            Check(!duplicate.Ok && duplicate.Error.Contains("already pending"),
                  $"[new] a second offer to the same seat is refused ({duplicate.Error})");
            Check(e.State.Diplomacy.Proposals.Count == 1, "[new] a refused offer adds no proposal");

            var badDuration = e.Submit(TreatyOrder("seat-a", "seat-c", TreatyType.Nap, "soon"));
            Check(!badDuration.Ok && badDuration.Error.Contains("duration must be a number"),
                  $"[new] a non-numeric duration is refused ({badDuration.Error})");
        }

        // ---- answer, then ratification at the rollover
        {
            var e = DiplomacyScenario();
            e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Nap, "3"));
            var proposal = e.State.Diplomacy.FindProposal("seat-a", "seat-b");
            Check(proposal != null && proposal.DurationSeasons == 3, "[new] args.duration sets the term (3)");

            var wrongResponder = e.Submit(TreatyAnswer("seat-c", "seat-a", "accept"));
            Check(!wrongResponder.Ok && wrongResponder.Error.Contains("no offer"),
                  $"[new] only the addressed seat can answer ({wrongResponder.Error})");

            var clamped = e.Submit(TreatyOrder("seat-a", "seat-c", TreatyType.Nap, "99"));
            Check(clamped.Ok && e.State.Diplomacy.FindProposal("seat-a", "seat-c").DurationSeasons
                                == BalanceConfig.TreatyDurationMaxSeasons,
                  $"[new] a long term clamps to {BalanceConfig.TreatyDurationMaxSeasons} seasons");

            var accept = e.Submit(TreatyAnswer("seat-b", "seat-a", "accept"));
            Check(accept.Ok && proposal.Accepted, $"[new] the addressed seat accepts ({accept.Error})");
            Check(!e.State.Diplomacy.HasTreaty(TreatyType.Nap, "seat-a", "seat-b"),
                  "[new] acceptance ratifies at the rollover, not immediately (ADR-010 #5)");

            e.BeginSeason();
            var ratified = Find(e.Log, "treaty_ratified");
            Check(ratified != null && ratified.Payload["treaty_type"] == TreatyType.Nap,
                  "[new] the rollover ratifies the accepted treaty");
            var treaty = e.State.Diplomacy.FindTreaty(TreatyType.Nap, "seat-a", "seat-b");
            Check(treaty != null && treaty.ExpirySeason == e.State.Season + 3 - 1,
                  $"[new] the 3-season term expires at season {e.State.Season + 3 - 1}");
            var lapsed = Find(e.Log, "treaty_lapsed");
            Check(lapsed != null && lapsed.Payload["to_seat_id"] == "seat-c",
                  "[new] an unanswered offer lapses at the rollover");
        }

        // ---- 贈禮 buys 信任 for the giver and 威望 (design §7.3)
        {
            var e = DiplomacyScenario();
            int goldBefore = TreasuryOf(e, "seat-a");
            int prestigeBefore = e.State.FindFaction("seat-a").Prestige;
            var gift = e.Submit(new ActionCommand
            {
                Type = ActionType.Gift, ActorSeatId = "seat-a", TargetId = "seat-b", ParamA = 2000,
            });
            Check(gift.Ok, $"[new] a 2,000-gold 贈禮 is accepted ({gift.Error})");
            Check(e.State.Diplomacy.GetTrust("seat-b", "seat-a") == 2 * BalanceConfig.GiftTrustPerUnit,
                  $"[new] 2,000 gold = +{2 * BalanceConfig.GiftTrustPerUnit} 信任 from the recipient");
            Check(e.State.FindFaction("seat-a").Prestige
                  == prestigeBefore + 2 * BalanceConfig.GiftPrestigePerUnit,
                  $"[new] the giver gains +{2 * BalanceConfig.GiftPrestigePerUnit} 威望");
            Check(TreasuryOf(e, "seat-a") == goldBefore - 2000, "[new] the gift leaves the treasury");

            var tooSmall = e.Submit(new ActionCommand
            {
                Type = ActionType.Gift, ActorSeatId = "seat-a", TargetId = "seat-b",
                ParamA = BalanceConfig.GiftGoldPerTrustUnit - 1,
            });
            Check(!tooSmall.Ok && tooSmall.Error.Contains("at least"),
                  $"[new] a gift below {BalanceConfig.GiftGoldPerTrustUnit} gold is refused ({tooSmall.Error})");
        }

        // ---- 背盟: breaking a treaty costs trust with the victim and every bystander
        {
            var e = DiplomacyScenario();
            e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Nap));
            e.Submit(TreatyAnswer("seat-b", "seat-a", "accept"));
            e.BeginSeason();

            var broke = e.Submit(new ActionCommand
            {
                Type = ActionType.BreakTreaty, ActorSeatId = "seat-a", TargetId = "seat-b",
                Args = new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } },
            });
            Check(broke.Ok, $"[new] 背盟 is accepted while the treaty is in force ({broke.Error})");
            Check(e.State.Diplomacy.FindTreaty(TreatyType.Nap, "seat-a", "seat-b") == null,
                  "[new] the treaty is gone");
            Check(e.State.Diplomacy.GetTrust("seat-b", "seat-a") == -BalanceConfig.BetrayalTrustPenalty,
                  $"[new] the victim's 信任 drops by {BalanceConfig.BetrayalTrustPenalty}");
            Check(e.State.Diplomacy.GetTrust("seat-c", "seat-a")
                  == -BalanceConfig.BetrayalThirdPartyTrustPenalty,
                  $"[new] every bystander loses {BalanceConfig.BetrayalThirdPartyTrustPenalty} 信任 (design §7.4)");
            var broken = Find(e.Log, "treaty_broken");
            Check(broken != null && broken.Visibility == "public", "[new] a betrayal is public knowledge");

            var again = e.Submit(new ActionCommand
            {
                Type = ActionType.BreakTreaty, ActorSeatId = "seat-a", TargetId = "seat-b",
                Args = new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } },
            });
            Check(!again.Ok && again.Error.Contains("no nap treaty"),
                  $"[new] breaking a treaty twice is refused ({again.Error})");
        }

        // ---- 宣戰 → 停戰: a ratified truce ends the war
        {
            var e = DiplomacyScenario();
            int prestigeBefore = e.State.FindFaction("seat-a").Prestige;
            var war = e.Submit(new ActionCommand
            {
                Type = ActionType.DeclareWar, ActorSeatId = "seat-a", TargetId = "seat-b",
            });
            Check(war.Ok && e.State.Diplomacy.IsAtWar("seat-a", "seat-b"),
                  $"[new] 宣戰 puts the pair at war ({war.Error})");
            Check(e.State.FindFaction("seat-a").Prestige
                  == prestigeBefore - BalanceConfig.DeclareWarPrestigeCost,
                  $"[new] 宣戰 costs {BalanceConfig.DeclareWarPrestigeCost} 威望");
            Check(e.Observe("seat-b").Relation("seat-a").AtWar, "[new] the defender sees the war in its observation");

            var again = e.Submit(new ActionCommand
            {
                Type = ActionType.DeclareWar, ActorSeatId = "seat-a", TargetId = "seat-b",
            });
            Check(!again.Ok && again.Error.Contains("already at war"),
                  $"[new] declaring war twice is refused ({again.Error})");

            e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Truce));
            e.Submit(TreatyAnswer("seat-b", "seat-a", "accept"));
            e.BeginSeason();
            var ratified = Find(e.Log, "treaty_ratified");
            Check(ratified != null && ratified.Payload["war_ended"] == "true",
                  "[new] a ratified 停戰 ends the war");
            Check(!e.State.Diplomacy.IsAtWar("seat-a", "seat-b"), "[new] the pair is at peace again");
        }

        // ---- fog: only the two parties see their treaty and its pending offer
        {
            var e = DiplomacyScenario();
            e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Alliance));
            Check(e.Observe("seat-b").Proposals.Count == 1, "[new] the addressed seat sees the pending offer");
            Check(e.Observe("seat-c").Proposals.Count == 0, "[new] a bystander does not see the pending offer");

            e.Submit(TreatyAnswer("seat-b", "seat-a", "accept"));
            e.BeginSeason();
            Check(e.Observe("seat-b").Treaties.Count == 1, "[new] a party sees its own treaty");
            Check(e.Observe("seat-c").Treaties.Count == 0, "[new] a third seat does not see the treaty (fog)");
            Check(e.Observe("seat-c").Relation("seat-a").MyTrust
                  == e.State.Diplomacy.GetTrust("seat-c", "seat-a"),
                  "[new] a bystander sees only its own side of 信任");
        }

        // ---- determinism of the diplomacy path
        Check(DiplomacyReplay() == DiplomacyReplay(), "[new] the diplomacy path replays byte-identically");
    }

    private static GameEngine DiplomacyScenario()
    {
        var e = new GameEngine(31);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 5000, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 2000, Gold = 300, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-c", Name = "City C", X = 10, Z = 0, OwnerSeatId = "seat-c",
            Population = 10000, Food = 2000, Gold = 300, Garrison = 500, Morale = 100,
        });
        e.BeginSeason();
        return e;
    }

    private static ActionCommand TreatyOrder(string seat, string toSeat, string treatyType, string duration = null)
    {
        var args = new Dictionary<string, string> { { "treaty_type", treatyType } };
        if (duration != null) args["duration"] = duration;
        return new ActionCommand
        {
            Type = ActionType.ProposeTreaty, ActorSeatId = seat, TargetId = toSeat, Args = args,
        };
    }

    private static ActionCommand TreatyAnswer(string seat, string toSeat, string response)
        => new ActionCommand
        {
            Type = ActionType.RespondTreaty, ActorSeatId = seat, TargetId = toSeat,
            Args = new Dictionary<string, string> { { "response", response } },
        };

    private static string DiplomacyReplay()
    {
        var e = DiplomacyScenario();
        e.Submit(new ActionCommand
        {
            Type = ActionType.Gift, ActorSeatId = "seat-a", TargetId = "seat-b", ParamA = 1000,
        });
        e.Submit(TreatyOrder("seat-a", "seat-b", TreatyType.Nap, "2"));
        e.Submit(TreatyAnswer("seat-b", "seat-a", "accept"));
        e.BeginSeason();
        e.Submit(new ActionCommand
        {
            Type = ActionType.DeclareWar, ActorSeatId = "seat-a", TargetId = "seat-b",
        });
        return e.Log.ToJsonLines();
    }
}
