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
    }

    /// <summary>Action vocabulary for the MVP strategic layer.</summary>
    public static class ActionType
    {
        public const string Farm = "farm";          // 屯田: target_id = city
        public const string Recruit = "recruit";    // 徵兵: target_id = city
        public const string March = "march";        // 行軍: target_id = army, param_a/b = dest hex x,z
    }

    /// <summary>
    /// The single command DTO every controller produces (CONTROLLER_PROTOCOL.md §2).
    /// A human click in the UI and an LLM's JSON line both become this object and enter
    /// the same validation pipeline. Flat fields keep JSON trivially serializable.
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
        public string Reason;           // free text, stored as research data, never executed

        public string ToJson()
        {
            var sb = new StringBuilder(160);
            sb.Append('{');
            sb.Append("\"action_id\":").Append(MiniJson.Str(ActionId));
            sb.Append(",\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"actor_seat_id\":").Append(MiniJson.Str(ActorSeatId));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(ControllerType));
            sb.Append(",\"target_id\":").Append(MiniJson.Str(TargetId));
            sb.Append(",\"param_a\":").Append(MiniJson.Num(ParamA));
            sb.Append(",\"param_b\":").Append(MiniJson.Num(ParamB));
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
