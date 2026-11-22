using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LuanShi.Engine;

internal static partial class Harness
{
    private static List<string> ExchangeIds(List<string> lines, string eventName)
    {
        var ids = new List<string>();
        foreach (string line in lines)
        {
            if (eventName != null && !line.Contains("\"exchange_event\":\"" + eventName + "\"")) continue;
            Match m = Regex.Match(line, "\"exchange_id\":\"([^\"]+)\"");
            if (m.Success) ids.Add(m.Groups[1].Value);
        }
        return ids;
    }

    private static void RunExchangeIdJoin()
    {
        // request_index restarts each turn; the id must still be unique and shared by the run row,
        // the exchange request and the exchange result of one request.
        var sink = new MemoryRecorderSink();
        var fake = new FakeLlmTransport();
        SpectatorQueueSeason(fake, 1);
        SpectatorQueueSeason(fake, 2);
        var runner = new LlmTurnRunner(fake, new LlmConfig { MaxTotalRequests = 20 }, sink);
        SpectatorSession session = SpectatorNew(SpectatorEngine(), runner, 2);
        session.Start();
        SpectatorDrain(session);
        List<string> requests = ExchangeIds(sink.ExchangeLines, "request");
        List<string> results = ExchangeIds(sink.ExchangeLines, "result");
        List<string> rows = ExchangeIds(sink.RunLines, null);
        Check(session.State == SpectatorState.Completed && requests.Count == 8,
            "the two-season fixture sends one request per queued reply");
        Check(requests.Distinct().Count() == requests.Count,
            "exchange ids are unique across turns, not just within one turn");
        Check(results.SequenceEqual(requests) && rows.SequenceEqual(requests),
            "each run row, exchange request and exchange result share one exchange id");
    }

    // Recorded live failure (run-20261122140759713-c8b0fcfc): one transport failure carried no usage,
    // which set a match-wide "usage unknown" flag and stopped the other seat's next request. Recovery is
    // an explicit allowance (default 0): the unreported request is counted, never turned into a token
    // estimate, because the provider may have billed it and a late reply is discarded.
    private static LlmConfig TransportUsageConfig(int sessionInputTokens, int allowance)
    {
        LlmConfig config = LlmConfig.RichDeliberation();
        config.MaxRequestsPerTurn = 6;
        config.MaxTotalRequests = 20;
        config.MaxPromptChars = 80000;
        config.MaxOutputTokens = 400;
        config.MaxSessionInputTokens = sessionInputTokens;
        config.MaxSessionOutputTokens = 100000;
        config.MaxUnknownTransportRequests = allowance;
        return config;
    }

    private static SpectatorSession TransportUsageRun(FakeLlmTransport fake, int ceiling, int allowance,
        out LlmTurnRunner runner, int seasons = 1)
    {
        runner = SpectatorRunner(fake, TransportUsageConfig(ceiling, allowance));
        SpectatorSession session = SpectatorNew(SpectatorEngine(), runner, seasons);
        session.Start();
        SpectatorDrain(session);
        return session;
    }

    private static void RunSpectatorTransportUsage()
    {
        Section("Spectator: unreported transport failures are counted, capped and never estimated");
        RunExchangeIdJoin();
        RunExchangeEvidence();
        RunFakeRehearsalScript();

        // 1. Default (allowance 0): the first unreported transport failure stops the match, as before.
        var fake = new FakeLlmTransport();
        fake.EnqueueError(LlmErrorKind.Network, "transport failure");
        SpectatorQueueTurn(fake, "seat-b", 1);
        SpectatorSession session = TransportUsageRun(fake, 1000000, 0, out LlmTurnRunner runner);
        Check(session.State == SpectatorState.Paused && runner.StopCeiling == "session_usage_unknown"
              && fake.CallCount == 1 && runner.UnknownUsageRequests == 1 && runner.UnknownTransportRequests == 0,
            "with no allowance one unreported network failure stops the match before the next request");

        // 2. Allowance 1: the match passes that one failure. No retry, the failed request still counts
        //    toward the request cap, the totals stay unknown and nothing is estimated.
        fake = new FakeLlmTransport();
        fake.EnqueueError(LlmErrorKind.Network, "transport failure");
        SpectatorQueueTurn(fake, "seat-b", 1);
        session = TransportUsageRun(fake, 1000000, 1, out runner);
        Check(session.State == SpectatorState.Completed && session.StopReason == "completed",
            "an explicit allowance of 1 lets the other seat play after one network failure");
        Check(fake.CallCount == 3 && runner.TotalRequests == 3,
            "the failed request counts toward the request cap and nothing is retried");
        Check(runner.UnknownUsageRequests == 1 && runner.UnknownTransportRequests == 1
              && runner.TotalInputTokens == null && runner.TotalOutputTokens == null && runner.CostUsd == null,
            "the missing usage is reported as unknown and tolerated, never as zero");
        Check(runner.ReportedInputTokens == 18 && runner.ReportedOutputTokens == 6,
            "the usage the provider did report is kept as a lower bound");

        // 3. The allowance is a count: a second unreported failure stops the match.
        fake = new FakeLlmTransport();
        fake.EnqueueError(LlmErrorKind.Timeout, "request timed out");
        fake.EnqueueError(LlmErrorKind.Network, "transport failure");
        SpectatorQueueSeason(fake, 2);
        session = TransportUsageRun(fake, 1000000, 1, out runner, 2);
        Check(session.State == SpectatorState.Paused && runner.StopCeiling == "session_usage_unknown"
              && runner.UnknownTransportRequests == 1,
            "a second unreported failure exceeds an allowance of 1 and stops the match");

        // 4. A reply that arrived without usage is not covered by the allowance.
        fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}");
        SpectatorQueueTurn(fake, "seat-b", 1);
        session = TransportUsageRun(fake, 1000000, 3, out runner);
        Check(session.State == SpectatorState.Paused && runner.StopCeiling == "session_usage_unknown"
              && runner.UnknownTransportRequests == 0 && fake.CallCount == 1,
            "a provider reply with no usage still stops the match even with an allowance");

