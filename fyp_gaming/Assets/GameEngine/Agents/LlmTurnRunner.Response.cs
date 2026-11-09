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
        GameOver,
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
                    case EndReason.InvalidResponseLimit:
                        // The ordinary limit keeps its historical text; a budget-exhausted empty
                        // answer reuses this category (so ending tallies stay complete) but shows
                        // the evidence-supported cause instead of a generic repair-limit message.
                        return FailureReason == "invalid-response limit reached"
                            ? "Stopped - invalid-response limit"
                            : "Stopped - " + (FailureReason ?? "invalid-response limit");
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
                    case EndReason.GameOver: return "Finished - game over";
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

            // Terminal stop: a winner declared while this request was in flight makes the response
            // stale. Discard it (never parse or execute it) and stop before any new dispatch.
            if (HasPlanningHost && PlanningHost.IsGameOver)
            {
                LastResult = "game over";
                AddActivity("validation", "Game over: in-flight response discarded.");
                FinishGameOver();
                WriteRecord(flight, response, false, null, null, "game over");
                return;
            }

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

            // Opt-in only: an inquiry-shaped reply is classified before the action parser. The
            // disabled path below is untouched, so legacy replies are handled exactly as before.
            if (_config.EnableManualLookup && IsInquiryCandidate(response.Text))
            {
                HandleInquiry(flight, response);
                return;
            }

            ParseResult parsed = LlmActionParser.Parse(response.Text);
            if (!parsed.Ok)
            {
                InvalidThisTurn++;
                // An empty answer that already used the whole completion budget is an evidenced
                // truncation, not a repairable schema mistake. Retrying at the same cap is the
                // behaviour that produced consecutive empty replies in the recorded real run.
                bool budgetExhaustedEmpty =
                    LlmResponseDiagnostics.IsBudgetExhaustedEmpty(response, flight.MaxOutputTokens);
                string diagnostic = budgetExhaustedEmpty
                    ? LlmResponseDiagnostics.Describe(response, flight.MaxOutputTokens)
                    : parsed.Error;
                LastResult = Redact(budgetExhaustedEmpty
                    ? diagnostic
                    : "invalid response: " + parsed.Error);
                AddActivity("validation", LastResult);
                // Keep model-facing feedback stable; the detailed provider diagnostic is evidence.
                AddHistory("invalid model response -> " + parsed.Error);
                resultError = diagnostic;
                if (budgetExhaustedEmpty)
                {
                    string why = LlmResponseDiagnostics.ReasoningConsumedBudget(
                        response, flight.MaxOutputTokens)
                        ? "output budget consumed by provider reasoning"
                        : "response truncated at output budget";
                    Status = "response truncated; stopping";
                    // Reuse the invalid-response-limit category so existing ending tallies count
                    // this stop; FailureReason and the run record carry the precise cause.
                    FinishLimit(why, EndReason.InvalidResponseLimit);
                    WriteRecord(flight, response, false, null, null, resultError);
                    return;
                }
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
                StoreMemory(LlmMemoryExtractor.FromSummary(_gameId, _seatId, flight.Season,
                    action.Summary ?? action.Reason));
                AddActivity("result", "Turn ended by model.");
                EndReason = EndReason.ModelEndedTurn;
                FinishTerminal(RunnerState.Finished, "end_turn");
                WriteRecord(flight, response, true, null, null, null);
                return;
            }

            // A march is only advertised to a planning host. A plain domestic host must never let a
            // model-submitted march reach the engine (it was not offered in that prompt).
            if (action.Type == ActionType.March && !HasPlanningHost)
            {
                InvalidThisTurn++;
                LastResult = FeedbackPolicy.UnsupportedAction;
                LastCommandJson = null;
                AddActivity("validation", "march is not advertised for this seat.");
                AddActivity("result", "Rejected: " + LastResult);
                AddHistory(HistoryLine(action, "rejected: " + LastResult));
                Status = "order rejected; continuing";
                resultOk = false;
                resultError = LastResult;
                WriteRecord(flight, response, true, null, resultOk, resultError);
                if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
                    FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                else
                    StartRequest();
                return;
            }

            ActionCommand command = LlmActionParser.ToCommand(action);
            ActionsThisTurn++;
            bool accepted = false;
            try
            {
                ValidationResult validation = _host.Submit(command);
                accepted = validation.Ok;
                // Deny-by-default feedback: raw engine text can distinguish a hidden-but-existing
                // id from a nonexistent one, so only the typed allowlist reaches the model.
                resultError = accepted ? null : FeedbackPolicy.ForModel(validation.Error);
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
            StoreMemory(LlmMemoryExtractor.FromAction(_gameId, _seatId, flight.Season,
                action, accepted, resultError));
            StoreMemory(LlmMemoryExtractor.FromSummary(_gameId, _seatId, flight.Season,
                action.Summary ?? action.Reason));

            string capture = accepted && action.Type == ActionType.March
                ? CapturedCityName(flight, action) : null;
            string resultText = accepted ? "Accepted by engine." : "Rejected: " + LastResult;
            if (!string.IsNullOrEmpty(capture)) resultText += " Captured " + capture + ".";
            AddActivity("result", resultText);

            // Terminal stop (c): the authoritative host signal right after execution, before limit
            // handling or any new request. A game-ending order must cost no further provider call.
            if (HasPlanningHost && PlanningHost.IsGameOver)
            {
                FinishGameOver();
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
                return;
            }

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

        private void StoreMemory(LlmMemory memory)
        {
            if (memory == null || _memoryStore == null) return;
            if (_memories.Exists(existing => existing.Id == memory.Id)) return;
            if (_memoryStore.Append(memory)) _memories.Add(memory);
        }

        private static string ActivityCommandText(LlmAction action)
        {
            if (action.Type == ActionType.March)
                return "march " + (action.TargetId ?? "?") + " -> ("
                     + action.ParamA.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                     + action.ParamB.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";

            string text = action.Type ?? "?";
            if (!string.IsNullOrEmpty(action.TargetId)) text += " " + action.TargetId;
            if (action.Args != null && action.Args.TryGetValue("unit_type", out string unit)
                && !string.IsNullOrEmpty(unit))
                text += " (" + unit + ")";
            return text;
        }
    }
}
