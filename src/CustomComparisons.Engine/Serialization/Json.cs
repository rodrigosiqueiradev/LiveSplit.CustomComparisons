using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CustomComparisons.Engine.Serialization;

// A small, strict JSON reader and writer. The engine stays dependency-free
// (netstandard2.0 has no built-in JSON), which avoids clashing with whatever
// System.Text.Json version LiveSplit happens to ship. Programs are tiny, and
// imported files come from strangers, so the reader enforces size and depth limits.

public abstract class JsonValue
{
    public override string ToString() => JsonWriter.Write(this, indented: false);
}

public sealed class JsonObject : JsonValue
{
    private readonly List<KeyValuePair<string, JsonValue>> members = new();
    private readonly Dictionary<string, JsonValue> lookup = new(StringComparer.Ordinal);

    public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => members;

    public JsonValue this[string key] => lookup.TryGetValue(key, out JsonValue value) ? value : null;

    public bool ContainsKey(string key) => lookup.ContainsKey(key);

    public JsonObject Add(string key, JsonValue value)
    {
        if (lookup.ContainsKey(key))
        {
            throw new ArgumentException($"Duplicate key '{key}'.", nameof(key));
        }

        members.Add(new KeyValuePair<string, JsonValue>(key, value ?? JsonNull.Instance));
        lookup.Add(key, value ?? JsonNull.Instance);
        return this;
    }

    public JsonObject Add(string key, string value) => Add(key, new JsonString(value));

    public JsonObject Add(string key, double value) => Add(key, new JsonNumber(value));
}

public sealed class JsonArray : JsonValue
{
    public JsonArray(IEnumerable<JsonValue> items = null)
    {
        Items = (items ?? Enumerable.Empty<JsonValue>()).ToList();
    }

    public List<JsonValue> Items { get; }
}

public sealed class JsonString : JsonValue
{
    public JsonString(string value)
    {
        Value = value ?? "";
    }

    public string Value { get; }
}

public sealed class JsonNumber : JsonValue
{
    public JsonNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
        }

        Value = value;
    }

    public double Value { get; }
}

public sealed class JsonBool : JsonValue
{
    public static readonly JsonBool True = new(true);
    public static readonly JsonBool False = new(false);

    private JsonBool(bool value)
    {
        Value = value;
    }

    public bool Value { get; }
}

public sealed class JsonNull : JsonValue
{
    public static readonly JsonNull Instance = new();

    private JsonNull()
    {
    }
}

public sealed class JsonException : Exception
{
    public JsonException(string message) : base(message)
    {
    }
}

public static class JsonReader
{
    public const int DefaultMaxLength = 1_000_000;
    public const int DefaultMaxDepth = 128;

    public static JsonValue Parse(string text, int maxLength = DefaultMaxLength, int maxDepth = DefaultMaxDepth)
    {
        if (text == null)
        {
            throw new JsonException("There is no text to read.");
        }

        if (text.Length > maxLength)
        {
            throw new JsonException($"The file is too large (over {maxLength / 1000} KB).");
        }

        var parser = new Parser(text, maxDepth);
        JsonValue value = parser.ParseDocument();
        return value;
    }

    private sealed class Parser
    {
        private readonly string text;
        private readonly int maxDepth;
        private int pos;
        private int depth;

        public Parser(string text, int maxDepth)
        {
            this.text = text;
            this.maxDepth = maxDepth;
        }

        public JsonValue ParseDocument()
        {
            // Tolerate a UTF-8 byte-order mark that survived decoding.
            if (pos < text.Length && text[pos] == '﻿')
            {
                pos++;
            }

            SkipWhitespace();
            JsonValue value = ParseValue();
            SkipWhitespace();
            if (pos != text.Length)
            {
                throw Error("Unexpected text after the end of the JSON.");
            }

            return value;
        }

        private JsonValue ParseValue()
        {
            if (pos >= text.Length)
            {
                throw Error("Unexpected end of the JSON.");
            }

            switch (text[pos])
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return new JsonString(ParseString());
                case 't':
                    ExpectWord("true");
                    return JsonBool.True;
                case 'f':
                    ExpectWord("false");
                    return JsonBool.False;
                case 'n':
                    ExpectWord("null");
                    return JsonNull.Instance;
                default:
                    if (text[pos] == '-' || char.IsDigit(text[pos]))
                    {
                        return ParseNumber();
                    }

                    throw Error($"Unexpected character '{text[pos]}'.");
            }
        }

