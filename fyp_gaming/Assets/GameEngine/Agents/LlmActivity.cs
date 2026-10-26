using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>A bounded observer trace. Model decision text is a brief explanation, not internal reasoning.</summary>
    public sealed class LlmActivity
    {
        public readonly int Sequence;
        public readonly string SeatId;
        public readonly int TurnToken;
        public readonly int Season;
        public readonly string Kind;
        public readonly string Text;

        /// <summary>
        /// Faction name captured from the seat-filtered OBSERVATION used for this request. Null for
        /// entries written before the first observation (e.g. the turn-started row).
        /// </summary>
        public readonly string FactionName;

        internal LlmActivity(int sequence, string seatId, int turnToken, int season, string kind,
            string text, string factionName)
        {
            Sequence = sequence; SeatId = seatId; TurnToken = turnToken; Season = season;
            Kind = kind; Text = text; FactionName = factionName;
        }

        /// <summary>
        /// Chat heading name that never consults canonical state. Uses the observation-captured
        /// faction name when present, otherwise a neutral seat label suitable for single-LLM mode.
        /// </summary>
        public string DisplayFactionName
            => !string.IsNullOrEmpty(FactionName) ? FactionName
             : !string.IsNullOrEmpty(SeatId) ? "LLM seat " + SeatId
             : "LLM seat";
    }

    public sealed partial class LlmTurnRunner
    {
        public const int MaxActivityEntries = 160;
        private readonly List<LlmActivity> _activity = new List<LlmActivity>();
        private int _activitySequence;

        /// <summary>
        /// Researcher-only cross-seat display. Never include this in model prompts. Returns a detached
        /// read-only snapshot: later AddActivity calls, eviction and ResetSession cannot mutate a list
        /// the UI is already holding.
        /// </summary>
        public IReadOnlyList<LlmActivity> Activity
            => new List<LlmActivity>(_activity).AsReadOnly();

        /// <summary>Explicitly filtered display for a seat. Returns a detached collection of immutable entries.</summary>
        public IReadOnlyList<LlmActivity> ActivityForSeat(string seatId)
            => _activity.FindAll(e => string.Equals(e.SeatId, seatId, StringComparison.Ordinal)).AsReadOnly();

        private void AddActivity(string kind, string text)
        {
            _activity.Add(new LlmActivity(++_activitySequence, _seatId, _turnToken, _season, kind,
                Limit(Redact(text), 320), _factionName));
            if (_activity.Count > MaxActivityEntries) _activity.RemoveAt(0);
        }
    }
}
