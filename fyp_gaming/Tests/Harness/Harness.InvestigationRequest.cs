using System;
using LuanShi.Engine;

// [LS-020] Strict investigation-request contract.
//
// Pins the parser boundary that a future host will sit behind: exactly one JSON object, one of
// four read-only tools, no model-owned identity, no action/tool ambiguity, no duplicate/unknown
// fields, no wrong types, no out-of-range arguments and no oversized input. Errors are stable,
// bounded and never echo input. Parsing itself performs no I/O and touches no game state.

internal static partial class Harness
{
    private static void RunInvestigationRequests()
    {
        Section("[new] LS-020 strict investigation request contract");

        InvestigationRequestValid();
        InvestigationRequestDefaults();
        InvestigationRequestMalformed();
        InvestigationRequestAuthorityAndAmbiguity();
        InvestigationRequestFieldValidation();
        InvestigationRequestBounds();
        InvestigationRequestDeterminism();
        InvestigationRequestDispatch();
    }

    // ---- helpers -----------------------------------------------------------

    private static InvestigationRequestParseResult InvestigationParse(string json,
        InvestigationRequestOptions options = null)
        => InvestigationRequestParser.Parse(json, options);

    private static void InvestigationRequestRejects(string json, string code, string label,
        string field = null, InvestigationRequestOptions options = null)
    {
        InvestigationRequestParseResult result = InvestigationParse(json, options);
        Check(!result.Ok && result.Request == null && result.ErrorCode == code,
            "[LS-020] " + label + " -> " + code);
        bool fieldOk = field == null ? result.ErrorField == null : result.ErrorField == field;
        Check(fieldOk, "[LS-020] " + label + " reports the expected bounded field");
        Check(result.Error != null && result.Error.Length <= 200 && !InvestigationHasControl(result.Error),
            "[LS-020] " + label + " error is bounded plain text");
        Check(result.ContractVersion == InvestigationRequestContract.Version,
            "[LS-020] " + label + " carries the contract version");
    }

    private static bool InvestigationHasControl(string text)
    {
        foreach (char c in text) if (char.IsControl(c)) return true;
        return false;
    }

    private static bool InvestigationParsesRequest(string json)
        => InvestigationParse(json).Ok;

    // ---- valid requests ----------------------------------------------------

