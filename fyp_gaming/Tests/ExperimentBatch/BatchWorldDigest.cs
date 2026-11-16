using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// A deterministic digest of the canonical match state, by walking it rather than listing fields: every public
    /// field and property reachable from GameState (factions, diplomacy, cities, armies, watch towers and the whole
    /// map: size, walkable, movement costs and neighbour tables) plus the BalanceProfile, in a fixed order. A field added
    /// to the engine later is covered without editing this file.
    ///
    /// Scope, stated so no one reads more into it: it does NOT cover private fields (the engine's RNG stream and action
    /// counter, DiplomacyState's next proposal id), the event log, or anything outside GameState and the balance profile. Equal digests mean "the same canonical
    /// state", not "the same future": two arms with the same state and seed still diverge if the orders differ.
    /// Researcher-side only; it is never a prompt input.
    /// </summary>
    public static class BatchWorldDigest
    {
        public const string Scope = "GameState (every public field/property, incl. map topology and costs) + BalanceProfile; "
                                    + "excludes private fields (the RNG stream, the action counter, the next proposal id) and the event log";

        private const int MaxDepth = 24;

        public static string Compute(GameEngine engine)
        {
            var sb = new StringBuilder(64 * 1024);
            Walk("state", engine.State, sb, new List<object>(), 0);
            Walk("balance", engine.Balance, sb, new List<object>(), 0);
            return BatchPlan.Sha256(sb.ToString());
        }

        /// <summary>Digest of any object graph with the same walk; lets the set and dictionary rules be tested on their own.</summary>
        public static string Fingerprint(object value)
        {
            var sb = new StringBuilder();
            Walk("value", value, sb, new List<object>(), 0);
            return BatchPlan.Sha256(sb.ToString());
        }

        private static void Walk(string path, object value, StringBuilder sb, List<object> ancestors, int depth)
        {
            sb.Append(path).Append('=');
            if (value == null) { sb.Append("null\n"); return; }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal)
            {
                sb.Append(Scalar(value)).Append('\n');
                return;
            }
            if (value is Delegate) { sb.Append("<delegate>\n"); return; }
            if (depth >= MaxDepth) { sb.Append("<depth>\n"); return; }
            foreach (object a in ancestors)
                if (ReferenceEquals(a, value)) { sb.Append("<cycle>\n"); return; }
            ancestors.Add(value);
            try
            {
                if (value is IDictionary dict) { Dictionary(path, dict, sb, ancestors, depth); return; }
                if (value is IEnumerable items) { Sequence(path, type, items, sb, ancestors, depth); return; }
                sb.Append("{\n");
                foreach (KeyValuePair<string, object> member in Members(value, type))
                    Walk(path + "." + member.Key, member.Value, sb, ancestors, depth + 1);
                sb.Append("}\n");
            }
            finally { ancestors.RemoveAt(ancestors.Count - 1); }
        }

        private static void Sequence(string path, Type type, IEnumerable items, StringBuilder sb, List<object> ancestors, int depth)
        {
            var parts = new List<string>();
            bool set = IsSet(type);
            int index = 0;
            foreach (object item in items)
            {
                var one = new StringBuilder();
                // A set member carries no position: an index in its path would make the sorted text depend on enumeration order.
                Walk(path + (set ? "{}" : "[" + (index++) + "]"), item, one, ancestors, depth + 1);
                parts.Add(one.ToString());
            }
            if (set) parts.Sort(StringComparer.Ordinal);   // set order is not part of the state
            sb.Append("[count=").Append(parts.Count).Append("]\n");
            foreach (string part in parts) sb.Append(part);
        }

        private static void Dictionary(string path, IDictionary dict, StringBuilder sb, List<object> ancestors, int depth)
        {
            var parts = new List<KeyValuePair<string, string>>();
            foreach (DictionaryEntry entry in dict)
            {
                string key = Scalar(entry.Key);
                var one = new StringBuilder();
                Walk(path + "{" + key + "}", entry.Value, one, ancestors, depth + 1);
                parts.Add(new KeyValuePair<string, string>(key, one.ToString()));
            }
            parts.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            sb.Append("{count=").Append(parts.Count).Append("}\n");
            foreach (KeyValuePair<string, string> part in parts) sb.Append(part.Value);
        }

        private static bool IsSet(Type type)
        {
            foreach (Type i in type.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>)) return true;
            return false;
        }

        private static List<KeyValuePair<string, object>> Members(object value, Type type)
        {
            var list = new List<KeyValuePair<string, object>>();
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                list.Add(new KeyValuePair<string, object>(f.Name, f.GetValue(value)));
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                object v;
                try { v = p.GetValue(value); } catch (Exception e) { v = "<error " + e.GetType().Name + ">"; }
                list.Add(new KeyValuePair<string, object>(p.Name, v));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        private static string Scalar(object value)
        {
            if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is bool b) return b ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }
}
