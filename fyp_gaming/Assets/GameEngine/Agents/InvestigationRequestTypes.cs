using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>The four read-only investigations a model may request (LS-020).</summary>
    public enum InvestigationRequestTool
    {
        ManualLookup,
        InspectEntity,
        SearchHistory,
        RecallMemory,
    }

    /// <summary>
    /// Versioned vocabulary for the investigation request contract. Names are stable wire values;
    /// the parser accepts them exactly (no aliases, no case folding).
    /// </summary>
    public static class InvestigationRequestContract
    {
        public const string Version = InvestigationRequestOptions.ContractVersion;

        public const string ManualLookup = "manual_lookup";
        public const string InspectEntity = "inspect_entity";
        public const string SearchHistory = "search_history";
        public const string RecallMemory = "recall_memory";

        public const string FieldTool = "tool";
        public const string FieldTopic = "topic";
        public const string FieldKind = "kind";
        public const string FieldId = "id";
        public const string FieldQuery = "query";
        public const string FieldLimit = "limit";
        public const string FieldOffset = "offset";

        public const string EntityKindCity = "city";
        public const string EntityKindArmy = "army";

        // Host-owned routing/identity. A model can never set these: the host binds match, seat,
        // provider and controller context before parsing. Presence is rejected, never ignored.
        private static readonly HashSet<string> AuthorityFields = new HashSet<string>(
            new[]
            {
                "game_id", "game", "game_state", "match_id", "match",
                "seat_id", "seat", "actor_seat_id", "actor", "player", "player_id",
                "player_seat_id", "controller", "controller_type", "token", "auth",
                "authority", "action_id",
            },
            StringComparer.Ordinal);

        // An action command mixed into the same object is ambiguous: the host must never have to
        // guess whether the model meant to investigate or to mutate the world.
        private static readonly HashSet<string> ActionFields = new HashSet<string>(
            new[]
            {
                "type", "action", "actions", "action_type", "command", "commands",
                "end_turn", "tool_call", "tool_calls",
            },
            StringComparer.Ordinal);

        internal static bool IsAuthorityField(string key) => AuthorityFields.Contains(key);

        internal static bool IsActionField(string key) => ActionFields.Contains(key);

        public static string NameOf(InvestigationRequestTool tool)
        {
            switch (tool)
            {
                case InvestigationRequestTool.ManualLookup: return ManualLookup;
                case InvestigationRequestTool.InspectEntity: return InspectEntity;
                case InvestigationRequestTool.SearchHistory: return SearchHistory;
                case InvestigationRequestTool.RecallMemory: return RecallMemory;
                default: return null;
            }
        }

        internal static bool TryParseTool(string value, out InvestigationRequestTool tool)
        {
            switch (value)
            {
                case ManualLookup: tool = InvestigationRequestTool.ManualLookup; return true;
                case InspectEntity: tool = InvestigationRequestTool.InspectEntity; return true;
                case SearchHistory: tool = InvestigationRequestTool.SearchHistory; return true;
                case RecallMemory: tool = InvestigationRequestTool.RecallMemory; return true;
                default: tool = default(InvestigationRequestTool); return false;
            }
        }
    }

    /// <summary>
    /// One validated request. Instances can only be produced by <see cref="InvestigationRequestParser"/>;
    /// field values are already type-, length- and range-checked. Only the fields relevant to
    /// <see cref="Tool"/> are populated, so a host never has to reinterpret a shared bag.
    /// </summary>
    public sealed class InvestigationRequest
    {
        public string ContractVersion => InvestigationRequestContract.Version;
        public InvestigationRequestTool Tool { get; private set; }
        public string ToolName => InvestigationRequestContract.NameOf(Tool);

        /// <summary>manual_lookup topic; empty means "return the bounded topic index".</summary>
        public string Topic { get; private set; }

        /// <summary>inspect_entity kind, normalized to "city" or "army".</summary>
        public string EntityKind { get; private set; }

        /// <summary>inspect_entity id, matched exactly against the current observation.</summary>
        public string EntityId { get; private set; }

        /// <summary>search_history/recall_memory query; empty means "newest/strongest first".</summary>
        public string Query { get; private set; }

        /// <summary>
        /// Concrete result limit for search_history/recall_memory. An omitted or zero limit is
        /// resolved to the configured default at parse time, never forwarded as zero.
        /// </summary>
        public int Limit { get; private set; }

        /// <summary>search_history pagination offset; zero for every other tool.</summary>
        public int Offset { get; private set; }

        internal bool IsValidated { get; private set; }

        private InvestigationRequest() { }

        internal static InvestigationRequest Manual(string topic)
            => new InvestigationRequest
            {
                Tool = InvestigationRequestTool.ManualLookup,
                Topic = topic,
                IsValidated = true,
            };

        internal static InvestigationRequest Inspect(string kind, string id)
            => new InvestigationRequest
            {
                Tool = InvestigationRequestTool.InspectEntity,
                EntityKind = kind,
                EntityId = id,
                IsValidated = true,
            };

        internal static InvestigationRequest History(string query, int limit, int offset)
            => new InvestigationRequest
            {
                Tool = InvestigationRequestTool.SearchHistory,
                Query = query,
                Limit = limit,
                Offset = offset,
                IsValidated = true,
            };

        internal static InvestigationRequest Memory(string query, int limit)
            => new InvestigationRequest
            {
                Tool = InvestigationRequestTool.RecallMemory,
                Query = query,
                Limit = limit,
                IsValidated = true,
            };
    }

    /// <summary>
    /// Deterministic, bounded parser outcome. Failures carry a stable code, a fixed plain-English
    /// message and (for known schema fields) the field name; they never echo model-supplied text.
    /// </summary>
    public sealed class InvestigationRequestParseResult
    {
        public string ContractVersion => InvestigationRequestContract.Version;
        public bool Ok { get; private set; }
        public InvestigationRequest Request { get; private set; }
        public string ErrorCode { get; private set; }
        public string Error { get; private set; }

        /// <summary>One of the contract field names, or null when naming it would echo input.</summary>
        public string ErrorField { get; private set; }

        internal static InvestigationRequestParseResult Success(InvestigationRequest request)
            => new InvestigationRequestParseResult { Ok = true, Request = request };

        internal static InvestigationRequestParseResult Failure(string code, string message,
            string field = null)
            => new InvestigationRequestParseResult
            {
                Ok = false,
                ErrorCode = code,
                Error = message,
                ErrorField = field,
            };
    }

    /// <summary>Stable error-code vocabulary for the LS-020 request contract.</summary>
    public static class InvestigationRequestError
    {
        public const string EmptyResponse = "empty_response";
        public const string InputTooLarge = "input_too_large";
        public const string InvalidJson = "invalid_json";
        public const string DuplicateKey = "duplicate_key";
        public const string TrailingContent = "trailing_content";
        public const string NotAnObject = "not_an_object";
        public const string ActionToolAmbiguity = "action_tool_ambiguity";
        public const string ForbiddenField = "forbidden_field";
        public const string MissingTool = "missing_tool";
        public const string UnknownTool = "unknown_tool";
        public const string UnknownField = "unknown_field";
        public const string InvalidFieldType = "invalid_field_type";
        public const string InvalidCharacter = "invalid_character";
        public const string FieldTooLong = "field_too_long";
        public const string MissingField = "missing_field";
        public const string InvalidEntityKind = "invalid_entity_kind";
        public const string InvalidLimit = "invalid_limit";
        public const string InvalidOffset = "invalid_offset";
    }
}
