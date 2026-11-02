using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Strict parser for exactly one investigation request (LS-020).
    ///
    /// This is a security and contract boundary, not a convenience layer. It accepts one raw JSON
    /// object (no prose, markdown fence, array or trailing content), rejects duplicate keys,
    /// unknown fields, action/tool ambiguity, host-owned identity fields, wrong JSON types,
    /// missing required fields, control characters and arguments outside the configured bounds.
    /// Errors are fixed strings that never echo model-supplied text. Parsing performs no I/O,
    /// executes nothing, touches no game state and consumes no budget; the host must obtain budget
    /// permission before dispatching a successful result.
    /// </summary>
    public static class InvestigationRequestParser
    {
        public static InvestigationRequestParseResult Parse(string responseText,
            InvestigationRequestOptions options = null)
        {
            options = (options ?? InvestigationRequestOptions.Defaults()).Copy();
            options.Validate();
            InvestigationOptions service = options.ServiceOptions;

            if (responseText == null) return Fail(InvestigationRequestError.EmptyResponse);
            if (responseText.Length > options.MaxInputChars)
                return Fail(InvestigationRequestError.InputTooLarge);
            if (responseText.Trim().Length == 0)
                return Fail(InvestigationRequestError.EmptyResponse);

            if (!StrictJson.TryParse(responseText, out JsonNode root, out string jsonError))
                return MapJsonFailure(jsonError);
            if (!(root is JsonObject obj))
                return Fail(InvestigationRequestError.NotAnObject);

            // Classification scans are independent of field order, so the same input always
            // produces the same error. Action ambiguity is reported before a missing tool because
            // an action object is the more dangerous interpretation to leave ambiguous.
            foreach (var field in obj.Items)
                if (InvestigationRequestContract.IsActionField(field.Key))
                    return Fail(InvestigationRequestError.ActionToolAmbiguity);
            foreach (var field in obj.Items)
                if (InvestigationRequestContract.IsAuthorityField(field.Key))
                    return Fail(InvestigationRequestError.ForbiddenField);

            if (!obj.TryGet(InvestigationRequestContract.FieldTool, out JsonNode toolNode))
                return Fail(InvestigationRequestError.MissingTool,
                    InvestigationRequestContract.FieldTool);
            if (!(toolNode is JsonString toolText))
                return Fail(InvestigationRequestError.InvalidFieldType,
                    InvestigationRequestContract.FieldTool);
            if (string.IsNullOrEmpty(toolText.Value))
                return Fail(InvestigationRequestError.MissingTool,
                    InvestigationRequestContract.FieldTool);
            if (!InvestigationRequestContract.TryParseTool(toolText.Value,
                    out InvestigationRequestTool tool))
                return Fail(InvestigationRequestError.UnknownTool,
                    InvestigationRequestContract.FieldTool);

            switch (tool)
            {
                case InvestigationRequestTool.ManualLookup: return ParseManual(obj, service);
                case InvestigationRequestTool.InspectEntity: return ParseInspect(obj, service);
                case InvestigationRequestTool.SearchHistory: return ParseHistory(obj, service);
                case InvestigationRequestTool.RecallMemory: return ParseMemory(obj, service);
                default: return Fail(InvestigationRequestError.UnknownTool,
                    InvestigationRequestContract.FieldTool);
            }
        }

        private static InvestigationRequestParseResult ParseManual(JsonObject obj,
            InvestigationOptions options)
        {
            string topic = "";
            foreach (var field in obj.Items)
            {
                if (field.Key == InvestigationRequestContract.FieldTool) continue;
                if (field.Key != InvestigationRequestContract.FieldTopic)
                    return Fail(InvestigationRequestError.UnknownField);
                if (!TryString(field.Value, options.MaxTopicLength, out topic, out string code))
                    return Fail(code, InvestigationRequestContract.FieldTopic);
            }
            return InvestigationRequestParseResult.Success(InvestigationRequest.Manual(topic));
        }

        private static InvestigationRequestParseResult ParseInspect(JsonObject obj,
            InvestigationOptions options)
        {
            string kind = null, id = null;
            bool hasKind = false, hasId = false;
            foreach (var field in obj.Items)
            {
                if (field.Key == InvestigationRequestContract.FieldTool) continue;
                if (field.Key == InvestigationRequestContract.FieldKind)
                {
                    if (!TryString(field.Value, options.MaxEntityIdLength, out kind, out string code))
                        return Fail(code, InvestigationRequestContract.FieldKind);
                    hasKind = true;
                }
                else if (field.Key == InvestigationRequestContract.FieldId)
                {
                    if (!TryString(field.Value, options.MaxEntityIdLength, out id, out string code))
                        return Fail(code, InvestigationRequestContract.FieldId);
                    hasId = true;
                }
                else
                {
                    return Fail(InvestigationRequestError.UnknownField);
                }
            }

            if (!hasKind)
                return Fail(InvestigationRequestError.MissingField,
                    InvestigationRequestContract.FieldKind);
            if (!hasId)
                return Fail(InvestigationRequestError.MissingField,
                    InvestigationRequestContract.FieldId);
            if (id.Length == 0)
                return Fail(InvestigationRequestError.MissingField,
                    InvestigationRequestContract.FieldId);

            string normalizedKind = InvestigationText.Normalize(kind);
            if (normalizedKind != InvestigationRequestContract.EntityKindCity
                && normalizedKind != InvestigationRequestContract.EntityKindArmy)
                return Fail(InvestigationRequestError.InvalidEntityKind,
                    InvestigationRequestContract.FieldKind);

            return InvestigationRequestParseResult.Success(
                InvestigationRequest.Inspect(normalizedKind, id));
        }

        private static InvestigationRequestParseResult ParseHistory(JsonObject obj,
            InvestigationOptions options)
        {
            string query = "";
            int limit = 0, offset = 0;
            bool hasLimit = false, hasOffset = false;
            foreach (var field in obj.Items)
            {
                if (field.Key == InvestigationRequestContract.FieldTool) continue;
                if (field.Key == InvestigationRequestContract.FieldQuery)
                {
                    if (!TryString(field.Value, options.MaxQueryLength, out query, out string code))
                        return Fail(code, InvestigationRequestContract.FieldQuery);
                }
                else if (field.Key == InvestigationRequestContract.FieldLimit)
                {
                    if (!TryInt(field.Value, InvestigationRequestError.InvalidLimit,
                            out limit, out string code))
                        return Fail(code, InvestigationRequestContract.FieldLimit);
                    hasLimit = true;
                }
                else if (field.Key == InvestigationRequestContract.FieldOffset)
                {
                    if (!TryInt(field.Value, InvestigationRequestError.InvalidOffset,
                            out offset, out string code))
                        return Fail(code, InvestigationRequestContract.FieldOffset);
                    hasOffset = true;
                }
                else
                {
                    return Fail(InvestigationRequestError.UnknownField);
                }
            }

            int effectiveMax = EffectiveMaxLimit(options.MaxHistoryLimit);
            if (!hasLimit || limit == 0)
                limit = EffectiveDefaultLimit(options.DefaultHistoryLimit, effectiveMax);
            else if (limit < 0 || limit > effectiveMax)
                return Fail(InvestigationRequestError.InvalidLimit,
                    InvestigationRequestContract.FieldLimit);

            if (!hasOffset) offset = 0;
            if (offset < 0 || offset > options.MaxPaginationOffset)
                return Fail(InvestigationRequestError.InvalidOffset,
                    InvestigationRequestContract.FieldOffset);

            return InvestigationRequestParseResult.Success(
                InvestigationRequest.History(query, limit, offset));
        }

        private static InvestigationRequestParseResult ParseMemory(JsonObject obj,
            InvestigationOptions options)
        {
            string query = "";
            int limit = 0;
            bool hasLimit = false;
            foreach (var field in obj.Items)
            {
                if (field.Key == InvestigationRequestContract.FieldTool) continue;
                if (field.Key == InvestigationRequestContract.FieldQuery)
                {
                    if (!TryString(field.Value, options.MaxQueryLength, out query, out string code))
                        return Fail(code, InvestigationRequestContract.FieldQuery);
                }
                else if (field.Key == InvestigationRequestContract.FieldLimit)
                {
                    if (!TryInt(field.Value, InvestigationRequestError.InvalidLimit,
                            out limit, out string code))
                        return Fail(code, InvestigationRequestContract.FieldLimit);
                    hasLimit = true;
                }
                else
                {
                    return Fail(InvestigationRequestError.UnknownField);
                }
            }

            int effectiveMax = EffectiveMaxLimit(options.MaxMemoryLimit);
            if (!hasLimit || limit == 0)
                limit = EffectiveDefaultLimit(options.DefaultMemoryLimit, effectiveMax);
            else if (limit < 0 || limit > effectiveMax)
                return Fail(InvestigationRequestError.InvalidLimit,
                    InvestigationRequestContract.FieldLimit);

            return InvestigationRequestParseResult.Success(
                InvestigationRequest.Memory(query, limit));
        }

        // Mirrors InvestigationService: a non-positive configured maximum is treated as one, and
        // an omitted/zero request limit resolves to the configured default within that maximum.
        private static int EffectiveMaxLimit(int configuredMax) => Math.Max(1, configuredMax);

        private static int EffectiveDefaultLimit(int configuredDefault, int effectiveMax)
            => Math.Max(1, Math.Min(configuredDefault, effectiveMax));

        private static bool TryString(JsonNode node, int maxChars, out string value,
            out string errorCode)
        {
            value = null;
            errorCode = null;
            if (!(node is JsonString text))
            {
                errorCode = InvestigationRequestError.InvalidFieldType;
                return false;
            }
            string candidate = text.Value;
            for (int i = 0; i < candidate.Length; i++)
            {
                if (char.IsControl(candidate[i]))
                {
                    errorCode = InvestigationRequestError.InvalidCharacter;
                    return false;
                }
            }
            if (candidate.Length > maxChars)
            {
                errorCode = InvestigationRequestError.FieldTooLong;
                return false;
            }
            value = candidate;
            return true;
        }

        private static bool TryInt(JsonNode node, string rangeCode, out int value,
            out string errorCode)
        {
            value = 0;
            errorCode = null;
            if (!(node is JsonInt number))
            {
                errorCode = InvestigationRequestError.InvalidFieldType;
                return false;
            }
            if (number.Value < int.MinValue || number.Value > int.MaxValue)
            {
                errorCode = rangeCode;
                return false;
            }
            value = (int)number.Value;
            return true;
        }

        private static InvestigationRequestParseResult MapJsonFailure(string detail)
        {
            switch (detail)
            {
                case "duplicate key":
                    return Fail(InvestigationRequestError.DuplicateKey);
                case "trailing data":
                    return Fail(InvestigationRequestError.TrailingContent);
                case "input too large":
                    return Fail(InvestigationRequestError.InputTooLarge);
                default:
                    return Fail(InvestigationRequestError.InvalidJson);
            }
        }

        private static InvestigationRequestParseResult Fail(string code, string field = null)
            => InvestigationRequestParseResult.Failure(code, MessageFor(code), field);

        /// <summary>Fixed, bounded messages; no model-supplied text is ever appended.</summary>
        private static string MessageFor(string code)
        {
            switch (code)
            {
                case InvestigationRequestError.EmptyResponse:
                    return "A response must contain exactly one JSON request object.";
                case InvestigationRequestError.InputTooLarge:
                    return "The request exceeds the configured character bound.";
                case InvestigationRequestError.InvalidJson:
                    return "The response is not valid JSON.";
                case InvestigationRequestError.DuplicateKey:
                    return "The request contains a duplicate field.";
                case InvestigationRequestError.TrailingContent:
                    return "The response contains content after the single request object.";
                case InvestigationRequestError.NotAnObject:
                    return "Exactly one request object is required; arrays and other values are not accepted.";
                case InvestigationRequestError.ActionToolAmbiguity:
                    return "The request mixes an action command with an investigation tool request.";
                case InvestigationRequestError.ForbiddenField:
                    return "A host-owned identity field is not permitted in an investigation request.";
                case InvestigationRequestError.MissingTool:
                    return "The 'tool' field is required.";
                case InvestigationRequestError.UnknownTool:
                    return "The requested tool is not supported.";
                case InvestigationRequestError.UnknownField:
                    return "A field is not part of the selected tool's request schema.";
                case InvestigationRequestError.InvalidFieldType:
                    return "A request field has the wrong JSON type.";
                case InvestigationRequestError.InvalidCharacter:
                    return "A request field contains a control character.";
                case InvestigationRequestError.FieldTooLong:
                    return "A request field exceeds the configured length bound.";
                case InvestigationRequestError.MissingField:
                    return "A required request field is missing.";
                case InvestigationRequestError.InvalidEntityKind:
                    return "Supported inspection kinds are 'city' and 'army'.";
                case InvestigationRequestError.InvalidLimit:
                    return "The result limit is outside the configured range.";
                case InvestigationRequestError.InvalidOffset:
                    return "The pagination offset is outside the configured range.";
                default:
                    return "The request was rejected.";
            }
        }
    }
}
