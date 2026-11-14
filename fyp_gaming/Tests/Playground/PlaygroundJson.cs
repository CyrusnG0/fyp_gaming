using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Tiny deterministic JSON writer for playground results. Values are pre-rendered fragments so
    /// object key order is explicit and stable across runs; the engine's MiniJson owns escaping.
    /// </summary>
    internal static class PlaygroundJson
    {
        public const string Null = "null";

        public static string Str(string value) => MiniJson.Str(value);

        public static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Num(double value)
            => value.ToString("0.######", CultureInfo.InvariantCulture);

        public static string Bool(bool value) => value ? "true" : "false";

        /// <summary>Builds an object from alternating key and pre-rendered value fragments.</summary>
        public static string Obj(params string[] keyValuePairs)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            for (int i = 0; i + 1 < keyValuePairs.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append(MiniJson.Str(keyValuePairs[i])).Append(':').Append(keyValuePairs[i + 1]);
            }
            return sb.Append('}').ToString();
        }

        public static string Arr(IEnumerable<string> fragments)
        {
            var sb = new StringBuilder(128);
            sb.Append('[');
            bool first = true;
            if (fragments != null)
            {
                foreach (string fragment in fragments)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(fragment ?? Null);
                }
            }
            return sb.Append(']').ToString();
        }

        public static string StrArr(IEnumerable<string> values)
        {
            var items = new List<string>();
            if (values != null)
                foreach (string value in values) items.Add(Str(value));
            return Arr(items);
        }

        /// <summary>Escapes control characters so a one-line summary is safe in the readable trace.</summary>
        public static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value) sb.Append(char.IsControl(c) ? ' ' : c);
            return sb.ToString();
        }
    }
}
