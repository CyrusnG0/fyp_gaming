using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LuanShi.Engine
{
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
    }
}
