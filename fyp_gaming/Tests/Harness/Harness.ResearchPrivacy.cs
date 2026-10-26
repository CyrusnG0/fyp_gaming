using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunResearchPrivacy()
    {
        // ------------------------------------------------ research generic-action privacy (ADR-014)
        Section("research privacy: generic action + domain event are owner-only (ADR-014 enforced)");

        Check(ActionType.IsOwnerOnlyLog(ActionType.Research), "research is in the owner-only generic-log set");
        Check(!ActionType.IsOwnerOnlyLog(ActionType.Reclaim), "ordinary actions are not");

        {
            var e = EspionageState(morale: 100, gold: 5000);
            e.State.FindFaction("seat-a").ResearchPoints = 100;
            int otherBefore = e.Observe("seat-c").ResearchPoints;   // its own rollover income, not seat-a's
            var r = e.Submit(new ActionCommand
            {
                Type = ActionType.Research, ActorSeatId = "seat-a",
                Args = new Dictionary<string, string> { { "line", ResearchLine.Agri } },
            });
            var action = LastAction(e, ActionType.Research);
            var domain = Find(e.Log, "research");

            Check(r.Ok && action != null, $"the research order succeeds and is logged ({r.Error})");
            Check(action != null && action.Visibility == "owner_only",
                  "the generic action record is owner-only");
            Check(action != null && action.Args != null && action.Args["line"] == "agri",
                  "it still carries args.line for the owner and the researcher's log");
            Check(e.SeesEvent("seat-a", action), "the researching seat sees its own order");
            Check(!e.SeesEvent("seat-c", action),
                  "another seat sees neither the generic action nor args.line");
            Check(domain != null && domain.Visibility == "owner_only", "the research domain event is owner-only");
            Check(domain != null && e.SeesEvent("seat-a", domain), "the owner sees the domain event");
            Check(domain != null && !e.SeesEvent("seat-c", domain), "another seat does not see it");

            bool leaked = false;
            foreach (var rec in e.Observe("seat-c").Events)
                if (rec.EventType == "research"
                    || (rec.EventType == "action" && rec.Payload["action"] == ActionType.Research))
                    leaked = true;
            Check(!leaked, "seat C's filtered log contains nothing about the research order");

            var mine = e.Observe("seat-a");
            var other = e.Observe("seat-c");
            Check(mine.ResearchPoints == 80 && mine.ResearchLevel(ResearchLine.Agri) == 1,
                  $"the owner's observation carries the points/level ({mine.ResearchPoints}/{mine.ResearchLevel(ResearchLine.Agri)})");
            Check(other.ResearchPoints == otherBefore && other.ResearchLevel(ResearchLine.Agri) == 0,
                  $"the other seat's observation carries neither ({other.ResearchPoints}/{other.ResearchLevel(ResearchLine.Agri)})");

            var reclaim = e.Submit(new ActionCommand { Type = ActionType.Reclaim, ActorSeatId = "seat-a", TargetId = "city-a" });
            var publicAction = LastAction(e, ActionType.Reclaim);
            Check(reclaim.Ok && publicAction != null && publicAction.Visibility == "public",
                  "an ordinary action record stays public");
            Check(e.SeesEvent("seat-c", publicAction), "and another seat still sees it");
        }

        // 吏治 L5 九品 emits a public prestige_changed; the cause and delta are research data, not news.
        {
            var e = EspionageState(morale: 100, gold: 5000);
            var fac = e.State.FindFaction("seat-a");
            fac.Research[ResearchLine.Governance] = BalanceConfig.ResearchMaxLevel - 1;
            fac.ResearchPoints = BalanceConfig.ResearchLevelCost(BalanceConfig.ResearchMaxLevel);
            var r = e.Submit(new ActionCommand
            {
                Type = ActionType.Research, ActorSeatId = "seat-a",
                Args = new Dictionary<string, string> { { "line", ResearchLine.Governance } },
            });
            var canonical = Find(e.Log, "prestige_changed");
            var bystander = e.Observe("seat-c");
            var shown = ObsEvent(bystander, "prestige_changed");
            var ownerShown = ObsEvent(e.Observe("seat-a"), "prestige_changed");

            Check(r.Ok && canonical != null, $"[LS-002] Governance L5 succeeds and changes prestige ({r.Error})");
            Check(canonical != null && canonical.Payload["reason"] == "research_governance_l5"
               && canonical.Payload["delta"] == "10",
                  "[LS-002] the canonical prestige event keeps the private reason and delta");
            Check(shown != null && shown.Payload.ContainsKey("seat_id")
               && shown.Payload.ContainsKey("prestige")
               && !shown.Payload.ContainsKey("reason") && !shown.Payload.ContainsKey("delta"),
                  "[LS-002] a bystander sees only the public seat/prestige state");
            Check(!bystander.ToJson().Contains("research_governance_l5")
               && !bystander.ToJson().Contains("\"delta\""),
                  "[LS-002] the bystander JSON carries neither the research reason nor the delta");
            Check(ownerShown != null && ownerShown.Payload["reason"] == "research_governance_l5"
               && ownerShown.Payload["delta"] == "10",
                  "[LS-002] the owner keeps the reason and delta in its own observation");
        }
    }
}
