using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // Multi-season route scenario: attack is enabled only if the engine itself offers a legal
    // combat choice, and the fake rehearsal carries state across seasons without leaking the
    // other seat's hidden facts. The fixture is plumbing; no choice here is a behaviour claim.
    public static partial class CsSelfTests
    {
        private static ActionCommand RouteSeasonsMarch(int x, int z) => new ActionCommand
        {
            Type = ActionType.March, ActorSeatId = "seat-0",
            TargetId = CsScenarioFactory.OwnArmyId, ParamA = x, ParamB = z,
        };

        private static ActionCommand RouteSeasonsAttack() => new ActionCommand
        {
            Type = ActionType.AttackArmy, ActorSeatId = "seat-0",
            TargetId = CsScenarioFactory.OwnArmyId, ParamA = 16, ParamB = 10,
        };

        private static void RouteSeasonsEngineChecks(TestLog log)
        {
            CsScenarioSpec spec = Builtin(CsScenarios.RouteSeasonsId, 5151);
            log.Check(spec.Seasons == CsScenarios.RouteSeasonsCount && spec.EnableAttack
                    && spec.EnableSpatial && spec.EnableManualLookup && spec.EnableDeliberation
                    && spec.Goal == CsScenarios.RouteGoal,
                "[CS-S1] seasons scenario keeps goal, spatial, lookup, deliberation and enables attack");

            GameEngine engine = CsWorld.Create(spec, "route-seasons-check");
            AttackView before = AttackPlanner.Build(engine, "seat-0");
            log.Check(before.TotalAvailable == 0,
                "[CS-S1] no combat choice is legal at the start (enemy out of range)",
                "available=" + before.TotalAvailable);
            ValidationResult early = engine.Submit(RouteSeasonsAttack());
            log.Check(!early.Ok, "[CS-S1] engine rejects an attack before contact", "error=" + early.Error);

            ValidationResult march = engine.Submit(RouteSeasonsMarch(15, 10));
            AttackView after = AttackPlanner.Build(engine, "seat-0");
            bool listed = false;
            foreach (ArmyAttackPlan plan in after.Armies)
                foreach (AttackOption o in plan.Options)
                    if (o.Kind == ActionType.AttackArmy && o.X == 16 && o.Z == 10) listed = true;
            log.Check(march.Ok && listed,
                "[CS-S1] after marching to 15,10 the engine lists an attack on the visible army",
                "march_ok=" + march.Ok + " available=" + after.TotalAvailable);
            ValidationResult attack = engine.Submit(RouteSeasonsAttack());
            log.Check(attack.Ok, "[CS-S1] engine accepts that attack (a legal combat choice)",
                "error=" + attack.Error);
            Console.WriteLine("       attack accepted=" + attack.Ok + " listed=" + listed);
        }

        private static void RouteSeasonsFakeRehearsal(TestLog log)
        {
            CsScenarioSpec spec = Builtin(CsScenarios.RouteSeasonsId, 5151);
            spec.MaxRequests = 40;
            CsScenarioResult result = RunSpec(spec, "css2", out string root);
            try
            {
                log.Check(result.Status == CsStatus.Completed && result.SeasonsPlayed == 3,
                    "[CS-S2] fake rehearsal completes three seasons", "status=" + result.Status
                    + " seasons=" + result.SeasonsPlayed + " detail=" + result.Detail);
                var runLines = new List<JsonElement>();
                foreach (string f in Directory.GetFiles(result.Directory, "run-*.jsonl"))
                    if (!f.EndsWith("-exchanges.ndjson")) runLines.AddRange(ReadJsonLines(f));
                List<JsonElement> prompts = ReadJsonLines(Path.Combine(result.Directory, "prompts.ndjson"));
                log.Check(runLines.Count == 10 && prompts.Count == 10,
                    "[CS-S2] every request has a run line and a recorded prompt",
                    "run=" + runLines.Count + " prompts=" + prompts.Count);

                bool fordCarried = false, positionCarried = false;
                int inquiryActions = -1, noteActions = -1, endings0 = 0, endings1 = 0;
                int homeFood1 = -1, homeFood2 = -1;
                foreach (JsonElement line in runLines)
                {
                    string seat = line.GetProperty("seat_id").GetString();
                    int season = line.GetProperty("season").GetInt32();
                    using (JsonDocument obs = JsonDocument.Parse(line.GetProperty("observation_json").GetString()))
                    {
                        JsonElement o = obs.RootElement;
                        foreach (JsonElement c in o.GetProperty("cities").EnumerateArray())
                        {
                            string id = c.GetProperty("id").GetString();
                            if (seat == "seat-0" && season == 2 && id == CsScenarios.RouteObjectiveCityId)
                                fordCarried = c.GetProperty("owner_seat_id").GetString() == "seat-1";
                            if (seat == "seat-0" && id == CsScenarioFactory.HomeCityId)
                            {
                                if (season == 1 && homeFood1 < 0) homeFood1 = c.GetProperty("food").GetInt32();
                                if (season == 2) homeFood2 = c.GetProperty("food").GetInt32();
                            }
                        }
                        if (seat == "seat-0" && season == 2)
                            foreach (JsonElement a in o.GetProperty("armies").EnumerateArray())
                                if (a.GetProperty("id").GetString() == CsScenarioFactory.OwnArmyId)
                                    positionCarried = a.GetProperty("x").GetInt32() == 15
                                        && a.GetProperty("z").GetInt32() == 10;
                    }
                    int actions = line.GetProperty("actions_this_turn").GetInt32();
                    int index = line.GetProperty("request_index").GetInt32();
                    if (seat == "seat-0" && season == 1 && index == 1) inquiryActions = actions;
                    if (seat == "seat-0" && season == 1 && index == 2) noteActions = actions;
                    string raw = line.TryGetProperty("raw_model_text", out JsonElement t) ? t.GetString() ?? "" : "";
                    if (raw.Contains("end_turn")) { if (seat == "seat-0") endings0++; else endings1++; }
                }
                log.Check(fordCarried && positionCarried,
                    "[CS-S2] season 2 seat-0 context shows the granary owned by seat-1 and its own army at 15,10");
                log.Check(homeFood1 > 0 && homeFood2 >= 0 && homeFood1 != homeFood2,
                    "[CS-S2] resources carry and change into the next season",
                    "home food " + homeFood1 + " -> " + homeFood2);
                log.Check(inquiryActions == 0 && noteActions == 0,
                    "[CS-S2] an inquiry and a note spend no action",
                    "after inquiry=" + inquiryActions + " after note=" + noteActions);
                log.Check(endings0 == 3 && endings1 == 3,
                    "[CS-S2] each seat ends its turn once per season",
                    "seat0=" + endings0 + " seat1=" + endings1);

                bool noteOwner = false, noteLeak = false, hiddenLeak = false, goalOther = false;
                foreach (JsonElement p in prompts)
                {
                    string seat = p.GetProperty("seat").GetString();
                    string user = p.GetProperty("user").GetString();
                    bool hasNote = user.Contains("fixture note: watch the ford");
                    if (seat == "seat-0")
                    {
                        if (hasNote) noteOwner = true;
                        if (user.Contains("cs-army-hidden") || user.Contains("cs-city-enemy")
                            || user.Contains("22,9")) hiddenLeak = true;
                    }
                    else
                    {
                        if (hasNote) noteLeak = true;
                        if (user.Contains(CsScenarios.RouteGoal)) goalOther = true;
                    }
                }
                log.Check(noteOwner && !noteLeak,
                    "[CS-S2] the note returns to its owner and never reaches the other seat");
                log.Check(!hiddenLeak && !goalOther,
                    "[CS-S2] seat-0 prompts hold no Shu city or hidden host; the goal is seat-0 only");
                log.Check(result.PrivacyLeaks == 0, "[CS-S2] runner privacy check reports no leak",
                    "leaks=" + result.PrivacyLeaks);
            }
            finally { Cleanup(root); }
        }
    }
}
