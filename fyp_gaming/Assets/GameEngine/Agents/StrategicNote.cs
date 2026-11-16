using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// One model-authored plan for the next turn of one seat. A note is evidence and prompt context,
    /// never game state: it cannot carry an order, spend a resource or change visibility.
    /// </summary>
    public sealed class StrategicNote
    {
        public string GameId { get; internal set; }
        public string SeatId { get; internal set; }
        public int Season { get; internal set; }
        public string Text { get; internal set; }

        internal StrategicNote Copy()
        {
            return new StrategicNote
            {
                GameId = GameId,
                SeatId = SeatId,
                Season = Season,
                Text = Text,
            };
        }
    }

    /// <summary>
    /// Session-local storage for strategic notes. The runner keys every read and write by the
    /// trusted match id and acting seat, so a note can never cross seats or matches.
    /// </summary>
    public interface IStrategicNoteStore
    {
        bool Save(string gameId, string seatId, int season, string text);
        bool TryGet(string gameId, string seatId, out StrategicNote note);
        void Clear();
    }

    /// <summary>
    /// In-memory store: one note per (game, seat), replaced by a newer note. A note is not persisted
    /// to disk or to canonical game state; it lives only for the runner session that created it.
    /// </summary>
    public sealed class InMemoryStrategicNoteStore : IStrategicNoteStore
    {
        public const int DefaultMaxTextChars = 400;

        private readonly object _gate = new object();
        private readonly Dictionary<string, StrategicNote> _notes =
            new Dictionary<string, StrategicNote>(StringComparer.Ordinal);
        private readonly int _maxTextChars;

        public InMemoryStrategicNoteStore(int maxTextChars = DefaultMaxTextChars)
        {
            _maxTextChars = maxTextChars < 1 ? 1 : maxTextChars;
        }

        public int MaxTextChars => _maxTextChars;

        public bool Save(string gameId, string seatId, int season, string text)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId)
                || season < 0 || string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim();
            if (normalized.Length == 0 || normalized.Length > _maxTextChars) return false;
            for (int i = 0; i < normalized.Length; i++)
                if (char.IsControl(normalized[i])) return false;

            lock (_gate)
            {
                _notes[Key(gameId, seatId)] = new StrategicNote
                {
                    GameId = gameId,
                    SeatId = seatId,
                    Season = season,
                    Text = normalized,
                };
            }
            return true;
        }

        public bool TryGet(string gameId, string seatId, out StrategicNote note)
        {
            note = null;
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId))
                return false;
            lock (_gate)
            {
                StrategicNote stored;
                if (!_notes.TryGetValue(Key(gameId, seatId), out stored) || stored == null)
                    return false;
                note = stored.Copy();
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate) _notes.Clear();
        }

        private static string Key(string gameId, string seatId)
            => gameId + "\u001f" + seatId;
    }
}
