using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>Base type of the tiny JSON DOM used by the LLM parser.</summary>
    public abstract class JsonNode
    {
    }

    /// <summary>An object with its members kept in input order (the model's field order is stable to inspect).</summary>
    public sealed class JsonObject : JsonNode
    {
        private readonly List<KeyValuePair<string, JsonNode>> _items = new List<KeyValuePair<string, JsonNode>>();
        private readonly Dictionary<string, JsonNode> _map = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        public int Count => _items.Count;
        public IReadOnlyList<KeyValuePair<string, JsonNode>> Items => _items;

        public bool TryGet(string key, out JsonNode value) => _map.TryGetValue(key, out value);

        internal bool ContainsKey(string key) => _map.ContainsKey(key);

        internal void Add(string key, JsonNode value)
        {
            _items.Add(new KeyValuePair<string, JsonNode>(key, value));
            _map.Add(key, value);
        }
    }

    public sealed class JsonArray : JsonNode
    {
        private readonly List<JsonNode> _items = new List<JsonNode>();

        public int Count => _items.Count;
        public IReadOnlyList<JsonNode> Items => _items;

        internal void Add(JsonNode value) => _items.Add(value);
    }

    public sealed class JsonString : JsonNode
    {
        public readonly string Value;
        public JsonString(string value) { Value = value; }
    }

    public sealed class JsonInt : JsonNode
    {
        public readonly long Value;
        public JsonInt(long value) { Value = value; }
    }

    public sealed class JsonBool : JsonNode
    {
        public readonly bool Value;
        public JsonBool(bool value) { Value = value; }
    }

    public sealed class JsonNull : JsonNode
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
    }

    /// <summary>
    /// Small strict JSON parser: no external dependencies, no comments, no trailing junk.
    /// It accepts only JSON integers (no fractions, exponents, NaN or Infinity) and bounds the
    /// input size and nesting depth. Every failure produces a short, generic message that is safe
    /// to hand back to a model — it never includes game state.
    /// </summary>
    public static class StrictJson
    {
        public const int MaxInputLength = 16384;
        public const int MaxDepth = 8;
        public const int MaxLenientInputLength = 262144;

        public static bool TryParse(string text, out JsonNode root, out string error)
            => TryParseCore(text, false, MaxInputLength, out root, out error);

        /// <summary>
        /// Provider-response variant: the same strict structural parser with a larger size bound and
        /// fraction/exponent numbers tolerated (they are consumed and represented as <see cref="JsonNull"/>
        /// because no code reads them). This is for OpenAI-compatible response envelopes only; model
        /// output must keep using <see cref="TryParse"/> so fractional params stay invalid.
        /// </summary>
        public static bool TryParseLenientNumbers(string text, out JsonNode root, out string error)
            => TryParseCore(text, true, MaxLenientInputLength, out root, out error);

        private static bool TryParseCore(string text, bool lenientNumbers, int maxLength,
            out JsonNode root, out string error)
        {
            root = null;
            error = null;
            if (text == null) { error = "missing JSON"; return false; }
            if (text.Length > maxLength) { error = "input too large"; return false; }
            var parser = new Parser(text, lenientNumbers);
            if (!parser.ParseValue(0, out root, out error)) { root = null; return false; }
            parser.SkipWhitespace();
            if (!parser.AtEnd) { root = null; error = "trailing data"; return false; }
            return true;
        }

        private sealed class Parser
        {
            private readonly string _text;
            private readonly bool _lenientNumbers;
            private int _pos;

            public Parser(string text, bool lenientNumbers)
            {
                _text = text;
                _lenientNumbers = lenientNumbers;
            }
            public bool AtEnd => _pos >= _text.Length;

            public void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                    else break;
                }
            }

            public bool ParseValue(int depth, out JsonNode node, out string error)
            {
                node = null;
                error = null;
                SkipWhitespace();
                if (AtEnd) { error = "unexpected end"; return false; }

                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject(depth, out node, out error);
                    case '[': return ParseArray(depth, out node, out error);
                    case '"':
                    {
                        if (!ParseString(out string value, out error)) return false;
                        node = new JsonString(value);
                        return true;
                    }
                    case 't': return ParseLiteral("true", new JsonBool(true), out node, out error);
                    case 'f': return ParseLiteral("false", new JsonBool(false), out node, out error);
                    case 'n': return ParseLiteral("null", JsonNull.Instance, out node, out error);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(out node, out error);
                        error = "invalid token";
                        return false;
                }
            }

            private bool ParseLiteral(string literal, JsonNode value, out JsonNode node, out string error)
            {
                node = null;
                error = null;
                if (_pos + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                {
                    error = "invalid token";
                    return false;
                }
                _pos += literal.Length;
                node = value;
                return true;
            }

            private bool ParseObject(int depth, out JsonNode node, out string error)
            {
                node = null;
                error = null;
                if (depth + 1 > MaxDepth) { error = "too deep"; return false; }
                _pos++; // '{'
                var obj = new JsonObject();
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == '}') { _pos++; node = obj; return true; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != '"') { error = "invalid object"; return false; }
                    if (!ParseString(out string key, out error)) return false;
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != ':') { error = "invalid object"; return false; }
                    _pos++;
                    if (!ParseValue(depth + 1, out JsonNode value, out error)) return false;
                    if (obj.ContainsKey(key)) { error = "duplicate key"; return false; }
                    obj.Add(key, value);
                    SkipWhitespace();
                    if (AtEnd) { error = "unexpected end"; return false; }
                    char c = _text[_pos++];
                    if (c == ',')
                    {
                        SkipWhitespace();
                        if (!AtEnd && _text[_pos] == '}') { error = "invalid object"; return false; }
                        continue;
                    }
                    if (c == '}') { node = obj; return true; }
                    error = "invalid object";
                    return false;
                }
            }

            private bool ParseArray(int depth, out JsonNode node, out string error)
            {
                node = null;
                error = null;
                if (depth + 1 > MaxDepth) { error = "too deep"; return false; }
                _pos++; // '['
                var array = new JsonArray();
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == ']') { _pos++; node = array; return true; }

                while (true)
                {
                    if (!ParseValue(depth + 1, out JsonNode value, out error)) return false;
                    array.Add(value);
                    SkipWhitespace();
                    if (AtEnd) { error = "unexpected end"; return false; }
                    char c = _text[_pos++];
                    if (c == ',')
                    {
                        SkipWhitespace();
                        if (!AtEnd && _text[_pos] == ']') { error = "invalid array"; return false; }
                        continue;
                    }
                    if (c == ']') { node = array; return true; }
                    error = "invalid array";
                    return false;
                }
            }

            private bool ParseNumber(out JsonNode node, out string error)
            {
                node = null;
                error = null;
                bool negative = false;
                if (_text[_pos] == '-')
                {
                    negative = true;
                    _pos++;
                    if (AtEnd) { error = "invalid number"; return false; }
                }
                if (AtEnd || !IsDigit(_text[_pos])) { error = "invalid number"; return false; }

                long value = 0;
                if (_text[_pos] == '0')
                {
                    _pos++;
                    if (!AtEnd && IsDigit(_text[_pos])) { error = "invalid number"; return false; }
                }
                else
                {
                    while (!AtEnd && IsDigit(_text[_pos]))
                    {
                        int digit = _text[_pos] - '0';
                        // Accumulate negatively so long.MinValue is representable.
                        if (value < (long.MinValue + digit) / 10) { error = "number too large"; return false; }
                        value = value * 10 - digit;
                        _pos++;
                    }
                }

                bool fractional = false;
                if (!AtEnd && _text[_pos] == '.')
                {
                    if (!_lenientNumbers) { error = "invalid number"; return false; }
                    fractional = true;
                    _pos++;
                    if (AtEnd || !IsDigit(_text[_pos])) { error = "invalid number"; return false; }
                    while (!AtEnd && IsDigit(_text[_pos])) _pos++;
                }

                if (!AtEnd && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    if (!_lenientNumbers) { error = "invalid number"; return false; }
                    fractional = true;
                    _pos++;
                    if (!AtEnd && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    if (AtEnd || !IsDigit(_text[_pos])) { error = "invalid number"; return false; }
                    while (!AtEnd && IsDigit(_text[_pos])) _pos++;
                }

                // Provider envelopes carry things like costs and logprob values. Nothing reads those
                // fields, so the lenient path consumes them and stores an inert null.
                if (fractional)
                {
                    node = JsonNull.Instance;
                    return true;
                }
                if (negative && value == 0) { error = "negative zero"; return false; }
                if (!negative)
                {
                    if (value == long.MinValue) { error = "number too large"; return false; }
                    value = -value;
                }
                node = new JsonInt(value);
                return true;
            }

            private bool ParseString(out string value, out string error)
            {
                value = null;
                error = null;
                _pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) { error = "unexpected end"; return false; }
                    char c = _text[_pos++];
                    if (c == '"') { value = sb.ToString(); return true; }

                    if (c == '\\')
                    {
                        if (AtEnd) { error = "unexpected end"; return false; }
                        char esc = _text[_pos++];
                        char escaped;
                        switch (esc)
                        {
                            case '"': escaped = '"'; break;
                            case '\\': escaped = '\\'; break;
                            case '/': escaped = '/'; break;
                            case 'b': escaped = '\b'; break;
                            case 'f': escaped = '\f'; break;
                            case 'n': escaped = '\n'; break;
                            case 'r': escaped = '\r'; break;
                            case 't': escaped = '\t'; break;
                            case 'u':
                                if (!ReadHex4(out int code)) { error = "invalid escape"; return false; }
                                if (char.IsHighSurrogate((char)code))
                                {
                                    if (_pos + 1 >= _text.Length || _text[_pos] != '\\' || _text[_pos + 1] != 'u')
                                    {
                                        error = "lone surrogate";
                                        return false;
                                    }
                                    _pos += 2;
                                    if (!ReadHex4(out int low)) { error = "invalid escape"; return false; }
                                    if (!char.IsLowSurrogate((char)low)) { error = "lone surrogate"; return false; }
                                    sb.Append((char)code).Append((char)low);
                                    continue;
                                }
                                if (char.IsLowSurrogate((char)code))
                                {
                                    error = "lone surrogate";
                                    return false;
                                }
                                // Escaped control code points are valid JSON (provider envelopes
                                // escape newlines/tabs in reasoning and formatted content); only
                                // raw, unescaped control characters stay rejected.
                                sb.Append((char)code);
                                continue;
                            default:
                                error = "invalid escape";
                                return false;
                        }
                        sb.Append(escaped);
                        continue;
                    }

                    if (c < ' ') { error = "control character"; return false; }
                    if (char.IsHighSurrogate(c))
                    {
                        if (AtEnd || !char.IsLowSurrogate(_text[_pos]))
                        {
                            error = "lone surrogate";
                            return false;
                        }
                        sb.Append(c).Append(_text[_pos++]);
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        error = "lone surrogate";
                        return false;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private bool ReadHex4(out int value)
            {
                value = 0;
                if (_pos + 4 > _text.Length) return false;
                for (int i = 0; i < 4; i++)
                {
                    char c = _text[_pos++];
                    int digit;
                    if (c >= '0' && c <= '9') digit = c - '0';
                    else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                    else return false;
                    value = (value << 4) | digit;
                }
                return true;
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';
            private static bool IsControlCode(int code) => code >= 0 && code <= 0x1F;
        }
    }
}
