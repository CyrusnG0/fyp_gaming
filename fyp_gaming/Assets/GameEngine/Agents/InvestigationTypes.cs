using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Small, dependency-free text helpers shared by the investigation tools (LS-018).
    /// They only inspect caller-supplied or observation-supplied strings; nothing here executes,
    /// interprets or fetches anything, so model text can never become an instruction.
    /// </summary>
    internal static class InvestigationText
    {
        /// <summary>Clips at a UTF-16 boundary without splitting a surrogate pair.</summary>
        public static string Clip(string value, int maxChars)
        {
            if (value == null) return null;
            if (maxChars <= 0) return "";
            if (value.Length <= maxChars) return value;
            int length = maxChars;
            if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
            return value.Substring(0, length);
        }

        /// <summary>Case-insensitive, separator-insensitive normal form used only for matching.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            bool pendingSpace = false;
            foreach (char c in value)
            {
                char normalized = c == '_' || c == '-' ? ' ' : c;
                if (char.IsWhiteSpace(normalized))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(char.ToLowerInvariant(normalized));
            }
            return sb.ToString();
        }

        /// <summary>Splits a normalized query into distinct non-empty tokens (AND semantics).</summary>
        public static List<string> Tokens(string value)
        {
            var tokens = new List<string>();
            string normalized = Normalize(value);
            int start = 0;
            for (int i = 0; i <= normalized.Length; i++)
            {
                if (i < normalized.Length && normalized[i] != ' ') continue;
                if (i > start)
                {
                    string token = normalized.Substring(start, i - start);
                    if (!tokens.Contains(token)) tokens.Add(token);
                }
                start = i + 1;
            }
            return tokens;
        }

        public static bool MatchesAll(string normalizedHaystack, List<string> tokens)
        {
            if (tokens == null || tokens.Count == 0) return true;
            foreach (string token in tokens)
                if (normalizedHaystack.IndexOf(token, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        public static void AppendTerm(StringBuilder sb, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(value);
        }

        public static string Percent(float value)
            => (value * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";

        public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Typed envelope shared by every read-only investigation result.</summary>
    public abstract class InvestigationResult
    {
        internal const string ResponseTooLargeCode = "response_too_large";
        internal const string ResponseTooLargeMessage =
            "Response exceeds the configured character bound.";

        private bool _compactPayload;

        public string Tool { get; internal set; }
        public string ResponseVersion => InvestigationOptions.ResponseVersion;
        public bool Ok { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
        public int ReturnedCount { get; internal set; }
        public bool Truncated { get; internal set; }

        protected InvestigationResult(string tool) { Tool = tool; }

        protected abstract string BuildPayloadJson();

        internal void Fail(string code, string message)
        {
            _compactPayload = false;
            Ok = false;
            ErrorCode = code;
            Error = message;
            ReturnedCount = 0;
            Truncated = false;
        }

        internal void Succeed(int returned)
        {
            _compactPayload = false;
            Ok = true;
            ErrorCode = null;
            Error = null;
            ReturnedCount = returned;
        }

        /// <summary>
        /// Enforces <see cref="InvestigationOptions.MaxResponseChars"/> on the serialized envelope.
        /// The length is measured after JSON escaping. Subclasses remove whole structural elements
        /// (or explicitly clip the frontier hit); if nothing more can be removed the result becomes a
        /// distinct, bounded explicit error. The caller's cap is never silently expanded.
        /// </summary>
        internal void ApplyResponseBound(InvestigationOptions options)
        {
            int cap = options.MaxResponseChars;
            while (ToJson().Length > cap)
            {
                if (!TrimForResponse(options))
                {
                    BecomeBoundedError(cap);
                    return;
                }
            }
        }

        /// <summary>
        /// Removes/shrinks one structural element. Returns false when the response cannot be made
        /// smaller without losing valid JSON; the base then emits a bounded error envelope.
        /// </summary>
        protected virtual bool TrimForResponse(InvestigationOptions options) => false;

        private void BecomeBoundedError(int cap)
        {
            // A compact payload drops the type-specific fields. If this was already a typed failure,
            // keep its semantic code/message as long as that smaller envelope still fits; only a
            // success that cannot fit (or an over-long message) becomes response_too_large.
            _compactPayload = true;
            if (!Ok && ErrorCode != null && ToJson().Length <= cap)
            {
                ReturnedCount = 0;
                Truncated = true;
                return;
            }
            Ok = false;
            ErrorCode = ResponseTooLargeCode;
            Error = ResponseTooLargeMessage;
            ReturnedCount = 0;
            Truncated = true;
        }

        public string ToJson()
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"tool\":").Append(MiniJson.Str(Tool));
            sb.Append(",\"response_version\":").Append(MiniJson.Str(ResponseVersion));
            sb.Append(",\"ok\":").Append(Ok ? "true" : "false");
            sb.Append(",\"error_code\":").Append(MiniJson.Str(ErrorCode));
            sb.Append(",\"error\":").Append(MiniJson.Str(Error));
            sb.Append(",\"returned\":").Append(MiniJson.Num(ReturnedCount));
            sb.Append(",\"truncated\":").Append(Truncated ? "true" : "false");
            sb.Append(",\"payload\":").Append(_compactPayload ? "{}" : BuildPayloadJson());
            sb.Append('}');
            return sb.ToString();
        }

        protected static string ObjectArray(IEnumerable<string> objects)
            => MiniJson.Array(objects);

        /// <summary>Copies a read-only view into a private mutable list so trimming stays structural.</summary>
        protected static List<T> CopyList<T>(IReadOnlyList<T> source)
            => source == null ? new List<T>() : new List<T>(source);
    }

    /// <summary>One discoverable operation exposed by the investigation foundation.</summary>
    public sealed class InvestigationToolDescriptor
    {
        public string Name { get; internal set; }
        public string Title { get; internal set; }
        public string Summary { get; internal set; }
        public bool ReadOnly { get; internal set; } = true;
        public bool MutatesGameState { get; internal set; } = false;

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"title\":").Append(MiniJson.Str(Title));
            sb.Append(",\"summary\":").Append(MiniJson.Str(Summary));
            sb.Append(",\"read_only\":").Append(ReadOnly ? "true" : "false");
            sb.Append(",\"mutates_game_state\":").Append(MutatesGameState ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Tool discovery result. It does not require a match context.</summary>
    public sealed class InvestigationToolCatalogueResult : InvestigationResult
    {
        private List<InvestigationToolDescriptor> _tools = new List<InvestigationToolDescriptor>();

        public IReadOnlyList<InvestigationToolDescriptor> Tools
        {
            get => _tools.AsReadOnly();
            set => _tools = CopyList(value);
        }

        public InvestigationToolCatalogueResult() : base("tool_catalogue") { }

        protected override string BuildPayloadJson()
        {
            var items = new List<string>(_tools.Count);
            foreach (InvestigationToolDescriptor tool in _tools) items.Add(tool.ToJson());
            return "{\"tools\":" + ObjectArray(items) + "}";
        }

        protected override bool TrimForResponse(InvestigationOptions options)
        {
            if (_tools.Count == 0) return false;
            _tools.RemoveAt(_tools.Count - 1);
            Truncated = true;
            Succeed(_tools.Count);
            return true;
        }
    }

    /// <summary>One plain-English rule fact. Values are rendered from BalanceConfig/engine contracts.</summary>
    public sealed class ManualFact
    {
        public string Name;
        public string Value;
        public string Note;

        public string ToJson()
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"value\":").Append(MiniJson.Str(Value));
            sb.Append(",\"note\":").Append(MiniJson.Str(Note));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One entry in the bounded topic index returned for discovery/unsupported requests.</summary>
    public sealed class TopicIndexEntry
    {
        public string Id;
        public string Title;
        public string Category;
        public bool LlmActionExposed;

        public string ToJson()
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"title\":").Append(MiniJson.Str(Title));
            sb.Append(",\"category\":").Append(MiniJson.Str(Category));
            sb.Append(",\"llm_action_exposed\":").Append(LlmActionExposed ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>A bounded manual_lookup result, including the discoverable topic index.</summary>
    public sealed class ManualLookupResult : InvestigationResult
    {
        private List<ManualFact> _facts = new List<ManualFact>();
        private List<string> _notes = new List<string>();
        private List<TopicIndexEntry> _topics = new List<TopicIndexEntry>();

        public string TopicId;
        public string Title;
        public string Category;
        public string RulesVersion;
        public bool Implemented;
        public bool LlmActionExposed;
        public int CommandPointCost = -1;   // -1 = this topic is not an action
        internal bool IsTopicIndex;

        public IReadOnlyList<ManualFact> Facts
        {
            get => _facts.AsReadOnly();
            set => _facts = CopyList(value);
        }

        public IReadOnlyList<string> Notes
        {
            get => _notes.AsReadOnly();
            set => _notes = CopyList(value);
        }

        public IReadOnlyList<TopicIndexEntry> Topics
        {
            get => _topics.AsReadOnly();
            set => _topics = CopyList(value);
        }

        public ManualLookupResult() : base("manual_lookup") { }

        protected override string BuildPayloadJson()
        {
            var facts = new List<string>(_facts.Count);
            foreach (ManualFact fact in _facts) facts.Add(fact.ToJson());
            var topics = new List<string>(_topics.Count);
            foreach (TopicIndexEntry topic in _topics) topics.Add(topic.ToJson());

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"topic_id\":").Append(MiniJson.Str(TopicId));
            sb.Append(",\"title\":").Append(MiniJson.Str(Title));
            sb.Append(",\"category\":").Append(MiniJson.Str(Category));
            sb.Append(",\"rules_version\":").Append(MiniJson.Str(RulesVersion));
            sb.Append(",\"implemented\":").Append(Implemented ? "true" : "false");
            sb.Append(",\"llm_action_exposed\":").Append(LlmActionExposed ? "true" : "false");
            sb.Append(",\"command_point_cost\":").Append(MiniJson.Num(CommandPointCost));
            sb.Append(",\"facts\":").Append(ObjectArray(facts));
            sb.Append(",\"notes\":").Append(MiniJson.StrArray(_notes));
            sb.Append(",\"topics\":").Append(ObjectArray(topics));
            sb.Append('}');
            return sb.ToString();
        }

        // The redundant index goes first for a detailed topic; then optional prose and, finally,
        // whole facts. Every step keeps the JSON structural and flips the explicit truncation flag.
        protected override bool TrimForResponse(InvestigationOptions options)
        {
            if (_topics.Count > 0)
            {
                _topics.RemoveAt(_topics.Count - 1);
                Truncated = true;
                if (Ok && IsTopicIndex) Succeed(_topics.Count);
                return true;
            }
            if (_notes.Count > 0)
            {
                _notes.RemoveAt(_notes.Count - 1);
                Truncated = true;
                return true;
            }
            if (_facts.Count > 0)
            {
                _facts.RemoveAt(_facts.Count - 1);
                Truncated = true;
                return true;
            }
            return false;
        }
    }
}
