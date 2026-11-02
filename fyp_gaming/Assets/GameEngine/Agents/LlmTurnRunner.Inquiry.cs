using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Smallest end-to-end inquiry loop for the demo (LS-022 scope correction).
    ///
    /// Opt-in only via <see cref="LlmConfig.EnableManualLookup"/>. A reply classified as an
    /// investigation request is parsed by the existing strict
    /// <see cref="InvestigationRequestParser"/> and, only for manual_lookup, executed by a fresh
    /// <see cref="InvestigationService"/>/<see cref="InvestigationDispatcher"/> built from the
    /// host-bound match/seat and the request's detached seat-filtered observation. Its bounded
    /// result is held privately and injected into the next prompt once.
    ///
    /// inspect_entity/search_history/recall_memory are rejected before dispatch. No budget is
    /// added here: the inquiry reply already consumed its existing request slot, and the next
    /// decision is dispatched through the normal StartRequest limit checks.
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
            InvestigationRequestParseResult parsed =
                InvestigationRequestParser.Parse(response.Text, requestOptions);
            if (!parsed.Ok)
            {
                FailInquiry(flight, response, parsed.ErrorCode, parsed.Error, null);
                return;
            }

            InvestigationRequest request = parsed.Request;
            if (request.Tool != InvestigationRequestTool.ManualLookup)
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
                // Never surface exception text, file paths or provider details.
                FailInquiry(flight, response, "inquiry_failed",
                    "The inquiry could not be completed.", request.ToolName);
                return;
            }

            int cap = requestOptions.ServiceOptions.MaxResponseChars;
            if (string.IsNullOrEmpty(resultJson)) resultJson = BoundedInquiryFailure("inquiry_failed");
            if (resultJson.Length > cap) resultJson = BoundedInquiryFailure("response_too_large");
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
            => "{\"tool\":\"manual_lookup\",\"response_version\":\"ls018-v1\",\"ok\":false,"
             + "\"error_code\":" + MiniJson.Str(code) + "}";
    }
}
