using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Stable machine-readable reason a turn stopped. <see cref="LlmTurnRunner.EndReasonText"/> is
    /// the display form; FailureReason remains the existing detailed diagnostic string.
    /// </summary>
    public enum EndReason
    {
        None,
        ModelEndedTurn,
        ActionLimit,
        RequestLimit,
        InvalidResponseLimit,
        TurnTimeLimit,
        SessionRequestLimit,
        UsdBudgetLimit,
        UsdBudgetUnknown,
        ProviderError,
        RequestTimeout,
        HostError,
        PromptTooLarge,
        TransportError,
        Cancelled,
    }

    public sealed partial class LlmTurnRunner
    {
        /// <summary>Plain-language, stable completion reason for panels and researcher records.</summary>
        public string EndReasonText
        {
            get
            {
                switch (EndReason)
                {
                    case EndReason.ModelEndedTurn: return "Finished - model ended turn";
                    case EndReason.ActionLimit:
                        return "Finished - action limit ("
                            + ActionsThisTurn + "/" + _config.MaxActionsPerTurn + ")";
                    case EndReason.RequestLimit:
                        return "Finished - request limit ("
                            + RequestsThisTurn + "/" + _config.MaxRequestsPerTurn + ")";
                    case EndReason.InvalidResponseLimit: return "Stopped - invalid-response limit";
                    case EndReason.TurnTimeLimit: return "Stopped - time limit";
                    case EndReason.SessionRequestLimit: return "Stopped - session request limit";
                    case EndReason.UsdBudgetLimit: return "Stopped - budget limit";
                    case EndReason.UsdBudgetUnknown: return "Failed - USD budget unknown";
                    case EndReason.ProviderError:
                        return "Failed - " + (FailureReason ?? "provider error");
                    case EndReason.RequestTimeout: return "Failed - request timed out";
                    case EndReason.HostError:
                        return "Failed - host " + (FailureReason ?? "error");
                    case EndReason.PromptTooLarge: return "Failed - prompt too large";
                    case EndReason.TransportError:
                        return "Failed - " + (FailureReason ?? "transport error");
                    case EndReason.Cancelled:
                        return "Cancelled - " + (FailureReason ?? "by host");
                    default: return "Turn not finished";
                }
            }
        }

        private void HandleResponse(InFlight flight, LlmResponse response)
        {
            string commandJson = null;
            bool? resultOk = null;
            string resultError = null;

            if (!response.Ok)
            {
                LastResult = Redact(ErrorText(response));
                AddActivity("error", LastResult);
                Status = "provider error: " + response.Error;
                resultError = response.ErrorDetail;
                FinishFailed("provider " + response.Error.ToString().ToLowerInvariant(),
                    EndReason.ProviderError);
                WriteRecord(flight, response, false, null, null, resultError);
                return;
            }

            // A successful response whose usage cannot be counted makes a configured MaxUsd
            // unenforceable. Stop here rather than continuing on an unknown budget.
            if (_config.MaxUsd.HasValue && !_costKnown)
            {
                LastResult = "usd budget unknown";
                FinishFailed("usd budget unknown", EndReason.UsdBudgetUnknown);
                WriteRecord(flight, response, false, null, null, "usd budget unknown");
                return;
            }

            ParseResult parsed = LlmActionParser.Parse(response.Text);
            if (!parsed.Ok)
            {
                InvalidThisTurn++;
                LastResult = Redact("invalid response: " + parsed.Error);
                AddActivity("validation", LastResult);
                AddHistory("invalid model response -> " + parsed.Error);
                resultError = parsed.Error;
                Status = "invalid response; requesting repair";
                if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
                {
                    FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                    WriteRecord(flight, response, false, null, null, resultError);
                }
                else
                {
                    // Record the counters/state of this response before the next request increments them.
                    WriteRecord(flight, response, false, null, null, resultError);
                    StartRequest();
                }
                return;
            }

            LlmAction action = parsed.Action;
            LastSummary = Limit(Redact(!string.IsNullOrEmpty(action.Summary) ? action.Summary : action.Reason), 200);
            AddActivity("decision", string.IsNullOrEmpty(LastSummary)
                ? "(no explanation provided)" : LastSummary);
            AddActivity("command", ActivityCommandText(action));

            if (action.EndTurn)
            {
                LastResult = "end_turn";
                LastCommandJson = "{\"type\":\"end_turn\"}";
                AddActivity("result", "Turn ended by model.");
                EndReason = EndReason.ModelEndedTurn;
                FinishTerminal(RunnerState.Finished, "end_turn");
                WriteRecord(flight, response, true, null, null, null);
                return;
            }

            ActionCommand command = LlmActionParser.ToCommand(action);
            ActionsThisTurn++;
            bool accepted = false;
            try
            {
                ValidationResult validation = _host.Submit(command);
                accepted = validation.Ok;
                // The engine's target errors can distinguish a hidden-but-existing id from a
                // nonexistent one. The model gets one indistinguishable answer in both cases.
                resultError = accepted ? null : SanitizeModelFeedback(validation.Error);
            }
            catch (Exception)
            {
                resultError = "host submit failed";
            }

            commandJson = Redact(command == null ? null : command.ToJson());
            LastCommandJson = commandJson;
            resultOk = accepted;
            if (accepted)
            {
                LastResult = "ok";
                AddHistory(HistoryLine(action, "ok"));
                Status = "order accepted";
            }
            else
            {
                InvalidThisTurn++;
                LastResult = Redact(resultError);
                AddHistory(HistoryLine(action, "rejected: " + resultError));
                Status = "order rejected; continuing";
            }
            AddActivity("result", accepted ? "Accepted by engine." : "Rejected: " + LastResult);

            if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
            {
                FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
            }
            else if (ActionsThisTurn >= _config.MaxActionsPerTurn)
            {
                FinishLimit("action limit reached", EndReason.ActionLimit);
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
            }
            else
            {
                // Snapshot before StartRequest increments RequestsThisTurn/dispatches the next request.
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
                StartRequest();
            }
        }

        private static string ActivityCommandText(LlmAction action)
        {
            string text = action.Type ?? "?";
            if (!string.IsNullOrEmpty(action.TargetId)) text += " " + action.TargetId;
            if (action.Args != null && action.Args.TryGetValue("unit_type", out string unit)
                && !string.IsNullOrEmpty(unit))
                text += " (" + unit + ")";
            return text;
        }
    }
}
