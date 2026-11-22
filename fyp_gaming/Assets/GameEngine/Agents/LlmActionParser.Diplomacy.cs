using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LuanShi.Engine
{
    /// <summary>
    /// The first opt-in diplomacy slice's parser material: its three-order vocabulary, the two answers
    /// this slice exposes, and the per-type argument rules. Kept in its own partial so the disabled
    /// parser's vocabulary, error precedence and wording stay exactly as they were before the slice.
    /// Only <c>allowDiplomacy</c> reaches any of it; the engine still decides legality.
    /// </summary>
    public static partial class LlmActionParser
    {
        /// <summary>
        /// The first opt-in diplomacy slice, accepted only with allowDiplomacy: 遣使, 提出條約 and
        /// an accept/reject answer. Not part of <see cref="SupportedTypes"/>.
        /// </summary>
        private static readonly string[] DiplomacyVocabulary =
        {
            ActionType.SendMessage,
            ActionType.ProposeTreaty,
            ActionType.RespondTreaty,
        };

        private static readonly ReadOnlyCollection<string> DiplomacyVocabularyView =
            Array.AsReadOnly(DiplomacyVocabulary);
        private static readonly HashSet<string> DiplomacySet =
            new HashSet<string>(DiplomacyVocabulary, StringComparer.Ordinal);

        /// <summary>
        /// send_message, propose_treaty and respond_treaty, in catalogue order. Gift, war, betrayal
        /// and treaty-counteroffers are deliberately absent: they stay unexposed even when the
        /// diplomacy switch is on.
        /// </summary>
        public static IReadOnlyCollection<string> DiplomacyTypes => DiplomacyVocabularyView;

        // The engine's two exposed answers (DoRespondTreaty also accepts "counter", which this slice
        // deliberately does not expose).
        private const string ResponseAccept = "accept";
        private const string ResponseReject = "reject";

        /// <summary>True for the two orders that may carry one player-written line.</summary>
        private static bool CarriesText(string type)
            => type == ActionType.SendMessage || type == ActionType.ProposeTreaty;

        /// <summary>
        /// args keys a diplomacy order accepts. Every other type keeps its own allow-list in the base
        /// parser, so this returns false for them.
        /// </summary>
        private static bool DiplomacyArgKeyAllowed(string type, string key)
        {
            if (type == ActionType.ProposeTreaty) return key == "treaty_type" || key == "duration";
            if (type == ActionType.RespondTreaty) return key == "response";
            return false;
        }

        /// <summary>
        /// Validates one accepted diplomacy args value; null means it is valid. The engine clamps
        /// <c>duration</c> to its configured range, so only the shape is enforced here.
        /// </summary>
        private static string DiplomacyArgError(string type, string key, string value)
        {
            if (type == ActionType.ProposeTreaty && key == "treaty_type" && !TreatyType.IsValid(value))
                return "unsupported treaty type";
            if (type == ActionType.ProposeTreaty && key == "duration" && !IsSeasonCount(value))
                return "duration must be a number of seasons";
            if (type == ActionType.RespondTreaty && key == "response"
                && value != ResponseAccept && value != ResponseReject)
                return "unsupported treaty response";
            return null;
        }

        /// <summary>
        /// Reads one args value. Every value is a string on the wire, with one narrow exception: a JSON
        /// integer for propose_treaty's args.duration, which the live run run-20261122140759713-c8b0fcfc
        /// showed models send. It is carried as its decimal string and then goes through the same
        /// <see cref="DiplomacyArgError"/> shape and range check as a quoted "8", so a bare integer is
        /// never wider than the string form; every other key and type stays strictly a string.
        /// </summary>
        private static bool TryArgValue(string type, string key, JsonNode node, out string value, out string error)
        {
            if (type == ActionType.ProposeTreaty && key == "duration" && node is JsonInt number)
            {
                value = number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                error = null;
                return true;
            }
            if (TryString(node, out value, out error)) return true;
            error = ArgTypeMessage(type, key, error);
            return false;
        }

        /// <summary>
        /// Names the args key whose JSON value had the wrong type, so the model's repair request can
        /// say what to change. The key is already on the order's allow-list and the message carries
        /// only schema text, never a game fact.
        /// </summary>
        private static string ArgTypeMessage(string type, string key, string error)
        {
            if (error != "invalid field type") return error;
            if (type == ActionType.ProposeTreaty && key == "duration")
                return "invalid field type: args.duration must be a whole number of seasons such as 8 or \"8\"";
            return "invalid field type: args." + key + " must be a quoted string";
        }

        /// <summary>The one required argument for a diplomacy order, or null when it is present.</summary>
        private static string DiplomacyRequiredArgument(string type, Dictionary<string, string> args)
        {
            if (type == ActionType.ProposeTreaty && (args == null || !args.ContainsKey("treaty_type")))
                return "treaty_type is required";
            if (type == ActionType.RespondTreaty && (args == null || !args.ContainsKey("response")))
                return "response is required";
            return null;
        }

        /// <summary>A whole-season count for args.duration; the engine clamps the range.</summary>
        private static bool IsSeasonCount(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 3) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            return int.TryParse(value, out int parsed) && parsed > 0;
        }
    }
}
