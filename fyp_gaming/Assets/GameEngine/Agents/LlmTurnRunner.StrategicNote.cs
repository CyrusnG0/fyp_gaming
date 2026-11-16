using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Terminal strategic-note reply. A validated note is stored for this seat's next turn and ends
    /// the turn: it submits no order, spends no command point and never becomes canonical state.
    /// A malformed note is a bounded repair, exactly like a malformed inquiry.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        /// <summary>Stable, bounded reason shown when a note cannot be stored by an unusable host.</summary>
        private const string NoteNotStoredCode = "note_not_stored";

        private void HandleStrategicNote(InFlight flight, LlmResponse response)
        {
            int maxChars = _config.MaxNoteChars < 1 ? 1 : _config.MaxNoteChars;
            StrategicNoteParseResult parsed = StrategicNoteParser.Parse(response.Text, maxChars);
            if (!parsed.Ok)
            {
                FailStrategicNote(flight, response, parsed.ErrorCode, parsed.Error);
                return;
            }

            if (!_noteStore.Save(_gameId, _seatId, flight.Season, parsed.Text))
            {
                // The parser accepted the note, so the only realistic cause is a host that did not
                // supply a game identity or a store that refuses the text. Both are bounded and
                // never echo model text.
                FailStrategicNote(flight, response, NoteNotStoredCode,
                    "The strategic note could not be stored for the next turn.");
                return;
            }

            _notesThisTurn++;
            LastSummary = Limit(Redact(parsed.Text), 200);
            LastResult = "strategic_note";
            LastCommandJson = null;
            AddActivity("decision", string.IsNullOrEmpty(LastSummary)
                ? "(strategic note; no explanation)" : LastSummary);
            AddActivity("note", "Strategic note stored for the next turn (" + parsed.Text.Length
                + " chars). No order was submitted.");
            AddHistory("strategic note -> stored (" + parsed.Text.Length + " chars)");
            Status = "strategic note stored; turn ended";
            EndReason = EndReason.NoteEndedTurn;
            // Terminal by contract: no Submit, no ActionsThisTurn increment, no command point and
            // no further provider request.
            FinishTerminal(RunnerState.Finished, "strategic_note");
            WriteRecord(flight, response, true, null, null, null);
        }

        private void FailStrategicNote(InFlight flight, LlmResponse response, string code,
            string message)
        {
            InvalidThisTurn++;
            LastResult = Redact("invalid strategic note: " + (message ?? code));
            AddActivity("validation", "Invalid strategic note: " + (code ?? "invalid") + ".");
            AddHistory("invalid strategic note -> " + (code ?? "invalid"));
            Status = "invalid strategic note; requesting repair";
            if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
            {
                FinishLimit("invalid-response limit reached", EndReason.InvalidResponseLimit);
                WriteRecord(flight, response, false, null, null, code);
            }
            else
            {
                WriteRecord(flight, response, false, null, null, code);
                StartRequest();
            }
        }
    }
}
