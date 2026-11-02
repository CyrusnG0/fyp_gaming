using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            Ok = false;
            ErrorCode = code;
            Error = message;
            ReturnedCount = 0;
            Truncated = false;
        }

        internal void Succeed(int returned)
        {
            Ok = true;
            ErrorCode = null;
            Error = null;
            ReturnedCount = returned;
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
            sb.Append(",\"payload\":").Append(BuildPayloadJson());
            sb.Append('}');
            return sb.ToString();
        }

        protected static string ObjectArray(IEnumerable<string> objects)
            => MiniJson.Array(objects);
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
        public IReadOnlyList<InvestigationToolDescriptor> Tools =
            new List<InvestigationToolDescriptor>().AsReadOnly();

        public InvestigationToolCatalogueResult() : base("tool_catalogue") { }

        protected override string BuildPayloadJson()
        {
            var items = new List<string>(Tools.Count);
            foreach (InvestigationToolDescriptor tool in Tools) items.Add(tool.ToJson());
            return "{\"tools\":" + ObjectArray(items) + "}";
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
        public string TopicId;
        public string Title;
        public string Category;
        public string RulesVersion;
        public bool Implemented;
        public bool LlmActionExposed;
        public int CommandPointCost = -1;   // -1 = this topic is not an action
        public IReadOnlyList<ManualFact> Facts = new List<ManualFact>().AsReadOnly();
        public IReadOnlyList<string> Notes = new List<string>().AsReadOnly();
        public IReadOnlyList<TopicIndexEntry> Topics = new List<TopicIndexEntry>().AsReadOnly();

        public ManualLookupResult() : base("manual_lookup") { }

        protected override string BuildPayloadJson()
        {
            var facts = new List<string>(Facts.Count);
            foreach (ManualFact fact in Facts) facts.Add(fact.ToJson());
            var topics = new List<string>(Topics.Count);
            foreach (TopicIndexEntry topic in Topics) topics.Add(topic.ToJson());

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
            sb.Append(",\"notes\":").Append(MiniJson.StrArray(Notes));
            sb.Append(",\"topics\":").Append(ObjectArray(topics));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>A detached copy of one observation entity. Only current-observation fields are exposed.</summary>
    public sealed class EntityInspectionResult : InvestigationResult
    {
        public string Kind;
        public bool Found;
        public string Knowledge;      // "current_observation" when Found
        public int Season;
        public CityView City;         // detached copy; null unless a city was found
        public ArmyView Army;         // detached copy; null unless an army was found

        public EntityInspectionResult() : base("inspect_entity") { }

        protected override string BuildPayloadJson()
        {
            string entity = City != null ? City.ToJson() : Army != null ? Army.ToJson() : "null";
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"kind\":").Append(MiniJson.Str(Kind));
            sb.Append(",\"found\":").Append(Found ? "true" : "false");
            sb.Append(",\"knowledge\":").Append(MiniJson.Str(Knowledge));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"entity\":").Append(entity);
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One detached history hit. Payload/args are copied and bounded.</summary>
    public sealed class HistoryHit
    {
        public int Seq;
        public int Season;
        public string Phase;
        public string ActorSeatId;
        public string ControllerType;
        public string EventType;
        public string Visibility;
        public string SourceActionId;
        public string Text;
        public IReadOnlyDictionary<string, string> Payload = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>());
        public IReadOnlyDictionary<string, string> Args;

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"seq\":").Append(MiniJson.Num(Seq));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"phase\":").Append(MiniJson.Str(Phase));
            sb.Append(",\"actor_seat_id\":").Append(MiniJson.Str(ActorSeatId));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(ControllerType));
            sb.Append(",\"event_type\":").Append(MiniJson.Str(EventType));
            sb.Append(",\"visibility\":").Append(MiniJson.Str(Visibility));
            sb.Append(",\"source_action_id\":").Append(MiniJson.Str(SourceActionId));
            sb.Append(",\"text\":").Append(MiniJson.Str(Text));
            sb.Append(",\"payload\":").Append(MiniJson.Dict(Payload));
            sb.Append(",\"args\":").Append(Args == null ? "null" : MiniJson.Dict(Args));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>A bounded, deterministic search over the seat's already filtered history.</summary>
    public sealed class HistorySearchResult : InvestigationResult
    {
        public string Query;
        public int Offset;
        public int NextOffset = -1;   // -1 = no further page
        public IReadOnlyList<HistoryHit> Hits = new List<HistoryHit>().AsReadOnly();

        public HistorySearchResult() : base("search_history") { }

        protected override string BuildPayloadJson()
        {
            var hits = new List<string>(Hits.Count);
            foreach (HistoryHit hit in Hits) hits.Add(hit.ToJson());
            var sb = new StringBuilder(384);
            sb.Append('{');
            sb.Append("\"query\":").Append(MiniJson.Str(Query));
            sb.Append(",\"offset\":").Append(MiniJson.Num(Offset));
            sb.Append(",\"next_offset\":").Append(MiniJson.Num(NextOffset));
            sb.Append(",\"hits\":").Append(ObjectArray(hits));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One detached memory hit. Beliefs are explicitly marked as untrusted model memory.</summary>
    public sealed class MemoryHit
    {
        public string MemoryId;
        public string Type;
        public string Source;
        public int Season;
        public string Content;
        public int Importance;
        public int Confidence;
        public string EpistemicStatus = "untrusted_model_memory";

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"memory_id\":").Append(MiniJson.Str(MemoryId));
            sb.Append(",\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"source\":").Append(MiniJson.Str(Source));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"content\":").Append(MiniJson.Str(Content));
            sb.Append(",\"importance\":").Append(MiniJson.Num(Importance));
            sb.Append(",\"confidence\":").Append(MiniJson.Num(Confidence));
            sb.Append(",\"epistemic_status\":").Append(MiniJson.Str(EpistemicStatus));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>A bounded, deterministic search over injected own-seat memory records.</summary>
    public sealed class MemoryRecallResult : InvestigationResult
    {
        public string Query;
        public IReadOnlyList<MemoryHit> Memories = new List<MemoryHit>().AsReadOnly();

        public MemoryRecallResult() : base("recall_memory") { }

        protected override string BuildPayloadJson()
        {
            var memories = new List<string>(Memories.Count);
            foreach (MemoryHit memory in Memories) memories.Add(memory.ToJson());
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"query\":").Append(MiniJson.Str(Query));
            sb.Append(",\"memories\":").Append(ObjectArray(memories));
            sb.Append('}');
            return sb.ToString();
        }
    }
}
