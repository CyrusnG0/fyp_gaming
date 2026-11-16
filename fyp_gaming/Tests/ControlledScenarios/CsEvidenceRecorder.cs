using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>One production run line, parsed and paired with the state observed when it was written.</summary>
    public sealed class CsRunLine
    {
        public int Line;
        public JsonElement Root;
        public string SeatId;
        public int? TurnToken;
        public int? RequestIndex;
        public int? Season;
        /// <summary>Seat observation immediately after this dispatch was handled; null if unavailable.</summary>
        public CsWorldSnapshot After;
    }

    /// <summary>One production exchange line (request, result, experiment_config, turn_end).</summary>
    public sealed class CsExchangeLine
    {
        public int Line;
        public JsonElement Root;
        public string Event;
        public string RunId;
        public string SeatId;
        public int? TurnToken;
        public int? RequestIndex;
    }

    /// <summary>
    /// Thin wrapper around the production <see cref="LlmRunRecorder"/>: it writes the same
    /// run-*.jsonl and run-*-exchanges.ndjson files while retaining parsed lines and line numbers
    /// so every dispatch can be joined to its run, exchange and prompt records. It also snapshots
    /// the acting seat's state at the moment a result line is written (the "after" state).
    /// </summary>
    public sealed class CsEvidenceRecorder : ILlmRunRecorder, ILlmExchangeRecorder
    {
        private readonly object _gate = new object();
        private readonly LlmRunRecorder _inner;
        private readonly List<CsRunLine> _runs = new List<CsRunLine>();
        private readonly List<CsExchangeLine> _exchanges = new List<CsExchangeLine>();
        private int _runCount;
        private int _exchangeCount;

        public CsEvidenceRecorder(string directory)
        {
            _inner = new LlmRunRecorder(directory);
        }

        public string DirectoryPath => _inner.DirectoryPath;
        public string LastWriteError => _inner.LastWriteError;
        public bool CaptureExchanges => _inner.CaptureExchanges;
        public string RunId { get; private set; }

        /// <summary>Called for each result record; returns the acting seat's post-dispatch snapshot.</summary>
        public Func<string, CsWorldSnapshot> AfterSnapshot;

        public string RunPath => RunId == null ? null : Path.Combine(DirectoryPath, RunId + ".jsonl");
        public string ExchangePath => RunId == null ? null : _inner.ExchangeFilePathFor(RunId);

        /// <summary>Binds the production runner's id before the first write (the wrapper cannot
        /// call the inner recorder's internal UseRunId).</summary>
        public void BindRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return;
            lock (_gate) { if (RunId == null) RunId = runId; }
        }

        public List<CsRunLine> RunLines() { lock (_gate) return new List<CsRunLine>(_runs); }
        public List<CsExchangeLine> ExchangeLines() { lock (_gate) return new List<CsExchangeLine>(_exchanges); }

        public void Record(string runId, string jsonLine)
        {
            if (jsonLine == null) return;
            CsRunLine parsed = ParseRun(jsonLine);
            lock (_gate)
            {
                if (RunId == null) RunId = runId;
                if (parsed != null)
                {
                    parsed.Line = ++_runCount;
                    if (AfterSnapshot != null && !string.IsNullOrEmpty(parsed.SeatId))
                    {
                        try { parsed.After = AfterSnapshot(parsed.SeatId); }
                        catch (Exception) { parsed.After = null; }
                    }
                    _runs.Add(parsed);
                }
            }
            _inner.Record(runId, jsonLine);
        }

        public void RecordExchange(string runId, string jsonLine)
        {
            if (jsonLine == null) return;
            CsExchangeLine parsed = ParseExchange(jsonLine);
            lock (_gate)
            {
                if (RunId == null) RunId = runId;
                if (parsed != null)
                {
                    parsed.Line = ++_exchangeCount;
                    _exchanges.Add(parsed);
                }
            }
            _inner.RecordExchange(runId, jsonLine);
        }

        private static CsRunLine ParseRun(string jsonLine)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonLine))
                {
                    JsonElement root = doc.RootElement.Clone();
                    return new CsRunLine
                    {
                        Root = root,
                        SeatId = Text(root, "seat_id"),
                        TurnToken = Int(root, "turn_token"),
                        RequestIndex = Int(root, "request_index"),
                        Season = Int(root, "season"),
                    };
                }
            }
            catch (JsonException) { return null; }
        }

        private static CsExchangeLine ParseExchange(string jsonLine)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonLine))
                {
                    JsonElement root = doc.RootElement.Clone();
                    return new CsExchangeLine
                    {
                        Root = root,
                        Event = Text(root, "exchange_event"),
                        RunId = Text(root, "run_id"),
                        SeatId = Text(root, "seat_id"),
                        TurnToken = Int(root, "turn_token"),
                        RequestIndex = Int(root, "request_index"),
                    };
                }
            }
            catch (JsonException) { return null; }
        }

        public static string Text(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(name, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        public static int? Int(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n)) return n;
            return null;
        }

        public static long? Long(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n)) return n;
            return null;
        }

        public static bool? Bool(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return null;
        }
    }
}
