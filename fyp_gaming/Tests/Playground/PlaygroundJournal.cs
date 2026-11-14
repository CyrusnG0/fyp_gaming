using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal enum PlaygroundJournalKind { BeginSeason, EndSeason, AcceptedCommand }

    internal sealed class PlaygroundJournalEntry
    {
        public PlaygroundJournalKind Kind;
        public int Season;
        public string SeatId;
        public string Type;
        public string TargetId;
        public int ParamA;
        public int ParamB;
        public Dictionary<string, string> Args;
        public string Text;
        public string Reason;

        public string Summary()
        {
            if (Kind == PlaygroundJournalKind.BeginSeason) return "begin_season " + Season;
            if (Kind == PlaygroundJournalKind.EndSeason) return "end_season " + Season;
            return "accepted " + SeatId + " " + Type + " " + (TargetId ?? "") + " ("
                + ParamA + "," + ParamB + ")";
        }
    }

    /// <summary>
    /// Ordered record of every state-changing transition the live engine took. Replaying it into a
    /// fresh scenario instance is the playground's faithful isolated copy for Check mode. Rejected
    /// orders are not journalled: they emit only a log record and never change canonical state, so
    /// excluding them preserves every field the validator reads.
    /// </summary>
    internal sealed class PlaygroundJournal
    {
        private readonly List<PlaygroundJournalEntry> _entries = new List<PlaygroundJournalEntry>();

        public IReadOnlyList<PlaygroundJournalEntry> Entries => _entries;

        public void BeginSeason(int season)
            => _entries.Add(new PlaygroundJournalEntry
            {
                Kind = PlaygroundJournalKind.BeginSeason,
                Season = season,
            });

        public void EndSeason(int season)
            => _entries.Add(new PlaygroundJournalEntry
            {
                Kind = PlaygroundJournalKind.EndSeason,
                Season = season,
            });

        public void AcceptedCommand(string seatId, ActionCommand command)
        {
            if (command == null) return;
            _entries.Add(new PlaygroundJournalEntry
            {
                Kind = PlaygroundJournalKind.AcceptedCommand,
                SeatId = seatId,
                Type = command.Type,
                TargetId = command.TargetId,
                ParamA = command.ParamA,
                ParamB = command.ParamB,
                Args = command.Args == null ? null : new Dictionary<string, string>(command.Args),
                Text = command.Text,
                Reason = command.Reason,
            });
        }

        /// <summary>
        /// Replays the journal into a fresh engine. Returns false with a bounded reason if any
        /// transition that was accepted live is rejected on replay; a mismatch must never be treated
        /// as a valid Check.
        /// </summary>
        public bool TryReplay(Func<GameEngine> factory, out GameEngine engine, out string error)
        {
            engine = null;
            error = null;
            try
            {
                engine = factory();
                foreach (PlaygroundJournalEntry entry in _entries)
                {
                    if (entry.Kind == PlaygroundJournalKind.BeginSeason)
                    {
                        engine.BeginSeason();
                        continue;
                    }
                    if (entry.Kind == PlaygroundJournalKind.EndSeason)
                    {
                        engine.EndSeason();
                        continue;
                    }

                    int token = engine.BeginSeatTurn(entry.SeatId);
                    ValidationResult result = engine.SubmitAs(entry.SeatId, token,
                        CopyCommand(entry));
                    engine.EndSeatTurn(token);
                    if (!result.Ok)
                    {
                        error = "replay rejected an accepted " + entry.Type + " order";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception)
            {
                engine = null;
                error = "replay failed";
                return false;
            }
        }

        private static ActionCommand CopyCommand(PlaygroundJournalEntry entry)
        {
            return new ActionCommand
            {
                Type = entry.Type,
                TargetId = entry.TargetId,
                ParamA = entry.ParamA,
                ParamB = entry.ParamB,
                Args = entry.Args == null ? null : new Dictionary<string, string>(entry.Args),
                Text = entry.Text,
                Reason = entry.Reason,
            };
        }
    }
}
