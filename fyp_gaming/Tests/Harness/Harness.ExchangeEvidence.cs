using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [EVID-1] Per-request evidence joins: one exchange id ties the run row, the exact prompt, the full
// result, the model-stated reason and the engine command; incomplete (cancelled) requests keep the same
// id and an explicit result; a reason never leaks from one request into the next.

internal static partial class Harness
{
    private static string ExchangeField(string line, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, "\"" + key + "\":\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static void RunExchangeEvidence()
    {
        Section("[EVID-1] exchange evidence joins prompt, reply, reason, command and result");

        // 1. Accepted action, end_turn, and a parse failure between them.
        var sink = new MemoryRecorderSink();
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("not json at all", 0, 10, 4);
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"reason\":\"gold is thin\"}", 0, 10, 4);
        fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"done for now\"}", 0, 8, 2);
        var runner = new LlmTurnRunner(fake, new LlmConfig { MaxTotalRequests = 20, MaxInvalidPerTurn = 3 }, sink);
        SpectatorSession session = SpectatorNew(SpectatorEngine(), runner, 1);
        session.Start();
        SpectatorDrain(session);
        List<string> results = sink.ExchangeLines.Where(l => l.Contains("\"exchange_event\":\"result\"")).ToList();
        string failed = results.FirstOrDefault(l => l.Contains("\"parse_ok\":false"));
        string accepted = results.FirstOrDefault(l => l.Contains("\"type\":\\\"levy_tax\\\"")
            || l.Contains("levy_tax"));
        string ended = results.FirstOrDefault(l => l.Contains("done for now") && l.Contains("model_stated_reason"));
        Check(failed != null && !failed.Contains("model_stated_reason"),
            "a reply that did not parse records no model reason");
        Check(accepted != null && ExchangeField(accepted, "model_stated_reason") == "gold is thin"
              && accepted.Contains("\"result_ok\":true") && accepted.Contains("\\\"action_id\\\":"),
            "an accepted action records its model-stated reason, the engine command with its action id and the result");
        Check(ended != null && ExchangeField(ended, "model_stated_reason") == "done for now",
            "an end_turn records its summary as the model-stated reason");
        Check(results.Count(l => l.Contains("model_stated_reason")) == 2,
            "the reason never carries over into the next request's record");
        Check(results.All(l => l.Contains("\"raw_body_may_contain_private_reasoning\":false")),
            "a provider reply without a reasoning stream is marked as having none");

        // 2. A request cancelled mid-flight keeps its id and gets an explicit incomplete result.
        sink = new MemoryRecorderSink();
        fake = new FakeLlmTransport();
        fake.EnqueueDeferred();
        var engine = SpectatorEngine();
        var host = new LlmPlanningHost(engine, "seat-a");
        runner = new LlmTurnRunner(fake, new LlmConfig { MaxTotalRequests = 20 }, sink);
        runner.Begin(host, host.Token, "seat-a", 5);
        runner.Tick();
        runner.Cancel("host stop");
        List<string> req = sink.ExchangeLines.Where(l => l.Contains("\"exchange_event\":\"request\"")).ToList();
        List<string> res = sink.ExchangeLines.Where(l => l.Contains("\"exchange_event\":\"result\"")).ToList();
        Check(req.Count == 1 && res.Count == 1
              && ExchangeField(req[0], "exchange_id") == ExchangeField(res[0], "exchange_id")
              && ExchangeField(sink.RunLines[0], "exchange_id") == ExchangeField(req[0], "exchange_id"),
            "a cancelled request shares one exchange id across run row, request and result");
        Check(res[0].Contains("\"error_kind\":\"Cancelled\"") && res[0].Contains("\"parse_ok\":false")
              && !res[0].Contains("model_stated_reason"),
            "the incomplete request is recorded as cancelled, with no reply, command or reason");
    }
}
