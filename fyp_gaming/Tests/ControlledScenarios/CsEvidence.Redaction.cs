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

        private static readonly object MatcherGate = new object();
        private static string _matcherKey;
        private static System.Text.RegularExpressions.Regex _matcher;

        /// <summary>
        /// Matches the raw key OR, for every character, its complete UTF-8 percent encoding with
        /// case-insensitive hex digits, freely mixed per character within one value (for example
        /// "%2B" and "%2f" in the same string, and unreserved characters such as "A" written
        /// "%41"). The pattern is built from escaped literals only and uses
        /// RegexOptions.NonBacktracking, so it cannot blow up. Cached with the exact key.
        /// </summary>
        private static System.Text.RegularExpressions.Regex KeyMatcher(string key)
        {
            lock (MatcherGate)
            {
                if (_matcher != null && string.Equals(_matcherKey, key, StringComparison.Ordinal))
                    return _matcher;
                var sb = new StringBuilder(key.Length * 8);
                var buffer = new byte[4];
                foreach (Rune rune in key.EnumerateRunes())
                {
                    // One group per key character, concatenated in order: the whole value must
                    // match the key sequence, with each character literal OR percent-encoded.
                    sb.Append("(?:");
                    sb.Append(System.Text.RegularExpressions.Regex.Escape(rune.ToString()));
                    sb.Append('|');
                    int count = rune.EncodeToUtf8(buffer);
                    for (int i = 0; i < count; i++)
                    {
                        sb.Append('%').Append(HexNibble(buffer[i] >> 4))
                            .Append(HexNibble(buffer[i] & 0xF));
                    }
                    sb.Append(')');
                }
                _matcher = new System.Text.RegularExpressions.Regex(sb.ToString(),
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant
                    | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
                _matcherKey = key;
                return _matcher;
            }
        }

        /// <summary>A hex digit, or a class accepting both cases for an A-F nibble.</summary>
        private static string HexNibble(int value)
        {
            if (value < 10) return ((char)('0' + value)).ToString();
            char lower = (char)('a' + value - 10);
            return "[" + char.ToUpperInvariant(lower) + lower + "]";
        }

        /// <summary>
        /// True when <paramref name="value"/> contains the raw key or any supported encoded form.
        /// The matcher is the same one <see cref="Redact"/> uses, so URL-path checks and evidence
        /// redaction cannot drift apart. Keys shorter than the minimum credential length are
        /// ignored, exactly as in <see cref="Redact"/>.
        /// </summary>
        internal static bool MatchesKey(string value, string key)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(key)
                || key.Length < CsLiveEnvironment.MinimumCredentialLength) return false;
            return KeyMatcher(key).IsMatch(value);
        }

        private static string JsonEscaped(string key)
        {
            string json = JsonSerializer.Serialize(key);
            return json.Length >= 2 ? json.Substring(1, json.Length - 2) : key;
        }

        /// <summary>
        /// The single redaction function every evidence field and diagnostic passes through.
        /// Supported key forms are: raw; percent-encoded with any hex case, any per-character mix
        /// and unreserved characters included; and the System.Text.Json string-escaped form. Every
        /// supported form becomes <see cref="RedactedKeyMarker"/> (exact values only, and only when
        /// long enough to be a credential). NOT supported: base64, case-folded, split,
        /// HTML-escaped or otherwise rewritten forms. Finally sk-/Bearer-shaped tokens are scrubbed.
        /// </summary>
        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string key = ConfiguredApiKey();
            string redacted = value;
            if (!string.IsNullOrEmpty(key) && key.Length >= CsLiveEnvironment.MinimumCredentialLength)
            {
                string json = JsonEscaped(key);
                if (!string.Equals(json, key, StringComparison.Ordinal)
                    && redacted.IndexOf(json, StringComparison.Ordinal) >= 0)
                    redacted = redacted.Replace(json, RedactedKeyMarker);
                redacted = KeyMatcher(key).Replace(redacted, RedactedKeyMarker);
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
        /// The only extensions the runner writes as text. Every other file is left byte-for-byte
        /// untouched (binary evidence, images, lock files). A single constant so a new artifact
        /// type is added deliberately rather than by broad sweeps.
        /// </summary>
        internal static readonly HashSet<string> RedactionTextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".json", ".jsonl", ".ndjson", ".md", ".txt", ".csv", ".log",
            };

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Rewrites text artifacts under <paramref name="directory"/> (recursive) in place through
        /// <see cref="Redact"/>, as UTF-8 without a BOM, and returns a bounded, secret-free failure
        /// summary. Non-text extensions are never opened for writing. A text-extension file that
        /// contains NUL bytes or is not strict UTF-8 is skipped (not rewritten) and reported as a
        /// failure: because it cannot be decoded, its key-free status cannot be proven, so the run
        /// must never report success for it. A file whose text does not change is not touched.
        /// </summary>
        internal static CsRedactionSweepResult RedactDirectory(string directory)
        {
            var sweep = new CsRedactionSweepResult();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return sweep;
            string[] files;
            try { files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories); }
            catch (Exception) { sweep.Add("<directory>"); return sweep; }
            foreach (string path in files)
            {
                string extension = Path.GetExtension(path);
                if (string.IsNullOrEmpty(extension)
                    || !RedactionTextExtensions.Contains(extension)) continue;
                string relative = RelativePath(directory, path);
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string text;
                    try { text = StrictUtf8.GetString(bytes); }
                    catch (DecoderFallbackException) { sweep.Add(relative); continue; }
                    if (text.IndexOf('\0') >= 0) { sweep.Add(relative); continue; }
                    if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
                    string clean = Redact(text);
                    if (string.Equals(text, clean, StringComparison.Ordinal)) continue;
                    File.WriteAllText(path, clean, new UTF8Encoding(false));
                }
                catch (IOException) { sweep.Add(relative); }
                catch (UnauthorizedAccessException) { sweep.Add(relative); }
                catch (Exception) { sweep.Add(relative); }
            }
            return sweep;
        }

        private static string RelativePath(string root, string path)
        {
            string trimmed = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(trimmed.Length) : Path.GetFileName(path);
        }

        /// <summary>Folds a failed sweep into a scenario result: never a success, never key text.</summary>
        internal static void ApplySweepFailure(CsScenarioResult result, CsRedactionSweepResult sweep)
        {
            if (result == null || sweep == null || !sweep.Failed) return;
            result.Status = CsStatus.Failed;
            result.Detail = AppendNote(result.Detail, sweep.Summary);
        }

        /// <summary>Folds a failed sweep into a run result: never a success, never key text.</summary>
        internal static void ApplyRunSweepFailure(CsRunResult result, CsRedactionSweepResult sweep)
        {
            if (result == null || sweep == null || !sweep.Failed) return;
            result.Status = CsStatus.Failed;
            result.Detail = AppendNote(result.Detail, sweep.Summary);
        }

        private static string AppendNote(string detail, string note)
            => string.IsNullOrEmpty(detail) ? note : detail + "; " + note;
    }

    /// <summary>
    /// Bounded, secret-free outcome of one directory sweep. Only up to <see cref="MaxListed"/>
    /// run-relative paths are retained (plus the total count); file content and key text are never
    /// stored, and each relative path passes through <see cref="CsEvidence.Redact"/>.
    /// </summary>
    internal sealed class CsRedactionSweepResult
    {
        internal const int MaxListed = 10;

        public int Total { get; private set; }
        public List<string> Paths { get; } = new List<string>();
        public bool Failed => Total > 0;

        internal void Add(string relativePath)
        {
            Total++;
            if (Paths.Count < MaxListed) Paths.Add(CsEvidence.Redact(relativePath ?? ""));
        }

        public string Summary
        {
            get
            {
                if (!Failed) return null;
                string list = string.Join(", ", Paths);
                if (Total > Paths.Count) list += ", ...";
                return "redaction sweep failed for " + Total + " file(s): " + list;
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
