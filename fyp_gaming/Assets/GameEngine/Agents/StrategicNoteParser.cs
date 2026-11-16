using System;

namespace LuanShi.Engine
{
    /// <summary>Wire vocabulary for the terminal strategic-note reply.</summary>
    public static class StrategicNoteContract
    {
        public const string Version = "ls023-v1";
        public const string Tool = "strategic_note";
        public const string FieldTool = "tool";
        public const string FieldText = "text";

        public const string EmptyResponse = "empty_response";
        public const string InputTooLarge = "input_too_large";
        public const string InvalidJson = "invalid_json";
        public const string DuplicateKey = "duplicate_key";
        public const string TrailingContent = "trailing_content";
        public const string NotAnObject = "not_an_object";
        public const string ActionNoteAmbiguity = "action_note_ambiguity";
        public const string ForbiddenField = "forbidden_field";
        public const string MissingTool = "missing_tool";
        public const string UnknownTool = "unknown_tool";
        public const string UnknownField = "unknown_field";
        public const string InvalidFieldType = "invalid_field_type";
        public const string InvalidCharacter = "invalid_character";
        public const string FieldTooLong = "field_too_long";
        public const string MissingField = "missing_field";
    }

    /// <summary>
    /// One validated strategic note. Notes are model-authored prompt context only; they are not
    /// commands and never reach the engine validator.
    /// </summary>
    public sealed class StrategicNoteParseResult
    {
        public string ContractVersion => StrategicNoteContract.Version;
        public bool Ok { get; internal set; }
        public string Text { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }

    /// <summary>
    /// Strict parser for exactly one strategic-note object:
    /// <c>{"tool":"strategic_note","text":"..."}</c>.
    ///
    /// It mirrors the investigation contract: one JSON object, no prose or markdown, no duplicate
    /// keys, no unknown or model-owned identity fields, no action/note ambiguity, no control
    /// characters and a hard length bound. Errors are fixed strings that never echo model text.
    /// Parsing performs no I/O, consumes no budget and changes no state.
    /// </summary>
    public static class StrategicNoteParser
    {
        public const int DefaultMaxInputChars = 4096;

        public static StrategicNoteParseResult Parse(string responseText, int maxTextChars)
        {
            int bound = maxTextChars < 1 ? 1 : maxTextChars;
            if (responseText == null) return Fail(StrategicNoteContract.EmptyResponse);
            if (responseText.Length > DefaultMaxInputChars)
                return Fail(StrategicNoteContract.InputTooLarge);
            if (responseText.Trim().Length == 0)
                return Fail(StrategicNoteContract.EmptyResponse);

            if (!StrictJson.TryParse(responseText, out JsonNode root, out string jsonError))
                return Fail(MapJsonFailure(jsonError));
            if (!(root is JsonObject obj))
                return Fail(StrategicNoteContract.NotAnObject);

            foreach (var field in obj.Items)
                if (InvestigationRequestContract.IsActionField(field.Key))
                    return Fail(StrategicNoteContract.ActionNoteAmbiguity);
            foreach (var field in obj.Items)
                if (InvestigationRequestContract.IsAuthorityField(field.Key))
                    return Fail(StrategicNoteContract.ForbiddenField);

            if (!obj.TryGet(StrategicNoteContract.FieldTool, out JsonNode toolNode))
                return Fail(StrategicNoteContract.MissingTool);
            if (!(toolNode is JsonString toolText))
                return Fail(StrategicNoteContract.InvalidFieldType);
            if (!string.Equals(toolText.Value, StrategicNoteContract.Tool, StringComparison.Ordinal))
                return Fail(StrategicNoteContract.UnknownTool);

            string text = null;
            bool hasText = false;
            foreach (var field in obj.Items)
            {
                if (field.Key == StrategicNoteContract.FieldTool) continue;
                if (field.Key != StrategicNoteContract.FieldText)
                    return Fail(StrategicNoteContract.UnknownField);
                if (!(field.Value is JsonString textNode))
                    return Fail(StrategicNoteContract.InvalidFieldType);
                text = textNode.Value;
                hasText = true;
            }

            if (!hasText) return Fail(StrategicNoteContract.MissingField);
            text = text == null ? null : text.Trim();
            if (string.IsNullOrEmpty(text)) return Fail(StrategicNoteContract.MissingField);
            for (int i = 0; i < text.Length; i++)
                if (char.IsControl(text[i])) return Fail(StrategicNoteContract.InvalidCharacter);
            if (text.Length > bound) return Fail(StrategicNoteContract.FieldTooLong);

            return new StrategicNoteParseResult { Ok = true, Text = text };
        }

        /// <summary>
        /// Cheap classification used before the action/inquiry parsers. A malformed note object is
        /// still classified as a note so it receives the bounded note-repair message instead of being
        /// misread as an action.
        /// </summary>
        public static bool IsCandidate(string responseText)
        {
            if (string.IsNullOrEmpty(responseText) || responseText.Length > DefaultMaxInputChars)
                return false;
            if (!StrictJson.TryParse(responseText, out JsonNode root, out _) || !(root is JsonObject obj))
                return false;
            return obj.TryGet(StrategicNoteContract.FieldTool, out JsonNode tool)
                   && tool is JsonString text
                   && string.Equals(text.Value, StrategicNoteContract.Tool, StringComparison.Ordinal);
        }

        private static string MapJsonFailure(string detail)
        {
            switch (detail)
            {
                case "duplicate key": return StrategicNoteContract.DuplicateKey;
                case "trailing data": return StrategicNoteContract.TrailingContent;
                case "input too large": return StrategicNoteContract.InputTooLarge;
                default: return StrategicNoteContract.InvalidJson;
            }
        }

        private static StrategicNoteParseResult Fail(string code)
            => new StrategicNoteParseResult { Ok = false, ErrorCode = code, Error = MessageFor(code) };

        /// <summary>Fixed, bounded messages; model text is never appended.</summary>
        private static string MessageFor(string code)
        {
            switch (code)
            {
                case StrategicNoteContract.EmptyResponse:
                    return "A response must contain exactly one JSON object.";
                case StrategicNoteContract.InputTooLarge:
                    return "The response exceeds the configured character bound.";
                case StrategicNoteContract.InvalidJson:
                    return "The response is not valid JSON.";
                case StrategicNoteContract.DuplicateKey:
                    return "The response contains a duplicate field.";
                case StrategicNoteContract.TrailingContent:
                    return "The response contains content after the single object.";
                case StrategicNoteContract.NotAnObject:
                    return "Exactly one object is required; arrays and other values are not accepted.";
                case StrategicNoteContract.ActionNoteAmbiguity:
                    return "A strategic note cannot also contain an action command.";
                case StrategicNoteContract.ForbiddenField:
                    return "A host-owned identity field is not permitted in a strategic note.";
                case StrategicNoteContract.MissingTool:
                    return "The 'tool' field is required.";
                case StrategicNoteContract.UnknownTool:
                    return "The requested tool is not the strategic_note tool.";
                case StrategicNoteContract.UnknownField:
                    return "A field is not part of the strategic note schema.";
                case StrategicNoteContract.InvalidFieldType:
                    return "A request field has the wrong JSON type.";
                case StrategicNoteContract.InvalidCharacter:
                    return "The note contains a control character.";
                case StrategicNoteContract.FieldTooLong:
                    return "The note exceeds the configured length bound.";
                case StrategicNoteContract.MissingField:
                    return "A non-empty 'text' field is required.";
                default:
                    return "The request was rejected.";
            }
        }
    }
}
