using System;
using System.IO;
using System.Text.Json;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static void DiplomacyVoluntaryResponse(TestLog log)
        {
            CsScenarioSpec spec = Builtin("cs-diplomacy-voluntary-response-v1", 4249);
            CsScenarioResult run = RunSpec(spec, "cs40-diplomacy", out string root);
            try
            {
                log.Check(run.Status == CsStatus.Completed && run.SeasonsPlayed == 1,
                    "[CS-40] the staged diplomacy situation completes one season",
                    run.Status + ", seasons=" + run.SeasonsPlayed + ", requests=" + run.Requests);
                string scenarioDir = run.Directory;
                string settings = File.ReadAllText(Path.Combine(scenarioDir, "settings.json"));
                log.Check(settings.Contains("\"diplomacy\": true"),
                    "[CS-40] the controlled scenario records diplomacy enabled in effective settings");
                string scenario = File.ReadAllText(Path.Combine(scenarioDir, "scenario.json"));
                log.Check(scenario.Contains("\"stage_incoming_diplomacy\": true"),
                    "[CS-40] evidence labels the incoming message and offer as staged setup");

                var dispatches = ReadJsonLines(Path.Combine(scenarioDir, "dispatches.ndjson"));
                JsonElement receiving = Find(dispatches,
                    d => CsEvidenceRecorder.Text(d, "seat_id") == "seat-1");
                JsonElement prompt = Obj(receiving, "prompt");
                string system = CsEvidenceRecorder.Text(prompt, "system");
                string user = CsEvidenceRecorder.Text(prompt, "user");
                JsonElement truncation = Obj(prompt, "truncation");
                log.Check(receiving.ValueKind == JsonValueKind.Object
                        && system != null && system.Contains("DIPLOMACY ORDERS")
                        && user != null
                        && user.Contains("The river border is quiet")
                        && user.Contains("Three seasons, subject to your decision.")
                        && user.Contains("type=respond_treaty target_id=seat-0 args={\"response\":\"accept\"}")
                        && user.Contains("type=respond_treaty target_id=seat-0 args={\"response\":\"reject\"}"),
                    "[CS-40] the receiving seat's real runner prompt has message, offer and both voluntary answers",
                    user ?? "missing receiving prompt");
                log.Check(Names(truncation, "sections_present", "DIPLOMACY MESSAGES AND OFFERS"),
                    "[CS-40] the structured prompt record names the diplomacy section");
                JsonElement action = Obj(receiving, "action");
                log.Check(CsEvidenceRecorder.Text(action, "kind") == "action"
                        && CsEvidenceRecorder.Bool(action, "accepted") == true
                        && CsEvidenceRecorder.Text(action, "command_json")?.Contains("respond_treaty") == true,
                    "[CS-40] the fake acceptance is recorded as an accepted engine action, not an inferred outcome");
                JsonElement before = Obj(Obj(receiving, "state"), "before");
                string observation = CsEvidenceRecorder.Text(before, "observation_json");
                log.Check(CsEvidenceRecorder.Int(receiving, "season") == 1
                        && CsEvidenceRecorder.Int(receiving, "request_index") == 1
                        && observation != null && observation.Contains("\"event_type\":\"message_sent\"")
                        && observation.Contains("\"visibility\":\"public\"")
                        && observation.Contains("\"event_type\":\"treaty_proposed\"")
                        && observation.Contains("\"visibility\":\"owner_and_observers\""),
                    "[CS-40] request timing and engine event audiences are retained with the seat-filtered observation");
                JsonElement provider = Obj(receiving, "provider");
                JsonElement reply = Obj(receiving, "reply");
                log.Check(CsEvidenceRecorder.Bool(provider, "live") == false
                        && CsEvidenceRecorder.Text(reply, "source") == "fake_scripted"
                        && run.Requests == 3,
                    "[CS-40] the plumbing reply is labelled fake and the configured request cap permits all three dispatches",
                    "requests=" + run.Requests);

                CsScenarioSpec cappedSpec = Builtin("cs-diplomacy-voluntary-response-v1", 4250);
                cappedSpec.MaxRequests = 2;
                CsScenarioResult capped = RunSpec(cappedSpec, "cs40-diplomacy-cap", out string cappedRoot);
                try
                {
                    log.Check(capped.Status != CsStatus.Completed && capped.Requests <= 2
                            && capped.SeasonsPlayed == 0,
                        "[CS-40] an exhausted request cap cannot mark the unobserved response season complete",
                        capped.Status + ", requests=" + capped.Requests + ", seasons=" + capped.SeasonsPlayed);
                }
                finally { Cleanup(cappedRoot); }
            }
            catch (JsonException e)
            {
                log.Check(false, "[CS-40] evidence JSON is valid", e.Message);
            }
            finally { Cleanup(root); }
        }
    }
}
