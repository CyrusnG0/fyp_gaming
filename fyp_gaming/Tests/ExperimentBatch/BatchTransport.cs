using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// Stand-in for the spatial-context feature, which is NOT integrated in the production runner or
    /// prompt. The switch only steers the scripted responder (which march it picks) and is recorded in
    /// every result as "fake-switch, not integrated". It never changes a production prompt, so the
    /// arms' prompts stay identical and a difference in outcome is scripting, not model behaviour.
    /// </summary>
    public sealed class SpatialSwitch
    {
        public const string AdapterId = "fake-switch";
        public const bool ProductionIntegrated = false;
        public bool On;
        public SpatialSwitch(bool on) { On = on; }
    }

    /// <summary>One dispatched request and the reply the fake returned, captured independently of the recorder.</summary>
    public sealed class ExchangeCapture
    {
        public int Index;               // 1-based across the match
        public string Seat;
        public int SeatRequest;         // 1-based per seat
        public string System;
        public string User;
        public string Fault;            // null when none was injected
        public bool Ok;
        public string Reply;
        public int? InputTokens;
        public int? OutputTokens;
        public string FinishReason;
        public string Error;
        public string ScriptNote;       // "scripted" | "defaulted_end_turn"
    }

    /// <summary>
    /// Scripted fake provider keyed by the ACTIVE SEAT (read from the spectator session), so each seat has
    /// its own reply script regardless of how requests interleave. Every request and reply is kept.
    /// Token usage is a deterministic function of the text; faults are injected by seat request index.
    /// </summary>
    public sealed class ScriptedSeatTransport : ILlmTransport
    {
        private readonly object _gate = new object();
        private readonly Func<string> _activeSeat;
        private readonly Dictionary<string, Queue<string>> _scripts;
        private readonly List<FaultSpec> _faults;
        private readonly Dictionary<string, int> _seatRequests = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<ExchangeCapture> _captures = new List<ExchangeCapture>();

        public ScriptedSeatTransport(string model, Func<string> activeSeat,
            Dictionary<string, List<string>> scripts, List<FaultSpec> faults)
        {
            Model = model;
            _activeSeat = activeSeat;
            _faults = faults ?? new List<FaultSpec>();
            _scripts = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<string>> kv in scripts) _scripts[kv.Key] = new Queue<string>(kv.Value);
        }

        public string Provider => "fake";
        public string Model { get; }

        public int CallCount { get { lock (_gate) return _captures.Count; } }
        public int Defaulted { get { lock (_gate) return _captures.FindAll(c => c.ScriptNote == "defaulted_end_turn").Count; } }

        public List<ExchangeCapture> Captures() { lock (_gate) return new List<ExchangeCapture>(_captures); }

        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ExchangeCapture cap;
            LlmResponse response;
            lock (_gate)
            {
                string seat = _activeSeat() ?? "unknown";
                _seatRequests.TryGetValue(seat, out int seatRequest);
                _seatRequests[seat] = ++seatRequest;
                cap = new ExchangeCapture
                {
                    Index = _captures.Count + 1, Seat = seat, SeatRequest = seatRequest,
                    System = req.System, User = req.User,
                };
                string fault = FaultFor(seat, seatRequest);
                cap.Fault = fault;
                response = Respond(cap, seat, fault, req);
                cap.Ok = response.Ok;
                cap.Reply = response.Text;
                cap.InputTokens = response.InputTokens;
                cap.OutputTokens = response.OutputTokens;
                cap.FinishReason = response.FinishReason;
                cap.Error = response.Ok ? null : response.Error + ": " + response.ErrorDetail;
                _captures.Add(cap);
            }
            return Task.FromResult(response);
        }

        private string FaultFor(string seat, int seatRequest)
        {
            foreach (FaultSpec f in _faults)
                if (f.Seat == seat && f.SeatRequest == seatRequest) return f.Kind;
            return null;
        }

        private LlmResponse Respond(ExchangeCapture cap, string seat, string fault, LlmRequest req)
        {
            if (fault == "provider_error")
            {
                cap.ScriptNote = "fault_without_script_step";
                return LlmResponse.Failure(LlmErrorKind.Server, "injected provider error", 5, null, 500);
            }
            string text;
            if (_scripts.TryGetValue(seat, out Queue<string> queue) && queue.Count > 0)
            {
                text = queue.Dequeue();
                cap.ScriptNote = "scripted";
            }
            else
            {
                text = "{\"type\":\"end_turn\",\"summary\":\"script exhausted\"}";
                cap.ScriptNote = "defaulted_end_turn";
            }
            int inputTokens = ((req.System?.Length ?? 0) + (req.User?.Length ?? 0)) / 4;
            int outputTokens = text.Length / 4 + 1;
            string finish = "stop";
            int? reportedIn = inputTokens, reportedOut = outputTokens;
            if (fault == "missing_usage") { reportedIn = null; reportedOut = null; }
            else if (fault == "empty_reply") { text = ""; reportedOut = 1; }
            else if (fault == "length_truncated")
            {
                text = text.Substring(0, Math.Max(1, text.Length / 2));
                finish = "length";
                reportedOut = req.MaxOutputTokens;
            }
            int? total = reportedIn.HasValue && reportedOut.HasValue ? reportedIn + reportedOut : null;
            return LlmResponse.Success(text, 5, reportedIn, reportedOut, "fake-req-" + (_captures.Count + 1), finish, null, total, null, null, 200);
        }
    }
}
