using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal sealed class PlaygroundNoteRecord
    {
        public string MatchId;
        public string SeatId;
        public string Text;
        public int Season;
    }

    /// <summary>
    /// Strategic notes are model data scoped to one match and one faction. They are never read by the
    /// engine, never treated as instructions and never shared across seats. Implementations keep only
    /// the latest note per (match, seat).
    /// </summary>
    internal interface IPlaygroundNoteStore
    {
        string Load(string matchId, string seatId);
        bool Save(string matchId, string seatId, string text, int season);
        IReadOnlyList<PlaygroundNoteRecord> Saved { get; }
        void Reset();
    }

    internal sealed class InMemoryPlaygroundNoteStore : IPlaygroundNoteStore
    {
        private readonly Dictionary<string, PlaygroundNoteRecord> _notes =
            new Dictionary<string, PlaygroundNoteRecord>(StringComparer.Ordinal);
        private readonly List<PlaygroundNoteRecord> _saved = new List<PlaygroundNoteRecord>();

        public IReadOnlyList<PlaygroundNoteRecord> Saved => _saved.AsReadOnly();

        public string Load(string matchId, string seatId)
        {
            PlaygroundNoteRecord record;
            return _notes.TryGetValue(Key(matchId, seatId), out record) ? record.Text : null;
        }

        public bool Save(string matchId, string seatId, string text, int season)
        {
            if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(seatId)
                || string.IsNullOrWhiteSpace(text)) return false;
            var record = new PlaygroundNoteRecord
            {
                MatchId = matchId, SeatId = seatId, Text = text, Season = season,
            };
            _notes[Key(matchId, seatId)] = record;
            _saved.Add(record);
            return true;
        }

        public void Reset()
        {
            _notes.Clear();
            _saved.Clear();
        }

        private static string Key(string matchId, string seatId) => (matchId ?? "") + "\n" + (seatId ?? "");
    }

    /// <summary>Append-only JSONL notes for a local run. Only the latest note per key is returned.</summary>
    internal sealed class FilePlaygroundNoteStore : IPlaygroundNoteStore
    {
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly List<PlaygroundNoteRecord> _saved = new List<PlaygroundNoteRecord>();
        private bool _loaded;

        public FilePlaygroundNoteStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("a note path is required", nameof(path));
            _path = Path.GetFullPath(path);
        }

        public string FilePath => _path;
        public IReadOnlyList<PlaygroundNoteRecord> Saved
        {
            get { lock (_gate) { EnsureLoaded(); return _saved.AsReadOnly(); } }
        }

        public string Load(string matchId, string seatId)
        {
            lock (_gate)
            {
                EnsureLoaded();
                string key = Key(matchId, seatId);
                for (int i = _saved.Count - 1; i >= 0; i--)
                    if (Key(_saved[i].MatchId, _saved[i].SeatId) == key) return _saved[i].Text;
                return null;
            }
        }

        public bool Save(string matchId, string seatId, string text, int season)
        {
            if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(seatId)
                || string.IsNullOrWhiteSpace(text)) return false;
            var record = new PlaygroundNoteRecord
            {
                MatchId = matchId, SeatId = seatId, Text = text, Season = season,
            };
            lock (_gate)
            {
                try
                {
                    string directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.AppendAllText(_path, ToJson(record) + "\n", new UTF8Encoding(false));
                    _saved.Add(record);
                    _loaded = true;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _saved.Clear();
                _loaded = true;
                try { if (File.Exists(_path)) File.Delete(_path); }
                catch (Exception) { }
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
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!StrictJson.TryParse(line, out JsonNode root, out _) || !(root is JsonObject obj))
                        continue;
                    string match = Text(obj, "match_id");
                    string seat = Text(obj, "seat_id");
                    string text = Text(obj, "note");
                    if (string.IsNullOrWhiteSpace(match) || string.IsNullOrWhiteSpace(seat)
                        || string.IsNullOrWhiteSpace(text)) continue;
                    _saved.Add(new PlaygroundNoteRecord
                    {
                        MatchId = match,
                        SeatId = seat,
                        Text = text,
                        Season = Int(obj, "season"),
                    });
                }
            }
            catch (Exception)
            {
                // A malformed local file degrades to "no note"; it must not crash the run.
            }
        }

        private static string ToJson(PlaygroundNoteRecord record)
            => PlaygroundJson.Obj(
                "match_id", PlaygroundJson.Str(record.MatchId),
                "seat_id", PlaygroundJson.Str(record.SeatId),
                "note", PlaygroundJson.Str(PlaygroundJson.Clean(record.Text)),
                "season", PlaygroundJson.Num(record.Season));

        private static string Text(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode node) && node is JsonString text ? text.Value : null;

        private static int Int(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode node) && node is JsonInt number
                && number.Value >= int.MinValue && number.Value <= int.MaxValue ? (int)number.Value : 0;

        private static string Key(string matchId, string seatId) => (matchId ?? "") + "\n" + (seatId ?? "");
    }
}
