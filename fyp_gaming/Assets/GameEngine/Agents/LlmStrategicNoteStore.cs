using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Private, bounded strategic-note storage. Notes are model data, never instructions and never
    /// game state: the engine never reads them, they never grant permission and they are scoped to
    /// exactly one match and one seat. Implementations keep only the latest note per (match, seat)
    /// and must reject empty, oversized or control-character content.
    /// </summary>
    public interface ILlmStrategicNoteStore
    {
        /// <summary>Safe diagnostic for the last failed operation; never note content.</summary>
        string LastError { get; }

        /// <summary>The latest note for this match/player, or null when none exists.</summary>
        string Load(string gameId, string seatId);

        /// <summary>Replace this match/player's note. Returns false without partial writes on failure.</summary>
        bool Save(string gameId, string seatId, string text, int season);
    }

    /// <summary>Shared validation so every implementation applies the same hard bound.</summary>
    public static class LlmStrategicNoteRules
    {
        /// <summary>
        /// Defensive absolute cap. The runner additionally applies the configurable
        /// <see cref="LlmConfig.MaxNoteChars"/>; this constant only stops a store from persisting an
        /// unbounded blob if a caller bypasses the runner.
        /// </summary>
        public const int MaxNoteChars = 4096;

        public const int MaxIdChars = 128;

        public static bool TryNormalize(string text, int maxChars, out string clean, out string error)
        {
            clean = null;
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "empty_note"; return false; }
            int effectiveMax = maxChars <= 0 ? 0 : Math.Min(maxChars, MaxNoteChars);
            if (text.Length > effectiveMax) { error = "note_too_long"; return false; }
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsControl(c)) { error = "invalid_character"; return false; }
                sb.Append(c);
            }
            clean = sb.ToString().Trim();
            if (clean.Length == 0) { error = "empty_note"; return false; }
            return true;
        }

        internal static bool IsValidKey(string gameId, string seatId)
            => !string.IsNullOrWhiteSpace(gameId) && gameId.Length <= MaxIdChars
               && !string.IsNullOrWhiteSpace(seatId) && seatId.Length <= MaxIdChars;
    }

    /// <summary>
    /// In-process note store. This is the safe default so a deliberation session keeps its notes
    /// across turns without the host supplying storage; it is intentionally not durable across a
    /// process restart.
    /// </summary>
    public sealed class MemoryLlmStrategicNoteStore : ILlmStrategicNoteStore
    {
        private sealed class Entry
        {
            public string Text;
            public int Season;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _notes =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        public string LastError { get; private set; }

        public string Load(string gameId, string seatId)
        {
            if (!LlmStrategicNoteRules.IsValidKey(gameId, seatId)) return null;
            lock (_gate)
            {
                return _notes.TryGetValue(Key(gameId, seatId), out Entry entry) ? entry.Text : null;
            }
        }

        public bool Save(string gameId, string seatId, string text, int season)
        {
            LastError = null;
            if (!LlmStrategicNoteRules.IsValidKey(gameId, seatId))
            {
                LastError = "invalid_note_scope";
                return false;
            }
            if (!LlmStrategicNoteRules.TryNormalize(text, LlmStrategicNoteRules.MaxNoteChars,
                    out string clean, out string error))
            {
                LastError = error;
                return false;
            }
            lock (_gate)
            {
                _notes[Key(gameId, seatId)] = new Entry { Text = clean, Season = Math.Max(0, season) };
            }
            return true;
        }

        public void Reset()
        {
            lock (_gate) { _notes.Clear(); }
        }

        private static string Key(string gameId, string seatId) => gameId + "\n" + seatId;
    }

    /// <summary>
    /// Append-only local JSONL note store, keyed by match and seat. Only the newest record for a key
    /// is returned. A malformed line degrades to "no note" and never crashes a run. The file is
    /// local evidence storage; the host chooses its ignored directory.
    /// </summary>
    public sealed class LocalLlmStrategicNoteStore : ILlmStrategicNoteStore
    {
        private const int MaxRecordChars = 8192;
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly Dictionary<string, Entry> _latest =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _loaded;

        private sealed class Entry
        {
            public string Text;
        }

        public string FilePath { get; }
        public string LastError { get; private set; }

        public LocalLlmStrategicNoteStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("a note path is required", nameof(path));
            FilePath = Path.GetFullPath(path);
            _path = FilePath;
        }

        public string Load(string gameId, string seatId)
        {
            if (!LlmStrategicNoteRules.IsValidKey(gameId, seatId)) return null;
            lock (_gate)
            {
                EnsureLoaded();
                return _latest.TryGetValue(Key(gameId, seatId), out Entry entry) ? entry.Text : null;
            }
        }

        public bool Save(string gameId, string seatId, string text, int season)
        {
            LastError = null;
            if (!LlmStrategicNoteRules.IsValidKey(gameId, seatId))
            {
                LastError = "invalid_note_scope";
                return false;
            }
            if (!LlmStrategicNoteRules.TryNormalize(text, LlmStrategicNoteRules.MaxNoteChars,
                    out string clean, out string error))
            {
                LastError = error;
                return false;
            }
            string line = "{\"game_id\":" + MiniJson.Str(gameId)
                + ",\"seat_id\":" + MiniJson.Str(seatId)
                + ",\"season\":" + Math.Max(0, season).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ",\"note\":" + MiniJson.Str(clean) + "}";
            if (line.Length > MaxRecordChars)
            {
                LastError = "note_record_too_large";
                return false;
            }
            lock (_gate)
            {
                EnsureLoaded();
                try
                {
                    string directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.AppendAllText(_path, line + "\n", new UTF8Encoding(false));
                    _latest[Key(gameId, seatId)] = new Entry { Text = clean };
                    return true;
                }
                catch (Exception)
                {
                    LastError = "note_record_could_not_be_written";
                    return false;
                }
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            if (!File.Exists(_path)) return;
            try
            {
                foreach (string line in File.ReadAllLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Length > MaxRecordChars) continue;
                    if (!StrictJson.TryParse(line, out JsonNode root, out _) || !(root is JsonObject obj))
                        continue;
                    string gameId = Text(obj, "game_id");
                    string seatId = Text(obj, "seat_id");
                    string note = Text(obj, "note");
                    if (!LlmStrategicNoteRules.IsValidKey(gameId, seatId)
                        || string.IsNullOrEmpty(note)) continue;
                    if (!LlmStrategicNoteRules.TryNormalize(note,
                            LlmStrategicNoteRules.MaxNoteChars, out string clean, out _)) continue;
                    _latest[Key(gameId, seatId)] = new Entry { Text = clean };
                }
            }
            catch (Exception)
            {
                LastError = "note_file_could_not_be_read";
            }
        }

        private static string Text(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode node) && node is JsonString text ? text.Value : null;

        private static string Key(string gameId, string seatId) => gameId + "\n" + seatId;
    }
}
