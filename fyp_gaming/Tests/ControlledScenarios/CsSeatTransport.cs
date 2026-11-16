using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>One exact dispatched request and the reply the fake returned, independent of the recorder.</summary>
    public sealed class CsDispatchCapture
    {
        public int Index;                 // 1-based across the scenario
        public string Seat;
        public string System;
        public string User;
        public string ReplyText;
        // Null (not false/zero) until a response exists: a refused or abandoned dispatch must not
        // look like a failed reply or a zero-latency one.
        public bool? Ok;
        public string ErrorKind;
        public string ErrorDetail;
        public string FinishReason;
        public int? InputTokens;
        public int? OutputTokens;
        public int? ReasoningTokens;
        public int? TotalTokens;
        public int? HttpStatus;
        public string ProviderRequestId;
        public long? ElapsedMs;
        public int? LatencyMs;
        public string ScriptNote;         // scripted | fallback_end_turn | fault:...
        public CsWorldSnapshot Before;
        /// <summary>
        /// Dispatch lifecycle, set by <see cref="CsRecordingTransport"/> (the evidence source of
        /// truth): in_flight | completed | failed | refused_before_dispatch |
        /// deadline_abandoned | timed_out | cancelled | transport_exception |
        /// no_response_before_seal.
        /// </summary>
        public string State;
        /// <summary>max_scenario_requests | max_run_requests | max_run_wall_seconds |
        /// max_scenario_wall_seconds, for a refused_before_dispatch capture.</summary>
        public string RefusalReason;
        /// <summary>State at response completion; the post-action state comes from the run line.</summary>
        public CsWorldSnapshot After;
    }

    /// <summary>
    /// Per-seat fake transport. It owns one <see cref="FakeLlmTransport"/> per seat, feeds it the
    /// next <see cref="CsScriptStep"/>, captures the exact prompt/reply, and sets the public
    /// response fields the fake's EnqueueSuccess cannot (finish reason, chosen usage). The test
    /// seam for ceilings is an oversized DelayMs, which the production runner sees as a timeout.
    /// </summary>
    public sealed class CsSeatTransport : ILlmTransport
    {
        private readonly object _gate = new object();
        private readonly Func<string> _activeSeat;
        private readonly Dictionary<string, Queue<CsScriptStep>> _scripts;
        private readonly Dictionary<string, FakeLlmTransport> _fakes =
            new Dictionary<string, FakeLlmTransport>(StringComparer.Ordinal);
        private readonly List<CsDispatchCapture> _captures = new List<CsDispatchCapture>();

        public CsSeatTransport(string model, Func<string> activeSeat,
            Dictionary<string, List<CsScriptStep>> scripts)
        {
            Model = string.IsNullOrEmpty(model) ? "fake-model" : model;
            _activeSeat = activeSeat;
            _scripts = new Dictionary<string, Queue<CsScriptStep>>(StringComparer.Ordinal);
            if (scripts != null)
                foreach (KeyValuePair<string, List<CsScriptStep>> kv in scripts)
                    _scripts[kv.Key] = new Queue<CsScriptStep>(kv.Value ?? new List<CsScriptStep>());
        }

        public string Provider => "fake";
        public string Model { get; }

        /// <summary>Called on the runner's main thread immediately before each dispatch.</summary>
        public Func<string, CsWorldSnapshot> BeforeHook;

        public int CallCount { get { lock (_gate) return _captures.Count; } }

        public List<CsDispatchCapture> Captures() { lock (_gate) return new List<CsDispatchCapture>(_captures); }

        public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            string seat = _activeSeat == null ? null : _activeSeat();
            if (string.IsNullOrEmpty(seat)) seat = "unknown";
            CsWorldSnapshot before = null;
            if (BeforeHook != null)
            {
                try { before = BeforeHook(seat); }
                catch (Exception) { before = null; }
            }

            CsScriptStep step;
            FakeLlmTransport fake;
            int index;
            bool fallback;
            lock (_gate)
            {
                index = _captures.Count + 1;
                if (!_scripts.TryGetValue(seat, out Queue<CsScriptStep> queue))
                {
                    // Compatibility with the design's single Script property: it applies to the
                    // active seat until an explicit per-seat queue exists.
                    if (!_scripts.TryGetValue("__active__", out queue))
                    {
                        queue = new Queue<CsScriptStep>();
                        _scripts[seat] = queue;
                    }
                }
                fallback = queue.Count == 0;
                step = fallback ? new CsScriptStep
                {
                    Text = "{\"type\":\"end_turn\",\"summary\":\"script exhausted\"}",
                } : queue.Dequeue();
                if (!_fakes.TryGetValue(seat, out fake))
                {
                    fake = new FakeLlmTransport { Model = Model };
                    _fakes[seat] = fake;
                }
                // A request_timeout fixture is delayed without observing cancellation, so the
                // production runner's own RequestTimedOut ceiling is the only bound. Any other
                // fault goes through the reusable fake transport.
                if (step.Fault != "request_timeout") Enqueue(fake, step, index, req);
            }

            var clock = Stopwatch.StartNew();
            LlmResponse response;
            try
            {
                if (step.Fault == "request_timeout")
                {
                    // Ignores ct on purpose: this models a provider that has not noticed the
                    // cancellation, so only the runner's request timeout can stop the turn.
                    await Task.Delay(step.DelayMs > 0 ? step.DelayMs : 30000).ConfigureAwait(false);
                    response = LlmResponse.Success(step.Text ?? "", step.DelayMs,
                        step.InputTokens, step.OutputTokens, "fake-req-" + index);
                }
                else
                {
                    response = await fake.SendAsync(req, ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // The production runner turns a throwing transport into a bounded failure; mirror it.
                response = LlmResponse.Failure(LlmErrorKind.Network, "fake transport failed");
            }
            clock.Stop();
            if (response == null) response = LlmResponse.Failure(LlmErrorKind.Network, "fake returned no response");
            ApplyStepFields(response, step, req, index);

            lock (_gate)
            {
                _captures.Add(new CsDispatchCapture
                {
                    Index = index, Seat = seat, System = req == null ? null : req.System,
                    User = req == null ? null : req.User, ReplyText = response.Text,
                    Ok = response.Ok, ErrorKind = response.Error.ToString(),
                    ErrorDetail = response.ErrorDetail, FinishReason = response.FinishReason,
                    InputTokens = response.InputTokens, OutputTokens = response.OutputTokens,
                    ReasoningTokens = response.ReasoningTokens, TotalTokens = response.TotalTokens,
                    HttpStatus = response.HttpStatus, ProviderRequestId = response.ProviderRequestId,
                    ElapsedMs = clock.ElapsedMilliseconds, LatencyMs = response.LatencyMs,
                    ScriptNote = fallback ? "fallback_end_turn" : NoteFor(step), Before = before,
                });
            }
            return response;
        }

        private static void Enqueue(FakeLlmTransport fake, CsScriptStep step, int index, LlmRequest req)
        {
            if (step.Fault == "provider_error")
            {
                fake.EnqueueError(LlmErrorKind.Server, "injected provider error", step.DelayMs);
                return;
            }
            string text = step.Text ?? "";
            if (step.Fault == "empty_reply") text = "";
            if (step.Fault == "length_truncated" && text.Length > 1)
                text = text.Substring(0, Math.Max(1, text.Length / 2));
            fake.EnqueueSuccess(text, step.DelayMs, step.InputTokens, step.OutputTokens, "fake-req-" + index);
        }

        /// <summary>Fills the public response fields the fake transport cannot set and keeps unknown null.</summary>
        private static void ApplyStepFields(LlmResponse response, CsScriptStep step, LlmRequest req, int index)
        {
            if (!response.Ok) return;
            if (step.Fault == "missing_usage")
            {
                response.InputTokens = null;
                response.OutputTokens = null;
                response.TotalTokens = null;
                response.ReasoningTokens = null;
                return;
            }

            int? input = step.InputTokens;
            int? output = step.OutputTokens;
            if (!input.HasValue && !output.HasValue && step.Fault != "length_truncated")
            {
                // Deterministic fake accounting, labelled fake_deterministic in the evidence.
                input = ((req?.System?.Length ?? 0) + (req?.User?.Length ?? 0)) / 4;
                output = (response.Text?.Length ?? 0) / 4 + 1;
            }
            if (step.Fault == "length_truncated")
            {
                response.FinishReason = "length";
                output = Math.Max(1, req?.MaxOutputTokens ?? 1);
            }
            else if (!string.IsNullOrEmpty(step.FinishReason))
            {
                response.FinishReason = step.FinishReason;
            }
            else if (string.IsNullOrEmpty(response.FinishReason))
            {
                response.FinishReason = "stop";
            }
            if (input.HasValue) response.InputTokens = input;
            if (output.HasValue) response.OutputTokens = output;
            response.TotalTokens = response.InputTokens.HasValue && response.OutputTokens.HasValue
                ? response.InputTokens.Value + response.OutputTokens.Value : (int?)null;
            response.HttpStatus = 200;
            if (string.IsNullOrEmpty(response.ProviderRequestId)) response.ProviderRequestId = "fake-req-" + index;
        }

        private static string NoteFor(CsScriptStep step)
        {
            if (!string.IsNullOrEmpty(step.Fault)) return "fault:" + step.Fault;
            return step.Text == null ? "fallback_end_turn" : "scripted";
        }
    }
}
