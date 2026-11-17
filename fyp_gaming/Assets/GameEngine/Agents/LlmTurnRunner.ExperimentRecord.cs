using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Experiment evidence: what the model chose (inquiry, note, order, end_turn), host-measured
    /// timing and command points, one settings line per run and one turn-end line per turn naming the
    /// ceiling that stopped it. Nothing here claims to observe private model reasoning: provider
    /// reasoning figures remain counts in the ordinary run record.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        // ---- recording ------------------------------------------------------------------

        private int? ObserveCommandPoints()
        {
            if (_host == null) return null;
            try
            {
                Observation fresh = _host.Observe();
                return fresh != null && string.Equals(fresh.SeatId, _seatId, StringComparison.Ordinal)
                    ? fresh.CommandPoints : (int?)null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Builds the optional <c>deliberation_event</c> object for the record being written. It
        /// describes what the model chose (inquiry, note, order, end_turn, invalid reply), timing the
        /// host measured and the command-point count before and after. It never contains model
        /// reasoning, the inquiry result text or note text.
        /// </summary>
        private void SetDeliberationEvent(string kind, string tool, string outcome, long execMs,
            int? resultChars, InFlight flight)
        {
            if (!ExperimentActive) return;
            DeliberationBudgetSnapshot snap = _deliberationLedger?.Snapshot();
            var sb = new StringBuilder(256);
            sb.Append("{\"kind\":").Append(MiniJson.Str(kind));
            if (!string.IsNullOrEmpty(tool)) sb.Append(",\"tool\":").Append(MiniJson.Str(tool));
            sb.Append(",\"outcome\":").Append(MiniJson.Str(outcome));
            sb.Append(",\"exec_ms\":").Append(execMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"result_chars\":").Append(resultChars.HasValue
                ? resultChars.Value.ToString(CultureInfo.InvariantCulture) : "null");
            int? before = flight?.Observation?.CommandPoints;
            int? after = ObserveCommandPoints();
            sb.Append(",\"command_points_before\":").Append(before.HasValue
                ? before.Value.ToString(CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"command_points_after\":").Append(after.HasValue
                ? after.Value.ToString(CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"accepted_actions_turn\":").Append(
                _acceptedActionsThisTurn.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"inquiries_turn\":").Append(InquiriesThisTurn.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"notes_turn\":").Append(NotesSavedThisTurn.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"inquiries_session\":").Append(
                (snap?.Session?.Inquiries ?? 0).ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            _pendingDeliberationEvent = sb.ToString();
        }

        /// <summary>Takes the explicit event, or infers one for an order/end_turn/invalid reply.</summary>
        private string TakeDeliberationEvent(InFlight flight, bool parseOk, string commandJson,
            bool? resultOk, string inquiryTool)
        {
            if (!ExperimentActive) { _pendingDeliberationEvent = null; return null; }
            if (_pendingDeliberationEvent == null && string.IsNullOrEmpty(inquiryTool))
            {
                if (commandJson != null)
                    SetDeliberationEvent("action", null, resultOk == true ? "accepted" : "rejected", 0, null, flight);
                else if (parseOk && LastResult == "end_turn")
                    SetDeliberationEvent("end_turn", null, "model_ended_turn", 0, null, flight);
                else
                    SetDeliberationEvent("no_decision", null, parseOk ? "no_command" : "invalid_or_failed",
                        0, null, flight);
            }
            string evt = _pendingDeliberationEvent;
            _pendingDeliberationEvent = null;
            return evt;
        }

        /// <summary>One experiment_config exchange line per runner: every ceiling and bound in force.</summary>
        private void RecordExperimentSettings(string promptVersion, string schemaHash)
        {
            if (_settingsRecorded || !ExperimentActive) return;
            _settingsRecorded = true;
            ILlmExchangeRecorder sink = ExchangeRecorder;
            if (sink == null) return;
            try
            {
                var parts = new List<string>(8)
                {
                    Str("record_type", "llm_exchange"),
                    Str("exchange_event", "experiment_config"),
                    Str("run_id", _runId),
                    Str("provider", _transport.Provider),
                    Str("model", _transport.Model),
                    Str("prompt_version", promptVersion),
                    Str("schema_hash", schemaHash),
                    Str("captured_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                    "\"settings\":" + _config.ExperimentSettingsJson(),
                };
                sink.RecordExchange(_runId, "{" + string.Join(",", parts) + "}");
            }
            catch (Exception) { /* evidence only */ }
        }

        /// <summary>One turn_end exchange line per turn: which ceiling or condition stopped it.</summary>
        private void RecordTurnEnd()
        {
            // Experiment turns always record turn_end. A non-experiment turn records one only when a
            // prompt-size failure produced counts-only diagnostics, so feature-off runs without such
            // a failure keep their historical record set.
            if (_turnEndRecorded || (!ExperimentActive && LastPromptSizeJson == null)) return;
            _turnEndRecorded = true;
            ILlmExchangeRecorder sink = ExchangeRecorder;
            if (sink == null) return;
            try
            {
                DeliberationBudgetSnapshot snap = _deliberationLedger?.Snapshot();
                string ceiling = StopCeiling;
                var parts = new List<string>(20)
                {
                    Str("record_type", "llm_exchange"),
                    Str("exchange_event", "turn_end"),
                    Str("run_id", _runId),
                    Str("seat_id", _seatId),
                    Num("turn_token", _turnToken),
                    Num("season", _season),
                    Str("end_reason", EndReason.ToString()),
                    Str("stop_ceiling", ceiling),
                    Bool("stopped_by_ceiling", ceiling != null && EndReason != EndReason.GameOver
                        && EndReason != EndReason.Cancelled),
                    Str("failure_reason", Redact(FailureReason)),
                    Num("requests_this_turn", RequestsThisTurn),
                    Num("accepted_actions_this_turn", _acceptedActionsThisTurn),
                    Num("inquiries_this_turn", InquiriesThisTurn),
                    Num("notes_this_turn", NotesSavedThisTurn),
                    Num("invalid_this_turn", InvalidThisTurn),
                    Str("captured_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                    "\"ledger\":" + (snap == null ? "null" : snap.ToJson()),
                };
                if (LastPromptSizeJson != null)
                    parts.Add("\"prompt_size\":" + LastPromptSizeJson);
                sink.RecordExchange(_runId, "{" + string.Join(",", parts) + "}");
            }
            catch (Exception) { /* evidence only */ }
        }
    }
}
