using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        /// <summary>
        /// 反序列化解析器。基于 span 的零拷贝游标解析：key 匹配不分配、数值零中间字符串、
        /// 未知字段跳过（前向/后向兼容）、深度守卫（防栈溢出）、严格闭合校验（截断即抛错，绝不静默丢数据）。
        /// </summary>
        /// <remarks>
        /// <para><b>兼容性</b>：接受新旧两种字典格式（标准对象格式与 legacy [{"key":..,"value":..}] 数组），
        /// 接受带引号的历史数值（"1.5"）与 NaN/Infinity 字面量；未知字段默认忽略；数值解析固定
        /// <see cref="CultureInfo.InvariantCulture"/>（历史区域损坏数据将显式失败而非静默错读）。</para>
        /// <para><b>安全</b>：所有读取以剩余长度为界，遇到非预期结尾抛出带偏移/行列位置的
        /// <see cref="GameException"/>；嵌套深度超过 <see cref="maxDepth"/> 立即失败，防止深嵌套输入导致栈溢出。</para>
        /// </remarks>
        internal sealed class Reader
        {
            #region 变量 [VARIABLES]

            private readonly string _json;
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

            #region 构造函数 [CONSTRUCTOR]

            public Reader(string json, int maxDepth)
            {
                _json = json;
                _maxDepth = maxDepth;
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

                // null 字面量（引用类型/可空类型 → null）
                if (Peek() == 'n' && MatchLiteral("null"))
                {
                    if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    {
                        return null;
                    }

                    Throw(StringUtility.Format("Cannot assign null to value type '{0}'.", targetType.Name));
                }

                // 覆盖模式：向现有实例写入
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
                char c = Peek();

                // null 字面量
                if (c == 'n')
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

                // Nullable<T> 统一按 T 解析（装箱后赋值语义一致）
                type = Nullable.GetUnderlyingType(type) ?? type;

                switch (c)
                {
                    case '"':
                        return ParseStringValue(type);

                    case '{':
                        // 深度超限：软跳过（与序列化侧软截断对称）——跳过该值，返回类型默认实例，可恢复历史超深数据
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

                        // 值类型结构体（Vector3/Quaternion/自定义 struct）同样按对象解析：
                        // Activator 创建装箱实例 → 反射写字段 → 调用方 (T) 拆箱保留修改
                        return ParseObject(type, null);

                    case '[':
                        // 深度超限：软跳过（同上）
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

                    case 't':
                    case 'f':
                        return ParseBoolean(type);

                    case 'N':
                    case 'I':
                        return ParseNonFinite(type);

                    default:
                        if (c == '-' && MatchLiteral("-Infinity"))
                        {
                            return NonFiniteResult(type, double.NegativeInfinity);
                        }

                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber(type);
                        }

                        Throw(StringUtility.Format("Unexpected character '{0}'.", c));
                        return null;
                }
            }

            /// <summary>带引号的字符串值：按目标类型转换（string/char/枚举/数值/Guid/DateTime 等，兼容历史带引号数值）。</summary>
            private object ParseStringValue(Type type)
            {
                string s = ReadStringValue();
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

            /// <summary>NaN/Infinity 字面量（与 Newtonsoft 兼容的非标准扩展，仅浮点目标）。</summary>
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

            /// <summary>读取带引号字符串的原始 span（不含引号），跳过转义对。</summary>
            private ReadOnlySpan<char> ReadStringSpan(out bool hasEscape)
            {
                hasEscape = false;
                if (_pos >= _json.Length || _json[_pos] != '"')
                {
                    Throw(StringUtility.Format("Expected a string but found '{0}'.",
                        _pos < _json.Length ? _json[_pos].ToString() : "<end>"));
                }

                int start = _pos + 1; // 跳过开引号
                int i = start;

                while (i < _json.Length)
                {
                    char c = _json[i];
                    if (c == '\\')
                    {
                        hasEscape = true;
                        i += 2; // 跳过转义字符对
                        continue;
                    }

                    if (c == '"')
                    {
                        _pos = i + 1;
                        return _json.AsSpan(start, i - start);
                    }

                    i++;
                }

                Throw("Unterminated string.");
                return default;
            }

            private string ReadStringValue()
            {
                bool hasEscape;
                ReadOnlySpan<char> span = ReadStringSpan(out hasEscape);
                return hasEscape ? Unescape(span) : span.ToString();
            }

            /// <summary>反转义（\uXXXX、标准转义对、代理对自然保留）。仅在含转义时分配。</summary>
            private string Unescape(ReadOnlySpan<char> input)
            {
                StringHandler.IStringBuilder sb = StringUtility.CreateStringBuilder(input.Length);
                try
                {
                    for (int i = 0; i < input.Length; i++)
                    {
                        char c = input[i];
                        if (c != '\\')
                        {
                            sb.Append(c);
                            continue;
                        }

                        i++;
                        if (i >= input.Length) Throw("Invalid trailing escape character.");

                        switch (input[i])
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

                                if (!uint.TryParse(input.Slice(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint code))
                                {
                                    Throw("Invalid \\u escape sequence.");
                                }

                                sb.Append((char)code);
                                i += 4;
                                break;
                            default:
                                Throw(StringUtility.Format("Unrecognized escape sequence '\\{0}'.", input[i]));
                                break;
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

                Expect('{');

                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        SkipWhitespace();
                        if (Peek() != '"')
                        {
                            Throw(StringUtility.Format("Expected an object key (string) but found '{0}'.", Peek()));
                        }

                        bool hasEscape;
                        ReadOnlySpan<char> keySpan = ReadStringSpan(out hasEscape);
                        string escapedKey = hasEscape ? Unescape(keySpan) : null;

                        SkipWhitespace();
                        Expect(':');

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
                                // 未知字段：跳过其值（存档前向/后向兼容）
                                SkipValue();
                            }
                        }

                        SkipWhitespace();
                        char c = Peek();
                        if (c == ',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == '}')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", c));
                    }
                }

                // 反序列化完成回调（元数据走缓存）
                foreach (MethodInfo info in meta.AfterDeserializeMethods)
                {
                    info.Invoke(instance, null);
                }

                return instance;
            }

            private static FieldInfo FindField(ReflectionCache.TypeMeta meta, ReadOnlySpan<char> keySpan, string escapedKey)
            {
                var fields = meta.DeserializeFields;
                for (int i = 0; i < fields.Length; i++)
                {
                    string[] names = fields[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (escapedKey != null ? names[j] == escapedKey : keySpan.SequenceEqual(names[j].AsSpan()))
                        {
                            return fields[i].Field;
                        }
                    }
                }

                return null;
            }

            private static PropertyInfo FindProperty(ReflectionCache.TypeMeta meta, ReadOnlySpan<char> keySpan, string escapedKey)
            {
                var properties = meta.DeserializeProperties;
                for (int i = 0; i < properties.Length; i++)
                {
                    string[] names = properties[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (escapedKey != null ? names[j] == escapedKey : keySpan.SequenceEqual(names[j].AsSpan()))
                        {
                            return properties[i].Property;
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

                list.Clear(); // 覆盖语义：清空后按 JSON 重建
                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
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
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == ']')
                    {
                        _pos++;
                        return;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                }
            }

            private object ParseArray(Type type)
            {
                Type elementType = type.GetElementType();
                var elements = new List<object>();

                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
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
                        char c = Peek();
                        if (c == ',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == ']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                    }
                }

                Array result = Array.CreateInstance(elementType, elements.Count);
                for (int i = 0; i < elements.Count; i++)
                {
                    result.SetValue(elements[i], i);
                }

                return result;
            }

            /// <summary>标准对象格式字典：{"key":value,...}。</summary>
            private object ParseDictionary(Type type, IDictionary existing)
            {
                IDictionary dict = existing ?? (IDictionary)Activator.CreateInstance(type);
                Type keyType = type.GenericTypeArguments[0];
                Type valueType = type.GenericTypeArguments[1];

                if (existing != null) dict.Clear(); // 覆盖语义：清空后按 JSON 重建

                Expect('{');

                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        Throw(StringUtility.Format("Expected a dictionary key (string) but found '{0}'.", Peek()));
                    }

                    string keyString = ReadStringValue();
                    object key = ConvertDictionaryKey(keyString, keyType);

                    SkipWhitespace();
                    Expect(':');

                    _depth++;
                    object value = ParseValue(valueType);
                    _depth--;

                    dict[key] = value;

                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == '}')
                    {
                        _pos++;
                        return dict;
                    }

                    Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", c));
                }
            }

            /// <summary>legacy 条目数组格式字典：[{"key":..,"value":..},...]（兼容历史存档）。</summary>
            private object ParseDictionaryLegacy(Type type)
            {
                IDictionary dict = (IDictionary)Activator.CreateInstance(type);
                Type keyType = type.GenericTypeArguments[0];
                Type valueType = type.GenericTypeArguments[1];

                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    Expect('{');

                    object key = null;
                    object value = null;
                    bool keyAssigned = false;
                    bool valueAssigned = false;

                    SkipWhitespace();
                    if (Peek() == '}')
                    {
                        _pos++;
                    }
                    else
                    {
                        while (true)
                        {
                            SkipWhitespace();
                            string member = ReadStringValue();

                            SkipWhitespace();
                            Expect(':');

                            if (member == TypeConverter.KeyMember)
                            {
                                if (keyAssigned) Throw("Duplicate key found.");
                                _depth++;
                                key = ParseValue(keyType);
                                _depth--;
                                keyAssigned = true;
                            }
                            else if (member == TypeConverter.ValueMember)
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
                            char c = Peek();
                            if (c == ',')
                            {
                                _pos++;
                                continue;
                            }

                            if (c == '}')
                            {
                                _pos++;
                                break;
                            }

                            Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", c));
                        }
                    }

                    if (!keyAssigned || !valueAssigned)
                    {
                        Throw("Dictionary entry requires both 'key' and 'value'.");
                    }

                    dict[key] = value;

                    SkipWhitespace();
                    char cc = Peek();
                    if (cc == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (cc == ']')
                    {
                        _pos++;
                        return dict;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", cc));
                }
            }

            #endregion

            #region 类型转换 [CONVERSIONS]

            /// <summary>字符串 → 目标类型。非数值类型委托 <see cref="TypeConverter"/>；数值类型走 span 解析。</summary>
            private object ConvertFromString(string s, Type type)
            {
                object result = TypeConverter.ConvertFromString(s, type);
                return result ?? ParseNumberSpan(type, s.AsSpan());
            }

            private object ConvertDictionaryKey(string s, Type keyType)
            {
                object result = TypeConverter.ConvertDictionaryKey(s, keyType);
                return result ?? ParseNumberSpan(keyType, s.AsSpan());
            }

            #endregion

            #region 数值 [NUMBERS]

            private object ParseNumber(Type type)
            {
                int start = _pos;
                while (_pos < _json.Length)
                {
                    char c = _json[_pos];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }

                ReadOnlySpan<char> span = _json.AsSpan(start, _pos - start);
                return ParseNumberSpan(type, span);
            }

            /// <summary>span 数值解析：枚举/整数按目标类型直取（零中间字符串），失败回退 double（科学计数法）。</summary>
            private object ParseNumberSpan(Type type, ReadOnlySpan<char> s)
            {
                if (s.IsEmpty)
                {
                    Throw("Empty numeric token.");
                }

                // 数值枚举
                if (type.IsEnum)
                {
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long enumRaw))
                    {
                        return Enum.ToObject(type, enumRaw);
                    }

                    Throw(StringUtility.Format("'{0}' is not a valid numeric value for enum '{1}'.", s.ToString(), type.Name));
                }

                // 整数类型：直取；失败回退 double 且必须整值
                bool isFloatTarget = type == typeof(float) || type == typeof(double) || type == typeof(decimal);
                if (!isFloatTarget && type != typeof(bool) && type != typeof(string))
                {
                    object direct = TryParseIntegral(type, s);
                    if (direct != null) return direct;

                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double integral) &&
                        !double.IsNaN(integral) && !double.IsInfinity(integral) && integral == Math.Floor(integral))
                    {
                        try
                        {
                            return Convert.ChangeType(integral, type, CultureInfo.InvariantCulture);
                        }
                        catch (Exception)
                        {
                            Throw(StringUtility.Format("'{0}' is out of range for '{1}'.", s.ToString(), type.Name));
                        }
                    }

                    if (type == typeof(char))
                    {
                        Throw(StringUtility.Format("'{0}' is not a valid char value.", s.ToString()));
                    }

                    Throw(StringUtility.Format("'{0}' is not a valid integer for '{1}'.", s.ToString(), type.Name));
                }

                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Single:
                        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
                        break;
                    case TypeCode.Double:
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                        break;
                    case TypeCode.Decimal:
                        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal m)) return m;
                        break;
                    case TypeCode.Boolean:
                        // 数值 → bool（0=false，非 0=true；兼容历史存档）
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)) return Math.Abs(b) > 0d;
                        break;
                    case TypeCode.String:
                        return s.ToString();
                    default:
                        if (type == typeof(string) || type == typeof(object)) return s.ToString();
                        break;
                }

                Throw(StringUtility.Format("'{0}' is not a valid value for '{1}'.", s.ToString(), type.Name));
                return null;
            }

            private static object TryParseIntegral(Type type, ReadOnlySpan<char> s)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.SByte:
                        return sbyte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sb) ? (object)sb : null;
                    case TypeCode.Byte:
                        return byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b) ? (object)b : null;
                    case TypeCode.Int16:
                        return short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out short sh) ? (object)sh : null;
                    case TypeCode.UInt16:
                        return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort ush) ? (object)ush : null;
                    case TypeCode.Int32:
                        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? (object)i : null;
                    case TypeCode.UInt32:
                        return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ui) ? (object)ui : null;
                    case TypeCode.Int64:
                        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? (object)l : null;
                    case TypeCode.UInt64:
                        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ul) ? (object)ul : null;
                    default:
                        return null;
                }
            }

            #endregion

            #region 词法工具 [LEXER UTILITIES]

            private void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    char c = _json[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private char Peek()
            {
                if (_pos >= _json.Length)
                {
                    Throw("Unexpected end of JSON input.");
                }

                return _json[_pos];
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                char c = Peek();
                if (c != expected)
                {
                    Throw(StringUtility.Format("Expected '{0}' but found '{1}'.", expected, c));
                }

                _pos++;
            }

            private bool MatchLiteral(string literal)
            {
                if (_json.Length - _pos < literal.Length) return false;
                if (string.CompareOrdinal(_json, _pos, literal, 0, literal.Length) != 0) return false;
                // 词边界：后续字符必须是分隔符或 EOF（防止 "trueX" 匹配 "true"）
                int next = _pos + literal.Length;
                if (next < _json.Length && !IsDelimiter(_json[next])) return false;
                _pos = next;
                return true;
            }

            private string ReadLiteralToken()
            {
                int start = _pos;
                while (_pos < _json.Length && !IsDelimiter(_json[_pos])) _pos++;
                return _json.Substring(start, _pos - start);
            }

            private static bool IsDelimiter(char c)
            {
                return c == ',' || c == '}' || c == ']' || c == ' ' || c == '\t' || c == '\r' || c == '\n';
            }

            /// <summary>跳过任意未知值（字面量/字符串/对象/数组），用于未知字段的前向兼容。</summary>
            private void SkipValue()
            {
                SkipWhitespace();
                char c = Peek();

                if (c == '"')
                {
                    bool hasEscape;
                    ReadStringSpan(out hasEscape);
                    return;
                }

                if (c == '{' || c == '[')
                {
                    int depth = 0;
                    bool inString = false;
                    while (_pos < _json.Length)
                    {
                        char x = _json[_pos];
                        if (inString)
                        {
                            if (x == '\\') _pos++;
                            else if (x == '"') inString = false;
                        }
                        else
                        {
                            if (x == '"') inString = true;
                            else if (x == '{' || x == '[') depth++;
                            else if (x == '}' || x == ']')
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

                // 裸字面量（数值/true/false/null/NaN...）
                while (_pos < _json.Length && !IsDelimiter(_json[_pos])) _pos++;
            }

            #endregion

            #region 错误 [ERRORS]

            /// <summary>带偏移/行列/上下文片段的错误（仅抛错时计算，热路径零开销）。</summary>
            [System.Diagnostics.CodeAnalysis.DoesNotReturn]
            private void Throw(string message)
            {
                int line = 1, col = 1;
                int limit = Math.Min(_pos, _json.Length);
                for (int i = 0; i < limit; i++)
                {
                    if (_json[i] == '\n')
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
                string snippet = snippetLen > 0 ? _json.Substring(snippetStart, snippetLen) : string.Empty;

                throw new GameException(StringUtility.Format(
                    "{0} At offset {1} (line {2}, column {3}) near \"{4}\".",
                    message, _pos, line, col, snippet));
            }

            #endregion
        }
    }
}
