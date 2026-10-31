using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

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

    public interface ILlmMemoryStore
    {
        string LastError { get; }
        IReadOnlyList<LlmMemory> Load(string gameId, string seatId);
        bool Append(LlmMemory memory);
        bool Consolidate(string gameId, string seatId, int currentSeason,
            int retentionSeasons, int maxItems);
    }

    /// <summary>
    /// Local append-only JSONL storage. Records are filtered by both game and seat on load.
    /// Invalid lines are skipped and exposed through LastError without exposing file contents.
    /// </summary>
    public sealed class LocalLlmMemoryStore : ILlmMemoryStore
    {
        private const int MaxRecordChars = 4096;
        private readonly object _gate = new object();
        public string DirectoryPath { get; private set; }
        public string LastError { get; private set; }

        public LocalLlmMemoryStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("a memory directory is required", nameof(directory));
            DirectoryPath = Path.GetFullPath(directory);
        }

        public IReadOnlyList<LlmMemory> Load(string gameId, string seatId)
        {
            var result = new List<LlmMemory>();
            LastError = null;
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId))
                return result.AsReadOnly();

            string path = FilePath(gameId, seatId);
            if (!File.Exists(path)) return result.AsReadOnly();
            try
            {
                var latest = new Dictionary<string, LlmMemory>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Length > MaxRecordChars) { LastError = "memory record ignored"; continue; }
                    if (!TryRead(line, out LlmMemory memory) || memory.GameId != gameId || memory.SeatId != seatId)
                    { LastError = "memory record ignored"; continue; }
                    latest[memory.Id] = memory;
                }
                foreach (LlmMemory memory in latest.Values) result.Add(memory);
            }
            catch (Exception) { LastError = "memory file could not be read"; }
            return result.AsReadOnly();
        }

        public bool Append(LlmMemory memory)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.Id)
                || string.IsNullOrWhiteSpace(memory.GameId) || string.IsNullOrWhiteSpace(memory.SeatId)
                || string.IsNullOrWhiteSpace(memory.Type) || string.IsNullOrWhiteSpace(memory.Content))
            {
                LastError = "invalid memory";
                return false;
            }
            string json = memory.ToJson();
            if (json.Length > MaxRecordChars) { LastError = "memory record too large"; return false; }
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(FilePath(memory.GameId, memory.SeatId), json + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                LastError = null;
                return true;
            }
            catch (Exception) { LastError = "memory record could not be written"; return false; }
        }

        private string FilePath(string gameId, string seatId)
            => Path.Combine(DirectoryPath, SafeName(gameId) + "__" + SafeName(seatId) + ".jsonl");

        private static string SafeName(string value)
        {
            var sb = new StringBuilder(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in value) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        private static bool TryRead(string json, out LlmMemory memory)
        {
            memory = null;
            if (!StrictJson.TryParse(json, out JsonNode root, out _) || !(root is JsonObject obj))
                return false;
            string id = StringField(obj, "id");
            string game = StringField(obj, "game_id");
            string seat = StringField(obj, "seat_id");
            string type = StringField(obj, "type");
            string content = StringField(obj, "content");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(game)
                || string.IsNullOrWhiteSpace(seat) || string.IsNullOrWhiteSpace(type)
                || string.IsNullOrWhiteSpace(content)) return false;
            int season = IntField(obj, "season");
            int schema = IntField(obj, "schema_version");
            if (schema != LlmMemory.CurrentSchemaVersion || season < 0) return false;
            memory = new LlmMemory
            {
                Id = id, GameId = game, SeatId = seat, Scope = StringField(obj, "scope"),
                Visibility = StringField(obj, "visibility"), Type = type, Content = content,
                Source = StringField(obj, "source"), Season = season,
                Importance = IntField(obj, "importance"), Confidence = IntField(obj, "confidence"),
                SupersededBy = StringField(obj, "superseded_by"),
                SchemaVersion = schema,
            };
            return memory.Scope == LlmMemoryScope.Game && memory.Visibility == LlmMemoryVisibility.Seat;
        }

        private static string StringField(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode value) && value is JsonString text ? text.Value : null;

        private static int IntField(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode value) && value is JsonInt number
                && number.Value >= int.MinValue && number.Value <= int.MaxValue ? (int)number.Value : 0;

        public bool Consolidate(string gameId, string seatId, int currentSeason,
            int retentionSeasons, int maxItems)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId)
                || currentSeason < 0 || retentionSeasons < 0 || maxItems <= 0)
            {
                LastError = "invalid memory consolidation";
                return false;
            }
            lock (_gate)
            {
                IReadOnlyList<LlmMemory> loaded = Load(gameId, seatId);
                IReadOnlyList<LlmMemory> consolidated = LlmMemoryLifecycle.Consolidate(
                    loaded, currentSeason, retentionSeasons, maxItems);
                string path = FilePath(gameId, seatId);
                string temp = path + ".tmp";
                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                    {
                        foreach (LlmMemory memory in consolidated)
                            writer.WriteLine(memory.ToJson());
                    }
                    File.Copy(temp, path, true);
                    File.Delete(temp);
                    LastError = null;
                    return true;
                }
                catch (Exception)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                    LastError = "memory consolidation failed";
                    return false;
                }
            }
        }
    }

    public static class LlmMemoryExtractor
    {
        public static LlmMemory FromAction(string gameId, string seatId, int season,
            LlmAction action, bool accepted, string result)
        {
            if (action == null || string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(seatId))
                return null;
            string target = string.IsNullOrEmpty(action.TargetId) ? "" : " " + action.TargetId;
            string content = action.Type + target + (accepted ? " was accepted" : " was rejected: " + result);
            return Create(gameId, seatId, season, accepted ? LlmMemoryType.Fact : LlmMemoryType.Lesson,
                content, "engine_action", accepted ? 2 : 3);
        }

        public static LlmMemory FromSummary(string gameId, string seatId, int season, string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return null;
            string content = Clean(summary, 200);
            return Create(gameId, seatId, season, LlmMemoryType.Decision, content, "model_summary", 1);
        }

        private static LlmMemory Create(string gameId, string seatId, int season, string type,
            string content, string source, int importance)
        {
            content = Clean(content, 240);
            return new LlmMemory
            {
                Id = Hash(gameId + "\n" + seatId + "\n" + season + "\n" + type + "\n" + content),
                GameId = gameId, SeatId = seatId, Type = type, Content = content,
                Source = source, Season = Math.Max(0, season), Importance = importance, Confidence = 1,
            };
        }

        private static string Clean(string value, int max)
        {
            var sb = new StringBuilder(Math.Min(value.Length, max));
            foreach (char c in value)
            {
                if (sb.Length >= max) break;
                sb.Append(char.IsControl(c) ? ' ' : c);
            }
            return sb.ToString().Trim();
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

    }

    public static class LlmMemoryLifecycle
    {
        public const int DefaultRetentionSeasons = 8;
        public const int DefaultMaxItems = 100;

        public static IReadOnlyList<LlmMemory> Consolidate(IReadOnlyList<LlmMemory> memories,
            int currentSeason, int retentionSeasons, int maxItems)
        {
            var deduped = new Dictionary<string, LlmMemory>(StringComparer.Ordinal);
            if (memories != null)
            {
                foreach (LlmMemory memory in memories)
                {
                    if (memory == null || string.IsNullOrWhiteSpace(memory.Content)
                        || memory.Season < Math.Max(0, currentSeason - retentionSeasons)
                        || !string.IsNullOrEmpty(memory.SupersededBy))
                        continue;
                    string key = memory.Type + "\n" + Normalize(memory.Content);
                    if (!deduped.TryGetValue(key, out LlmMemory existing)
                        || IsNewer(memory, existing))
                        deduped[key] = memory.Copy();
                    else
                        Merge(existing, memory);
                }
            }

            var result = new List<LlmMemory>(deduped.Values);
            var conflicts = new Dictionary<string, LlmMemory>(StringComparer.Ordinal);
            foreach (LlmMemory memory in result)
            {
                string conflictKey = ConflictKey(memory);
                if (conflictKey == null) continue;
                if (!conflicts.TryGetValue(conflictKey, out LlmMemory prior))
                {
                    conflicts[conflictKey] = memory;
                    continue;
                }
                if (prior.Content == memory.Content) continue;
                LlmMemory newer = IsNewer(memory, prior) ? memory : prior;
                LlmMemory older = ReferenceEquals(newer, memory) ? prior : memory;
                older.SupersededBy = newer.Id;
            }
            result.Sort((a, b) =>
            {
                int active = string.IsNullOrEmpty(a.SupersededBy).CompareTo(
                    string.IsNullOrEmpty(b.SupersededBy));
                if (active != 0) return -active;
                int season = b.Season.CompareTo(a.Season);
                return season != 0 ? season : string.CompareOrdinal(a.Id, b.Id);
            });
            if (result.Count > maxItems) result.RemoveRange(maxItems, result.Count - maxItems);
            return result.AsReadOnly();
        }

        private static bool IsNewer(LlmMemory left, LlmMemory right)
            => left.Season > right.Season
                || (left.Season == right.Season && string.CompareOrdinal(left.Id, right.Id) > 0);

        private static void Merge(LlmMemory target, LlmMemory source)
        {
            target.Importance = Math.Max(target.Importance, source.Importance);
            target.Confidence = Math.Min(100, Math.Max(target.Confidence, source.Confidence) + 1);
            target.Season = Math.Max(target.Season, source.Season);
        }

        private static string ConflictKey(LlmMemory memory)
        {
            if (memory.Type != LlmMemoryType.Fact && memory.Type != LlmMemoryType.Lesson)
                return null;
            int marker = memory.Content.IndexOf(" was ", StringComparison.Ordinal);
            return marker <= 0 ? null : memory.Content.Substring(0, marker).Trim().ToLowerInvariant();
        }

        private static string Normalize(string value)
            => value.Trim().ToLowerInvariant();
    }

    public static class LlmMemoryRetriever
        {
            public const int DefaultMaxChars = 3000;

            public static IReadOnlyList<LlmMemory> Select(IReadOnlyList<LlmMemory> memories,
                Observation observation, IReadOnlyList<ActionOption> options, int maxChars = DefaultMaxChars)
            {
                var selected = new List<LlmMemory>();
                if (memories == null || maxChars <= 0) return selected.AsReadOnly();

                var terms = new List<string>();
                if (options != null)
                {
                    foreach (ActionOption option in options)
                    {
                        AddTerm(terms, option == null ? null : option.Type);
                        AddTerm(terms, option == null ? null : option.TargetId);
                    }
                }
                int season = observation == null ? 0 : observation.Season;
                string gameId = observation == null ? null : observation.GameId;
                string seatId = observation == null ? null : observation.SeatId;
                var ranked = new List<RankedMemory>();
                foreach (LlmMemory memory in memories)
                {
                    if (memory == null || string.IsNullOrWhiteSpace(memory.Content)
                        || string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(seatId)
                        || memory.GameId != gameId || memory.SeatId != seatId
                        || memory.Visibility != LlmMemoryVisibility.Seat
                        || memory.Scope != LlmMemoryScope.Game
                        || !string.IsNullOrEmpty(memory.SupersededBy))
                        continue;
                    int score = memory.Importance * 10 + memory.Confidence * 4;
                    score += Math.Max(0, 10 - Math.Abs(season - memory.Season));
                    string lower = memory.Content.ToLowerInvariant();
                    foreach (string term in terms)
                        if (lower.Contains(term)) score += 12;
                    ranked.Add(new RankedMemory(memory, score));
                }
                ranked.Sort((a, b) =>
                {
                    int result = b.Score.CompareTo(a.Score);
                    return result != 0 ? result : string.CompareOrdinal(a.Memory.Id, b.Memory.Id);
                });

                int used = 0;
                foreach (RankedMemory item in ranked)
                {
                    string line = Format(item.Memory);
                    int extra = line.Length + 2;
                    if (used + extra > maxChars) continue;
                    selected.Add(item.Memory.Copy());
                    used += extra;
                }
                return selected.AsReadOnly();
            }

            private static string Format(LlmMemory memory)
                => "[" + memory.Type + "] " + memory.Content;

            private static void AddTerm(List<string> terms, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                string term = value.Trim().ToLowerInvariant();
                if (term.Length >= 3 && !terms.Contains(term)) terms.Add(term);
            }

            private sealed class RankedMemory
            {
                public readonly LlmMemory Memory;
                public readonly int Score;
                public RankedMemory(LlmMemory memory, int score) { Memory = memory; Score = score; }
        }
    }
}
