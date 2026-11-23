using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class InterleaveDeferredTransport : ILlmTransport
    {
        public string Provider => "fake";
        public string Model => "fake";
        public readonly List<LlmRequest> Requests = new List<LlmRequest>();
        private TaskCompletionSource<LlmResponse> _pending;

        public Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            _pending = new TaskCompletionSource<LlmResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task;
        }

        public void Complete(string json, string reqId)
            => _pending.TrySetResult(LlmResponse.Success(json, 1, 10, 5, reqId, null));
    }

    private static void SpatialProductionInterleavedRunnersImpl()
    {
        GameEngine baseEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 29);
        var baseRun = RunOneCompactSeasonAsEndTurnWithRunner(baseEngine, true, 29);
        string baselineSystem = baseRun.Transport.Requests[1].System;
        string baselineUser = baseRun.Transport.Requests[1].User;

        GameEngine engOff = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 29);
        GameEngine engRich = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 29);

        var transportOff = new InterleaveDeferredTransport();
        var transportRich = new InterleaveDeferredTransport();
        var runnerOff = new LlmTurnRunner(transportOff, new LlmConfig { RequireVisibleMarchRoutes = true }, null);
        var runnerRich = new LlmTurnRunner(transportRich, new LlmConfig
        {
            RequireVisibleMarchRoutes = true,
            SpatialContext = new SpatialContextOptions
            {
                Level = SpatialDetailLevel.Rich,
                // The public map is mandatory prompt content, so the cap must leave room for it
                // (~700 chars on the compact map); 10500 no longer fits the minimum spatial prompt.
                ProviderCharLimit = 12000,
            },
        }, null);

        var sessOff = new SpectatorSession(engOff, runnerOff, 1, 29);
        var sessRich = new SpectatorSession(engRich, runnerRich, 1, 29);

        // Interleave seat-0 (Wei): Off dispatches first, Rich dispatches and finishes while Off is in flight.
        sessOff.Start();
        sessRich.Start();
        transportRich.Complete("{\"type\":\"end_turn\"}", "rich-wei");
        sessRich.Tick();
        transportOff.Complete("{\"type\":\"end_turn\"}", "off-wei");
        sessOff.Tick();

        // Now seat-1 (Shu): Off dispatches, Rich dispatches and finishes while Off is in flight.
        sessOff.Tick();
        sessRich.Tick();
        transportRich.Complete("{\"type\":\"end_turn\"}", "rich-shu");
        sessRich.Tick();
        transportOff.Complete("{\"type\":\"end_turn\"}", "off-shu");
        sessOff.Tick();

        Check(sessOff.State == SpectatorState.Completed
              && sessRich.State == SpectatorState.Completed
              && transportOff.Requests.Count == 2
              && transportRich.Requests.Count == 2
              && runnerOff.LastSpatialTruncation == null
              && runnerRich.LastSpatialTruncation != null
              && runnerRich.LastSpatialTruncation.ProviderCharLimit == 12000
              && runnerRich.LastSpatialTruncation.Truncated
              && string.Equals(transportOff.Requests[1].System, baselineSystem, StringComparison.Ordinal)
              && string.Equals(transportOff.Requests[1].User, baselineUser, StringComparison.Ordinal)
              && transportRich.Requests[1].User.Contains("context_config: level=rich provider_char_limit=12000", StringComparison.Ordinal),
            "interleaved runners with Off vs Rich settings do not contaminate each other's prompts or truncation reports");
    }
}
