using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // Central credential redaction and the directory sweep. Kept in its own file so the section
    // builders stay under the line cap.
    public static partial class CsEvidence
    {
        /// <summary>Fixed marker for the configured key, distinct from the pattern marker.</summary>
        internal const string RedactedKeyMarker = "[redacted-key]";

        private static readonly object KeyGate = new object();
        private static string _cachedKey;
        private static string _cachedDirectEnv;
        private static bool _keyCached;

        /// <summary>
        /// The configured API key, read once from LlmConfig's environment value and held only in
        /// memory. The cache is invalidated when the direct environment value changes, so tests
        /// can set and restore the variable safely. Empty and very short keys are ignored.
        /// </summary>
        internal static string ConfiguredApiKey()
        {
            string direct = Environment.GetEnvironmentVariable("LUANSHI_LLM_API_KEY");
            lock (KeyGate)
            {
                if (_keyCached && string.Equals(direct, _cachedDirectEnv, StringComparison.Ordinal))
                    return _cachedKey;
                string key;
                try { key = LlmConfig.Load().ApiKeyValue; }
                catch (Exception) { key = direct; }
                _cachedKey = key;
                _cachedDirectEnv = direct;
                _keyCached = true;
                return key;
            }
        }

        private static readonly object VariantGate = new object();
        private static string _variantKey;
        private static string[] _variants;

        /// <summary>
        /// Every stored form of the key: raw, percent-encoded (upper- and lower-case hex) and the
        /// System.Text.Json string-escaped form, de-duplicated and longest first. Cached with the
        /// exact key so the common no-key case stays cheap.
        /// </summary>
        private static string[] RedactionVariants(string key)
        {
            lock (VariantGate)
            {
                if (_variants != null && string.Equals(_variantKey, key, StringComparison.Ordinal))
                    return _variants;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var list = new List<string>();
                void Add(string value)
                {
                    if (!string.IsNullOrEmpty(value) && seen.Add(value)) list.Add(value);
                }
                Add(key);
                string encoded = Uri.EscapeDataString(key);
                Add(encoded);
                Add(PercentHexLower(encoded));
                Add(JsonEscaped(key));
                list.Sort((a, b) => b.Length.CompareTo(a.Length));
                _variantKey = key;
                _variants = list.ToArray();
                return _variants;
            }
        }

        /// <summary>Lower-cases only the hex digits of percent escapes, so both forms are caught.</summary>
        private static string PercentHexLower(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0) return value;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '%' && i + 2 < value.Length)
                {
                    sb.Append('%').Append(char.ToLowerInvariant(value[i + 1]))
                        .Append(char.ToLowerInvariant(value[i + 2]));
                    i += 2;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string JsonEscaped(string key)
        {
            string json = JsonSerializer.Serialize(key);
            return json.Length >= 2 ? json.Substring(1, json.Length - 2) : key;
        }

        /// <summary>
        /// The single redaction function every evidence field and diagnostic passes through: every
        /// stored form of the configured key becomes <see cref="RedactedKeyMarker"/> (exact values
        /// only, and only when long enough to be a credential), then sk-/Bearer-shaped tokens are
        /// scrubbed.
        /// </summary>
        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string redacted = value;
            string key = ConfiguredApiKey();
            if (!string.IsNullOrEmpty(key) && key.Length >= CsLiveEnvironment.MinimumCredentialLength)
            {
                foreach (string variant in RedactionVariants(key))
                    if (redacted.IndexOf(variant, StringComparison.Ordinal) >= 0)
                        redacted = redacted.Replace(variant, RedactedKeyMarker);
            }
            return Secret.Replace(redacted, "<redacted>");
        }

        /// <summary>Recursively applies <see cref="Redact"/> to every string in a record tree.</summary>
        internal static object RedactValue(object value)
        {
            if (value == null) return null;
            if (value is string text) return Redact(text);
            if (value is Dictionary<string, object> map)
            {
                var copy = new Dictionary<string, object>(map.Count, StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> pair in map)
                    copy[pair.Key] = RedactValue(pair.Value);
                return copy;
            }
            if (value is System.Collections.IEnumerable sequence)
            {
                var list = new List<object>();
                foreach (object item in sequence) list.Add(RedactValue(item));
                return list;
            }
            return value;
        }

        /// <summary>
        /// Rewrites every file under <paramref name="directory"/> (recursive, every extension) in
        /// place through <see cref="Redact"/>, as UTF-8 without a BOM. Files whose text does not
        /// change are not touched, so mtimes and binary artifacts stay stable. This is the last
        /// line of defence for artifacts written by production stores after the evidence pass.
        /// </summary>
        internal static void RedactDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            string[] files;
            try { files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories); }
            catch (Exception) { return; }
            foreach (string path in files)
            {
                try
                {
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    string clean = Redact(text);
                    if (string.Equals(text, clean, StringComparison.Ordinal)) continue;
                    File.WriteAllText(path, clean, new UTF8Encoding(false));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Redacts model-authored text before the production note store persists it, so the stored
    /// (and therefore prompt-visible) record never contains a configured credential.
    /// </summary>
    internal sealed class CsRedactingNoteStore : ILlmStrategicNoteStore
    {
        private readonly ILlmStrategicNoteStore _inner;

        public CsRedactingNoteStore(ILlmStrategicNoteStore inner) { _inner = inner; }

        public string LastError => _inner.LastError;

        public string Load(string gameId, string seatId) => _inner.Load(gameId, seatId);

        public bool Save(string gameId, string seatId, string text, int season)
            => _inner.Save(gameId, seatId, CsEvidence.Redact(text), season);
    }

    /// <summary>
    /// Redacts the model-authored fields of a memory record before the production store persists
    /// it. Identity and scope fields are left untouched so load filtering still works.
    /// </summary>
    internal sealed class CsRedactingMemoryStore : ILlmMemoryStore
    {
        private readonly ILlmMemoryStore _inner;

        public CsRedactingMemoryStore(ILlmMemoryStore inner) { _inner = inner; }

        public string LastError => _inner.LastError;

        public IReadOnlyList<LlmMemory> Load(string gameId, string seatId)
            => _inner.Load(gameId, seatId);

        public bool Append(LlmMemory memory)
        {
            if (memory == null) return _inner.Append(null);
            var clean = new LlmMemory
            {
                Id = memory.Id, GameId = memory.GameId, SeatId = memory.SeatId,
                Scope = memory.Scope, Visibility = memory.Visibility, Type = memory.Type,
                Content = CsEvidence.Redact(memory.Content),
                Source = CsEvidence.Redact(memory.Source),
                Season = memory.Season, Importance = memory.Importance,
                Confidence = memory.Confidence,
                SupersededBy = CsEvidence.Redact(memory.SupersededBy),
                SchemaVersion = memory.SchemaVersion,
            };
            return _inner.Append(clean);
        }

        public bool Consolidate(string gameId, string seatId, int currentSeason,
            int retentionSeasons, int maxItems)
            => _inner.Consolidate(gameId, seatId, currentSeason, retentionSeasons, maxItems);
    }
}
