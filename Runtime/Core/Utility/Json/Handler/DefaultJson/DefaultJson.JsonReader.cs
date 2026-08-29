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
        /// 统一反序列化解析器（string / UTF8 字节双路径的单一结构实现）。
        /// </summary>
        /// <remarks>
        /// <para><b>单一来源</b>：值分派、容器/对象/字典（标准与 legacy 格式）解析、深度守卫、
        /// 未知字段跳过、null 字面量、覆盖模式、类型化数组/列表快路径——全部只实现一次；
        /// token 的编码差异（char / UTF8）下沉到 <see cref="IJsonLexer"/> 的两个实现。</para>
        /// <para><b>类型化集合注册表</b>：<see cref="LexerTokens{TLexer}"/> 按（Lexer 类型 × 元素类型）
        /// 静态化 token 读取委托——两个 Lexer 各自注册，数组/列表循环逻辑单一来源。</para>
        /// <para><b>兼容性</b>：接受标准与 legacy 字典格式、带引号历史数值、NaN/Infinity 字面量、BOM 头；
        /// 未知字段默认忽略；数值解析固定 InvariantCulture。</para>
        /// <para><b>安全</b>：闭合括号循环（截断即抛错）、深度守卫（容器递归软跳过）、
        /// 错误信息带偏移/行列/上下文片段。</para>
        /// </remarks>
        internal static class JsonReader<TLexer> where TLexer : class, IJsonLexer
        {
            #region 公共入口 [PUBLIC ENTRY]

            /// <summary>解析根值。existing 非空时向其覆盖（FromJsonOverwrite 语义：集合清空复用）。</summary>
            public static object Parse(TLexer lexer, Type targetType, object existing)
            {
                lexer.SkipWhitespace();

                // null 字面量（引用类型/可空类型 → null）
                if (lexer.Peek() == 'n' && lexer.MatchLiteral("null"))
                {
                    if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    {
                        return null;
                    }

                    lexer.Throw(StringUtility.Format("Cannot assign null to value type '{0}'.", targetType.Name));
                }

                // 覆盖模式：向现有实例写入
                if (existing != null)
                {
                    if (existing is IDictionary existingDict &&
                        targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        // 按实际 token 分发：标准对象格式 or legacy 条目数组格式（历史存档兼容）
                        lexer.SkipWhitespace();
                        int dictToken = lexer.Peek();
                        if (dictToken == '{')
                        {
                            ParseDictionary(lexer, targetType, existingDict, 0);
                        }
                        else if (dictToken == '[')
                        {
                            ParseDictionaryLegacy(lexer, targetType, existingDict, 0);
                        }
                        else
                        {
                            lexer.Throw(StringUtility.Format("Expected '{{' or '[' for dictionary but found '{0}'.", (char)dictToken));
                        }

                        return existing;
                    }

                    if (existing is IList existingList &&
                        targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        ParseList(lexer, targetType, existingList, 0);
                        return existing;
                    }

                    if (!targetType.IsArray && existing is not IDictionary && existing is not IList)
                    {
                        return ParseObject(lexer, targetType, existing, 0);
                    }
                }

                return ParseValue(lexer, targetType, 0);
            }

            #endregion

            #region 值分派 [VALUE DISPATCH]

            private static object ParseValue(TLexer lexer, Type type, int depth)
            {
                lexer.SkipWhitespace();
                int c = lexer.Peek();

                // null 字面量
                if (c == 'n')
                {
                    if (lexer.MatchLiteral("null"))
                    {
                        if (!type.IsValueType || Nullable.GetUnderlyingType(type) != null)
                        {
                            return null;
                        }

                        lexer.Throw(StringUtility.Format("Cannot assign null to value type '{0}'.", type.Name));
                    }

                    lexer.Throw(StringUtility.Format("Unexpected token '{0}'.", ReadLiteralToken(lexer)));
                }

                // Nullable<T> 统一按 T 解析（装箱后赋值语义一致）
                type = Nullable.GetUnderlyingType(type) ?? type;

                switch (c)
                {
                    case '"':
                        return ParseStringValue(lexer, type);

                    case '{':
                        // 深度超限：软跳过（与序列化侧软截断对称）——跳过该值，返回类型默认实例
                        if (depth >= lexer.MaxDepth)
                        {
                            lexer.SkipValue();
                            return type.IsValueType ? Activator.CreateInstance(type) : null;
                        }

                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            return ParseDictionary(lexer, type, null, depth);
                        }

                        // 值类型结构体（Vector3/Quaternion/自定义 struct）同样按对象解析：
                        // Activator 创建装箱实例 → 反射写字段 → 调用方 (T) 拆箱保留修改
                        return ParseObject(lexer, type, null, depth);

                    case '[':
                        if (depth >= lexer.MaxDepth)
                        {
                            lexer.SkipValue();
                            return type.IsValueType ? Activator.CreateInstance(type) : null;
                        }

                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            return ParseDictionaryLegacy(lexer, type, null, depth);
                        }

                        if (type.IsArray)
                        {
                            return ParseArray(lexer, type, depth);
                        }

                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            IList list = (IList)Activator.CreateInstance(type);
                            ParseList(lexer, type, list, depth);
                            return list;
                        }

                        lexer.Throw(StringUtility.Format("Cannot parse a JSON array into '{0}'.", type.Name));
                        return null;

                    case 't':
                    case 'f':
                        return ParseBoolean(lexer, type);

                    case 'N':
                    case 'I':
                        return ParseNonFinite(lexer, type);

                    default:
                        if (c == '-' && lexer.MatchLiteral("-Infinity"))
                        {
                            return NonFiniteResult(lexer, type, double.NegativeInfinity);
                        }

                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return lexer.ParseNumber(type);
                        }

                        lexer.Throw(StringUtility.Format("Unexpected character '{0}'.", (char)c));
                        return null;
                }
            }

            private static object ParseStringValue(TLexer lexer, Type type)
            {
                string s = lexer.ReadString();
                return lexer.ConvertString(s, type);
            }

            private static object ParseBoolean(TLexer lexer, Type type)
            {
                bool value;
                if (lexer.MatchLiteral("true")) value = true;
                else if (lexer.MatchLiteral("false")) value = false;
                else
                {
                    lexer.Throw("Invalid boolean literal.");
                    return null;
                }

                if (type == typeof(bool)) return value;
                if (type == typeof(string)) return value ? "true" : "false";

                lexer.Throw(StringUtility.Format("Cannot parse a boolean into '{0}'.", type.Name));
                return null;
            }

            /// <summary>NaN/Infinity 字面量（与 Newtonsoft 兼容的非标准扩展，仅浮点目标）。</summary>
            private static object ParseNonFinite(TLexer lexer, Type type)
            {
                double value;
                if (lexer.MatchLiteral("NaN")) value = double.NaN;
                else if (lexer.MatchLiteral("Infinity")) value = double.PositiveInfinity;
                else if (lexer.MatchLiteral("-Infinity")) value = double.NegativeInfinity;
                else
                {
                    lexer.Throw(StringUtility.Format("Unexpected token '{0}'.", ReadLiteralToken(lexer)));
                    return null;
                }

                return NonFiniteResult(lexer, type, value);
            }

            private static object NonFiniteResult(TLexer lexer, Type type, double value)
            {
                if (type == typeof(double)) return value;
                if (type == typeof(float)) return (float)value;

                lexer.Throw(StringUtility.Format("Non-finite value is only valid for float/double, not '{0}'.", type.Name));
                return null;
            }

            private static string ReadLiteralToken(TLexer lexer)
            {
                // 读取到下一个分隔符的裸字面量（仅错误信息用）
                lexer.SkipWhitespace();
                int start = lexer.Peek();
                return ((char)start).ToString();
            }

            #endregion

            #region 对象 [OBJECTS]

            private static object ParseObject(TLexer lexer, Type type, object instance, int depth)
            {
                if (instance == null)
                {
                    instance = Activator.CreateInstance(type);
                    if (instance == null)
                    {
                        lexer.Throw(StringUtility.Format("Cannot create an instance of '{0}'.", type.Name));
                    }
                }

                var meta = ReflectionCache.Get(type);

                lexer.Expect('{');

                lexer.SkipWhitespace();
                if (lexer.Peek() == '}')
                {
                    Consume(lexer);
                }
                else
                {
                    while (true)
                    {
                        lexer.SkipWhitespace();
                        if (lexer.Peek() != '"')
                        {
                            lexer.Throw(StringUtility.Format("Expected an object key (string) but found '{0}'.", (char)lexer.Peek()));
                        }

                        MemberMatch member = lexer.MatchMember(meta);

                        lexer.SkipWhitespace();
                        lexer.Expect(':');

                        if (member.Field != null)
                        {
                            object value = ParseValue(lexer, member.Field.FieldType, depth + 1);
                            member.Field.SetValue(instance, value);
                        }
                        else if (member.Property != null)
                        {
                            object value = ParseValue(lexer, member.Property.PropertyType, depth + 1);
                            member.Property.SetValue(instance, value);
                        }
                        else
                        {
                            lexer.SkipValue(); // 未知字段：跳过其值（存档前向/后向兼容）
                        }

                        lexer.SkipWhitespace();
                        int c = lexer.Peek();
                        if (c == ',')
                        {
                            Consume(lexer);
                            continue;
                        }

                        if (c == '}')
                        {
                            Consume(lexer);
                            break;
                        }

                        lexer.Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                    }
                }

                // 反序列化完成回调（元数据走缓存；基类在前、派生类在后）
                foreach (MethodInfo info in meta.AfterDeserializeMethods)
                {
                    info.Invoke(instance, null);
                }

                return instance;
            }

            #endregion

            #region 集合 [COLLECTIONS]

            private static void ParseList(TLexer lexer, Type type, IList list, int depth)
            {
                list.Clear(); // 覆盖语义：清空后按 JSON 重建

                // 高频容器专用热循环：Lexer 具体类型直调（Mono 泛型委托分发在紧密循环中退化 ~4× 的补偿）
                if (lexer is ITypedArrayParser fastParser)
                {
                    if (list is List<int> l32) { fastParser.ParseInt32ListFast(l32); return; }
                    if (list is List<float> lf) { fastParser.ParseSingleListFast(lf); return; }
                    if (list is List<double> ld) { fastParser.ParseDoubleListFast(ld); return; }
                    if (list is List<long> l64) { fastParser.ParseInt64ListFast(l64); return; }
                    if (list is List<string> lst) { fastParser.ParseStringListFast(lst); return; }
                }

                if (list is List<int> l32g) { ParseTypedList(lexer, l32g, LexerTokens<TLexer>.Int32); return; }
                if (list is List<long> l64g) { ParseTypedList(lexer, l64g, LexerTokens<TLexer>.Int64); return; }
                if (list is List<float> lfg) { ParseTypedList(lexer, lfg, LexerTokens<TLexer>.Single); return; }
                if (list is List<double> ldg) { ParseTypedList(lexer, ldg, LexerTokens<TLexer>.Double); return; }
                if (list is List<bool> lb) { ParseTypedList(lexer, lb, LexerTokens<TLexer>.Boolean); return; }
                if (list is List<sbyte> lsb) { ParseTypedList(lexer, lsb, LexerTokens<TLexer>.SByte); return; }
                if (list is List<byte> lby) { ParseTypedList(lexer, lby, LexerTokens<TLexer>.Byte); return; }
                if (list is List<short> ls) { ParseTypedList(lexer, ls, LexerTokens<TLexer>.Int16); return; }
                if (list is List<ushort> lus) { ParseTypedList(lexer, lus, LexerTokens<TLexer>.UInt16); return; }
                if (list is List<uint> lui) { ParseTypedList(lexer, lui, LexerTokens<TLexer>.UInt32); return; }
                if (list is List<ulong> lul) { ParseTypedList(lexer, lul, LexerTokens<TLexer>.UInt64); return; }
                if (list is List<decimal> lde) { ParseTypedList(lexer, lde, LexerTokens<TLexer>.Decimal); return; }
                if (list is List<string> lstg) { ParseStringTypedList(lexer, lstg); return; }

                Type itemType = GenericArgsCache.Get(type)[0];

                lexer.Expect('[');

                lexer.SkipWhitespace();
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                    return;
                }

                while (true)
                {
                    object value = ParseValue(lexer, itemType, depth + 1);
                    list.Add(value);

                    lexer.SkipWhitespace();
                    int c = lexer.Peek();
                    if (c == ',')
                    {
                        Consume(lexer);
                        continue;
                    }

                    if (c == ']')
                    {
                        Consume(lexer);
                        return;
                    }

                    lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>类型化基元列表解析骨架（基元元素不可嵌套，无需深度计数）。</summary>
            private static void ParseTypedList<T>(TLexer lexer, List<T> list, Func<TLexer, T> read) where T : struct
            {
                lexer.Expect('[');

                lexer.SkipWhitespace();
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                    return;
                }

                while (true)
                {
                    list.Add(read(lexer));

                    lexer.SkipWhitespace();
                    int c = lexer.Peek();
                    if (c == ',')
                    {
                        Consume(lexer);
                        continue;
                    }

                    if (c == ']')
                    {
                        Consume(lexer);
                        return;
                    }

                    lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>字符串列表快路径（高频场景；含 null 字面量）。</summary>
            private static void ParseStringTypedList(TLexer lexer, List<string> list)
            {
                lexer.Expect('[');

                lexer.SkipWhitespace();
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                    return;
                }

                while (true)
                {
                    lexer.SkipWhitespace();
                    list.Add(lexer.Peek() == 'n' && lexer.MatchLiteral("null") ? null : lexer.ReadString());

                    lexer.SkipWhitespace();
                    int c = lexer.Peek();
                    if (c == ',')
                    {
                        Consume(lexer);
                        continue;
                    }

                    if (c == ']')
                    {
                        Consume(lexer);
                        return;
                    }

                    lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            private static object ParseArray(TLexer lexer, Type type, int depth)
            {
                Type elementType = type.GetElementType();

                // 类型化零装箱快速路径（string 数组 + 值类型基元数组）
                if (elementType == typeof(string))
                {
                    return ParseStringArray(lexer);
                }

                // 枚举数组：必须置于 TypeCode switch 之前——Type.GetTypeCode(enumType) 返回底层类型码
                // （如 Int32），会被误路由进 int 快路径返回 int[]（元素类型错误）。
                if (elementType.IsEnum)
                {
                    return ParseEnumArray(lexer, elementType, depth);
                }

                // 高频容器专用热循环：Lexer 具体类型直调（Mono 接口/委托分发在紧密循环中退化 ~4× 的补偿）
                if (lexer is ITypedArrayParser fast)
                {
                    if (elementType == typeof(int)) return fast.ParseInt32ArrayFast();
                    if (elementType == typeof(float)) return fast.ParseSingleArrayFast();
                    if (elementType == typeof(double)) return fast.ParseDoubleArrayFast();
                    if (elementType == typeof(long)) return fast.ParseInt64ArrayFast();
                    if (elementType == typeof(string)) return fast.ParseStringArrayFast();
                }

                switch (Type.GetTypeCode(elementType))
                {
                    case TypeCode.Boolean: return ParsePrimitiveArray<bool>(lexer, LexerTokens<TLexer>.Boolean);
                    case TypeCode.SByte: return ParsePrimitiveArray<sbyte>(lexer, LexerTokens<TLexer>.SByte);
                    case TypeCode.Byte: return ParsePrimitiveArray<byte>(lexer, LexerTokens<TLexer>.Byte);
                    case TypeCode.Int16: return ParsePrimitiveArray<short>(lexer, LexerTokens<TLexer>.Int16);
                    case TypeCode.UInt16: return ParsePrimitiveArray<ushort>(lexer, LexerTokens<TLexer>.UInt16);
                    case TypeCode.Int32: return ParsePrimitiveArray<int>(lexer, LexerTokens<TLexer>.Int32);
                    case TypeCode.UInt32: return ParsePrimitiveArray<uint>(lexer, LexerTokens<TLexer>.UInt32);
                    case TypeCode.Int64: return ParsePrimitiveArray<long>(lexer, LexerTokens<TLexer>.Int64);
                    case TypeCode.UInt64: return ParsePrimitiveArray<ulong>(lexer, LexerTokens<TLexer>.UInt64);
                    case TypeCode.Single: return ParsePrimitiveArray<float>(lexer, LexerTokens<TLexer>.Single);
                    case TypeCode.Double: return ParsePrimitiveArray<double>(lexer, LexerTokens<TLexer>.Double);
                    case TypeCode.Decimal: return ParsePrimitiveArray<decimal>(lexer, LexerTokens<TLexer>.Decimal);
                }

                // 回退路径（char/对象元素）
                var elements = new List<object>();

                lexer.Expect('[');

                lexer.SkipWhitespace();
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                }
                else
                {
                    while (true)
                    {
                        object value = ParseValue(lexer, elementType, depth + 1);

                        if (!elementType.IsInstanceOfType(value) && value != null)
                        {
                            try
                            {
                                value = Convert.ChangeType(value, elementType, CultureInfo.InvariantCulture);
                            }
                            catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException)
                            {
                                lexer.Throw(StringUtility.Format("Cannot convert '{0}' to element type '{1}'.", value, elementType.Name));
                            }
                        }

                        elements.Add(value);

                        lexer.SkipWhitespace();
                        int c = lexer.Peek();
                        if (c == ',')
                        {
                            Consume(lexer);
                            continue;
                        }

                        if (c == ']')
                        {
                            Consume(lexer);
                            break;
                        }

                        lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                Array result = Array.CreateInstance(elementType, elements.Count);
                for (int i = 0; i < elements.Count; i++)
                {
                    result.SetValue(elements[i], i);
                }

                return result;
            }

            /// <summary>基元数组零装箱解析（经类型化注册表按元素类型分派）。</summary>
            private static T[] ParsePrimitiveArray<T>(TLexer lexer, Func<TLexer, T> read) where T : struct
            {
                lexer.Expect('[');

                lexer.SkipWhitespace();
                var tmp = new List<T>(16);
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(read(lexer));

                        lexer.SkipWhitespace();
                        int c = lexer.Peek();
                        if (c == ',')
                        {
                            Consume(lexer);
                            continue;
                        }

                        if (c == ']')
                        {
                            Consume(lexer);
                            break;
                        }

                        lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            private static string[] ParseStringArray(TLexer lexer)
            {
                lexer.Expect('[');

                lexer.SkipWhitespace();
                var tmp = new List<string>(16);
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                }
                else
                {
                    while (true)
                    {
                        lexer.SkipWhitespace();
                        tmp.Add(lexer.Peek() == 'n' && lexer.MatchLiteral("null") ? null : lexer.ReadString());

                        lexer.SkipWhitespace();
                        int c = lexer.Peek();
                        if (c == ',')
                        {
                            Consume(lexer);
                            continue;
                        }

                        if (c == ']')
                        {
                            Consume(lexer);
                            break;
                        }

                        lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>枚举数组解析：名称字符串走通用 ParseValue（返回值已是枚举实例）；
            /// 数值按底层类型快读后经 Enum.ToObject 装箱（IsInstanceOfType 防双重包装）。</summary>
            private static Array ParseEnumArray(TLexer lexer, Type elementType, int depth)
            {
                Type underlying = Enum.GetUnderlyingType(elementType);

                lexer.Expect('[');

                lexer.SkipWhitespace();
                var tmp = new List<object>(16);
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                }
                else
                {
                    while (true)
                    {
                        lexer.SkipWhitespace();
                        object raw = lexer.Peek() == '"'
                            ? ParseValue(lexer, elementType, depth + 1)
                            : lexer.ParseNumber(underlying);
                        tmp.Add(elementType.IsInstanceOfType(raw) ? raw : Enum.ToObject(elementType, raw));

                        lexer.SkipWhitespace();
                        int c = lexer.Peek();
                        if (c == ',')
                        {
                            Consume(lexer);
                            continue;
                        }

                        if (c == ']')
                        {
                            Consume(lexer);
                            break;
                        }

                        lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                Array result = Array.CreateInstance(elementType, tmp.Count);
                for (int i = 0; i < tmp.Count; i++)
                {
                    result.SetValue(tmp[i], i);
                }

                return result;
            }

            /// <summary>标准对象格式字典：{"key":value,...}。</summary>
            private static object ParseDictionary(TLexer lexer, Type type, IDictionary existing, int depth)
            {
                IDictionary dict = existing ?? (IDictionary)Activator.CreateInstance(type);
                Type[] args = GenericArgsCache.Get(type);
                Type keyType = args[0];
                Type valueType = args[1];

                if (existing != null) dict.Clear(); // 覆盖语义：清空后按 JSON 重建

                lexer.Expect('{');

                lexer.SkipWhitespace();
                if (lexer.Peek() == '}')
                {
                    Consume(lexer);
                    return dict;
                }

                while (true)
                {
                    lexer.SkipWhitespace();
                    if (lexer.Peek() != '"')
                    {
                        lexer.Throw(StringUtility.Format("Expected a dictionary key (string) but found '{0}'.", (char)lexer.Peek()));
                    }

                    string keyString = lexer.ReadString();
                    object key = ConvertDictionaryKey(lexer, keyString, keyType);

                    lexer.SkipWhitespace();
                    lexer.Expect(':');

                    object value = ParseValue(lexer, valueType, depth + 1);
                    dict[key] = value;

                    lexer.SkipWhitespace();
                    int c = lexer.Peek();
                    if (c == ',')
                    {
                        Consume(lexer);
                        continue;
                    }

                    if (c == '}')
                    {
                        Consume(lexer);
                        return dict;
                    }

                    lexer.Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                }
            }

            /// <summary>legacy 条目数组格式字典：[{"key":..,"value":..},...]（兼容历史存档）。</summary>
            private static object ParseDictionaryLegacy(TLexer lexer, Type type, IDictionary existing, int depth)
            {
                IDictionary dict = existing ?? (IDictionary)Activator.CreateInstance(type);
                Type[] args = GenericArgsCache.Get(type);
                Type keyType = args[0];
                Type valueType = args[1];

                if (existing != null) dict.Clear(); // 覆盖语义：清空后按 JSON 重建

                lexer.Expect('[');

                lexer.SkipWhitespace();
                if (lexer.Peek() == ']')
                {
                    Consume(lexer);
                    return dict;
                }

                while (true)
                {
                    lexer.Expect('{');

                    object key = null;
                    object value = null;
                    bool keyAssigned = false;
                    bool valueAssigned = false;

                    lexer.SkipWhitespace();
                    if (lexer.Peek() == '}')
                    {
                        Consume(lexer);
                    }
                    else
                    {
                        while (true)
                        {
                            lexer.SkipWhitespace();
                            string member = lexer.ReadString();

                            lexer.SkipWhitespace();
                            lexer.Expect(':');

                            if (member == TypeConverter.KeyMember)
                            {
                                if (keyAssigned) lexer.Throw("Duplicate key found.");
                                key = ParseValue(lexer, keyType, depth + 1);
                                keyAssigned = true;
                            }
                            else if (member == TypeConverter.ValueMember)
                            {
                                if (valueAssigned) lexer.Throw("Duplicate value found.");
                                value = ParseValue(lexer, valueType, depth + 1);
                                valueAssigned = true;
                            }
                            else
                            {
                                lexer.Throw(StringUtility.Format("Invalid dictionary entry member '{0}'.", member));
                            }

                            lexer.SkipWhitespace();
                            int c = lexer.Peek();
                            if (c == ',')
                            {
                                Consume(lexer);
                                continue;
                            }

                            if (c == '}')
                            {
                                Consume(lexer);
                                break;
                            }

                            lexer.Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                        }
                    }

                    if (!keyAssigned || !valueAssigned)
                    {
                        lexer.Throw("Dictionary entry requires both 'key' and 'value'.");
                    }

                    dict[key] = value;

                    lexer.SkipWhitespace();
                    int cc = lexer.Peek();
                    if (cc == ',')
                    {
                        Consume(lexer);
                        continue;
                    }

                    if (cc == ']')
                    {
                        Consume(lexer);
                        return dict;
                    }

                    lexer.Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)cc));
                }
            }

            /// <summary>字符串 → 字典 key 类型（string/char/bool/枚举/Guid；数值走 lexer 的 span 解析）。</summary>
            private static object ConvertDictionaryKey(TLexer lexer, string s, Type keyType)
            {
                return TypeConverter.TryConvertDictionaryKey(s, keyType, out object result)
                    ? result
                    : ParseNumericDictionaryKey(lexer, s, keyType);
            }

            /// <summary>数值 key：经 lexer 的字符串转换（含带引号数值桥接）。</summary>
            private static object ParseNumericDictionaryKey(TLexer lexer, string s, Type keyType)
            {
                return lexer.ConvertString(s, keyType);
            }

            #endregion

            #region 词法辅助 [LEXER HELPERS]

            /// <summary>消费当前 token（Peek 已确认的单字符；单次推进，无二次 SkipWhitespace）。</summary>
            private static void Consume(TLexer lexer)
            {
                lexer.Consume();
            }

            #endregion
        }

        /// <summary>
        /// 类型化 token 读取注册表：按（Lexer 类型 × 元素类型）静态化委托，
        /// 消除值类型数组/列表解析的逐元素装箱。两个 Lexer 在各自静态构造中注册。
        /// </summary>
        internal static class LexerTokens<TLexer> where TLexer : class, IJsonLexer
        {
            public static Func<TLexer, bool> Boolean;
            public static Func<TLexer, sbyte> SByte;
            public static Func<TLexer, byte> Byte;
            public static Func<TLexer, short> Int16;
            public static Func<TLexer, ushort> UInt16;
            public static Func<TLexer, int> Int32;
            public static Func<TLexer, uint> UInt32;
            public static Func<TLexer, long> Int64;
            public static Func<TLexer, ulong> UInt64;
            public static Func<TLexer, float> Single;
            public static Func<TLexer, double> Double;
            public static Func<TLexer, decimal> Decimal;
        }

        // CharLexer 的类型化 token 读取注册（供 JsonReader<CharLexer> 的数组/列表快路径）
    }
}
