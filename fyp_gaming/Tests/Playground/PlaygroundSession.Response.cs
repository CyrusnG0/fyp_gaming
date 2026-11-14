using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Response classification and the three allowed non-action outcomes. Every failure path is
    /// bounded, counted against the invalid-response allowance and returns without submitting.
    /// </summary>
    internal sealed partial class PlaygroundSession
    {
        private TurnStep HandleResponse(PlaygroundRequestRecord request, LlmResponse response,
            string seatId, Observation observation, int token, ref int actions, ref int inquiries,
            ref int invalid, ref string noteSaved)
        {
            if (!response.Ok)
            {
                request.Outcome = "provider_error:" + response.Error.ToString().ToLowerInvariant();
                request.Detail = response.ErrorDetail;
                string endReason = response.Error == LlmErrorKind.Timeout ? "timeout" : "provider_error";
                return TurnStep.StopWith(endReason);
            }

            string text = response.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                request.Outcome = "empty_response";
                request.Detail = "the fake provider returned no content";
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            if (!StrictJson.TryParse(text, out JsonNode root, out _) || !(root is JsonObject obj))
            {
                request.Outcome = "invalid_json";
                request.Detail = "response is not one JSON object";
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            if (obj.TryGet("tool", out _))
                return HandleInquiry(request, text, obj, seatId, observation, ref inquiries, ref invalid);
            if (obj.TryGet("note", out _))
                return HandleNote(request, obj, seatId, ref noteSaved, ref invalid);
            return HandleAction(request, text, seatId, observation, token, ref actions, ref invalid);
        }

        private TurnStep HandleInquiry(PlaygroundRequestRecord request, string text, JsonObject obj,
            string seatId, Observation observation, ref int inquiries, ref int invalid)
        {
            InvestigationRequestOptions options = InvestigationRequestOptions.Defaults();
            options.ServiceOptions.MaxResponseChars = Math.Max(InvestigationOptions.MinimumResponseChars,
                _config.InquiryMaxResponseChars);
            InvestigationRequestParseResult parsed = InvestigationRequestParser.Parse(text, options);
            if (!parsed.Ok)
            {
                request.Outcome = "invalid_inquiry:" + parsed.ErrorCode;
                request.Detail = parsed.Error;
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            InvestigationRequest inquiry = parsed.Request;
            FactionState faction = _engine.State.FindFaction(seatId);
            int cpBefore = faction == null ? 0 : faction.CommandPoints;
            long actionsBefore = _ledger.Snapshot().Session.ActionAdmissions;

            DeliberationAdmission admission = _ledger.TryAdmitInquiry();
            if (!admission.Allowed)
            {
                _record.Inquiries.Add(new PlaygroundInquiryRecord
                {
                    RequestIndex = request.Index, SeatId = seatId, Season = observation.Season,
                    Tool = inquiry.ToolName, RequestJson = text,
                    Ok = false, ErrorCode = admission.Code,
                    CpBefore = cpBefore, CpAfter = cpBefore,
                    ActionAdmissionsBefore = actionsBefore, ActionAdmissionsAfter = actionsBefore,
                });
                request.Outcome = "inquiry_denied:" + admission.Code;
                request.Detail = admission.Message;
                return TurnStep.StopWith("inquiry_budget:" + admission.Code);
            }

            DeliberationBindingResult dispatch = _ledger.MarkDispatched(admission.ReservationId);
            if (!dispatch.Ok)
            {
                _ledger.Cancel(admission.ReservationId, DeliberationCancelReason.Aborted);
                _record.Inquiries.Add(new PlaygroundInquiryRecord
                {
                    RequestIndex = request.Index, SeatId = seatId, Season = observation.Season,
                    Tool = inquiry.ToolName, RequestJson = text,
                    Ok = false, ErrorCode = "dispatch_" + dispatch.Code,
                    CpBefore = cpBefore, CpAfter = cpBefore,
                    ActionAdmissionsBefore = actionsBefore, ActionAdmissionsAfter = actionsBefore,
                });
                request.Outcome = "inquiry_dispatch_denied:" + dispatch.Code;
                request.Detail = dispatch.Message;
                return TurnStep.StopWith("inquiry_dispatch:" + dispatch.Code);
            }

            string resultJson;
            bool truncated = false;
            bool resultOk = false;
            string resultErrorCode = null;
            string failureCode = null;
            try
            {
                var service = new InvestigationService(_config.GameId, seatId, observation,
                    _memories, options.ServiceOptions);
                InvestigationResult result = new InvestigationDispatcher(service).Dispatch(inquiry);
                resultJson = result == null ? null : result.ToJson();
                truncated = result != null && result.Truncated;
                resultOk = result != null && result.Ok;
                resultErrorCode = result == null ? null : result.ErrorCode;
                if (string.IsNullOrEmpty(resultJson)) failureCode = "inquiry_failed";
                else if (resultJson.Length > options.ServiceOptions.MaxResponseChars)
                    failureCode = "response_too_large";
            }
            catch (Exception)
            {
                resultJson = null;
                failureCode = "inquiry_failed";
            }

            if (failureCode != null)
                resultJson = BoundedInquiryFailure(inquiry.ToolName, failureCode);

            _clock.Advance(_config.SimulatedInquiryMs);
            _ledger.Settle(admission.ReservationId,
                new DeliberationSettlement { ElapsedMillis = _config.SimulatedInquiryMs });

            _pendingInquiryResult = resultJson;
            _retainedInquiryResults.Add(resultJson);
            while (_retainedInquiryResults.Count > _config.MaxRetainedInquiryResults)
                _retainedInquiryResults.RemoveAt(0);
            int retainedIndex = _retainedInquiryResults.Count - 1;

            FactionState after = _engine.State.FindFaction(seatId);
            int cpAfter = after == null ? 0 : after.CommandPoints;
            long actionsAfter = _ledger.Snapshot().Session.ActionAdmissions;
            _record.Inquiries.Add(new PlaygroundInquiryRecord
            {
                RequestIndex = request.Index, SeatId = seatId, Season = observation.Season,
                Tool = inquiry.ToolName, RequestJson = text, ResultJson = resultJson,
                ResultChars = resultJson.Length, Ok = failureCode == null && resultOk,
                ErrorCode = failureCode ?? resultErrorCode, Truncated = truncated,
                ElapsedMs = _config.SimulatedInquiryMs,
                CpBefore = cpBefore, CpAfter = cpAfter,
                ActionAdmissionsBefore = actionsBefore, ActionAdmissionsAfter = actionsAfter,
                RetainedIndex = retainedIndex,
            });

            inquiries++;
            AddHistory(seatId, "inquiry " + inquiry.ToolName + " -> "
                + (failureCode == null ? "answered (" + resultJson.Length + " chars)" : failureCode));
            request.Outcome = "inquiry_result";
            request.Detail = inquiry.ToolName + " result " + resultJson.Length + " chars"
                + (truncated ? " (truncated)" : "");
            return TurnStep.Continue();
        }

        private TurnStep HandleNote(PlaygroundRequestRecord request, JsonObject obj, string seatId,
            ref string noteSaved, ref int invalid)
        {
            string text = null;
            bool onlyNote = obj.Items.Count == 1;
            if (onlyNote && obj.TryGet("note", out JsonNode node) && node is JsonString value)
                text = value.Value;

            string error = null;
            if (!onlyNote) error = "note_must_be_alone";
            else if (string.IsNullOrEmpty(text)) error = "empty_note";
            else if (text.Length > _config.MaxNoteChars) error = "note_too_long";
            else
                foreach (char c in text)
                    if (char.IsControl(c)) { error = "invalid_character"; break; }

            if (error != null)
            {
                request.Outcome = "note_rejected:" + error;
                request.Detail = "bounded note was not saved";
                _record.Notes.Add(new PlaygroundNoteRecordView
                {
                    RequestIndex = request.Index, SeatId = seatId,
                    Season = _engine.State.Season, Event = "rejected", Text = null, Chars = 0,
                });
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            string clean = PlaygroundJson.Clean(text).Trim();
            if (clean.Length == 0)
            {
                request.Outcome = "note_rejected:empty_note";
                request.Detail = "bounded note was not saved";
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            if (!_noteStore.Save(_config.GameId, seatId, clean, _engine.State.Season))
            {
                request.Outcome = "note_rejected:storage_failed";
                request.Detail = "bounded note was not saved";
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            noteSaved = clean;
            _record.Notes.Add(new PlaygroundNoteRecordView
            {
                RequestIndex = request.Index, SeatId = seatId, Season = _engine.State.Season,
                Event = "saved", Text = clean, Chars = clean.Length,
            });
            AddHistory(seatId, "note saved (" + clean.Length + " chars)");
            request.Outcome = "note_saved";
            request.Detail = clean.Length + " chars";
            return TurnStep.Continue();
        }

        private TurnStep HandleAction(PlaygroundRequestRecord request, string text, string seatId,
            Observation observation, int token, ref int actions, ref int invalid)
        {
            ParseResult parsed = LlmActionParser.Parse(text, _config.EnableAttack);
            if (!parsed.Ok)
            {
                request.Outcome = "invalid_action";
                request.Detail = parsed.Error;
                invalid++;
                return invalid >= _config.MaxInvalidPerTurn
                    ? TurnStep.StopWith("invalid_limit") : TurnStep.Continue();
            }

            LlmAction action = parsed.Action;
            if (action.EndTurn)
            {
                StoreMemory(seatId, LlmMemoryExtractor.FromSummary(_config.GameId, seatId,
                    request.Season, action.Summary ?? action.Reason));
                AddHistory(seatId, "end_turn");
                request.Outcome = "end_turn";
                request.Detail = "model ended the turn";
                return TurnStep.StopWith("end_turn");
            }

            ActionCommand command = LlmActionParser.ToCommand(action);
            FactionState faction = _engine.State.FindFaction(seatId);
            var record = new PlaygroundActionRecord
            {
                RequestIndex = request.Index,
                SeatId = seatId,
                Season = request.Season,
                Type = action.Type,
                CommandJson = command == null ? null : command.ToJson(),
                CpBefore = faction == null ? 0 : faction.CommandPoints,
                ActionAdmissionsBefore = _ledger.Snapshot().Session.ActionAdmissions,
            };
            string liveBefore = PlaygroundStateHash.Authoritative(_engine);
            record.LiveStateHashBeforeCheck = liveBefore;

            bool runCheck = _config.CheckBeforeExecute || _config.ActionMode == PlaygroundActionMode.CheckOnly;
            if (runCheck)
            {
                PlaygroundCheckResult check = PlaygroundCheck.Validate(_config, _journal, seatId,
                    command, PlaygroundStateHash.Canonical(_engine));
                record.CheckRequested = true;
                record.CheckRan = true;
                record.ReplayMatched = check.ReplayMatched;
                record.ReplayStateHash = check.ReplayStateHash;
                record.CheckOk = check.Ok;
                record.CheckError = check.Error;
                record.LiveStateHashAfterCheck = PlaygroundStateHash.Authoritative(_engine);
                _record.Checks.Add(record);
            }

            if (_config.ActionMode == PlaygroundActionMode.CheckOnly)
            {
                record.Executed = false;
                record.Accepted = false;
                record.LiveStateHashBeforeExecute = liveBefore;
                record.LiveStateHashAfterExecute = record.LiveStateHashAfterCheck;
                _record.Actions.Add(record);
                AddHistory(seatId, "check " + action.Type + " -> "
                    + (record.CheckOk ? "valid" : record.CheckError ?? "invalid"));
                request.Outcome = record.CheckOk ? "action_checked" : "action_check_rejected";
                request.Detail = record.CheckOk
                    ? "validated on an isolated replay; authoritative state unchanged"
                    : (record.CheckError ?? "check rejected the order");
                return TurnStep.StopWith(record.CheckOk ? "check_only" : "check_rejected");
            }

            if (runCheck && (!record.CheckOk || !record.ReplayMatched))
            {
                record.Executed = false;
                record.Accepted = false;
                record.LiveStateHashBeforeExecute = liveBefore;
                record.LiveStateHashAfterExecute = liveBefore;
                _record.Actions.Add(record);
                string failure = record.CheckError ?? "check_rejected";
                AddHistory(seatId, "check failed -> " + failure);
                request.Outcome = "action_check_rejected";
                request.Detail = failure;
                return TurnStep.StopWith("check_rejected");
            }

            DeliberationAdmission admission = _ledger.TryAdmitGameAction();
            if (!admission.Allowed)
            {
                record.Executed = false;
                record.Accepted = false;
                record.LiveStateHashBeforeExecute = liveBefore;
                record.LiveStateHashAfterExecute = liveBefore;
                _record.Actions.Add(record);
                request.Outcome = "action_denied:" + admission.Code;
                request.Detail = admission.Message;
                return TurnStep.StopWith("action_budget:" + admission.Code);
            }

            DeliberationBindingResult dispatch = _ledger.MarkDispatched(admission.ReservationId);
            if (!dispatch.Ok)
            {
                _ledger.Cancel(admission.ReservationId, DeliberationCancelReason.Aborted);
                record.Executed = false;
                record.Accepted = false;
                record.LiveStateHashBeforeExecute = liveBefore;
                record.LiveStateHashAfterExecute = liveBefore;
                _record.Actions.Add(record);
                request.Outcome = "action_dispatch_denied:" + dispatch.Code;
                request.Detail = dispatch.Message;
                return TurnStep.StopWith("action_dispatch:" + dispatch.Code);
            }

            ValidationResult validation;
            try
            {
                validation = _engine.SubmitAs(seatId, token, command);
            }
            catch (Exception)
            {
                validation = ValidationResult.Fail("host submit failed");
            }
            _ledger.Settle(admission.ReservationId, new DeliberationSettlement
            {
                Accepted = validation.Ok,
                Failed = false,
                ElapsedMillis = 0,
            });
            actions++;

            record.Executed = true;
            record.Accepted = validation.Ok;
            record.EngineError = validation.Error;
            record.LiveStateHashBeforeExecute = liveBefore;
            record.LiveStateHashBeforeCheck = record.LiveStateHashBeforeCheck ?? liveBefore;
            if (record.LiveStateHashAfterCheck == null)
                record.LiveStateHashAfterCheck = liveBefore;

            if (validation.Ok)
            {
                _journal.AcceptedCommand(seatId, command);
                StoreMemory(seatId, LlmMemoryExtractor.FromAction(_config.GameId, seatId,
                    request.Season, action, true, null));
                AddHistory(seatId, action.Type + (string.IsNullOrEmpty(action.TargetId)
                    ? "" : " " + action.TargetId) + " -> ok");
                request.Outcome = "action_executed";
                request.Detail = "accepted by the engine";
            }
            else
            {
                string feedback = FeedbackPolicy.ForModel(validation.Error, command, observation);
                record.FeedbackShown = feedback;
                StoreMemory(seatId, LlmMemoryExtractor.FromAction(_config.GameId, seatId,
                    request.Season, action, false, feedback));
                AddHistory(seatId, action.Type + (string.IsNullOrEmpty(action.TargetId)
                    ? "" : " " + action.TargetId) + " -> rejected: " + feedback);
                request.Outcome = "action_rejected";
                request.Detail = "model-facing feedback: " + feedback;
                invalid++;
            }
            StoreMemory(seatId, LlmMemoryExtractor.FromSummary(_config.GameId, seatId,
                request.Season, action.Summary ?? action.Reason));

            FactionState after = _engine.State.FindFaction(seatId);
            record.CpAfter = after == null ? record.CpBefore : after.CommandPoints;
            record.ActionAdmissionsAfter = _ledger.Snapshot().Session.ActionAdmissions;
            record.LiveStateHashAfterExecute = PlaygroundStateHash.Authoritative(_engine);
            _record.Actions.Add(record);

            if (_engine.State.WinnerSeatId != null) return TurnStep.StopWith("game_over");
            if (actions >= _config.MaxActionsPerTurn) return TurnStep.StopWith("action_limit");
            if (invalid >= _config.MaxInvalidPerTurn) return TurnStep.StopWith("invalid_limit");
            return TurnStep.Continue();
        }

        private static string BoundedInquiryFailure(string tool, string code)
            => "{\"tool\":" + PlaygroundJson.Str(tool)
               + ",\"response_version\":\"ls018-v1\",\"ok\":false,\"error_code\":"
               + PlaygroundJson.Str(code) + "}";
    }
}
