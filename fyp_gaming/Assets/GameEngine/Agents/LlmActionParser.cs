using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LuanShi.Engine
{
    /// <summary>Outcome of <see cref="LlmActionParser.Parse"/>; <see cref="Error"/> is safe to show to a model.</summary>
    public sealed class ParseResult
    {
        public bool Ok;
        public string Error;
        public LlmAction Action;

        internal static ParseResult Failure(string error)
            => new ParseResult { Ok = false, Error = error };
    }

    /// <summary>
    /// Converts one model response into a proposed <see cref="LlmAction"/>. This parser is a
    /// security boundary, not a convenience: it accepts exactly one JSON object from the fixed
    /// schema, rejects host-owned/spoofed fields, and never trusts strings as instructions.
    /// The engine remains the only authority on whether an action is legal.
    /// </summary>
    public static class LlmActionParser
    {
        private const int MaxStringLength = 200;
        private const string EndTurnType = "end_turn";

        private static readonly string[] Vocabulary =
        {
            ActionType.LevyTax,
            ActionType.LightenLabor,
            ActionType.Reclaim,
            ActionType.Recruit,
            ActionType.Train,
            ActionType.Fortify,
            ActionType.Relief,
            ActionType.March,
            EndTurnType,
        };

        private static readonly ReadOnlyCollection<string> VocabularyView = Array.AsReadOnly(Vocabulary);
        private static readonly HashSet<string> VocabularySet =
            new HashSet<string>(Vocabulary, StringComparer.Ordinal);

        private static readonly HashSet<string> ForbiddenFields = new HashSet<string>(
            new[] { "actor_seat_id", "action_id", "controller_type", "seat", "token", "seat_id" },
            StringComparer.Ordinal);

        /// <summary>The exact demo-slice vocabulary, in catalogue order (domestic orders, march, end_turn).</summary>
        public static IReadOnlyCollection<string> SupportedTypes => VocabularyView;

        public static ParseResult Parse(string modelText)
        {
            if (!UnwrapFence(modelText, out string json, out string error))
                return ParseResult.Failure(error);
            if (!StrictJson.TryParse(json, out JsonNode root, out error))
                return ParseResult.Failure(error);

            var obj = root as JsonObject;
            if (obj == null) return ParseResult.Failure("response must be an object");

            string type = null, target = null, reason = null, summary = null;
            int paramA = 0, paramB = 0;
            bool hasTarget = false, hasParamA = false, hasParamB = false;
            JsonObject argsNode = null;

            foreach (var field in obj.Items)
            {
                switch (field.Key)
                {
                    case "type":
                        if (!TryString(field.Value, out type, out error)) return ParseResult.Failure(error);
                        break;
                    case "target_id":
                        // end_turn may omit target_id or send JSON null; both mean "no target". A null
                        // target for any other action is rejected below by the required-target check.
                        if (field.Value is JsonNull)
                        {
                            target = null;
                        }
                        else if (!TryString(field.Value, out target, out error))
                        {
                            return ParseResult.Failure(error);
                        }
                        hasTarget = true;
                        break;
                    case "param_a":
                        if (!TryInt(field.Value, out paramA, out error)) return ParseResult.Failure(error);
                        hasParamA = true;
                        break;
                    case "param_b":
                        if (!TryInt(field.Value, out paramB, out error)) return ParseResult.Failure(error);
                        hasParamB = true;
                        break;
                    case "args":
                        argsNode = field.Value as JsonObject;
                        if (argsNode == null) return ParseResult.Failure("invalid field type");
                        break;
                    case "reason":
                        if (!TryString(field.Value, out reason, out error)) return ParseResult.Failure(error);
                        break;
                    case "summary":
                        if (!TryString(field.Value, out summary, out error)) return ParseResult.Failure(error);
                        break;
                    default:
                        if (ForbiddenFields.Contains(field.Key))
                            return ParseResult.Failure("forbidden field");
                        return ParseResult.Failure("unknown field");
                }
            }

            if (type == null) return ParseResult.Failure("type is required");
            if (!VocabularySet.Contains(type)) return ParseResult.Failure("unsupported action type");

            bool endTurn = type == EndTurnType;
            if (endTurn)
            {
                // end_turn carries no target. Empty/null/omitted are equivalent; a real target is a
                // schema violation, not a no-op, so the model gets an explicit correction.
                if (!string.IsNullOrEmpty(target)) return ParseResult.Failure("invalid field");
                target = null;
            }
            else if (!hasTarget || string.IsNullOrEmpty(target))
            {
                return ParseResult.Failure("target is required");
            }

            // A march carries the destination as two required small integers (map coordinates).
            // The model never supplies a path or a cost; the engine revalidates the destination.
            if (type == ActionType.March)
            {
                if (!hasParamA || !hasParamB)
                    return ParseResult.Failure("coordinates are required");
                if (paramA < 0 || paramA > 255 || paramB < 0 || paramB > 255)
                    return ParseResult.Failure("coordinates out of range");
            }

            // Defaults are explicit here so an omitted field is not confused with a value of 0.
            if (!hasParamA) paramA = 0;
            if (!hasParamB) paramB = 0;

            Dictionary<string, string> args = null;
            if (argsNode != null)
            {
                args = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var arg in argsNode.Items)
                {
                    if (!ArgKeyAllowed(type, arg.Key))
                        return ParseResult.Failure("unsupported argument");
                    if (!TryString(arg.Value, out string argValue, out error))
                        return ParseResult.Failure(error);
                    if (type == ActionType.Recruit && argValue != UnitTypes.Infantry)
                        return ParseResult.Failure("unsupported unit type");
                    args.Add(arg.Key, argValue);
                }
            }

            return new ParseResult
            {
                Ok = true,
                Action = new LlmAction
                {
                    EndTurn = endTurn,
                    Type = type,
                    TargetId = target,
                    ParamA = paramA,
                    ParamB = paramB,
                    Args = args,
                    Reason = reason,
                    Summary = summary,
                },
            };
        }

        /// <summary>
        /// The trusted host builds the command. ActorSeatId, ControllerType and ActionId stay null:
        /// the host binds seat and token, and the engine assigns the action id.
        /// </summary>
        public static ActionCommand ToCommand(LlmAction action)
        {
            if (action == null) return null;
            Dictionary<string, string> args = null;
            if (action.Args != null)
                args = new Dictionary<string, string>(action.Args, StringComparer.Ordinal);
            return new ActionCommand
            {
                Type = action.Type,
                TargetId = action.TargetId,
                ParamA = action.ParamA,
                ParamB = action.ParamB,
                Args = args,
                Reason = action.Reason,
            };
        }

        private static bool ArgKeyAllowed(string type, string key)
        {
            if (type == ActionType.Recruit) return key == "unit_type";
            return false;
        }

        private static bool TryString(JsonNode node, out string value, out string error)
        {
            value = null;
            error = null;
            var text = node as JsonString;
            if (text == null) { error = "invalid field type"; return false; }
            if (text.Value.Length > MaxStringLength) { error = "string too long"; return false; }
            for (int i = 0; i < text.Value.Length; i++)
            {
                if (char.IsControl(text.Value[i])) { error = "invalid character"; return false; }
            }
            value = text.Value;
            return true;
        }

        private static bool TryInt(JsonNode node, out int value, out string error)
        {
            value = 0;
            error = null;
            var number = node as JsonInt;
            if (number == null) { error = "invalid field type"; return false; }
            if (number.Value < int.MinValue || number.Value > int.MaxValue)
            {
                error = "number out of range";
                return false;
            }
            value = (int)number.Value;
            return true;
        }

        private static bool UnwrapFence(string text, out string json, out string error)
        {
            json = null;
            error = null;
            if (text == null) { error = "empty response"; return false; }
            string trimmed = text.Trim();
            if (trimmed.Length == 0) { error = "empty response"; return false; }
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                json = trimmed;
                return true;
            }

            const string tag = "```json";
            if (!trimmed.StartsWith(tag, StringComparison.Ordinal))
            {
                error = "invalid fence";
                return false;
            }
            if (trimmed.Length < tag.Length + 4
                || !trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                error = "invalid fence";
                return false;
            }

            int start = tag.Length;
            if (start >= trimmed.Length || !IsWhitespace(trimmed[start]))
            {
                error = "invalid fence";
                return false;
            }
            while (start < trimmed.Length && IsWhitespace(trimmed[start])) start++;
            int end = trimmed.Length - 3;
            if (end <= start) { error = "empty response"; return false; }

            json = trimmed.Substring(start, end - start).Trim();
            if (json.Length == 0) { error = "empty response"; return false; }
            return true;
        }

        private static bool IsWhitespace(char c)
            => c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }
}