        // 5. The allowance never hides a reached ceiling: reported usage still stops the match.
        fake = new FakeLlmTransport();
        fake.EnqueueError(LlmErrorKind.Network, "transport failure");
        SpectatorQueueTurn(fake, "seat-b", 1);
        session = TransportUsageRun(fake, 10, 1, out runner);
        Check(session.State == SpectatorState.Paused && runner.StopCeiling == "session_input_tokens",
            "reported usage still enforces the input ceiling while an unreported failure is tolerated");

        // 6. Configuration: bounded, off by default and absent from the record unless opted in.
        LlmConfig bounded = TransportUsageConfig(1000000, LlmConfig.ExperimentHardMaxUnknownTransportRequests + 1);
        Check(bounded.ValidateExperiment().Contains("max_unknown_transport_requests_out_of_range"),
            "the allowance has a hard upper bound");
        Check(!LlmConfig.RichDeliberation().ExperimentSettingsJson().Contains("unknown_transport")
              && !TransportUsageConfig(1000000, 0).Redacted().Contains("unknown_transport")
              && TransportUsageConfig(1000000, 2).ExperimentSettingsJson()
                   .Contains("\"max_unknown_transport_requests\":2"),
            "the setting appears in the recorded configuration only when it is opted in");

        // 7. The summary never calls a ceiling strict when the totals are incomplete or an allowance is on.
        Check(CeilingBasis(0, false) == "actual_usage",
            "a run where every request reported usage and no allowance is set reports actual usage");
        Check(CeilingBasis(1, false) == "reported_usage_only",
            "enabling the allowance reports reported usage only, even when no failure happened");
        Check(CeilingBasis(0, true) == "reported_usage_only",
            "a request without usage reports reported usage only");

        // 8. An invalid experiment configuration is a match-wide stop, never an empty "Completed" match.
        fake = new FakeLlmTransport();
        SpectatorQueueSeason(fake, 2);
        LlmConfig invalid = TransportUsageConfig(1000000, 0);
        invalid.MaxOutputTokens = 0;   // an unset output bound is refused by ValidateExperiment
        Check(invalid.ValidateExperiment().Count > 0, "the invalid fixture really fails experiment validation");
        runner = SpectatorRunner(fake, invalid);
        session = SpectatorNew(SpectatorEngine(), runner, 2);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused && fake.CallCount == 0
              && runner.StopCeiling == "experiment_configuration"
              && session.StopReason != null && session.StopReason.StartsWith("experiment configuration invalid"),
            "an invalid experiment configuration pauses the spectator with its reason and sends no request");
        Check(session.TurnRecords.Count == 1,
            "the invalid configuration stops at the first seat instead of skipping every seat");
    }

    private static string CeilingBasis(int allowance, bool unreportedFailure)
    {
        var fake = new FakeLlmTransport();
        if (unreportedFailure)
        {
            fake.EnqueueError(LlmErrorKind.Network, "transport failure");
            SpectatorQueueTurn(fake, "seat-b", 1);
        }
        else SpectatorQueueSeason(fake, 1);
        SpectatorSession session = TransportUsageRun(fake, 1000000, allowance, out LlmTurnRunner runner);
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "luanshi-ceiling-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            string path = SpectatorSummary.Write(dir, session, TransportUsageConfig(1000000, allowance), fake, runner);
            string json = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
            Match m = Regex.Match(json, "\"token_ceiling_basis\":\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch (System.Exception) { }
        }
    }
}
