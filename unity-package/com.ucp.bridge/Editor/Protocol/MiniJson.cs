/*
 * MiniJson - A minimal JSON parser/serializer for Unity.
 * Based on the public domain MiniJSON by Calvin Rien.
 * Handles Dictionary<string, object>, List<object>, string, long, double, bool, null.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UCP.Bridge
{
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            return Parser.Parse(json);
        }

        public static string Serialize(object obj)
        {
            return Serializer.Serialize(obj);
        }

        private sealed class Parser : IDisposable
        {
            private StringReader _reader;

            private Parser(string jsonString)
            {
                _reader = new StringReader(jsonString);
            }

            public static object Parse(string jsonString)
            {
                using var parser = new Parser(jsonString);
                return parser.ParseValue();
            }

            public void Dispose()
            {
                _reader?.Dispose();
                _reader = null;
            }

            private object ParseValue()
            {
                EatWhitespace();
                var c = PeekChar();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case '-':
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        return ParseNumber();
                    default:
                        return ParseLiteral();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                ReadChar(); // {
                var dict = new Dictionary<string, object>();

                while (true)
                {
                    EatWhitespace();
                    if (PeekChar() == '}') { ReadChar(); return dict; }
                    if (PeekChar() == ',') { ReadChar(); continue; }

                    var key = ParseString();
                    EatWhitespace();
                    ReadChar(); // :
                    dict[key] = ParseValue();
                }
            }

            private List<object> ParseArray()
            {
                ReadChar(); // [
                var list = new List<object>();

                while (true)
                {
                    EatWhitespace();
                    if (PeekChar() == ']') { ReadChar(); return list; }
                    if (PeekChar() == ',') { ReadChar(); continue; }

                    list.Add(ParseValue());
                }
            }

            private string ParseString()
            {
                ReadChar(); // opening "
                var sb = new StringBuilder();

                while (true)
                {
                    var c = ReadChar();
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        c = ReadChar();
                        switch (c)
                        {
                            case '"': case '\\': case '/': sb.Append(c); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                var hex = new char[4];
                                for (int i = 0; i < 4; i++) hex[i] = ReadChar();
                                sb.Append((char)Convert.ToUInt16(new string(hex), 16));
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private object ParseNumber()
            {
                var sb = new StringBuilder();
                bool isFloat = false;

                while (true)
                {
                    var c = PeekChar();
                    if (c == '.' || c == 'e' || c == 'E') isFloat = true;
                    if ((c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E')
                    {
                        sb.Append(ReadChar());
                    }
                    else break;
                }

                var s = sb.ToString();
                if (isFloat)
                    return double.Parse(s, CultureInfo.InvariantCulture);
                return long.Parse(s, CultureInfo.InvariantCulture);
            }

            private object ParseLiteral()
            {
                var sb = new StringBuilder();
                while (true)
                {
                    var c = PeekChar();
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                        sb.Append(ReadChar());
                    else break;
                }

                var s = sb.ToString();
                return s switch
                {
                    "true" => (object)true,
                    "false" => (object)false,
                    "null" => null,
                    _ => throw new FormatException($"Unexpected literal: {s}")
                };
            }

            private void EatWhitespace()
            {
                while (true)
                {
                    var c = PeekChar();
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                        ReadChar();
                    else break;
                }
            }

            private char PeekChar()
            {
                int c = _reader.Peek();
                return c < 0 ? '\0' : (char)c;
            }

            private char ReadChar()
            {
                int c = _reader.Read();
                return c < 0 ? '\0' : (char)c;
            }
        }

        private sealed class Serializer
        {
            private readonly StringBuilder _sb = new();

            public static string Serialize(object obj)
            {
                var s = new Serializer();
                s.WriteValue(obj);
                return s._sb.ToString();
            }

            private void WriteValue(object obj)
            {
                switch (obj)
                {
                    case null:
                        _sb.Append("null");
                        break;
                    case string s:
                        WriteString(s);
                        break;
                    case bool b:
                        _sb.Append(b ? "true" : "false");
                        break;
                    case IDictionary dict:
                        WriteDict(dict);
                        break;
                    case IList list:
                        WriteArray(list);
                        break;
                    case float f:
                        _sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    case double d:
                        _sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    case int i:
                        _sb.Append(i);
                        break;
                    case long l:
                        _sb.Append(l);
                        break;
                    case Enum e:
                        _sb.Append(Convert.ToInt32(e));
                        break;
                    default:
                        // For anonymous types and other objects, use reflection
                        WriteObject(obj);
                        break;
                }
            }

            private void WriteString(string s)
            {
                _sb.Append('"');
                foreach (var c in s)
                {
                    switch (c)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\b': _sb.Append("\\b"); break;
                        case '\f': _sb.Append("\\f"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:
                            if (c < ' ')
                                _sb.AppendFormat("\\u{0:x4}", (int)c);
                            else
                                _sb.Append(c);
                            break;
                    }
                }
                _sb.Append('"');
            }

            private void WriteDict(IDictionary dict)
            {
                _sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    WriteString(entry.Key.ToString());
                    _sb.Append(':');
                    WriteValue(entry.Value);
                }
                _sb.Append('}');
            }

            private void WriteArray(IList list)
            {
                _sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) _sb.Append(',');
                    WriteValue(list[i]);
                }
                _sb.Append(']');
            }

            private void WriteObject(object obj)
            {
                var type = obj.GetType();
                var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                _sb.Append('{');
                bool first = true;

                foreach (var prop in props)
                {
                    if (!prop.CanRead) continue;
                    try
                    {
                        var val = prop.GetValue(obj);
                        if (!first) _sb.Append(',');
                        first = false;
                        // Convert PascalCase to camelCase
                        var name = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
                        WriteString(name);
                        _sb.Append(':');
                        WriteValue(val);
                    }
                    catch { }
                }

                foreach (var field in fields)
                {
                    try
                    {
                        var val = field.GetValue(obj);
                        if (!first) _sb.Append(',');
                        first = false;
                        var name = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
                        WriteString(name);
                        _sb.Append(':');
                        WriteValue(val);
                    }
                    catch { }
                }

                _sb.Append('}');
            }
        }
    }
}
