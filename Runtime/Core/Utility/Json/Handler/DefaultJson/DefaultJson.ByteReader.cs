using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        /// <summary>
        /// UTF8 字节反序列化解析器。与 <see cref="Reader"/> 逻辑对称，但直接消费 byte[]：
        /// key 用预编码 UTF8 字节表零拷贝比较、字符串值按需物化、数值 token 零中间字符串
        /// （整数手工解析、浮点经 stackalloc char 缓冲转换），整型数组走类型化零装箱快速路径。
        /// </summary>
        /// <remarks>
        /// <para><b>文件组织</b>：本文件为核心（入口/值分派/字符串/对象/词法/错误）；
        /// 集合解析在 <c>DefaultJson.ByteReader.Collections.cs</c>；
        /// 数值解析在 <c>DefaultJson.ByteReader.Numbers.cs</c>。</para>
        /// <para><b>输入契约</b>：接受与 <see cref="Reader"/> 相同的输入集合（含 legacy 字典格式、
        /// 带引号历史数值、NaN/Infinity 字面量、BOM 头）。</para>
        /// <para><b>安全</b>：闭合括号循环（截断即抛错）、深度守卫、未知字段跳过、
        /// 错误信息带偏移/行列/上下文片段。</para>
        /// </remarks>
        internal sealed partial class ByteReader
        {
            #region 变量 [VARIABLES]
            private readonly byte[] _json;
            private readonly int _maxDepth;
            private int _pos;
            private int _depth;
            private bool _depthWarned;

            /// <summary>深度软跳过告警（每次解析仅一次）。</summary>
            private void WarnDepthExceeded()
            {
                if (_depthWarned) return;
                _depthWarned = true;
                Log.Warning("[DefaultJson] Deserialization depth exceeded the limit of {0}. Values beyond the limit are skipped and defaulted.", _maxDepth);
            }

            #endregion

            #region 构造函数 [CONSTRUCTOR]
            public ByteReader(byte[] json, int maxDepth)
            {
                _json = json;
                _maxDepth = maxDepth;

                // 跳过 UTF8 BOM
                if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
                {
                    _pos = 3;
                }
            }

            #endregion

            #region 公共入口 [PUBLIC ENTRY]
            /// <summary>解析根值。existing 非空时向其覆盖（FromJsonOverwrite 语义：集合清空复用）。</summary>
            public object Parse(Type targetType, object existing)
            {
                SkipWhitespace();

                if (_pos >= _json.Length)
                {
                    Throw("Unexpected end of JSON input.");
                }

                if (Peek() == (byte)'n' && MatchLiteral("null"))
                {
                    if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    {
                        return null;
                    }

                    Throw(StringUtility.Format("Cannot assign null to value type '{0}'.", targetType.Name));
                }

                if (existing != null)
                {
                    if (existing is IDictionary existingDict &&
                        targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        // 按实际 token 分发：标准对象格式 or legacy 条目数组格式（历史存档兼容）
                        SkipWhitespace();
                        byte dictToken = Peek();
                        if (dictToken == (byte)'{')
                        {
                            ParseDictionary(targetType, existingDict);
                        }
                        else if (dictToken == (byte)'[')
                        {
                            ParseDictionaryLegacy(targetType, existingDict);
                        }
                        else
                        {
                            Throw(StringUtility.Format("Expected '{{' or '[' for dictionary but found '{0}'.", (char)dictToken));
                        }

                        return existing;
                    }

                    if (existing is IList existingList &&
                        targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        ParseList(targetType, existingList);
                        return existing;
                    }

                    if (!targetType.IsArray && existing is not IDictionary && existing is not IList)
                    {
                        return ParseObject(targetType, existing);
                    }
                }

                return ParseValue(targetType);
            }

            #endregion

            #region 值分派 [VALUE DISPATCH]
            private object ParseValue(Type type)
            {
                SkipWhitespace();
                byte c = Peek();

                if (c == (byte)'n')
                {
                    if (MatchLiteral("null"))
                    {
                        if (!type.IsValueType || Nullable.GetUnderlyingType(type) != null)
                        {
                            return null;
                        }

                        Throw(StringUtility.Format("Cannot assign null to value type '{0}'.", type.Name));
                    }

                    Throw(StringUtility.Format("Unexpected token '{0}'.", ReadLiteralToken()));
                }

                // Nullable<T> 统一按 T 解析
                type = Nullable.GetUnderlyingType(type) ?? type;

                switch (c)
                {
                    case (byte)'"':
                        return ParseStringValue(type);

                    case (byte)'{':
                        // 深度超限：软跳过（与序列化侧软截断对称）
                        if (_depth >= _maxDepth)
                        {
                            WarnDepthExceeded();
                            SkipValue();
                            return type.IsValueType ? Activator.CreateInstance(type) : null;
                        }

                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            return ParseDictionary(type, null);
                        }

                        // 值类型结构体同样按对象解析（装箱 → 写字段 → 拆箱）
                        return ParseObject(type, null);

                    case (byte)'[':
                        if (_depth >= _maxDepth)
                        {
                            WarnDepthExceeded();
                            SkipValue();
                            return type.IsValueType ? Activator.CreateInstance(type) : null;
                        }

                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            return ParseDictionaryLegacy(type);
                        }

                        if (type.IsArray)
                        {
                            return ParseArray(type);
                        }

                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            IList list = (IList)Activator.CreateInstance(type);
                            ParseList(type, list);
                            return list;
                        }

                        Throw(StringUtility.Format("Cannot parse a JSON array into '{0}'.", type.Name));
                        return null;

                    case (byte)'t':
                    case (byte)'f':
                        return ParseBoolean(type);

                    case (byte)'N':
                    case (byte)'I':
                        return ParseNonFinite(type);

                    default:
                        if (c == (byte)'-' && MatchLiteral("-Infinity"))
                        {
                            return NonFiniteResult(type, double.NegativeInfinity);
                        }

                        if (c == (byte)'-' || (c >= (byte)'0' && c <= (byte)'9'))
                        {
                            return ParseNumber(type);
                        }

                        Throw(StringUtility.Format("Unexpected character '{0}'.", (char)c));
                        return null;
                }
            }

            private object ParseStringValue(Type type)
            {
                string s = ReadStringMaterialized();
                return ConvertFromString(s, type);
            }

            private object ParseBoolean(Type type)
            {
                bool value;
                if (MatchLiteral("true")) value = true;
                else if (MatchLiteral("false")) value = false;
                else
                {
                    Throw("Invalid boolean literal.");
                    return null;
                }

                if (type == typeof(bool)) return value;
                if (type == typeof(string)) return value ? "true" : "false";

                Throw(StringUtility.Format("Cannot parse a boolean into '{0}'.", type.Name));
                return null;
            }

            private object ParseNonFinite(Type type)
            {
                double value;
                if (MatchLiteral("NaN")) value = double.NaN;
                else if (MatchLiteral("Infinity")) value = double.PositiveInfinity;
                else if (MatchLiteral("-Infinity")) value = double.NegativeInfinity;
                else
                {
                    Throw(StringUtility.Format("Unexpected token '{0}'.", ReadLiteralToken()));
                    return null;
                }

                return NonFiniteResult(type, value);
            }

            private object NonFiniteResult(Type type, double value)
            {
                if (type == typeof(double)) return value;
                if (type == typeof(float)) return (float)value;

                Throw(StringUtility.Format("Non-finite value is only valid for float/double, not '{0}'.", type.Name));
                return null;
            }

            #endregion

            #region 字符串/键 [STRINGS/KEYS]
            /// <summary>读取带引号字符串的原始字节 span（不含引号），跳过转义对。start 输出段起点（供 GetString 的 byte[] 重载）。</summary>
            private ReadOnlySpan<byte> ReadStringSpanBytes(out bool hasEscape, out int start)
            {
                hasEscape = false;
                start = 0;
                if (_pos >= _json.Length || _json[_pos] != (byte)'"')
                {
                    Throw(StringUtility.Format("Expected a string but found '{0}'.",
                        _pos < _json.Length ? ((char)_json[_pos]).ToString() : "<end>"));
                }

                start = _pos + 1;
                int i = start;

                while (i < _json.Length)
                {
                    byte c = _json[i];
                    if (c == (byte)'\\')
                    {
                        hasEscape = true;
                        i += 2;
                        continue;
                    }

                    if (c == (byte)'"')
                    {
                        _pos = i + 1;
                        return new ReadOnlySpan<byte>(_json, start, i - start);
                    }

                    i++;
                }

                Throw("Unterminated string.");
                return default;
            }

            /// <summary>物化字符串值：普通段直接 UTF8 解码，含转义时走反转义（按需分配）。</summary>
            private string ReadStringMaterialized()
            {
                bool hasEscape;
                int start;
                ReadOnlySpan<byte> span = ReadStringSpanBytes(out hasEscape, out start);
                return hasEscape ? UnescapeBytes(span) : Encoding.UTF8.GetString(_json, start, span.Length);
            }

            /// <summary>反转义（标准转义对、\uXXXX；原始段手动解码 UTF8，无效序列 → U+FFFD）。</summary>
            private string UnescapeBytes(ReadOnlySpan<byte> input)
            {
                StringHandler.IStringBuilder sb = StringUtility.CreateStringBuilder(input.Length);
                try
                {
                    int i = 0;
                    while (i < input.Length)
                    {
                        byte c = input[i];
                        if (c == (byte)'\\')
                        {
                            i++;
                            if (i >= input.Length) Throw("Invalid trailing escape character.");

                            switch ((char)input[i])
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
                                    if (i + 4 >= input.Length)
                                    {
                                        Throw("Incomplete \\u escape sequence.");
                                    }

                                    if (!TryParseHex4(input.Slice(i + 1, 4), out uint code))
                                    {
                                        Throw("Invalid \\u escape sequence.");
                                    }

                                    AppendRuneAsUtf16(sb, code);
                                    i += 4;
                                    break;
                                default:
                                    Throw(StringUtility.Format("Unrecognized escape sequence '\\{0}'.", (char)input[i]));
                                    break;
                            }

                            i++;
                        }
                        else
                        {
                            // ASCII 快路径：直接追加字符，跳过 DecodeUtf8Rune 的 span 切片与函数调用开销
                            if (c < 0x80)
                            {
                                sb.Append((char)c);
                                i++;
                            }
                            else
                            {
                                i += DecodeUtf8Rune(input.Slice(i), out uint rune);
                                AppendRuneAsUtf16(sb, rune);
                            }
                        }
                    }

                    return sb.ToStringAndDispose();
                }
                catch
                {
                    sb.Dispose();
                    throw;
                }
            }

            private static void AppendRuneAsUtf16(StringHandler.IStringBuilder sb, uint rune)
            {
                if (rune <= 0xFFFF)
                {
                    sb.Append((char)rune);
                }
                else
                {
                    rune -= 0x10000;
                    sb.Append((char)(0xD800 + (rune >> 10)));
                    sb.Append((char)(0xDC00 + (rune & 0x3FF)));
                }
            }

            /// <summary>从 span 头解码一个 UTF8 码点；无效序列产出 U+FFFD 并前进 1 字节。返回消耗的字节数。</summary>
            private static int DecodeUtf8Rune(ReadOnlySpan<byte> s, out uint rune)
            {
                byte b0 = s[0];
                if (b0 < 0x80)
                {
                    rune = b0;
                    return 1;
                }

                if (b0 >= 0xC2 && b0 <= 0xDF && s.Length >= 2 && IsContinuation(s[1]))
                {
                    rune = ((uint)(b0 & 0x1F) << 6) | (uint)(s[1] & 0x3F);
                    return 2;
                }

                if (b0 >= 0xE0 && b0 <= 0xEF && s.Length >= 3 && IsContinuation(s[1]) && IsContinuation(s[2]))
                {
                    rune = ((uint)(b0 & 0x0F) << 12) | ((uint)(s[1] & 0x3F) << 6) | (uint)(s[2] & 0x3F);
                    if (rune >= 0xD800 && rune <= 0xDFFF)
                    {
                        rune = 0xFFFD; // UTF16 代理区不是合法码点
                        return 1;
                    }

                    return 3;
                }

                if (b0 >= 0xF0 && b0 <= 0xF4 && s.Length >= 4 && IsContinuation(s[1]) && IsContinuation(s[2]) && IsContinuation(s[3]))
                {
                    rune = ((uint)(b0 & 0x07) << 18) | ((uint)(s[1] & 0x3F) << 12) | ((uint)(s[2] & 0x3F) << 6) | (uint)(s[3] & 0x3F);
                    if (rune > 0x10FFFF)
                    {
                        rune = 0xFFFD;
                        return 1;
                    }

                    return 4;
                }

                rune = 0xFFFD;
                return 1;
            }

            private static bool IsContinuation(byte b)
            {
                return (b & 0xC0) == 0x80;
            }

            private static bool TryParseHex4(ReadOnlySpan<byte> s, out uint value)
            {
                value = 0;
                for (int i = 0; i < 4; i++)
                {
                    uint nibble;
                    byte c = s[i];
                    if (c >= (byte)'0' && c <= (byte)'9') nibble = (uint)(c - '0');
                    else if (c >= (byte)'a' && c <= (byte)'f') nibble = (uint)(c - 'a' + 10);
                    else if (c >= (byte)'A' && c <= (byte)'F') nibble = (uint)(c - 'A' + 10);
                    else return false;

                    value = (value << 4) | nibble;
                }

                return true;
            }

            #endregion

            #region 对象 [OBJECTS]
            private object ParseObject(Type type, object instance)
            {
                if (instance == null)
                {
                    instance = Activator.CreateInstance(type);
                    if (instance == null)
                    {
                        Throw(StringUtility.Format("Cannot create an instance of '{0}'.", type.Name));
                    }
                }

                var meta = ReflectionCache.Get(type);

                Expect((byte)'{');

                SkipWhitespace();
                if (Peek() == (byte)'}')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        SkipWhitespace();
                        if (Peek() != (byte)'"')
                        {
                            Throw(StringUtility.Format("Expected an object key (string) but found '{0}'.", (char)Peek()));
                        }

                        bool hasEscape;
                        int keyStart;
                        ReadOnlySpan<byte> keySpan = ReadStringSpanBytes(out hasEscape, out keyStart);
                        string escapedKey = hasEscape ? UnescapeBytes(keySpan) : null;

                        SkipWhitespace();
                        Expect((byte)':');

                        FieldInfo field = FindField(meta, keySpan, escapedKey);
                        if (field != null)
                        {
                            _depth++;
                            object value = ParseValue(field.FieldType);
                            _depth--;
                            field.SetValue(instance, value);
                        }
                        else
                        {
                            PropertyInfo property = FindProperty(meta, keySpan, escapedKey);
                            if (property != null)
                            {
                                _depth++;
                                object value = ParseValue(property.PropertyType);
                                _depth--;
                                property.SetValue(instance, value);
                            }
                            else
                            {
                                SkipValue(); // 未知字段：跳过其值
                            }
                        }

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)'}')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                    }
                }

                foreach (MethodInfo info in meta.AfterDeserializeMethods)
                {
                    info.Invoke(instance, null);
                }

                return instance;
            }

            private static FieldInfo FindField(ReflectionCache.TypeMeta meta, ReadOnlySpan<byte> keySpan, string escapedKey)
            {
                var fields = meta.DeserializeFields;
                var namesUtf8 = meta.DeserializeFieldNamesUtf8;
                for (int i = 0; i < fields.Length; i++)
                {
                    if (escapedKey != null)
                    {
                        string[] names = fields[i].Names;
                        for (int j = 0; j < names.Length; j++)
                        {
                            if (names[j] == escapedKey) return fields[i].Field;
                        }
                    }
                    else
                    {
                        byte[][] encoded = namesUtf8[i];
                        for (int j = 0; j < encoded.Length; j++)
                        {
                            if (keySpan.SequenceEqual(encoded[j])) return fields[i].Field;
                        }
                    }
                }

                return null;
            }

            private static PropertyInfo FindProperty(ReflectionCache.TypeMeta meta, ReadOnlySpan<byte> keySpan, string escapedKey)
            {
                var properties = meta.DeserializeProperties;
                var namesUtf8 = meta.DeserializePropertyNamesUtf8;
                for (int i = 0; i < properties.Length; i++)
                {
                    if (escapedKey != null)
                    {
                        string[] names = properties[i].Names;
                        for (int j = 0; j < names.Length; j++)
                        {
                            if (names[j] == escapedKey) return properties[i].Property;
                        }
                    }
                    else
                    {
                        byte[][] encoded = namesUtf8[i];
                        for (int j = 0; j < encoded.Length; j++)
                        {
                            if (keySpan.SequenceEqual(encoded[j])) return properties[i].Property;
                        }
                    }
                }

                return null;
            }

            #endregion

            #region 词法工具 [LEXER UTILITIES]
            private void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    byte c = _json[_pos];
                    if (c == 0x20 || c == 0x09 || c == 0x0A || c == 0x0D)
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private byte Peek()
            {
                if (_pos >= _json.Length)
                {
                    Throw("Unexpected end of JSON input.");
                }

                return _json[_pos];
            }

            private void Expect(byte expected)
            {
                SkipWhitespace();
                byte c = Peek();
                if (c != expected)
                {
                    Throw(StringUtility.Format("Expected '{0}' but found '{1}'.", (char)expected, (char)c));
                }

                _pos++;
            }

            private bool MatchLiteral(string literal)
            {
                if (_json.Length - _pos < literal.Length) return false;

                for (int i = 0; i < literal.Length; i++)
                {
                    if (_json[_pos + i] != (byte)literal[i]) return false;
                }

                // 词边界：后续字符必须是分隔符或 EOF
                int next = _pos + literal.Length;
                if (next < _json.Length && !IsDelimiter(_json[next])) return false;
                _pos = next;
                return true;
            }

            private string ReadLiteralToken()
            {
                int start = _pos;
                while (_pos < _json.Length && !IsDelimiter(_json[_pos])) _pos++;
                return Encoding.UTF8.GetString(_json, start, _pos - start);
            }

            private static bool IsDelimiter(byte c)
            {
                return c == (byte)',' || c == (byte)'}' || c == (byte)']' ||
                       c == 0x20 || c == 0x09 || c == 0x0A || c == 0x0D;
            }

            /// <summary>跳过任意未知值（字面量/字符串/对象/数组），用于未知字段的前向兼容。</summary>
            private void SkipValue()
            {
                SkipWhitespace();
                byte c = Peek();

                if (c == (byte)'"')
                {
                    bool hasEscape;
                    int start;
                    ReadStringSpanBytes(out hasEscape, out start);
                    return;
                }

                if (c == (byte)'{' || c == (byte)'[')
                {
                    int depth = 0;
                    bool inString = false;
                    while (_pos < _json.Length)
                    {
                        byte x = _json[_pos];
                        if (inString)
                        {
                            if (x == (byte)'\\') _pos++;
                            else if (x == (byte)'"') inString = false;
                        }
                        else
                        {
                            if (x == (byte)'"') inString = true;
                            else if (x == (byte)'{' || x == (byte)'[') depth++;
                            else if (x == (byte)'}' || x == (byte)']')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    _pos++;
                                    return;
                                }
                            }
                        }

                        _pos++;
                    }

                    Throw("Unexpected end of JSON input.");
                }

                while (_pos < _json.Length && !IsDelimiter(_json[_pos])) _pos++;
            }

            #endregion

            #region 错误 [ERRORS]
            [System.Diagnostics.CodeAnalysis.DoesNotReturn]
            private void Throw(string message)
            {
                int line = 1, col = 1;
                int limit = Math.Min(_pos, _json.Length);
                for (int i = 0; i < limit; i++)
                {
                    if (_json[i] == 0x0A)
                    {
                        line++;
                        col = 1;
                    }
                    else
                    {
                        col++;
                    }
                }

                int snippetStart = Math.Max(0, _pos - 20);
                int snippetLen = Math.Min(40, _json.Length - snippetStart);
                string snippet = snippetLen > 0 ? Encoding.UTF8.GetString(_json, snippetStart, snippetLen) : string.Empty;

                throw new GameException(StringUtility.Format(
                    "{0} At offset {1} (line {2}, column {3}) near \"{4}\".",
                    message, _pos, line, col, snippet));
            }

            #endregion
        }
    }
}
