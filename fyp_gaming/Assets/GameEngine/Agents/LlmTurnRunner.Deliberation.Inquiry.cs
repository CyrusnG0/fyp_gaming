using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LuanShi.Engine
{
    /// <summary>
    /// Inquiry and private-note half of opt-in production deliberation. Kept separate from the
    /// budget/admission half so each file stays focused; both are the same partial runner.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        // ---- inquiry -----------------------------------------------------------

        private InvestigationRequestOptions BuildInquiryOptions()
        {
            InvestigationRequestOptions options = InvestigationRequestOptions.Defaults();
            int cap = _config.InquiryMaxResponseChars;
            if (cap < InvestigationOptions.MinimumResponseChars)
                cap = InvestigationOptions.MinimumResponseChars;
            options.ServiceOptions.MaxResponseChars = cap;
            return options;
        }

        /// <summary>
        /// Executes exactly one already-validated read-only inquiry. A fresh seat-filtered
        /// observation is fetched for this inquiry, so the tool never reads a stale snapshot and
        /// never touches canonical state. The result is bounded and retained for this seat only.
        /// </summary>
        private void HandleDeliberationInquiry(InFlight flight, LlmResponse response)
        {
            InvestigationRequestOptions options = BuildInquiryOptions();
            InvestigationRequestParseResult parsed =
                InvestigationRequestParser.Parse(response.Text, options);
            if (!parsed.Ok)
            {
                SetDeliberationEvent("inquiry", null, "invalid:" + parsed.ErrorCode, 0, null, flight);
                FailInquiry(flight, response, parsed.ErrorCode, parsed.Error, null);
                return;
            }

            InvestigationRequest request = parsed.Request;
            AddActivity("inquiry", "Inquiry requested: " + request.ToolName + ".");
            var execTimer = Stopwatch.StartNew();

            DeliberationAdmission admission = _deliberationLedger.TryAdmitInquiry();
            if (!admission.Allowed)
            {
                string denial = BoundedDeliberationFailure(request.ToolName, admission.Code);
                _pendingInquiryResult = denial;
                RetainInquiryResult(denial);
                LogInquiryResult(request.ToolName, denial);
                _stopDetail = admission.Code;
                SetDeliberationEvent("inquiry", request.ToolName, "denied:" + admission.Code,
                    execTimer.ElapsedMilliseconds, denial.Length, flight);
                LastResult = "inquiry denied: " + admission.Code;
                AddActivity("inquiry", "Inquiry denied by the deliberation budget.");
                AddHistory("inquiry " + request.ToolName + " -> denied: " + admission.Code);
                FinishLimit("deliberation inquiry limit reached", EndReason.InquiryLimit);
                WriteRecord(flight, response, true, null, null, admission.Code,
                    request.ToolName, denial.Length);
                return;
            }

            DeliberationBindingResult dispatch = _deliberationLedger.MarkDispatched(admission.ReservationId);
            if (!dispatch.Ok)
            {
                _deliberationLedger.Cancel(admission.ReservationId, DeliberationCancelReason.Aborted);
                FinishFailed("deliberation inquiry dispatch denied: " + dispatch.Code,
                    EndReason.DispatchDenied);
                SetDeliberationEvent("inquiry", request.ToolName, "dispatch_denied:" + dispatch.Code,
                    execTimer.ElapsedMilliseconds, null, flight);
                WriteRecord(flight, response, true, null, null, dispatch.Code, request.ToolName, null);
                return;
            }

            string resultJson = null;
            bool technicalFailure = false;
            try
            {
                // A fresh, seat-filtered observation per inquiry. A foreign or missing snapshot is
                // reported as a bounded failure, never used.
                Observation fresh = _host == null ? null : _host.Observe();
                // With the diplomacy orders enabled, player-written text is projected out of the
                // tool context too: the prompt's bounded untrusted section is the only copy a model
                // may read, so a history search cannot hand back the raw string.
                if (fresh != null && _config != null && _config.EnableDiplomacy)
                    fresh = PromptBuilder.ProjectDiplomacyObservation(fresh);
                if (fresh == null
                    || !string.Equals(fresh.SeatId, _seatId, StringComparison.Ordinal)
                    || !string.Equals(fresh.GameId, _deliberationMatchId, StringComparison.Ordinal))
                {
                    resultJson = BoundedDeliberationFailure(request.ToolName, "inquiry_context_mismatch");
                    technicalFailure = true;
                }
                else
                {
                    var service = new InvestigationService(_deliberationMatchId, _seatId, fresh,
                        _memories, options.ServiceOptions);
                    InvestigationResult result = new InvestigationDispatcher(service).Dispatch(request);
                    resultJson = result == null ? null : result.ToJson();
                    if (string.IsNullOrEmpty(resultJson))
                    {
                        resultJson = BoundedDeliberationFailure(request.ToolName, "inquiry_failed");
                        technicalFailure = true;
                    }
                    else if (resultJson.Length > options.ServiceOptions.MaxResponseChars)
                    {
                        // Never retry blindly; report the bound explicitly in a bounded envelope.
                        resultJson = BoundedDeliberationFailure(request.ToolName, "response_too_large");
                        technicalFailure = true;
                    }
                }
            }
            catch (Exception)
            {
                resultJson = BoundedDeliberationFailure(request.ToolName, "inquiry_failed");
                technicalFailure = true;
            }

            _deliberationLedger.Settle(admission.ReservationId,
                new DeliberationSettlement { ElapsedMillis = 0, Failed = technicalFailure });

            _pendingInquiryResult = resultJson;
            RetainInquiryResult(resultJson);
            LogInquiryResult(request.ToolName, resultJson);
            InquiriesThisTurn++;
            SetDeliberationEvent("inquiry", request.ToolName,
                technicalFailure ? "failed" : "answered", execTimer.ElapsedMilliseconds,
                resultJson.Length, flight);
            LastResult = "inquiry answered: " + request.ToolName;
            AddActivity("inquiry", "Inquiry answered: " + request.ToolName
                + " (result chars=" + resultJson.Length + ").");
            AddHistory("inquiry " + request.ToolName + " -> answered ("
                + resultJson.Length + " chars)");
            Status = "inquiry answered; requesting next decision";
            WriteRecord(flight, response, true, null, null, technicalFailure ? "inquiry_failed" : null,
                request.ToolName, resultJson.Length);
            StartRequest();
        }

        private void RetainInquiryResult(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            _retainedInquiryResults.Add(json);
            int max = _config.MaxRetainedInquiryResults < 0 ? 0 : _config.MaxRetainedInquiryResults;
            while (_retainedInquiryResults.Count > max) _retainedInquiryResults.RemoveAt(0);
        }

        /// <summary>
        /// Earlier retained results for the prompt, excluding the just-consumed latest result so it
        /// is not duplicated. The list is bounded by config and each entry by the inquiry cap.
        /// </summary>
        private IReadOnlyList<string> EarlierRetainedInquiryResults(string latest)
        {
            var earlier = new List<string>(_retainedInquiryResults);
            if (!string.IsNullOrEmpty(latest) && earlier.Count > 0
                && string.Equals(earlier[earlier.Count - 1], latest, StringComparison.Ordinal))
                earlier.RemoveAt(earlier.Count - 1);
            return earlier.AsReadOnly();
        }

        private static string BoundedDeliberationFailure(string tool, string code)
            => "{\"tool\":" + MiniJson.Str(tool) + ",\"response_version\":\"ls018-v1\","
             + "\"ok\":false,\"error_code\":" + MiniJson.Str(code) + "}";

        // ---- private note ------------------------------------------------------

        /// <summary>True for a valid JSON object that claims the note form.</summary>
        private static bool IsNoteCandidate(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!StrictJson.TryParse(text, out JsonNode root, out _)) return false;
            return root is JsonObject obj && obj.TryGet("note", out _);
        }

        /// <summary>
        /// Parses and stores one bounded private note. A malformed or oversized note is an invalid
        /// response (repair within the existing invalid allowance), never an action and never a
        /// partial save. The note is model data, not a command.
        /// </summary>
        private void HandleDeliberationNote(InFlight flight, LlmResponse response)
        {
            string text = null;
            string error = null;
            if (!(StrictJson.TryParse(response.Text, out JsonNode root, out _) && root is JsonObject obj)
                || !obj.TryGet("note", out JsonNode node) || !(node is JsonString noteText))
            {
                error = "invalid_note";
            }
            else if (obj.Items.Count != 1)
            {
                // A note mixed with an action/tool is ambiguous. Reject the whole reply.
                error = "note_must_be_alone";
            }
            else if (!LlmStrategicNoteRules.TryNormalize(noteText.Value, _config.MaxNoteChars,
                         out text, out error))
            {
                // TryNormalize supplies a stable error code.
            }

            if (error != null)
            {
                InvalidThisTurn++;
                SetDeliberationEvent("note", null, "invalid:" + error, 0, null, flight);
                LastResult = "invalid note: " + error;
                AddActivity("validation", "Invalid strategic note: " + error + ".");
                AddHistory("invalid note -> " + error);
                Status = "invalid note; requesting repair";
                if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
                {
                    FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                    WriteRecord(flight, response, false, null, null, error);
                }
                else
                {
                    WriteRecord(flight, response, false, null, null, error);
                    StartRequest();
                }
                return;
            }

            var noteTimer = Stopwatch.StartNew();
            if (!NoteStore.Save(_deliberationMatchId, _seatId, text, _season))
            {
                InvalidThisTurn++;
                SetDeliberationEvent("note", null, "storage_failed", noteTimer.ElapsedMilliseconds, null, flight);
                LastResult = "strategic note storage failed";
                AddActivity("validation", "Strategic note storage failed.");
                AddHistory("note storage failed");
                Status = "note storage failed; requesting repair";
                if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
                {
                    FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                    WriteRecord(flight, response, false, null, null, "note_storage_failed");
                }
                else
                {
                    WriteRecord(flight, response, false, null, null, "note_storage_failed");
                    StartRequest();
                }
                return;
            }

            _strategicNote = text;
            NotesSavedThisTurn++;
            SetDeliberationEvent("note", null, "saved", noteTimer.ElapsedMilliseconds, text.Length, flight);
            LastResult = "note saved";
            AddActivity("note", "Strategic note saved (" + text.Length + " chars).");
            AddHistory("note saved (" + text.Length + " chars)");
            Status = "note saved; requesting next decision";
            WriteRecord(flight, response, true, null, null, null);
            StartRequest();
        }
    }
}
