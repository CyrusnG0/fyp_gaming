using System;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Test-only adapter: appends a scenario goal to the user prompt of ONE seat before the
    /// request reaches the recording transport, so the evidence holds the exact final prompt.
    /// It sits outside production code and changes nothing else (limits, timeouts, system text).
    /// </summary>
    public sealed class CsGoalTransport : ILlmTransport
    {
        public const string Header = "\nSCENARIO GOAL (stated by the scenario host):\n";

        private readonly ILlmTransport _inner;
        private readonly Func<string> _activeSeat;
        private readonly string _seat;
        private readonly string _goal;

        public CsGoalTransport(ILlmTransport inner, Func<string> activeSeat, string seat, string goal)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _activeSeat = activeSeat;
            _seat = seat;
            _goal = goal;
        }

        public string Provider => _inner.Provider;
        public string Model => _inner.Model;

        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            string seat = _activeSeat == null ? null : _activeSeat();
            if (req != null && !string.IsNullOrEmpty(_goal) && string.Equals(seat, _seat, StringComparison.Ordinal))
                req = new LlmRequest(req.System, (req.User ?? "") + Header + _goal + "\n",
                    req.MaxOutputTokens, req.TimeoutMs);
            return _inner.SendAsync(req, ct);
        }
    }
}
