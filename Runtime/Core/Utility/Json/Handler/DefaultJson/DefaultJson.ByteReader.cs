using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
        /// <para><b>输入契约</b>：接受与 <see cref="Reader"/> 相同的输入集合（含 legacy 字典格式、
        /// 带引号历史数值、NaN/Infinity 字面量、BOM 头）。</para>
        /// <para><b>安全</b>：闭合括号循环（截断即抛错）、深度守卫、未知字段跳过、
        /// 错误信息带偏移/行列/上下文片段。</para>
        /// </remarks>
        internal sealed class ByteReader
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
                Log.Warning(StringUtility.Format(
                    "[DefaultJson] Deserialization depth exceeded the limit of {0}. Values beyond the limit are skipped and defaulted.", _maxDepth));
            }

            #endregion

            #region 类型化数组注册 [TYPED ARRAY REGISTRY]

            /// <summary>类型化读取器注册表：按元素类型闭包静态化，消除值类型数组解析的逐元素装箱。</summary>
            private static class TypedRead<T> where T : struct
            {
                public static Func<ByteReader, T> Read;
            }

            static ByteReader()
            {
                TypedRead<bool>.Read = r => r.ReadBooleanToken();
                TypedRead<sbyte>.Read = r => r.ReadIntegralSByte();
                TypedRead<byte>.Read = r => r.ReadIntegralByte();
                TypedRead<short>.Read = r => r.ReadIntegralInt16();
                TypedRead<ushort>.Read = r => r.ReadIntegralUInt16();
                TypedRead<int>.Read = r => r.ReadIntegralInt32();
                TypedRead<uint>.Read = r => r.ReadIntegralUInt32();
                TypedRead<long>.Read = r => r.ReadInt64Token();
                TypedRead<ulong>.Read = r => r.ReadUInt64Token();
                TypedRead<float>.Read = r => r.ReadSingleToken();
                TypedRead<double>.Read = r => r.ReadDoubleToken();
                TypedRead<decimal>.Read = r => r.ReadDecimalToken();
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

            /// <summary>解析根值。existing 非空时向其覆盖（FromJSONOverwrite 语义：集合清空复用）。</summary>
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
                        ParseDictionary(targetType, existingDict);
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
                            i += DecodeUtf8Rune(input.Slice(i), out uint rune);
                            AppendRuneAsUtf16(sb, rune);
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

            #region 集合 [COLLECTIONS]

            private void ParseList(Type type, IList list)
            {
                Type itemType = type.GenericTypeArguments[0];

                list.Clear();

                // 类型化基元列表快速路径：具体类型模式匹配（AOT 安全），消除逐元素 ParseValue 分派与 IList.Add 装箱
                switch (list)
                {
                    case List<int> l:
                        ParseTypedList(l, TypedRead<int>.Read);
                        return;
                    case List<long> l:
                        ParseTypedList(l, TypedRead<long>.Read);
                        return;
                    case List<float> l:
                        ParseTypedList(l, TypedRead<float>.Read);
                        return;
                    case List<double> l:
                        ParseTypedList(l, TypedRead<double>.Read);
                        return;
                    case List<bool> l:
                        ParseTypedList(l, TypedRead<bool>.Read);
                        return;
                    case List<sbyte> l:
                        ParseTypedList(l, TypedRead<sbyte>.Read);
                        return;
                    case List<byte> l:
                        ParseTypedList(l, TypedRead<byte>.Read);
                        return;
                    case List<short> l:
                        ParseTypedList(l, TypedRead<short>.Read);
                        return;
                    case List<ushort> l:
                        ParseTypedList(l, TypedRead<ushort>.Read);
                        return;
                    case List<uint> l:
                        ParseTypedList(l, TypedRead<uint>.Read);
                        return;
                    case List<ulong> l:
                        ParseTypedList(l, TypedRead<ulong>.Read);
                        return;
                    case List<decimal> l:
                        ParseTypedList(l, TypedRead<decimal>.Read);
                        return;
                }

                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    _depth++;
                    object value = ParseValue(itemType);
                    _depth--;
                    list.Add(value);

                    SkipWhitespace();
                    byte c = Peek();
                    if (c == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == (byte)']')
                    {
                        _pos++;
                        return;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>类型化基元列表解析骨架（基元元素不可嵌套，无需深度计数）。</summary>
            private void ParseTypedList<T>(List<T> list, Func<ByteReader, T> read) where T : struct
            {
                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    list.Add(read(this));

                    SkipWhitespace();
                    byte c = Peek();
                    if (c == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == (byte)']')
                    {
                        _pos++;
                        return;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            private object ParseArray(Type type)
            {
                Type elementType = type.GetElementType();

                // 类型化零装箱快速路径（值类型基元数组 + string）
                if (elementType == typeof(string))
                {
                    return ParseStringArray();
                }

                switch (Type.GetTypeCode(elementType))
                {
                    case TypeCode.Boolean: return ParsePrimitiveArray<bool>();
                    case TypeCode.SByte: return ParsePrimitiveArray<sbyte>();
                    case TypeCode.Byte: return ParsePrimitiveArray<byte>();
                    case TypeCode.Int16: return ParsePrimitiveArray<short>();
                    case TypeCode.UInt16: return ParsePrimitiveArray<ushort>();
                    case TypeCode.Int32: return ParsePrimitiveArray<int>();
                    case TypeCode.UInt32: return ParsePrimitiveArray<uint>();
                    case TypeCode.Int64: return ParsePrimitiveArray<long>();
                    case TypeCode.UInt64: return ParsePrimitiveArray<ulong>();
                    case TypeCode.Single: return ParsePrimitiveArray<float>();
                    case TypeCode.Double: return ParsePrimitiveArray<double>();
                    case TypeCode.Decimal: return ParsePrimitiveArray<decimal>();
                }

                // 回退路径（枚举/char/对象元素）
                var elements = new List<object>();

                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        _depth++;
                        object value = ParseValue(elementType);
                        _depth--;

                        if (!elementType.IsInstanceOfType(value) && value != null)
                        {
                            try
                            {
                                value = Convert.ChangeType(value, elementType, CultureInfo.InvariantCulture);
                            }
                            catch (Exception)
                            {
                                Throw(StringUtility.Format("Cannot convert '{0}' to element type '{1}'.", value, elementType.Name));
                            }
                        }

                        elements.Add(value);

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                Array result = Array.CreateInstance(elementType, elements.Count);
                for (int i = 0; i < elements.Count; i++)
                {
                    result.SetValue(elements[i], i);
                }

                return result;
            }

            /// <summary>基元数组零装箱解析（经 TypedRead 注册表按元素类型分派）。</summary>
            private T[] ParsePrimitiveArray<T>() where T : struct
            {
                Func<ByteReader, T> read = TypedRead<T>.Read;

                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<T>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(read(this));

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            private string[] ParseStringArray()
            {
                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<string>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add((string)ParseValue(typeof(string)));

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            private object ParseDictionary(Type type, IDictionary existing)
            {
                IDictionary dict = existing ?? (IDictionary)Activator.CreateInstance(type);
                Type keyType = type.GenericTypeArguments[0];
                Type valueType = type.GenericTypeArguments[1];

                if (existing != null) dict.Clear();

                Expect((byte)'{');

                SkipWhitespace();
                if (Peek() == (byte)'}')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != (byte)'"')
                    {
                        Throw(StringUtility.Format("Expected a dictionary key (string) but found '{0}'.", (char)Peek()));
                    }

                    string keyString = ReadStringMaterialized();
                    object key = ConvertDictionaryKey(keyString, keyType);

                    SkipWhitespace();
                    Expect((byte)':');

                    _depth++;
                    object value = ParseValue(valueType);
                    _depth--;

                    dict[key] = value;

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
                        return dict;
                    }

                    Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                }
            }

            private object ParseDictionaryLegacy(Type type)
            {
                IDictionary dict = (IDictionary)Activator.CreateInstance(type);
                Type keyType = type.GenericTypeArguments[0];
                Type valueType = type.GenericTypeArguments[1];

                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    Expect((byte)'{');

                    object key = null;
                    object value = null;
                    bool keyAssigned = false;
                    bool valueAssigned = false;

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
                            string member = ReadStringMaterialized();

                            SkipWhitespace();
                            Expect((byte)':');

                            if (member == "key")
                            {
                                if (keyAssigned) Throw("Duplicate key found.");
                                _depth++;
                                key = ParseValue(keyType);
                                _depth--;
                                keyAssigned = true;
                            }
                            else if (member == "value")
                            {
                                if (valueAssigned) Throw("Duplicate value found.");
                                _depth++;
                                value = ParseValue(valueType);
                                _depth--;
                                valueAssigned = true;
                            }
                            else
                            {
                                Throw(StringUtility.Format("Invalid dictionary entry member '{0}'.", member));
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

                    if (!keyAssigned || !valueAssigned)
                    {
                        Throw("Dictionary entry requires both 'key' and 'value'.");
                    }

                    dict[key] = value;

                    SkipWhitespace();
                    byte cc = Peek();
                    if (cc == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (cc == (byte)']')
                    {
                        _pos++;
                        return dict;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)cc));
                }
            }

            #endregion

            #region 类型转换 [CONVERSIONS]

            /// <summary>字符串 → 目标类型（string/char/bool/枚举/数值/Guid/DateTime/TimeSpan/DateTimeOffset）。</summary>
            private object ConvertFromString(string s, Type type)
            {
                if (type == typeof(string) || type == typeof(object)) return s;
                if (type == typeof(char)) return s.Length > 0 && s != "null" ? (object)s[0] : '\0';
                if (type == typeof(bool)) return ParseBooleanString(s);

                if (type.IsEnum)
                {
                    if (Enum.TryParse(type, s, false, out object enumValue)) return enumValue;
                    Throw(StringUtility.Format("'{0}' is not a valid name or value for enum '{1}'.", s, type.Name));
                }

                if (type == typeof(Guid))
                {
                    if (Guid.TryParse(s, out Guid guid)) return guid;
                    Throw(StringUtility.Format("'{0}' is not a valid Guid.", s));
                }

                if (type == typeof(DateTime))
                {
                    if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt)) return dt;
                    Throw(StringUtility.Format("'{0}' is not a valid DateTime.", s));
                }

                if (type == typeof(DateTimeOffset))
                {
                    if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset dto)) return dto;
                    Throw(StringUtility.Format("'{0}' is not a valid DateTimeOffset.", s));
                }

                if (type == typeof(TimeSpan))
                {
                    if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out TimeSpan ts)) return ts;
                    Throw(StringUtility.Format("'{0}' is not a valid TimeSpan.", s));
                }

                // 历史带引号数值
                return ParseNumberSpanBytes(type, Encoding.UTF8.GetBytes(s));
            }

            private bool ParseBooleanString(string s)
            {
                switch (s)
                {
                    case "true":
                    case "TRUE":
                    case "True":
                    case "1":
                    case "-1":
                        return true;
                    case "false":
                    case "FALSE":
                    case "False":
                    case "0":
                        return false;
                    default:
                        Throw(StringUtility.Format("Invalid value for boolean: '{0}'.", s));
                        return false;
                }
            }

            private object ConvertDictionaryKey(string s, Type keyType)
            {
                if (keyType == typeof(string)) return s;
                if (keyType == typeof(char)) return s.Length > 0 ? (object)s[0] : '\0';
                if (keyType == typeof(bool)) return ParseBooleanString(s);

                if (keyType.IsEnum)
                {
                    if (Enum.TryParse(keyType, s, false, out object v)) return v;
                    Throw(StringUtility.Format("'{0}' is not a valid dictionary key for enum '{1}'.", s, keyType.Name));
                }

                if (keyType == typeof(Guid))
                {
                    if (Guid.TryParse(s, out Guid guid)) return guid;
                    Throw(StringUtility.Format("'{0}' is not a valid Guid dictionary key.", s));
                }

                return ParseNumberSpanBytes(keyType, Encoding.UTF8.GetBytes(s));
            }

            #endregion

            #region 数值 [NUMBERS]

            private object ParseNumber(Type type)
            {
                return ParseNumberSpanBytes(type, ScanNumberToken());
            }

            /// <summary>扫描数值 token（支持历史带引号形式）。</summary>
            private ReadOnlySpan<byte> ScanNumberToken()
            {
                SkipWhitespace();

                bool quoted = false;
                if (Peek() == (byte)'"')
                {
                    quoted = true;
                    _pos++;
                }

                int start = _pos;
                while (_pos < _json.Length)
                {
                    byte c = _json[_pos];
                    if ((c >= (byte)'0' && c <= (byte)'9') || c == (byte)'-' || c == (byte)'+' ||
                        c == (byte)'.' || c == (byte)'e' || c == (byte)'E')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (quoted)
                {
                    if (_pos >= _json.Length || _json[_pos] != (byte)'"')
                    {
                        Throw("Unterminated quoted number.");
                    }

                    _pos++;
                }

                return new ReadOnlySpan<byte>(_json, start, _pos - start - (quoted ? 1 : 0));
            }

            private object ParseNumberSpanBytes(Type type, ReadOnlySpan<byte> s)
            {
                if (s.IsEmpty)
                {
                    Throw("Empty numeric token.");
                }

                if (type.IsEnum)
                {
                    if (TryParseInt64Bytes(s, out long enumRaw))
                    {
                        return Enum.ToObject(type, enumRaw);
                    }

                    if (TryParseUInt64Bytes(s, out ulong enumRawU))
                    {
                        return Enum.ToObject(type, enumRawU);
                    }

                    Throw(StringUtility.Format("'{0}' is not a valid numeric value for enum '{1}'.", EncodeSpan(s), type.Name));
                }

                bool isFloatTarget = type == typeof(float) || type == typeof(double) || type == typeof(decimal);
                if (!isFloatTarget && type != typeof(bool) && type != typeof(string))
                {
                    object direct = TryParseIntegralBytes(type, s);
                    if (direct != null) return direct;

                    if (TryParseDoubleBytes(s, out double integral) &&
                        !double.IsNaN(integral) && !double.IsInfinity(integral) && integral == Math.Floor(integral))
                    {
                        try
                        {
                            return Convert.ChangeType(integral, type, CultureInfo.InvariantCulture);
                        }
                        catch (Exception)
                        {
                            Throw(StringUtility.Format("'{0}' is out of range for '{1}'.", EncodeSpan(s), type.Name));
                        }
                    }

                    if (type == typeof(char))
                    {
                        Throw(StringUtility.Format("'{0}' is not a valid char value.", EncodeSpan(s)));
                    }

                    Throw(StringUtility.Format("'{0}' is not a valid integer for '{1}'.", EncodeSpan(s), type.Name));
                }

                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Single:
                        if (TryParseSingleBytes(s, out float f)) return f;
                        break;
                    case TypeCode.Double:
                        if (TryParseDoubleBytes(s, out double d)) return d;
                        break;
                    case TypeCode.Decimal:
                        if (TryParseDecimalBytes(s, out decimal m)) return m;
                        break;
                    case TypeCode.Boolean:
                        if (TryParseDoubleBytes(s, out double b)) return Math.Abs(b) > 0d;
                        break;
                    case TypeCode.String:
                        return EncodeSpan(s);
                    default:
                        if (type == typeof(string) || type == typeof(object)) return EncodeSpan(s);
                        break;
                }

                Throw(StringUtility.Format("'{0}' is not a valid value for '{1}'.", EncodeSpan(s), type.Name));
                return null;
            }

            private static object TryParseIntegralBytes(Type type, ReadOnlySpan<byte> s)
            {
                // 解析到 long/ulong 后按目标范围收窄；越界返回 null 交由 double 回退路径抛出准确错误
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.SByte:
                        if (TryParseInt64Bytes(s, out long sb) && sb >= sbyte.MinValue && sb <= sbyte.MaxValue) return (sbyte)sb;
                        return null;
                    case TypeCode.Byte:
                        if (TryParseUInt64Bytes(s, out ulong b) && b <= byte.MaxValue) return (byte)b;
                        return null;
                    case TypeCode.Int16:
                        if (TryParseInt64Bytes(s, out long sh) && sh >= short.MinValue && sh <= short.MaxValue) return (short)sh;
                        return null;
                    case TypeCode.UInt16:
                        if (TryParseUInt64Bytes(s, out ulong ush) && ush <= ushort.MaxValue) return (ushort)ush;
                        return null;
                    case TypeCode.Int32:
                        if (TryParseInt64Bytes(s, out long i) && i >= int.MinValue && i <= int.MaxValue) return (int)i;
                        return null;
                    case TypeCode.UInt32:
                        if (TryParseUInt64Bytes(s, out ulong ui) && ui <= uint.MaxValue) return (uint)ui;
                        return null;
                    case TypeCode.Int64:
                        return TryParseInt64Bytes(s, out long l) ? (object)l : null;
                    case TypeCode.UInt64:
                        return TryParseUInt64Bytes(s, out ulong ul) ? (object)ul : null;
                    default:
                        return null;
                }
            }

            // ===== 手工整数解析（ASCII 数字循环，零分配） =====

            private static bool TryParseInt64Bytes(ReadOnlySpan<byte> s, out long value)
            {
                value = 0;
                int i = 0;
                bool negative = false;
                if (i < s.Length && s[i] == (byte)'-')
                {
                    negative = true;
                    i++;
                }

                if (i >= s.Length) return false;

                ulong acc = 0;
                while (i < s.Length)
                {
                    byte c = s[i];
                    if (c < (byte)'0' || c > (byte)'9') return false;
                    acc = acc * 10 + (uint)(c - '0');
                    if (acc > 9223372036854775807UL + (negative ? 1UL : 0UL)) return false; // 溢出
                    i++;
                }

                value = negative ? -(long)acc : (long)acc;
                return true;
            }

            private static bool TryParseUInt64Bytes(ReadOnlySpan<byte> s, out ulong value)
            {
                value = 0;
                int i = 0;
                if (i < s.Length && s[i] == (byte)'-') return false;

                if (i >= s.Length) return false;

                ulong acc = 0;
                while (i < s.Length)
                {
                    byte c = s[i];
                    if (c < (byte)'0' || c > (byte)'9') return false;

                    ulong digit = (uint)(c - '0');
                    if (acc > (ulong.MaxValue - digit) / 10) return false; // 溢出

                    acc = acc * 10 + digit;
                    i++;
                }

                value = acc;
                return true;
            }

            // ===== 浮点解析（ASCII token → 调用方栈上 char 缓冲 → TryParse，零堆分配） =====

            private const int NUMBER_CHAR_BUFFER = 64;
            private const int NUMBER_NOT_ASCII = -1;

            private static bool TryParseSingleBytes(ReadOnlySpan<byte> s, out float value)
            {
                Span<char> buffer = stackalloc char[NUMBER_CHAR_BUFFER];
                int len = PrepareNumberChars(s, buffer);
                return len >= 0
                    ? float.TryParse(buffer.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    : float.TryParse(EncodeSpan(s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static bool TryParseDoubleBytes(ReadOnlySpan<byte> s, out double value)
            {
                Span<char> buffer = stackalloc char[NUMBER_CHAR_BUFFER];
                int len = PrepareNumberChars(s, buffer);
                return len >= 0
                    ? double.TryParse(buffer.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    : double.TryParse(EncodeSpan(s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static bool TryParseDecimalBytes(ReadOnlySpan<byte> s, out decimal value)
            {
                Span<char> buffer = stackalloc char[NUMBER_CHAR_BUFFER];
                int len = PrepareNumberChars(s, buffer);
                return len >= 0
                    ? decimal.TryParse(buffer.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    : decimal.TryParse(EncodeSpan(s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            /// <summary>
            /// 将 ASCII 数值 token 复制进调用方栈缓冲，返回复制的字符数。
            /// 超长或含非 ASCII 返回 <see cref="NUMBER_NOT_ASCII"/>（调用方回退字符串路径）。
            /// </summary>
            private static int PrepareNumberChars(ReadOnlySpan<byte> s, Span<char> buffer)
            {
                if (s.Length == 0 || s.Length > buffer.Length) return NUMBER_NOT_ASCII;

                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] > 0x7F) return NUMBER_NOT_ASCII; // 数值 token 必为 ASCII
                    buffer[i] = (char)s[i];
                }

                return s.Length;
            }

            private static string EncodeSpan(ReadOnlySpan<byte> s)
            {
                return Encoding.UTF8.GetString(s.ToArray());
            }

            // ===== 类型化 token 读取（供基元数组零装箱路径） =====

            private bool ReadBooleanToken()
            {
                SkipWhitespace();
                if (MatchLiteral("true")) return true;
                if (MatchLiteral("false")) return false;

                Throw("Invalid boolean literal in array.");
                return false;
            }

            private sbyte ReadIntegralSByte()
            {
                long v = ReadInt64Token();
                if (v < sbyte.MinValue || v > sbyte.MaxValue) ThrowIntegralRange("sbyte");
                return (sbyte)v;
            }

            private byte ReadIntegralByte()
            {
                ulong v = ReadUInt64Token();
                if (v > byte.MaxValue) ThrowIntegralRange("byte");
                return (byte)v;
            }

            private short ReadIntegralInt16()
            {
                long v = ReadInt64Token();
                if (v < short.MinValue || v > short.MaxValue) ThrowIntegralRange("short");
                return (short)v;
            }

            private ushort ReadIntegralUInt16()
            {
                ulong v = ReadUInt64Token();
                if (v > ushort.MaxValue) ThrowIntegralRange("ushort");
                return (ushort)v;
            }

            private int ReadIntegralInt32()
            {
                long v = ReadInt64Token();
                if (v < int.MinValue || v > int.MaxValue) ThrowIntegralRange("int");
                return (int)v;
            }

            private uint ReadIntegralUInt32()
            {
                ulong v = ReadUInt64Token();
                if (v > uint.MaxValue) ThrowIntegralRange("uint");
                return (uint)v;
            }

            /// <summary>long token：直取；科学计数法/溢出回退 double 且必须整值（兼容 "1e2" 类输入）。</summary>
            private long ReadInt64Token()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseInt64Bytes(s, out long v)) return v;

                if (TryParseDoubleBytes(s, out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) &&
                    d >= long.MinValue && d <= long.MaxValue)
                {
                    return (long)d;
                }

                Throw(StringUtility.Format("'{0}' is not a valid integer for 'long'.", EncodeSpan(s)));
                return 0;
            }

            private ulong ReadUInt64Token()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseUInt64Bytes(s, out ulong v)) return v;

                if (TryParseDoubleBytes(s, out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) &&
                    d >= 0 && d <= ulong.MaxValue)
                {
                    return (ulong)d;
                }

                Throw(StringUtility.Format("'{0}' is not a valid integer for 'ulong'.", EncodeSpan(s)));
                return 0;
            }

            private float ReadSingleToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseSingleBytes(s, out float f)) return f;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'float'.", EncodeSpan(s)));
                return 0;
            }

            private double ReadDoubleToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseDoubleBytes(s, out double d)) return d;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'double'.", EncodeSpan(s)));
                return 0;
            }

            private decimal ReadDecimalToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseDecimalBytes(s, out decimal m)) return m;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'decimal'.", EncodeSpan(s)));
                return 0;
            }

            private void ThrowIntegralRange(string typeName)
            {
                Throw(StringUtility.Format("Numeric value is out of range for '{0}'.", typeName));
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

                _pos += literal.Length;
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