    private static void InvestigationRequestValid()
    {
        InvestigationRequestParseResult manual = InvestigationParse(
            "{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
        Check(manual.Ok && manual.Request != null
              && manual.Request.Tool == InvestigationRequestTool.ManualLookup
              && manual.Request.ToolName == "manual_lookup"
              && manual.Request.Topic == "recruit",
            "[LS-020] manual_lookup request parses with its topic");
        Check(manual.Request.Query == null && manual.Request.EntityKind == null
              && manual.Request.EntityId == null && manual.Request.Limit == 0
              && manual.Request.Offset == 0,
            "[LS-020] manual_lookup exposes no unrelated request fields");
        Check(manual.ContractVersion == InvestigationRequestContract.Version,
            "[LS-020] a successful parse carries the contract version");

        InvestigationRequestParseResult inspect = InvestigationParse(
            "{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
        Check(inspect.Ok && inspect.Request.Tool == InvestigationRequestTool.InspectEntity
              && inspect.Request.EntityKind == "city" && inspect.Request.EntityId == "city-a",
            "[LS-020] inspect_entity request parses with kind and id");

        InvestigationRequestParseResult army = InvestigationParse(
            "{\"tool\":\"inspect_entity\",\"kind\":\"Army\",\"id\":\"army-a\"}");
        Check(army.Ok && army.Request.EntityKind == "army",
            "[LS-020] inspection kind is normalized exactly like the service");

        InvestigationRequestParseResult history = InvestigationParse(
            "{\"tool\":\"search_history\",\"query\":\"tax\",\"limit\":10,\"offset\":0}");
        Check(history.Ok && history.Request.Tool == InvestigationRequestTool.SearchHistory
              && history.Request.Query == "tax" && history.Request.Limit == 10
              && history.Request.Offset == 0,
            "[LS-020] search_history request parses with query, limit and offset");

        InvestigationRequestParseResult memory = InvestigationParse(
            "{\"tool\":\"recall_memory\",\"query\":\"food\",\"limit\":5}");
        Check(memory.Ok && memory.Request.Tool == InvestigationRequestTool.RecallMemory
              && memory.Request.Query == "food" && memory.Request.Limit == 5
              && memory.Request.Offset == 0,
            "[LS-020] recall_memory request parses with query and limit");

        Check(InvestigationParse("{\"topic\":\"recruit\",\"tool\":\"manual_lookup\"}").Ok,
            "[LS-020] field order does not change a valid request");
    }

    private static void InvestigationRequestDefaults()
    {
        InvestigationRequestOptions options = InvestigationRequestOptions.Defaults();
        InvestigationOptions service = options.ServiceOptions;

        InvestigationRequestParseResult manual = InvestigationParse("{\"tool\":\"manual_lookup\"}");
        Check(manual.Ok && manual.Request.Topic == "",
            "[LS-020] omitted manual topic defaults to the bounded topic index");

        InvestigationRequestParseResult history = InvestigationParse("{\"tool\":\"search_history\"}");
        Check(history.Ok && history.Request.Query == ""
              && history.Request.Limit == service.DefaultHistoryLimit
              && history.Request.Offset == 0,
            "[LS-020] omitted history arguments use documented service defaults");

        InvestigationRequestParseResult zero = InvestigationParse(
            "{\"tool\":\"search_history\",\"limit\":0}");
        Check(zero.Ok && zero.Request.Limit == service.DefaultHistoryLimit,
            "[LS-020] an explicit zero limit resolves to the service default");

        InvestigationRequestParseResult memory = InvestigationParse("{\"tool\":\"recall_memory\"}");
        Check(memory.Ok && memory.Request.Limit == service.DefaultMemoryLimit,
            "[LS-020] omitted memory limit uses the documented service default");

        var lowered = new InvestigationRequestOptions
        {
            ServiceOptions = new InvestigationOptions
            {
                DefaultHistoryLimit = 10,
                MaxHistoryLimit = 3,
            },
        };
        InvestigationRequestParseResult clamped = InvestigationParse(
            "{\"tool\":\"search_history\"}", lowered);
        Check(clamped.Ok && clamped.Request.Limit == 3,
            "[LS-020] request defaults follow the configured service bounds");

        Check(service.DefaultHistoryLimit == InvestigationOptions.Defaults().DefaultHistoryLimit
              && service.MaxHistoryLimit == InvestigationOptions.Defaults().MaxHistoryLimit,
            "[LS-020] request defaults are consistent with the LS-018 service defaults");
    }

    // ---- structural rejection ---------------------------------------------

    private static void InvestigationRequestMalformed()
    {
        InvestigationRequestRejects(null, "empty_response", "null response");
        InvestigationRequestRejects("", "empty_response", "empty response");
        InvestigationRequestRejects("   \n\t ", "empty_response", "whitespace-only response");
        InvestigationRequestRejects("{", "invalid_json", "unterminated object");
        InvestigationRequestRejects("{\"tool\":}", "invalid_json", "missing value");
        InvestigationRequestRejects("[]", "not_an_object", "empty array");
        InvestigationRequestRejects(
            "[{\"tool\":\"manual_lookup\"}]", "not_an_object", "array of one request");
        InvestigationRequestRejects("\"manual_lookup\"", "not_an_object", "bare string");
        InvestigationRequestRejects("42", "not_an_object", "bare number");
        InvestigationRequestRejects("{}", "missing_tool", "empty object", "tool");
        InvestigationRequestRejects("{\"tool\":\"\"}", "missing_tool", "empty tool", "tool");
        InvestigationRequestRejects("{\"tool\":\"read_file\"}", "unknown_tool",
            "unknown tool", "tool");
        InvestigationRequestRejects("{\"tool\":42}", "invalid_field_type",
            "non-string tool", "tool");
        InvestigationRequestRejects("{\"tool\":null}", "invalid_field_type",
            "null tool", "tool");

        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\"}{\"tool\":\"manual_lookup\"}",
            "trailing_content", "two concatenated requests");
        InvestigationRequestRejects("{\"tool\":\"manual_lookup\"} trailing",
            "trailing_content", "trailing prose");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"topic\":\"a\",\"topic\":\"b\"}",
            "duplicate_key", "duplicate topic key");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"topic\":{\"x\":1,\"x\":2}}",
            "duplicate_key", "duplicate nested key");