        private JsonObject ParseObject()
        {
            Enter();
            pos++; // {
            var result = new JsonObject();
            SkipWhitespace();
            if (Peek() == '}')
            {
                pos++;
                Leave();
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    throw Error("Expected a quoted property name.");
                }

                string key = ParseString();
                if (result.ContainsKey(key))
                {
                    throw Error($"The property \"{key}\" appears twice.");
                }

                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
                result.Add(key, ParseValue());
                SkipWhitespace();
                if (Peek() == ',')
                {
                    pos++;
                    continue;
                }

                Expect('}');
                Leave();
                return result;
            }
        }

        private JsonArray ParseArray()
        {
            Enter();
            pos++; // [
            var result = new JsonArray();
            SkipWhitespace();
            if (Peek() == ']')
            {
                pos++;
                Leave();
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Items.Add(ParseValue());
                SkipWhitespace();
                if (Peek() == ',')
                {
                    pos++;
                    continue;
                }

                Expect(']');
                Leave();
                return result;
            }
        }

        private string ParseString()
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= text.Length)
                {
                    throw Error("A string is missing its closing quote.");
                }

                char c = text[pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c < 0x20)
                {
                    throw Error("Control characters must be escaped inside strings.");
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (pos >= text.Length)
                {
                    throw Error("A string ends in the middle of an escape.");
                }

                char e = text[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > text.Length ||
                            !int.TryParse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            throw Error("Invalid \\u escape in a string.");
                        }

                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default:
                        throw Error($"Unknown escape '\\{e}' in a string.");
                }
            }
        }

        private JsonNumber ParseNumber()
        {
            int start = pos;
            if (Peek() == '-')
            {
                pos++;
            }

            if (Peek() == '0')
            {
                pos++;
            }
            else if (char.IsDigit(Peek()))
            {
                while (char.IsDigit(Peek()))
                {
                    pos++;
                }
            }
            else
            {
                throw Error("Invalid number.");
            }

            if (Peek() == '.')
            {
                pos++;
                if (!char.IsDigit(Peek()))
                {
                    throw Error("Invalid number.");
                }

                while (char.IsDigit(Peek()))
                {
                    pos++;
                }
            }

            if (Peek() == 'e' || Peek() == 'E')
            {
                pos++;
                if (Peek() == '+' || Peek() == '-')
                {
                    pos++;
                }

                if (!char.IsDigit(Peek()))
                {
                    throw Error("Invalid number.");
                }

                while (char.IsDigit(Peek()))
                {
                    pos++;
                }
            }

            string literal = text.Substring(start, pos - start);
            if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsInfinity(value))
            {
                throw Error("Number out of range.");
            }

            return new JsonNumber(value);
        }

        private void ExpectWord(string word)
        {
            if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
            {
                throw Error($"Expected '{word}'.");
            }

            pos += word.Length;
        }

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw Error(pos >= text.Length ? "Unexpected end of the JSON." : $"Expected '{c}'.");
            }

            pos++;
        }

        private char Peek() => pos < text.Length ? text[pos] : '\0';

        private void SkipWhitespace()
        {
            while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\n' || text[pos] == '\r'))
            {
                pos++;
            }
        }

        private void Enter()
        {
            if (++depth > maxDepth)
            {
                throw Error("The JSON is nested too deeply.");
            }
        }

        private void Leave() => depth--;

        private JsonException Error(string message)
        {
            int line = 1;
            int column = 1;
            for (int i = 0; i < pos && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new JsonException($"{message} (line {line}, column {column})");
        }
    }
}

public static class JsonWriter
{
    public static string Write(JsonValue value, bool indented = true)
    {
        var sb = new StringBuilder();
        WriteValue(sb, value, indented, 0);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, JsonValue value, bool indented, int level)
    {
        switch (value)
        {
            case null:
            case JsonNull:
                sb.Append("null");
                break;
            case JsonBool b:
                sb.Append(b.Value ? "true" : "false");
                break;
            case JsonNumber n:
                sb.Append(n.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case JsonString s:
                WriteString(sb, s.Value);
                break;
            case JsonArray a:
                WriteContainer(sb, '[', ']', a.Items.Count, indented, level, (i, l) => WriteValue(sb, a.Items[i], indented, l));
                break;
            case JsonObject o:
                WriteContainer(sb, '{', '}', o.Members.Count, indented, level, (i, l) =>
                {
                    WriteString(sb, o.Members[i].Key);
                    sb.Append(indented ? ": " : ":");
                    WriteValue(sb, o.Members[i].Value, indented, l);
                });
                break;
            default:
                throw new ArgumentException("Unknown JSON value.", nameof(value));
        }
    }

    private static void WriteContainer(StringBuilder sb, char open, char close, int count, bool indented, int level, Action<int, int> writeItem)
    {
        sb.Append(open);
        if (count == 0)
        {
            sb.Append(close);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            if (indented)
            {
                sb.Append('\n').Append(' ', (level + 1) * 2);
            }

            writeItem(i, level + 1);
        }

        if (indented)
        {
            sb.Append('\n').Append(' ', level * 2);
        }

        sb.Append(close);
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
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
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
