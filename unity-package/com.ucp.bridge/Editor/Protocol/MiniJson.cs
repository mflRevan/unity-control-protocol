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
            /// Guards against stack overflow on deeply nested input. A stack overflow is not
            /// catchable in .NET and kills the editor process outright, so the parser trades
            /// pathological depth for a normal, catchable exception.
            private const int MaxParseDepth = 256;

            private StringReader _reader;
            private int _depth;

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

                EnterContainer();
                try
                {
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
                finally
                {
                    _depth--;
                }
            }

            private List<object> ParseArray()
            {
                ReadChar(); // [
                var list = new List<object>();

                EnterContainer();
                try
                {
                    while (true)
                    {
                        EatWhitespace();
                        if (PeekChar() == ']') { ReadChar(); return list; }
                        if (PeekChar() == ',') { ReadChar(); continue; }

                        list.Add(ParseValue());
                    }
                }
                finally
                {
                    _depth--;
                }
            }

            private void EnterContainer()
            {
                if (_depth >= MaxParseDepth)
                    throw new FormatException($"JSON nesting exceeds {MaxParseDepth} levels");
                _depth++;
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

            /// Truncated input used to yield '\0' forever, which spun ParseString in an infinite
            /// loop and hung the editor's main thread. Fail loudly at end-of-input instead.
            private char ReadChar()
            {
                int c = _reader.Read();
                if (c < 0) throw new FormatException("Unexpected end of JSON input");
                return (char)c;
            }
        }

        private sealed class Serializer
        {
            /// <summary>
            /// Reflection is the dangerous path: computed properties can hand back fresh instances
            /// of their own type forever (UnityEngine.Vector3.normalized is the canonical example),
            /// so an unbounded walk stack-overflows -- which is not catchable in .NET and takes the
            /// whole editor process down with it. Bound it.
            /// </summary>
            private const int MaxReflectionDepth = 8;

            /// Backstop for container nesting (dictionaries/lists). Reference cycles are caught
            /// separately, so this only fires on genuinely pathological payloads.
            private const int MaxDepth = 96;

            /// Total reflected objects per payload, to bound fan-out: a type whose properties each
            /// return new instances of a similar type grows exponentially, not linearly, with depth.
            private const int MaxReflectedObjects = 20000;

            private const string MaxDepthMarker = "<ucp:max-depth>";
            private const string CycleMarker = "<ucp:cycle>";
            private const string TruncatedMarker = "<ucp:truncated>";

            private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

            private readonly StringBuilder _sb = new();
            private readonly HashSet<object> _visited = new(ReferenceComparer.Instance);
            private int _depth;
            private int _reflectionDepth;
            private int _reflectedObjects;

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
                        return;
                    case string s:
                        WriteString(s);
                        return;
                    case bool b:
                        _sb.Append(b ? "true" : "false");
                        return;
                    case float f:
                        WriteFloat(f);
                        return;
                    case double d:
                        WriteDouble(d);
                        return;
                    case decimal m:
                        _sb.Append(m.ToString(Inv));
                        return;
                    case Enum e:
                        WriteEnum(e);
                        return;
                    case byte v:
                        _sb.Append(((int)v).ToString(Inv));
                        return;
                    case sbyte v:
                        _sb.Append(((int)v).ToString(Inv));
                        return;
                    case short v:
                        _sb.Append(((int)v).ToString(Inv));
                        return;
                    case ushort v:
                        _sb.Append(((int)v).ToString(Inv));
                        return;
                    case int v:
                        _sb.Append(v.ToString(Inv));
                        return;
                    case uint v:
                        _sb.Append(v.ToString(Inv));
                        return;
                    case long v:
                        _sb.Append(v.ToString(Inv));
                        return;
                    case ulong v:
                        _sb.Append(v.ToString(Inv));
                        return;
                    case char c:
                        WriteString(c.ToString());
                        return;
                    case DateTime dt:
                        WriteString(dt.ToString("o", Inv));
                        return;
                    case DateTimeOffset dto:
                        WriteString(dto.ToString("o", Inv));
                        return;
                    case TimeSpan ts:
                        WriteString(ts.ToString(null, Inv));
                        return;
                    case Guid g:
                        WriteString(g.ToString());
                        return;
                    case Type t:
                        WriteString(t.FullName);
                        return;
                }

                // Unity's math structs expose self-referential computed properties
                // (Vector3.normalized, Quaternion.normalized, Bounds.extents, ...) plus indexers.
                // Reflecting over them is what crashed the editor, so give them explicit shapes.
                if (TryWriteUnityValue(obj)) return;

                // UnityEngine.Object graphs are cyclic by construction
                // (GameObject.transform.gameObject), reach the entire scene, and carry no useful
                // JSON projection. Emit an identity instead of walking them.
                if (obj is UnityEngine.Object uo)
                {
                    WriteUnityObject(uo);
                    return;
                }

                // Everything below here recurses, so it needs the depth and cycle guards.
                if (_depth >= MaxDepth)
                {
                    WriteString(MaxDepthMarker);
                    return;
                }

                var track = !obj.GetType().IsValueType;
                if (track && !_visited.Add(obj))
                {
                    WriteString(CycleMarker);
                    return;
                }

                _depth++;
                try
                {
                    switch (obj)
                    {
                        case IDictionary dict:
                            WriteDict(dict);
                            break;
                        case IList list:
                            WriteArray(list);
                            break;
                        case IEnumerable seq:
                            WriteEnumerable(seq);
                            break;
                        default:
                            WriteObject(obj);
                            break;
                    }
                }
                finally
                {
                    _depth--;
                    if (track) _visited.Remove(obj);
                }
            }

            private void WriteEnum(Enum e)
            {
                try
                {
                    if (Enum.GetUnderlyingType(e.GetType()) == typeof(ulong))
                        _sb.Append(Convert.ToUInt64(e).ToString(Inv));
                    else
                        _sb.Append(Convert.ToInt64(e).ToString(Inv));
                }
                catch
                {
                    WriteString(e.ToString());
                }
            }

            /// NaN and Infinity are not valid JSON; emitting them raw yields a payload the CLI
            /// cannot parse. Unity produces them routinely (degenerate bounds, zero-length
            /// normalize, uninitialized transforms).
            private void WriteFloat(float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) _sb.Append("null");
                else _sb.Append(f.ToString("R", Inv));
            }

            private void WriteDouble(double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) _sb.Append("null");
                else _sb.Append(d.ToString("R", Inv));
            }

            private bool TryWriteUnityValue(object obj)
            {
                switch (obj)
                {
                    case UnityEngine.Vector2 v:
                        WriteFloats(("x", v.x), ("y", v.y));
                        return true;
                    case UnityEngine.Vector3 v:
                        WriteFloats(("x", v.x), ("y", v.y), ("z", v.z));
                        return true;
                    case UnityEngine.Vector4 v:
                        WriteFloats(("x", v.x), ("y", v.y), ("z", v.z), ("w", v.w));
                        return true;
                    case UnityEngine.Quaternion q:
                        WriteFloats(("x", q.x), ("y", q.y), ("z", q.z), ("w", q.w));
                        return true;
                    case UnityEngine.Color c:
                        WriteFloats(("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a));
                        return true;
                    case UnityEngine.Color32 c:
                        WriteFloats(("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a));
                        return true;
                    case UnityEngine.Vector2Int v:
                        WriteFloats(("x", v.x), ("y", v.y));
                        return true;
                    case UnityEngine.Vector3Int v:
                        WriteFloats(("x", v.x), ("y", v.y), ("z", v.z));
                        return true;
                    case UnityEngine.Rect r:
                        WriteFloats(("x", r.x), ("y", r.y), ("width", r.width), ("height", r.height));
                        return true;
                    case UnityEngine.RectInt r:
                        WriteFloats(("x", r.x), ("y", r.y), ("width", r.width), ("height", r.height));
                        return true;
                    case UnityEngine.Bounds b:
                        WriteBounds(b.center, b.size);
                        return true;
                    case UnityEngine.BoundsInt b:
                        WriteBounds(b.center, b.size);
                        return true;
                    case UnityEngine.Matrix4x4 mtx:
                        _sb.Append('[');
                        for (int i = 0; i < 16; i++)
                        {
                            if (i > 0) _sb.Append(',');
                            WriteFloat(mtx[i]);
                        }
                        _sb.Append(']');
                        return true;
                    default:
                        return false;
                }
            }

            private void WriteFloats(params (string Name, float Value)[] members)
            {
                _sb.Append('{');
                for (int i = 0; i < members.Length; i++)
                {
                    if (i > 0) _sb.Append(',');
                    WriteString(members[i].Name);
                    _sb.Append(':');
                    WriteFloat(members[i].Value);
                }
                _sb.Append('}');
            }

            private void WriteBounds(UnityEngine.Vector3 center, UnityEngine.Vector3 size)
            {
                _sb.Append("{\"center\":");
                WriteFloats(("x", center.x), ("y", center.y), ("z", center.z));
                _sb.Append(",\"size\":");
                WriteFloats(("x", size.x), ("y", size.y), ("z", size.z));
                _sb.Append('}');
            }

            private void WriteUnityObject(UnityEngine.Object uo)
            {
                // Unity's overloaded == reports destroyed objects as null even though the managed
                // reference is alive, and touching .name on those throws.
                if (uo == null)
                {
                    _sb.Append("null");
                    return;
                }

                string name;
                int id;
                try
                {
                    name = uo.name;
                    id = uo.GetId();
                }
                catch
                {
                    _sb.Append("null");
                    return;
                }

                _sb.Append('{');
                WriteString("name");
                _sb.Append(':');
                WriteString(name);
                _sb.Append(",\"instanceId\":");
                _sb.Append(id.ToString(Inv));
                _sb.Append(",\"type\":");
                WriteString(uo.GetType().Name);
                _sb.Append('}');
            }

            private void WriteString(string s)
            {
                if (s == null)
                {
                    _sb.Append("null");
                    return;
                }
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
                    // A key must always be a quoted string, even if ToString() yields null.
                    WriteString(entry.Key?.ToString() ?? string.Empty);
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

            private void WriteEnumerable(IEnumerable seq)
            {
                _sb.Append('[');
                bool first = true;
                foreach (var item in seq)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    WriteValue(item);
                }
                _sb.Append(']');
            }

            private void WriteObject(object obj)
            {
                if (_reflectionDepth >= MaxReflectionDepth)
                {
                    WriteString(MaxDepthMarker);
                    return;
                }
                if (_reflectedObjects >= MaxReflectedObjects)
                {
                    WriteString(TruncatedMarker);
                    return;
                }

                _reflectedObjects++;
                _reflectionDepth++;
                try
                {
                    WriteObjectMembers(obj);
                }
                finally
                {
                    _reflectionDepth--;
                }
            }

            private void WriteObjectMembers(object obj)
            {
                var type = obj.GetType();
                var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                _sb.Append('{');
                bool first = true;

                foreach (var prop in props)
                {
                    if (!prop.CanRead) continue;
                    // Indexers (Vector3's this[int], IList's this[int], ...) have no readable value.
                    if (prop.GetIndexParameters().Length > 0) continue;
                    var captured = prop;
                    WriteMember(captured.Name, () => captured.GetValue(obj), ref first);
                }

                foreach (var field in fields)
                {
                    var captured = field;
                    WriteMember(captured.Name, () => captured.GetValue(obj), ref first);
                }

                _sb.Append('}');
            }

            /// Writes one member, rolling the buffer back if reading or serializing it throws.
            /// The separator and key used to be appended before the value was evaluated, so a
            /// throwing getter left a dangling `"key":` behind and produced invalid JSON.
            private void WriteMember(string name, Func<object> read, ref bool first)
            {
                var rollback = _sb.Length;
                var wasFirst = first;
                try
                {
                    var val = read();
                    if (!first) _sb.Append(',');
                    first = false;
                    // Convert PascalCase to camelCase
                    WriteString(string.IsNullOrEmpty(name)
                        ? name
                        : char.ToLowerInvariant(name[0]) + name.Substring(1));
                    _sb.Append(':');
                    WriteValue(val);
                }
                catch
                {
                    _sb.Length = rollback;
                    first = wasFirst;
                }
            }

            /// Identity comparer for cycle detection: value equality would collapse distinct but
            /// equal nodes, and a payload type's own Equals/GetHashCode can be arbitrarily
            /// expensive or throw.
            private sealed class ReferenceComparer : IEqualityComparer<object>
            {
                public static readonly ReferenceComparer Instance = new();

                public new bool Equals(object x, object y) => ReferenceEquals(x, y);

                public int GetHashCode(object obj) =>
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