        Check(InvestigationParsesRequest("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}"),
            "[LS-020] a valid single object remains accepted after malformed probes");
    }

    // ---- authority and action/tool ambiguity -------------------------------

    private static void InvestigationRequestAuthorityAndAmbiguity()
    {
        string[] authority =
        {
            "game_id", "seat_id", "actor_seat_id", "player", "player_id",
            "match_id", "controller_type", "token",
        };
        foreach (string field in authority)
            InvestigationRequestRejects(
                "{\"tool\":\"manual_lookup\",\"" + field + "\":\"attacker\"}",
                "forbidden_field", "authority field " + field);

        InvestigationRequestRejects(
            "{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\",\"seat_id\":\"seat-b\"}",
            "forbidden_field", "seat injection alongside a valid request");
        InvestigationRequestRejects(
            "{\"game_id\":\"g\",\"tool\":\"manual_lookup\"}",
            "forbidden_field", "identity field before the tool");

        string[] actionFields = { "type", "action", "actions", "command", "end_turn" };
        foreach (string field in actionFields)
            InvestigationRequestRejects(
                "{\"tool\":\"manual_lookup\",\"" + field + "\":\"levy_tax\"}",
                "action_tool_ambiguity", "action field " + field);

        InvestigationRequestRejects(
            "{\"action\":{\"type\":\"levy_tax\",\"target_id\":\"city-a\"},\"tool\":\"manual_lookup\"}",
            "action_tool_ambiguity", "nested action object");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"reason\":\"because\"}",
            "unknown_field", "unsupported field");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"id\":\"city-a\"}",
            "unknown_field", "cross-tool entity id");
        InvestigationRequestRejects(
            "{\"tool\":\"recall_memory\",\"query\":\"food\",\"offset\":0}",
            "unknown_field", "offset on recall_memory");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"topic\":\"tax\"}",
            "unknown_field", "topic on search_history");
    }

    // ---- field types, required fields and ranges ---------------------------

    private static void InvestigationRequestFieldValidation()
    {
        InvestigationRequestRejects("{\"tool\":\"inspect_entity\",\"id\":\"city-a\"}",
            "missing_field", "inspect without kind", "kind");
        InvestigationRequestRejects("{\"tool\":\"inspect_entity\",\"kind\":\"city\"}",
            "missing_field", "inspect without id", "id");
        InvestigationRequestRejects("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"\"}",
            "missing_field", "inspect with empty id", "id");
        InvestigationRequestRejects(
            "{\"tool\":\"inspect_entity\",\"kind\":\"unit\",\"id\":\"unit-a\"}",
            "invalid_entity_kind", "unsupported entity kind", "kind");
        InvestigationRequestRejects(
            "{\"tool\":\"inspect_entity\",\"kind\":7,\"id\":\"city-a\"}",
            "invalid_field_type", "numeric entity kind", "kind");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"topic\":[]}",
            "invalid_field_type", "array topic", "topic");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"topic\":null}",
            "invalid_field_type", "null topic", "topic");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"query\":false}",
            "invalid_field_type", "boolean query", "query");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"limit\":\"10\"}",
            "invalid_field_type", "string limit", "limit");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"limit\":-1}",
            "invalid_limit", "negative history limit", "limit");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"limit\":51}",
            "invalid_limit", "history limit above the default maximum", "limit");
        InvestigationRequestRejects(
            "{\"tool\":\"recall_memory\",\"limit\":999999999999}",
            "invalid_limit", "limit beyond the integer range", "limit");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"offset\":-1}",
            "invalid_offset", "negative offset", "offset");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"offset\":1001}",
            "invalid_offset", "offset above the configured maximum", "offset");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"topic\":\"a\\u0001b\"}",
            "invalid_character", "escaped control character", "topic");

        // Exactly at the bound is accepted; one character past it is rejected.
        Check(InvestigationParse("{\"tool\":\"manual_lookup\",\"topic\":\"" + new string('t', 64) + "\"}").Ok,
            "[LS-020] a topic exactly at the configured bound is accepted");
        InvestigationRequestRejects(
            "{\"tool\":\"manual_lookup\",\"topic\":\"" + new string('t', 65) + "\"}",
            "field_too_long", "topic past the configured bound", "topic");
        InvestigationRequestRejects(
            "{\"tool\":\"search_history\",\"query\":\"" + new string('q', 201) + "\"}",
            "field_too_long", "query past the configured bound", "query");
        InvestigationRequestRejects(
            "{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"" + new string('i', 65) + "\"}",
            "field_too_long", "entity id past the configured bound", "id");
    }

    // ---- input bound and deterministic errors ------------------------------

    private static void InvestigationRequestBounds()
    {
        InvestigationRequestOptions options = InvestigationRequestOptions.Defaults();
        string oversized = new string('x', options.MaxInputChars + 1);
        InvestigationRequestRejects(oversized, "input_too_large", "oversized raw input");

        var tight = new InvestigationRequestOptions { MaxInputChars = 300 };
        InvestigationRequestRejects(new string('x', 301), "input_too_large",
            "oversized input under a lowered cap", null, tight);

        bool rejected = false;
        try
        {
            new InvestigationRequestOptions
            {
                MaxInputChars = InvestigationRequestOptions.MinimumMaxInputChars - 1,
            }.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            rejected = ex.ParamName == nameof(InvestigationRequestOptions.MaxInputChars);
        }
        Check(rejected, "[LS-020] an impossible input cap is rejected, not expanded");

        Check(InvestigationParse("{\"tool\":\"manual_lookup\"}", tight).Ok,
            "[LS-020] a normally sized request fits a lowered input cap");
    }

    private static void InvestigationRequestDeterminism()
    {
        string[] invalid =
        {
            "{\"tool\":\"manual_lookup\",\"type\":\"levy_tax\"}",
            "{\"tool\":\"manual_lookup\",\"seat_id\":\"seat-b\"}",
            "{\"tool\":\"manual_lookup\",\"topic\":\"a\",\"topic\":\"b\"}",
            "{\"tool\":\"search_history\",\"limit\":-1}",
            "{\"tool\":\"inspect_entity\",\"kind\":\"city\"}",
        };

        foreach (string json in invalid)
        {
            InvestigationRequestParseResult first = InvestigationParse(json);
            InvestigationRequestParseResult second = InvestigationParse(json);
            InvestigationRequestParseResult third = InvestigationParse(json);
            Check(!first.Ok
                  && first.ErrorCode == second.ErrorCode && second.ErrorCode == third.ErrorCode
                  && first.Error == second.Error && second.Error == third.Error
                  && first.ErrorField == second.ErrorField
                  && second.ErrorField == third.ErrorField,
                "[LS-020] repeated " + first.ErrorCode + " failure is deterministic");
        }

        // Model-supplied text must never be appended to an error message, not even a field name.
        string[] injections =
        {
            "{\"tool\":\"SUPERSECRET\"}",
            "{\"tool\":\"manual_lookup\",\"game_id\":\"SUPERSECRET\"}",
            "{\"tool\":\"manual_lookup\",\"SUPERSECRET\":1}",
            "{\"tool\":\"manual_lookup\",\"topic\":\"" + new string('S', 65) + "\"}",
        };
        foreach (string json in injections)
        {
            InvestigationRequestParseResult result = InvestigationParse(json);
            Check(!result.Ok && result.Error != null
                  && result.Error.IndexOf("SUPERSECRET", StringComparison.Ordinal) < 0
                  && result.Error.IndexOf(new string('S', 65), StringComparison.Ordinal) < 0,
                "[LS-020] " + result.ErrorCode + " does not echo model-supplied input");
        }
    }
}
