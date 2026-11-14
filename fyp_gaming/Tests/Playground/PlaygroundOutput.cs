using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LuanShi.Playground
{
    /// <summary>Writes a readable trace and machine-readable results for one run.</summary>
    internal static class PlaygroundOutput
    {
        public static void Write(PlaygroundRunRecord record, string directory)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("an output directory is required", nameof(directory));
            Directory.CreateDirectory(directory);
            string jsonPath = Path.Combine(directory, "results.json");
            string tracePath = Path.Combine(directory, "trace.txt");
            File.WriteAllText(jsonPath, ResultsJson(record), new UTF8Encoding(false));
            File.WriteAllText(tracePath, TraceText(record), new UTF8Encoding(false));
        }

        public static string ResultsJson(PlaygroundRunRecord record)
        {
            var turns = new List<string>();
            foreach (PlaygroundTurnRecord turn in record.Turns) turns.Add(TurnJson(turn));
            var requests = new List<string>();
            foreach (PlaygroundRequestRecord request in record.Requests) requests.Add(RequestJson(request));
            var inquiries = new List<string>();
            foreach (PlaygroundInquiryRecord inquiry in record.Inquiries) inquiries.Add(InquiryJson(inquiry));
            var actions = new List<string>();
            foreach (PlaygroundActionRecord action in record.Actions) actions.Add(ActionJson(action));
            var checks = new List<string>();
            foreach (PlaygroundActionRecord check in record.Checks) checks.Add(ActionJson(check));
            var notes = new List<string>();
            foreach (PlaygroundNoteRecordView note in record.Notes) notes.Add(NoteJson(note));
            var assertions = new List<string>();
            foreach (PlaygroundAssertion assertion in record.Assertions) assertions.Add(AssertionJson(assertion));

            return PlaygroundJson.Obj(
                "schema_version", PlaygroundJson.Str(PlaygroundRunRecord.SchemaVersion),
                "kind", PlaygroundJson.Str(record.Kind),
                "run_id", PlaygroundJson.Str(record.RunId),
                "provider", PlaygroundJson.Str(record.Provider),
                "fake_provider", PlaygroundJson.Bool(record.Fake),
                "seed", PlaygroundJson.Num(record.Seed),
                "game_id", PlaygroundJson.Str(record.GameId),
                "scenario", PlaygroundJson.Str(record.ScenarioId),
                "prompt", PlaygroundJson.Obj(
                    "base_version", PlaygroundJson.Str(record.PromptBaseVersion),
                    "addendum_version", PlaygroundJson.Str(record.PromptAddendumVersion),
                    "combined_version", PlaygroundJson.Str(
                        record.PromptBaseVersion + "+" + record.PromptAddendumVersion)),
                "config", record.ConfigJson ?? "null",
                "start_state_hash", PlaygroundJson.Str(record.StartStateHash),
                "end_state_hash", PlaygroundJson.Str(record.EndStateHash),
                "totals", PlaygroundJson.Obj(
                    "requests", PlaygroundJson.Num(record.Requests.Count),
                    "accepted_actions", PlaygroundJson.Num(record.AcceptedActions),
                    "rejected_actions", PlaygroundJson.Num(record.RejectedActions),
                    "inquiries", PlaygroundJson.Num(record.Inquiries.Count),
                    "notes", PlaygroundJson.Num(record.Notes.Count)),
                "turns", PlaygroundJson.Arr(turns),
                "requests", PlaygroundJson.Arr(requests),
                "inquiries", PlaygroundJson.Arr(inquiries),
                "actions", PlaygroundJson.Arr(actions),
                "checks", PlaygroundJson.Arr(checks),
                "notes", PlaygroundJson.Arr(notes),
                "budget", record.BudgetJson ?? "null",
                "assertions", PlaygroundJson.Arr(assertions),
                "errors", PlaygroundJson.StrArr(record.Errors));
        }

        private static string TurnJson(PlaygroundTurnRecord turn)
            => PlaygroundJson.Obj(
                "seat_id", PlaygroundJson.Str(turn.SeatId),
                "season", PlaygroundJson.Num(turn.Season),
                "turn_token", PlaygroundJson.Num(turn.TurnToken),
                "state_hash_before", PlaygroundJson.Str(turn.StateHashBefore),
                "state_hash_after", PlaygroundJson.Str(turn.StateHashAfter),
                "note_shown", PlaygroundJson.Str(turn.NoteShown),
                "note_saved", PlaygroundJson.Str(turn.NoteSaved),
                "end_reason", PlaygroundJson.Str(turn.EndReason),
                "request_start_index", PlaygroundJson.Num(turn.RequestStartIndex),
                "request_count", PlaygroundJson.Num(turn.RequestCount),
                "command_points_before", PlaygroundJson.Num(turn.CpBefore),
                "command_points_after", PlaygroundJson.Num(turn.CpAfter),
                "inquiries", PlaygroundJson.Num(turn.InquiryCount),
                "actions", PlaygroundJson.Num(turn.ActionCount));

        private static string RequestJson(PlaygroundRequestRecord request)
            => PlaygroundJson.Obj(
                "index", PlaygroundJson.Num(request.Index),
                "seat_id", PlaygroundJson.Str(request.SeatId),
                "season", PlaygroundJson.Num(request.Season),
                "turn_token", PlaygroundJson.Num(request.TurnToken),
                "prompt_version", PlaygroundJson.Str(request.PromptVersion),
                "prompt_base_version", PlaygroundJson.Str(request.PromptBaseVersion),
                "prompt_addendum_version", PlaygroundJson.Str(request.PromptAddendumVersion),
                "schema_hash", PlaygroundJson.Str(request.SchemaHash),
                "seed", PlaygroundJson.Num(request.Seed),
                "system", PlaygroundJson.Str(request.System),
                "user", PlaygroundJson.Str(request.User),
                "observation", PlaygroundJson.Str(request.ObservationJson),
                "offered_actions", PlaygroundJson.Str(request.OfferedActions),
                "option_count", PlaygroundJson.Num(request.OptionCount),
                "memory", PlaygroundJson.StrArr(request.MemoryLines),
                "prior_inquiry_results", PlaygroundJson.StrArr(request.PriorInquiryResults),
                "note_shown", PlaygroundJson.Str(request.NoteShown),
                "remaining", request.RemainingJson ?? "null",
                "config", request.ConfigJson ?? "null",
                "admission", PlaygroundJson.Str(request.Admission),
                "response_kind", PlaygroundJson.Str(request.ResponseKind),
                "response_text", PlaygroundJson.Str(request.ResponseText),
                "input_tokens", request.InputTokens.HasValue ? PlaygroundJson.Num(request.InputTokens.Value) : "null",
                "output_tokens", request.OutputTokens.HasValue ? PlaygroundJson.Num(request.OutputTokens.Value) : "null",
                "latency_ms", PlaygroundJson.Num(request.LatencyMs),
                "elapsed_ms", PlaygroundJson.Num(request.ElapsedMs),
                "outcome", PlaygroundJson.Str(request.Outcome),
                "detail", PlaygroundJson.Str(request.Detail));

        private static string InquiryJson(PlaygroundInquiryRecord inquiry)
            => PlaygroundJson.Obj(
                "request_index", PlaygroundJson.Num(inquiry.RequestIndex),
                "seat_id", PlaygroundJson.Str(inquiry.SeatId),
                "season", PlaygroundJson.Num(inquiry.Season),
                "tool", PlaygroundJson.Str(inquiry.Tool),
                "request_json", PlaygroundJson.Str(inquiry.RequestJson),
                "result_json", PlaygroundJson.Str(inquiry.ResultJson),
                "result_chars", PlaygroundJson.Num(inquiry.ResultChars),
                "ok", PlaygroundJson.Bool(inquiry.Ok),
                "error_code", PlaygroundJson.Str(inquiry.ErrorCode),
                "truncated", PlaygroundJson.Bool(inquiry.Truncated),
                "elapsed_ms", PlaygroundJson.Num(inquiry.ElapsedMs),
                "command_points_before", PlaygroundJson.Num(inquiry.CpBefore),
                "command_points_after", PlaygroundJson.Num(inquiry.CpAfter),
                "action_admissions_before", PlaygroundJson.Num(inquiry.ActionAdmissionsBefore),
                "action_admissions_after", PlaygroundJson.Num(inquiry.ActionAdmissionsAfter),
                "retained_index", PlaygroundJson.Num(inquiry.RetainedIndex));

        private static string ActionJson(PlaygroundActionRecord action)
            => PlaygroundJson.Obj(
                "request_index", PlaygroundJson.Num(action.RequestIndex),
                "seat_id", PlaygroundJson.Str(action.SeatId),
                "season", PlaygroundJson.Num(action.Season),
                "type", PlaygroundJson.Str(action.Type),
                "command_json", PlaygroundJson.Str(action.CommandJson),
                "check_requested", PlaygroundJson.Bool(action.CheckRequested),
                "check_ran", PlaygroundJson.Bool(action.CheckRan),
                "check_ok", PlaygroundJson.Bool(action.CheckOk),
                "check_error", PlaygroundJson.Str(action.CheckError),
                "replay_matched", PlaygroundJson.Bool(action.ReplayMatched),
                "replay_state_hash", PlaygroundJson.Str(action.ReplayStateHash),
                "live_state_hash_before_check", PlaygroundJson.Str(action.LiveStateHashBeforeCheck),
                "live_state_hash_after_check", PlaygroundJson.Str(action.LiveStateHashAfterCheck),
                "live_state_hash_before_execute", PlaygroundJson.Str(action.LiveStateHashBeforeExecute),
                "live_state_hash_after_execute", PlaygroundJson.Str(action.LiveStateHashAfterExecute),
                "executed", PlaygroundJson.Bool(action.Executed),
                "accepted", PlaygroundJson.Bool(action.Accepted),
                "engine_error", PlaygroundJson.Str(action.EngineError),
                "feedback_shown", PlaygroundJson.Str(action.FeedbackShown),
                "command_points_before", PlaygroundJson.Num(action.CpBefore),
                "command_points_after", PlaygroundJson.Num(action.CpAfter),
                "action_admissions_before", PlaygroundJson.Num(action.ActionAdmissionsBefore),
                "action_admissions_after", PlaygroundJson.Num(action.ActionAdmissionsAfter));

        private static string NoteJson(PlaygroundNoteRecordView note)
            => PlaygroundJson.Obj(
                "request_index", PlaygroundJson.Num(note.RequestIndex),
                "seat_id", PlaygroundJson.Str(note.SeatId),
                "season", PlaygroundJson.Num(note.Season),
                "event", PlaygroundJson.Str(note.Event),
                "text", PlaygroundJson.Str(note.Text),
                "chars", PlaygroundJson.Num(note.Chars));

        private static string AssertionJson(PlaygroundAssertion assertion)
            => PlaygroundJson.Obj(
                "name", PlaygroundJson.Str(assertion.Name),
                "ok", PlaygroundJson.Bool(assertion.Ok),
                "detail", PlaygroundJson.Str(assertion.Detail));

        public static string TraceText(PlaygroundRunRecord record)
        {
            var sb = new StringBuilder(16384);
            sb.Append("LuanShi headless playground trace\n");
            sb.Append("run_id: ").Append(record.RunId).Append('\n');
            sb.Append("provider: ").Append(record.Provider)
              .Append(record.Fake ? " (FAKE; no network calls)" : " (REAL provider requested locally)").Append('\n');
            sb.Append("seed: ").Append(record.Seed).Append('\n');
            sb.Append("game_id: ").Append(record.GameId).Append('\n');
            sb.Append("scenario: ").Append(record.ScenarioId).Append('\n');
            sb.Append("prompt_version: ").Append(record.PromptBaseVersion).Append('+')
              .Append(record.PromptAddendumVersion).Append('\n');
            sb.Append("start_state_hash: ").Append(record.StartStateHash).Append('\n');
            sb.Append("end_state_hash: ").Append(record.EndStateHash).Append('\n');
            sb.Append("config:\n").Append(record.ConfigJson).Append("\n\n");

            foreach (PlaygroundRequestRecord request in record.Requests)
            {
                sb.Append("=== REQUEST ").Append(request.Index).Append(" | seat ").Append(request.SeatId)
                  .Append(" | season ").Append(request.Season).Append(" | turn ").Append(request.TurnToken)
                  .Append(" | outcome ").Append(request.Outcome).Append(" ===\n");
                sb.Append("prompt_version: ").Append(request.PromptVersion).Append('\n');
                sb.Append("prompt_base_version: ").Append(request.PromptBaseVersion).Append('\n');
                sb.Append("prompt_addendum_version: ").Append(request.PromptAddendumVersion).Append('\n');
                sb.Append("schema_hash: ").Append(request.SchemaHash).Append('\n');
                sb.Append("seed: ").Append(request.Seed).Append('\n');
                sb.Append("admission: ").Append(request.Admission).Append('\n');
                sb.Append("remaining: ").Append(request.RemainingJson).Append('\n');
                sb.Append("observation_json:\n").Append(request.ObservationJson).Append('\n');
                sb.Append("offered_actions:\n").Append(request.OfferedActions).Append('\n');
                sb.Append("memory:\n").Append(ListText(request.MemoryLines)).Append('\n');
                sb.Append("prior_inquiry_results:\n").Append(ListText(request.PriorInquiryResults)).Append('\n');
                sb.Append("note_shown: ").Append(request.NoteShown ?? "(none)").Append("\n");
                sb.Append("relevant_config:\n").Append(request.ConfigJson).Append('\n');
                sb.Append("--- SYSTEM (exact) ---\n").Append(request.System).Append('\n');
                sb.Append("--- USER (exact) ---\n").Append(request.User).Append('\n');
                sb.Append("--- RESPONSE (fake) ---\n");
                sb.Append("kind: ").Append(request.ResponseKind).Append('\n');
                sb.Append("input_tokens: ").Append(request.InputTokens?.ToString() ?? "unknown")
                  .Append("; output_tokens: ").Append(request.OutputTokens?.ToString() ?? "unknown")
                  .Append("; ledger_elapsed_ms: ").Append(request.ElapsedMs).Append('\n');
                sb.Append("text:\n").Append(request.ResponseText).Append('\n');
                sb.Append("--- OUTCOME ---\n").Append(request.Outcome)
                  .Append(request.Detail == null ? "" : " :: " + request.Detail).Append("\n\n");
            }

            sb.Append("=== TURNS ===\n");
            foreach (PlaygroundTurnRecord turn in record.Turns)
                sb.Append(turn.SeatId).Append(" season ").Append(turn.Season)
                  .Append(" token ").Append(turn.TurnToken)
                  .Append(" end=").Append(turn.EndReason)
                  .Append(" cp ").Append(turn.CpBefore).Append("->").Append(turn.CpAfter)
                  .Append(" requests=").Append(turn.RequestCount)
                  .Append(" inquiries=").Append(turn.InquiryCount)
                  .Append(" actions=").Append(turn.ActionCount)
                  .Append(" hash_before=").Append(turn.StateHashBefore)
                  .Append(" hash_after=").Append(turn.StateHashAfter).Append('\n');

            sb.Append("\n=== INQUIRIES ===\n");
            foreach (PlaygroundInquiryRecord inquiry in record.Inquiries)
                sb.Append("req ").Append(inquiry.RequestIndex).Append(' ').Append(inquiry.SeatId)
                  .Append(' ').Append(inquiry.Tool)
                  .Append(" ok=").Append(inquiry.Ok)
                  .Append(" chars=").Append(inquiry.ResultChars)
                  .Append(" cp=").Append(inquiry.CpBefore).Append("->").Append(inquiry.CpAfter)
                  .Append(" result=").Append(PlaygroundJson.Clean(inquiry.ResultJson)).Append('\n');

            sb.Append("\n=== ACTIONS ===\n");
            foreach (PlaygroundActionRecord action in record.Actions)
                sb.Append("req ").Append(action.RequestIndex).Append(' ').Append(action.SeatId)
                  .Append(' ').Append(action.Type)
                  .Append(" check=").Append(action.CheckRan
                      ? (action.CheckOk ? "valid" : "invalid:" + action.CheckError) : "off")
                  .Append(" replay_matched=").Append(action.ReplayMatched)
                  .Append(" executed=").Append(action.Executed)
                  .Append(" accepted=").Append(action.Accepted)
                  .Append(" hash_check_before=").Append(action.LiveStateHashBeforeCheck)
                  .Append(" hash_check_after=").Append(action.LiveStateHashAfterCheck)
                  .Append(" hash_exec_before=").Append(action.LiveStateHashBeforeExecute)
                  .Append(" hash_exec_after=").Append(action.LiveStateHashAfterExecute)
                  .Append('\n');

            sb.Append("\n=== NOTES ===\n");
            foreach (PlaygroundNoteRecordView note in record.Notes)
                sb.Append(note.Event).Append(" seat=").Append(note.SeatId)
                  .Append(" season=").Append(note.Season)
                  .Append(" chars=").Append(note.Chars)
                  .Append(" text=").Append(note.Text ?? "(rejected)").Append('\n');

            sb.Append("\n=== BUDGET ===\n").Append(record.BudgetJson).Append('\n');
            sb.Append("\n=== ASSERTIONS ===\n");
            foreach (PlaygroundAssertion assertion in record.Assertions)
                sb.Append(assertion.Ok ? "PASS " : "FAIL ").Append(assertion.Name)
                  .Append(assertion.Detail == null ? "" : " :: " + assertion.Detail).Append('\n');
            return sb.ToString();
        }

        private static string ListText(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return "(none)";
            var sb = new StringBuilder();
            foreach (string value in values) sb.Append("- ").Append(PlaygroundJson.Clean(value)).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }
    }
}
