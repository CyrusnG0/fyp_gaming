using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Minimal JSON writer for the flat DTOs used by the command/event schema.
    /// Engine stays dependency-free (no UnityEngine.JsonUtility, no System.Text.Json)
    /// so it compiles in any pure-C# harness. Serialization only; parsing lives with
    /// the LLM adapter when it is built.
    /// </summary>
    internal static class MiniJson
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Str(string s) => s == null ? "null" : "\"" + Escape(s) + "\"";

        public static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

        public static string Dict(Dictionary<string, string> d)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Str(kv.Key)).Append(':').Append(Str(kv.Value));
            }
            return sb.Append('}').ToString();
        }

        public static string IntMap(Dictionary<string, int> d)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Str(kv.Key)).Append(':').Append(Num(kv.Value));
            }
            return sb.Append('}').ToString();
        }

        /// <summary>Array of already-rendered JSON fragments (objects, numbers, strings).</summary>
        public static string Array(IEnumerable<string> fragments)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var fragment in fragments)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(fragment);
            }
            return sb.Append(']').ToString();
        }

        public static string StrArray(IEnumerable<string> values)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var value in values)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Str(value));
            }
            return sb.Append(']').ToString();
        }
    }

    /// <summary>Action vocabulary for the MVP strategic layer.</summary>
    public static class ActionType
    {
        public const string Reclaim = "reclaim";    // 開墾 D3: target_id = city, food yield +ReclaimPctPerUse%
        public const string Recruit = "recruit";    // 徵兵 D4: target_id = city
        public const string March = "march";        // 行軍 M1: target_id = army, param_a/b = dest hex x,z

        /// <summary>Legacy alias of Reclaim (pre-ADR-010 logs and UI); normalized in Submit().</summary>
        public const string Farm = "farm";
    }

    /// <summary>
    /// The single command DTO every controller produces (CONTROLLER_PROTOCOL.md §2;
    /// schema v2 in ACTIONS.md §1). A human click in the UI and an LLM's JSON line both
    /// become this object and enter the same validation pipeline.
    /// </summary>
    public sealed class ActionCommand
    {
        public string ActionId;         // assigned by the engine: "a-1", "a-2", ...
        public string Type;             // ActionType.*
        public string ActorSeatId;
        public string ControllerType;   // who produced it (human | llm | scripted_ai)
        public string TargetId;         // city or army id
        public int ParamA;              // march: dest x
        public int ParamB;              // march: dest z
        public Dictionary<string, string> Args;   // schema v2 extras, e.g. treaty_type; null when unused
        public string Text;             // 遣使/條約附言, logged but never executed; null when unused
        public string Reason;           // free text, stored as research data, never executed

        public string ToJson()
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"action_id\":").Append(MiniJson.Str(ActionId));
            sb.Append(",\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"actor_seat_id\":").Append(MiniJson.Str(ActorSeatId));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(ControllerType));
            sb.Append(",\"target_id\":").Append(MiniJson.Str(TargetId));
            sb.Append(",\"param_a\":").Append(MiniJson.Num(ParamA));
            sb.Append(",\"param_b\":").Append(MiniJson.Num(ParamB));
            if (Args != null) sb.Append(",\"args\":").Append(MiniJson.Dict(Args));
            if (Text != null) sb.Append(",\"text\":").Append(MiniJson.Str(Text));
            sb.Append(",\"reason\":").Append(MiniJson.Str(Reason));
            sb.Append('}');
            return sb.ToString();
        }

        public override string ToString() => ToJson();
    }

    public readonly struct ValidationResult
    {
        public readonly bool Ok;
        public readonly string Error;
        public ValidationResult(bool ok, string error) { Ok = ok; Error = error; }
        public static ValidationResult Success => new ValidationResult(true, null);
        public static ValidationResult Fail(string why) => new ValidationResult(false, why);
    }
}
