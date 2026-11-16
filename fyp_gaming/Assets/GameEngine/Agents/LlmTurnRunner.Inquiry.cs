using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Bounded inquiry loop.
    ///
    /// Opt-in via <see cref="LlmConfig.EnableManualLookup"/> (legacy manual_lookup only) or
    /// <see cref="LlmConfig.EnableDeliberation"/> (all four read-only routes). A reply classified as
    /// an investigation request is parsed by the strict <see cref="InvestigationRequestParser"/> and,
    /// only after the deliberation ledger admits it, executed by a fresh
    /// <see cref="InvestigationService"/>/<see cref="InvestigationDispatcher"/> built from the
    /// host-bound match/seat and the request's detached seat-filtered observation. Its bounded result
    /// is held privately and injected into the next prompt once.
    ///
    /// An inquiry consumes an inquiry admission and no command point or game action. A per-turn cap
    /// refusal is a bounded answer, not a model error: it adds no invalid-response count and lets the
    /// model act or end its turn on the next reply.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        private string _pendingInquiryResult;

        /// <summary>
        /// True when the raw reply is one JSON object carrying a <c>tool</c> field. Invalid JSON is
        /// left to the existing action parser so no permissive inquiry fallback exists; a valid
        /// object that fails the strict parser is reported as an invalid inquiry instead.
        /// </summary>
        private static bool IsInquiryCandidate(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!StrictJson.TryParse(text, out JsonNode root, out _)) return false;
            return root is JsonObject obj && obj.TryGet(InvestigationRequestContract.FieldTool, out _);
        }

        private string TakePendingInquiryResult()
        {
            string result = _pendingInquiryResult;
            _pendingInquiryResult = null;
            return result;
        }

        private void HandleInquiry(InFlight flight, LlmResponse response)
        {
            InvestigationRequestOptions requestOptions = InvestigationRequestOptions.Defaults();
            if (DeliberationActive)
                requestOptions.ServiceOptions.MaxResponseChars = InquiryResultCap();
            InvestigationRequestParseResult parsed =
                InvestigationRequestParser.Parse(response.Text, requestOptions);
            if (!parsed.Ok)
            {
                FailInquiry(flight, response, parsed.ErrorCode, parsed.Error, null);
                return;
            }

            InvestigationRequest request = parsed.Request;
            if (request.Tool != InvestigationRequestTool.ManualLookup && !DeliberationActive)
            {
                // Do not execute any other tool. The rejection is bounded, uses only the fixed
                // tool vocabulary and never echoes model-supplied topic/query/id/authority fields.
                string rejected = "{\"tool\":" + MiniJson.Str(request.ToolName)
                    + ",\"ok\":false,\"error_code\":\"tool_not_enabled\"}";
                _pendingInquiryResult = rejected;
                InvalidThisTurn++;
                LastResult = "inquiry rejected: " + request.ToolName;
                AddActivity("inquiry", "Inquiry rejected: " + request.ToolName
                    + " (tool not enabled).");
                AddHistory("inquiry " + request.ToolName + " -> rejected: tool_not_enabled");
                Status = "inquiry rejected; requesting next decision";
                if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
                {
                    FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                    WriteRecord(flight, response, true, null, null, "tool_not_enabled",
                        request.ToolName, rejected.Length);
                }
                else
                {
                    WriteRecord(flight, response, true, null, null, "tool_not_enabled",
                        request.ToolName, rejected.Length);
                    StartRequest();
                }
                return;
            }

            // Admission precedes tool execution. A spent per-turn allowance returns a bounded
            // answer instead of dispatching work or incrementing the invalid-response count.
            DeliberationAdmission inquiryAdmission = null;
            if (_ledger != null)
            {
                inquiryAdmission = _ledger.TryAdmitInquiry();
                if (!inquiryAdmission.Allowed)
                {
                    RefuseInquiry(flight, response, request.ToolName, "inquiry_limit_reached");
                    return;
                }
                DeliberationBindingResult dispatch =
                    _ledger.MarkDispatched(inquiryAdmission.ReservationId);
                if (!dispatch.Ok)
                {
                    _ledger.Cancel(inquiryAdmission.ReservationId, DeliberationCancelReason.Aborted);
                    _deliberationStopCode = dispatch.Code;
                    FinishLimit("deliberation dispatch refused: " + dispatch.Code,
                        EndReason.DeliberationBudget);
                    WriteRecord(flight, response, true, null, null, dispatch.Code,
                        request.ToolName, null);
                    return;
                }
                _inquiriesThisTurn++;
            }

            AddActivity("inquiry", "Inquiry requested: " + request.ToolName + ".");
            string resultJson;
            bool truncated;
            try
            {
                var service = new InvestigationService(_gameId, _seatId, flight.Observation,
                    _memories, requestOptions.ServiceOptions);
                var dispatcher = new InvestigationDispatcher(service);
                InvestigationResult result = dispatcher.Dispatch(request);
                resultJson = result == null ? null : result.ToJson();
                truncated = result != null && result.Truncated;
            }
            catch (Exception)
            {
                SettleInquiryReservation(inquiryAdmission, true);
                // Never surface exception text, file paths or provider details.
                FailInquiry(flight, response, "inquiry_failed",
                    "The inquiry could not be completed.", request.ToolName);
                return;
            }
            SettleInquiryReservation(inquiryAdmission, false);

            int cap = InquiryResultCap();
            if (string.IsNullOrEmpty(resultJson))
                resultJson = BoundedInquiryFailure("inquiry_failed", request.ToolName);
            if (resultJson.Length > cap)
                resultJson = BoundedInquiryFailure("response_too_large", request.ToolName);
            _pendingInquiryResult = resultJson;
            LastResult = "inquiry answered: " + request.ToolName;
            AddActivity("inquiry", "Inquiry answered: " + request.ToolName
                + " (result chars=" + resultJson.Length + ", truncated="
                + (truncated ? "true" : "false") + ").");
            AddHistory("inquiry " + request.ToolName + " -> answered ("
                + resultJson.Length + " chars)");
            Status = "inquiry answered; requesting next decision";
            WriteRecord(flight, response, true, null, null, null,
                request.ToolName, resultJson.Length);
            StartRequest();
        }

        /// <summary>
        /// The prompt-facing result cap. A value below the service minimum is raised to it rather
        /// than silently producing an unrepresentable response envelope.
        /// </summary>
        private int InquiryResultCap()
        {
            if (!DeliberationActive) return InvestigationOptions.Defaults().MaxResponseChars;
            int cap = _config.MaxInquiryResultChars;
            return cap < InvestigationOptions.MinimumResponseChars
                ? InvestigationOptions.MinimumResponseChars
                : cap;
        }

        private void RefuseInquiry(InFlight flight, LlmResponse response, string toolName,
            string code)
        {
            string refused = BoundedInquiryFailure(code, toolName);
            _pendingInquiryResult = refused;
            LastResult = "inquiry refused: " + code;
            AddActivity("inquiry", "Inquiry refused: " + toolName
                + " (per-turn allowance spent).");
            AddHistory("inquiry " + toolName + " -> refused: " + code);
            Status = "inquiry refused; requesting next decision";
            WriteRecord(flight, response, true, null, null, code, toolName, refused.Length);
            StartRequest();
        }

        private void SettleInquiryReservation(DeliberationAdmission admission, bool failed)
        {
            if (_ledger == null || admission == null) return;
            _ledger.Settle(admission.ReservationId, new DeliberationSettlement { Failed = failed });
        }

        private void FailInquiry(InFlight flight, LlmResponse response, string code,
            string message, string toolName)
        {
            InvalidThisTurn++;
            LastResult = Redact("invalid inquiry: " + (message ?? code));
            AddActivity("validation", "Invalid inquiry request: " + (code ?? "invalid") + ".");
            AddHistory("invalid inquiry request -> " + (code ?? "invalid"));
            Status = "invalid inquiry; requesting repair";
            if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
            {
                FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                WriteRecord(flight, response, false, null, null, code, toolName, null);
            }
            else
            {
                WriteRecord(flight, response, false, null, null, code, toolName, null);
                StartRequest();
            }
        }

        private static string BoundedInquiryFailure(string code)
            => BoundedInquiryFailure(code, InvestigationRequestContract.ManualLookup);

        private static string BoundedInquiryFailure(string code, string toolName)
        {
            string tool = string.IsNullOrEmpty(toolName)
                ? InvestigationRequestContract.ManualLookup : toolName;
            return "{\"tool\":" + MiniJson.Str(tool) + ",\"response_version\":\"ls018-v1\","
                 + "\"ok\":false,\"error_code\":" + MiniJson.Str(code) + "}";
        }
    }
}
