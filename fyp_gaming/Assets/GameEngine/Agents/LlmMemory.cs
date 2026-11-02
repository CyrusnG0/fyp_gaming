using System;
using System.Collections.Generic;
using System.Globalization;

namespace LuanShi.Engine
{
    public static class LlmMemoryType
    {
        public const string Fact = "fact";
        public const string Lesson = "lesson";
        public const string Decision = "decision";
    }

    public static class LlmMemoryScope
    {
        public const string Game = "game";
    }

    public static class LlmMemoryVisibility
    {
        public const string Seat = "seat";
    }

    /// <summary>One durable, seat-private memory extracted from trusted turn results.</summary>
    public sealed class LlmMemory
    {
        public const int CurrentSchemaVersion = 1;
        public string Id;
        public string GameId;
        public string SeatId;
        public string Scope = LlmMemoryScope.Game;
        public string Visibility = LlmMemoryVisibility.Seat;
        public string Type;
        public string Content;
        public string Source;
        public int Season;
        public int Importance = 1;
        public int Confidence = 1;
        public string SupersededBy;
        public int SchemaVersion = CurrentSchemaVersion;

        internal string ToJson()
        {
            return "{\"schema_version\":" + SchemaVersion.ToString(CultureInfo.InvariantCulture)
                + ",\"id\":" + MiniJson.Str(Id)
                + ",\"game_id\":" + MiniJson.Str(GameId)
                + ",\"seat_id\":" + MiniJson.Str(SeatId)
                + ",\"scope\":" + MiniJson.Str(Scope)
                + ",\"visibility\":" + MiniJson.Str(Visibility)
                + ",\"type\":" + MiniJson.Str(Type)
                + ",\"content\":" + MiniJson.Str(Content)
                + ",\"source\":" + MiniJson.Str(Source)
                + ",\"season\":" + Season.ToString(CultureInfo.InvariantCulture)
                + ",\"importance\":" + Importance.ToString(CultureInfo.InvariantCulture)
                + ",\"confidence\":" + Confidence.ToString(CultureInfo.InvariantCulture)
                + ",\"superseded_by\":" + MiniJson.Str(SupersededBy)
                + "}";
        }

        internal LlmMemory Copy()
        {
            return new LlmMemory
            {
                Id = Id, GameId = GameId, SeatId = SeatId, Scope = Scope,
                Visibility = Visibility, Type = Type, Content = Content, Source = Source,
                Season = Season, Importance = Importance, Confidence = Confidence,
                SchemaVersion = SchemaVersion,
                SupersededBy = SupersededBy,
            };
        }
    }

    /// <summary>Durable memory backend. Implementations filter by game and seat.</summary>
    public interface ILlmMemoryStore
    {
        string LastError { get; }
        IReadOnlyList<LlmMemory> Load(string gameId, string seatId);
        bool Append(LlmMemory memory);
        bool Consolidate(string gameId, string seatId, int currentSeason,
            int retentionSeasons, int maxItems);
    }
}
