using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        /// <summary>
        /// 成员匹配结果（对象键 → 字段/属性）。
        /// </summary>
        internal readonly struct MemberMatch
        {
            public readonly FieldInfo Field;
            public readonly PropertyInfo Property;

            public MemberMatch(FieldInfo field, PropertyInfo property)
            {
                Field = field;
                Property = property;
            }
        }

        /// <summary>
        /// 词法原语接口：token 读取的编码差异（char / UTF8 字节、字符串物化、数值解析、键匹配）
        /// 由各 Lexer 实现；解析结构逻辑（分派/容器/对象/深度守卫）统一在 <see cref="JsonReader{TLexer}"/>。
        /// </summary>
        /// <remarks>
        /// <para><b>调用粒度契约</b>：接口按"每 token"分发（每个键/值/字面量一次调用），
        /// 而非每字符——接口开销由 token 内部工作量摊薄。</para>
        /// <para><b>Peek 契约</b>：返回当前字符/字节为 int（结构字符均为 ASCII，两路径统一可比）；
        /// EOF 时抛带位置信息的 <see cref="GameException"/>。</para>
        /// <para><b>Throw 契约</b>：各实现基于自身缓冲计算偏移/行列/上下文片段。</para>
        /// </remarks>
        internal interface IJsonLexer
        {
            /// <summary>配置的最大解析深度。</summary>
            int MaxDepth { get; }

            /// <summary>跳过空白（含 UTF8 BOM 后的常规空白）。</summary>
            void SkipWhitespace();

            /// <summary>预览当前字符/字节（ASCII 结构统一为 int）；EOF 抛错。</summary>
            int Peek();

            /// <summary>断言当前 token 为指定 ASCII 字符（先跳空白），消费之。</summary>
            void Expect(int expected);

            /// <summary>匹配字面量（含词边界校验：后续必须是分隔符或 EOF）。</summary>
            bool MatchLiteral(string literal);

            /// <summary>读取带引号字符串（物化 + 反转义）。调用方保证下一个 token 是 '"'。</summary>
            string ReadString();

            /// <summary>解析数值字面量为目标类型（各实现用最优 span 解析）。</summary>
            object ParseNumber(Type type);

            /// <summary>字符串值 → 目标类型（含带引号历史数值的桥接解析）。</summary>
            object ConvertString(string s, Type type);

            /// <summary>读取对象键并匹配到字段/属性（各实现用最优键匹配：char span / UTF8 字节表）。</summary>
            MemberMatch MatchMember(ReflectionCache.TypeMeta meta);

            /// <summary>跳过任意未知值（字面量/字符串/对象/数组）。</summary>
            void SkipValue();

            /// <summary>消费当前字符（Peek 已确认后单次推进；避免 Expect(Peek()) 的二次 SkipWhitespace）。</summary>
            void Consume();

            /// <summary>抛带偏移/行列/上下文片段的错误。</summary>
            [System.Diagnostics.CodeAnalysis.DoesNotReturn]
            void Throw(string message);
        }

        /// <summary>
        /// 高频容器专用热循环入口：泛型 Reader 无法静态特化（Mono 接口/委托分发在紧密循环中退化 ~4×），
        /// 高频容器解析由 Lexer 具体类型直接实现（具体方法调用可内联），Reader 做能力探测与委托。
        /// 覆盖基准测试中的高频类型（int/float/double/long/string）。
        /// </summary>
        internal interface ITypedArrayParser
        {
            /// <summary>解析 int[] 专用热循环（最高频类型）。</summary>
            int[] ParseInt32ArrayFast();

            /// <summary>解析 List&lt;int&gt; 专用热循环（向既有列表填充）。</summary>
            void ParseInt32ListFast(List<int> list);

            /// <summary>解析 float[] 专用热循环。</summary>
            float[] ParseSingleArrayFast();

            /// <summary>解析 List&lt;float&gt; 专用热循环。</summary>
            void ParseSingleListFast(List<float> list);

            /// <summary>解析 double[] 专用热循环。</summary>
            double[] ParseDoubleArrayFast();

            /// <summary>解析 List&lt;double&gt; 专用热循环。</summary>
            void ParseDoubleListFast(List<double> list);

            /// <summary>解析 long[] 专用热循环。</summary>
            long[] ParseInt64ArrayFast();

            /// <summary>解析 List&lt;long&gt; 专用热循环。</summary>
            void ParseInt64ListFast(List<long> list);

            /// <summary>解析 string[] 专用热循环（含 null 字面量）。</summary>
            string[] ParseStringArrayFast();

            /// <summary>解析 List&lt;string&gt; 专用热循环（含 null 字面量）。</summary>
            void ParseStringListFast(List<string> list);
        }

        /// <summary>
        /// 字符串词法器（span 零拷贝 key 匹配、BCL span 数值解析）。
        /// </summary>
        internal sealed class CharLexer : IJsonLexer, ITypedArrayParser
        {
            private readonly string _json;
            private readonly int _maxDepth;
            private int _pos;

            public int MaxDepth => _maxDepth;

            /// <summary>防御性无参构造（泛型约束兼容；正常路径使用 (json, maxDepth) 主构造）。</summary>
            internal CharLexer()
            {
                _json = string.Empty;
                _maxDepth = 0;
            }

            public CharLexer(string json, int maxDepth)
            {
                _json = json;
                _maxDepth = maxDepth;
            }

            static CharLexer()
            {
                // 类型化 token 读取注册：供 JsonReader<CharLexer> 的数组/列表零装箱快路径
                LexerTokens<CharLexer>.Boolean = l => l.ReadBooleanToken();
                LexerTokens<CharLexer>.SByte = l => (sbyte)l.ReadTypedNumber(typeof(sbyte));
                LexerTokens<CharLexer>.Byte = l => (byte)l.ReadTypedNumber(typeof(byte));
                LexerTokens<CharLexer>.Int16 = l => (short)l.ReadTypedNumber(typeof(short));
                LexerTokens<CharLexer>.UInt16 = l => (ushort)l.ReadTypedNumber(typeof(ushort));
                LexerTokens<CharLexer>.Int32 = l => (int)l.ReadTypedNumber(typeof(int));
                LexerTokens<CharLexer>.UInt32 = l => (uint)l.ReadTypedNumber(typeof(uint));
                LexerTokens<CharLexer>.Int64 = l => (long)l.ReadTypedNumber(typeof(long));
                LexerTokens<CharLexer>.UInt64 = l => (ulong)l.ReadTypedNumber(typeof(ulong));
                LexerTokens<CharLexer>.Single = l => (float)l.ReadTypedNumber(typeof(float));
                LexerTokens<CharLexer>.Double = l => (double)l.ReadTypedNumber(typeof(double));
                LexerTokens<CharLexer>.Decimal = l => (decimal)l.ReadTypedNumber(typeof(decimal));
            }

            public void SkipWhitespace()
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

            public int Peek()
            {
                if (_pos >= _json.Length)
                {
                    Throw("Unexpected end of JSON input.");
                }

                return _json[_pos];
            }

            public void Expect(int expected)
            {
                SkipWhitespace();
                int c = Peek();
                if (c != expected)
                {
                    Throw(StringUtility.Format("Expected '{0}' but found '{1}'.", (char)expected, (char)c));
                }

                _pos++;
            }

            public bool MatchLiteral(string literal)
            {
                if (_json.Length - _pos < literal.Length) return false;
                if (string.CompareOrdinal(_json, _pos, literal, 0, literal.Length) != 0) return false;
                // 词边界：后续字符必须是分隔符或 EOF（防止 "trueX" 匹配 "true"）
                int next = _pos + literal.Length;
                if (next < _json.Length && !IsDelimiter(_json[next])) return false;
                _pos = next;
                return true;
            }

            public string ReadString()
            {
                bool hasEscape;
                ReadOnlySpan<char> span = ReadStringSpan(out hasEscape);
                return hasEscape ? Unescape(span) : span.ToString();
            }

            public object ParseNumber(Type type)
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

            public object ConvertString(string s, Type type)
            {
                return TypeConverter.TryConvertFromString(s, type, out object result)
                    ? result
                    : ParseNumberSpan(type, s.AsSpan());
            }

            public MemberMatch MatchMember(ReflectionCache.TypeMeta meta)
            {
                bool hasEscape;
                ReadOnlySpan<char> keySpan = ReadStringSpan(out hasEscape);

                if (hasEscape)
                {
                    // 含转义的键：物化后字符串比较（按别名表优先级）
                    string escapedKey = Unescape(keySpan);
                    return MatchByName(meta, escapedKey);
                }

                // 快路径：span 零分配比较
                var fields = meta.DeserializeFields;
                for (int i = 0; i < fields.Length; i++)
                {
                    string[] names = fields[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (keySpan.SequenceEqual(names[j].AsSpan()))
                        {
                            return new MemberMatch(fields[i].Field, null);
                        }
                    }
                }

                var properties = meta.DeserializeProperties;
                for (int i = 0; i < properties.Length; i++)
                {
                    string[] names = properties[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (keySpan.SequenceEqual(names[j].AsSpan()))
                        {
                            return new MemberMatch(null, properties[i].Property);
                        }
                    }
                }

                return default;
            }

            public void SkipValue()
            {
                SkipWhitespace();
                char c = (char)Peek();

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

            public void Consume()
            {
                _pos++;
            }

            [System.Diagnostics.CodeAnalysis.DoesNotReturn]
            public void Throw(string message)
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

            #region 私有方法 [PRIVATE METHODS]

            /// <summary>布尔 token（数组/列表快路径用；值域外抛错）。</summary>
            private bool ReadBooleanToken()
            {
                SkipWhitespace();
                if (MatchLiteral("true")) return true;
                if (MatchLiteral("false")) return false;

                Throw("Invalid boolean literal in array.");
                return false;
            }

            /// <summary>数值 token → 目标类型（数组/列表快路径用；经 ParseNumberSpan 全路径校验含范围）。</summary>
            private object ReadTypedNumber(Type type)
            {
                SkipWhitespace();
                return ParseNumber(type);
            }

            #region int 专用热循环 [INT32 HOT LOOPS]

            /// <summary>int[] 专用热循环：具体类型直调（绕过接口/委托分发——Mono 泛型容器循环退化 ~4× 的补偿）。</summary>
            public int[] ParseInt32ArrayFast()
            {
                Expect('[');

                SkipWhitespace();
                var tmp = new List<int>(16);
                if (Peek() == ']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add((int)ParseNumber(typeof(int)));

                        SkipWhitespace();
                        char c = (char)Peek();
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

                return tmp.ToArray();
            }

            /// <summary>List&lt;int&gt; 专用热循环（向既有列表填充）。</summary>
            public void ParseInt32ListFast(List<int> list)
            {
                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    list.Add((int)ParseNumber(typeof(int)));

                    SkipWhitespace();
                    char c = (char)Peek();
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

            /// <summary>float[] 专用热循环。</summary>
            public float[] ParseSingleArrayFast()
            {
                Expect('[');

                SkipWhitespace();
                var tmp = new List<float>(16);
                if (Peek() == ']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add((float)ParseNumber(typeof(float)));

                        SkipWhitespace();
                        char c = (char)Peek();
                        if (c == ',') { _pos++; continue; }
                        if (c == ']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;float&gt; 专用热循环。</summary>
            public void ParseSingleListFast(List<float> list)
            {
                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    list.Add((float)ParseNumber(typeof(float)));

                    SkipWhitespace();
                    char c = (char)Peek();
                    if (c == ',') { _pos++; continue; }
                    if (c == ']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                }
            }

            /// <summary>double[] 专用热循环。</summary>
            public double[] ParseDoubleArrayFast()
            {
                Expect('[');

                SkipWhitespace();
                var tmp = new List<double>(16);
                if (Peek() == ']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add((double)ParseNumber(typeof(double)));

                        SkipWhitespace();
                        char c = (char)Peek();
                        if (c == ',') { _pos++; continue; }
                        if (c == ']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;double&gt; 专用热循环。</summary>
            public void ParseDoubleListFast(List<double> list)
            {
                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    list.Add((double)ParseNumber(typeof(double)));

                    SkipWhitespace();
                    char c = (char)Peek();
                    if (c == ',') { _pos++; continue; }
                    if (c == ']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                }
            }

            /// <summary>long[] 专用热循环。</summary>
            public long[] ParseInt64ArrayFast()
            {
                Expect('[');

                SkipWhitespace();
                var tmp = new List<long>(16);
                if (Peek() == ']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add((long)ParseNumber(typeof(long)));

                        SkipWhitespace();
                        char c = (char)Peek();
                        if (c == ',') { _pos++; continue; }
                        if (c == ']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;long&gt; 专用热循环。</summary>
            public void ParseInt64ListFast(List<long> list)
            {
                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    list.Add((long)ParseNumber(typeof(long)));

                    SkipWhitespace();
                    char c = (char)Peek();
                    if (c == ',') { _pos++; continue; }
                    if (c == ']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                }
            }

            /// <summary>string[] 专用热循环（含 null 字面量）。</summary>
            public string[] ParseStringArrayFast()
            {
                Expect('[');

                SkipWhitespace();
                var tmp = new List<string>(16);
                if (Peek() == ']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        SkipWhitespace();
                        tmp.Add(Peek() == 'n' && MatchLiteral("null") ? null : ReadString());

                        SkipWhitespace();
                        char c = (char)Peek();
                        if (c == ',') { _pos++; continue; }
                        if (c == ']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;string&gt; 专用热循环（含 null 字面量）。</summary>
            public void ParseStringListFast(List<string> list)
            {
                Expect('[');

                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    SkipWhitespace();
                    list.Add(Peek() == 'n' && MatchLiteral("null") ? null : ReadString());

                    SkipWhitespace();
                    char c = (char)Peek();
                    if (c == ',') { _pos++; continue; }
                    if (c == ']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", c));
                }
            }

            #endregion

            /// <summary>读取带引号字符串的原始 span（不含引号），跳过转义对。</summary>
            private ReadOnlySpan<char> ReadStringSpan(out bool hasEscape)
            {
                hasEscape = false;
                if (_pos >= _json.Length || _json[_pos] != '"')
                {
                    Throw(StringUtility.Format("Expected a string but found '{0}'.",
                        _pos < _json.Length ? _json[_pos].ToString() : "<end>"));
                }

                int start = _pos + 1;
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

            private static MemberMatch MatchByName(ReflectionCache.TypeMeta meta, string escapedKey)
            {
                var fields = meta.DeserializeFields;
                for (int i = 0; i < fields.Length; i++)
                {
                    string[] names = fields[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (names[j] == escapedKey) return new MemberMatch(fields[i].Field, null);
                    }
                }

                var properties = meta.DeserializeProperties;
                for (int i = 0; i < properties.Length; i++)
                {
                    string[] names = properties[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (names[j] == escapedKey) return new MemberMatch(null, properties[i].Property);
                    }
                }

                return default;
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
                        catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException)
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

            private object TryParseIntegral(Type type, ReadOnlySpan<char> s)
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

            private static bool IsDelimiter(char c)
            {
                return c == ',' || c == '}' || c == ']' || c == ' ' || c == '\t' || c == '\r' || c == '\n';
            }

            #endregion
        }

        /// <summary>
        /// UTF8 字节词法器（预编码 UTF8 字节表零拷贝 key 匹配、手工整数解析 + 栈缓冲浮点转换）。
        /// </summary>
        internal sealed class ByteLexer : IJsonLexer, ITypedArrayParser
        {
            private const int NUMBER_CHAR_BUFFER = 64;
            private const int NUMBER_NOT_ASCII = -1;

            private readonly byte[] _json;
            private readonly int _maxDepth;
            private int _pos;

            public int MaxDepth => _maxDepth;

            /// <summary>防御性无参构造（泛型约束兼容；正常路径使用 (json, maxDepth) 主构造）。</summary>
            internal ByteLexer()
            {
                _json = Array.Empty<byte>();
                _maxDepth = 0;
            }

            public ByteLexer(byte[] json, int maxDepth)
            {
                _json = json;
                _maxDepth = maxDepth;

                // 跳过 UTF8 BOM
                if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
                {
                    _pos = 3;
                }
            }

            static ByteLexer()
            {
                // 类型化 token 读取注册：供 JsonReader<ByteLexer> 的数组/列表零装箱快路径
                LexerTokens<ByteLexer>.Boolean = l => l.ReadBooleanToken();
                LexerTokens<ByteLexer>.SByte = l => (sbyte)l.ReadTypedNumber(typeof(sbyte));
                LexerTokens<ByteLexer>.Byte = l => (byte)l.ReadTypedNumber(typeof(byte));
                LexerTokens<ByteLexer>.Int16 = l => (short)l.ReadTypedNumber(typeof(short));
                LexerTokens<ByteLexer>.UInt16 = l => (ushort)l.ReadTypedNumber(typeof(ushort));
                LexerTokens<ByteLexer>.Int32 = l => (int)l.ReadTypedNumber(typeof(int));
                LexerTokens<ByteLexer>.UInt32 = l => (uint)l.ReadTypedNumber(typeof(uint));
                LexerTokens<ByteLexer>.Int64 = l => (long)l.ReadTypedNumber(typeof(long));
                LexerTokens<ByteLexer>.UInt64 = l => (ulong)l.ReadTypedNumber(typeof(ulong));
                LexerTokens<ByteLexer>.Single = l => (float)l.ReadTypedNumber(typeof(float));
                LexerTokens<ByteLexer>.Double = l => (double)l.ReadTypedNumber(typeof(double));
                LexerTokens<ByteLexer>.Decimal = l => (decimal)l.ReadTypedNumber(typeof(decimal));
            }

            public void SkipWhitespace()
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

            public int Peek()
            {
                if (_pos >= _json.Length)
                {
                    Throw("Unexpected end of JSON input.");
                }

                return _json[_pos];
            }

            public void Expect(int expected)
            {
                SkipWhitespace();
                int c = Peek();
                if (c != expected)
                {
                    Throw(StringUtility.Format("Expected '{0}' but found '{1}'.", (char)expected, (char)c));
                }

                _pos++;
            }

            public bool MatchLiteral(string literal)
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

            public string ReadString()
            {
                bool hasEscape;
                int start;
                ReadOnlySpan<byte> span = ReadStringSpanBytes(out hasEscape, out start);
                return hasEscape ? UnescapeBytes(span) : Encoding.UTF8.GetString(_json, start, span.Length);
            }

            public object ParseNumber(Type type)
            {
                return ParseNumberSpanBytes(type, ScanNumberToken());
            }

            public object ConvertString(string s, Type type)
            {
                return TypeConverter.TryConvertFromString(s, type, out object result)
                    ? result
                    : ParseQuotedNumberBytes(type, s);
            }

            public MemberMatch MatchMember(ReflectionCache.TypeMeta meta)
            {
                bool hasEscape;
                int keyStart;
                ReadOnlySpan<byte> keySpan = ReadStringSpanBytes(out hasEscape, out keyStart);

                if (hasEscape)
                {
                    return MatchByName(meta, UnescapeBytes(keySpan));
                }

                // 快路径：预编码 UTF8 字节表零分配比较
                var fields = meta.DeserializeFields;
                var namesUtf8 = meta.DeserializeFieldNamesUtf8;
                for (int i = 0; i < fields.Length; i++)
                {
                    byte[][] encoded = namesUtf8[i];
                    for (int j = 0; j < encoded.Length; j++)
                    {
                        if (keySpan.SequenceEqual(encoded[j]))
                        {
                            return new MemberMatch(fields[i].Field, null);
                        }
                    }
                }

                var properties = meta.DeserializeProperties;
                var propNamesUtf8 = meta.DeserializePropertyNamesUtf8;
                for (int i = 0; i < properties.Length; i++)
                {
                    byte[][] encoded = propNamesUtf8[i];
                    for (int j = 0; j < encoded.Length; j++)
                    {
                        if (keySpan.SequenceEqual(encoded[j]))
                        {
                            return new MemberMatch(null, properties[i].Property);
                        }
                    }
                }

                return default;
            }

            public void SkipValue()
            {
                SkipWhitespace();
                byte c = (byte)Peek();

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

            public void Consume()
            {
                _pos++;
            }

            [System.Diagnostics.CodeAnalysis.DoesNotReturn]
            public void Throw(string message)
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

            #region 私有方法 [PRIVATE METHODS]

            /// <summary>布尔 token（数组/列表快路径用；值域外抛错）。</summary>
            private bool ReadBooleanToken()
            {
                SkipWhitespace();
                if (MatchLiteral("true")) return true;
                if (MatchLiteral("false")) return false;

                Throw("Invalid boolean literal in array.");
                return false;
            }

            /// <summary>数值 token → 目标类型（数组/列表快路径用；含溢出预判与范围校验）。</summary>
            private object ReadTypedNumber(Type type)
            {
                SkipWhitespace();
                return ParseNumber(type);
            }

            #region int 专用热循环 [INT32 HOT LOOPS]

            /// <summary>int[] 专用热循环：直调 span 解析（绕过 ParseNumber 的 Type 查询/装箱与接口分发）。
            /// 科学计数法等非常规形式回退 TryParseDoubleBytes 整值校验。</summary>
            public int[] ParseInt32ArrayFast()
            {
                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<int>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(ReadInt32Token());

                        SkipWhitespace();
                        byte c = (byte)Peek();
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

            /// <summary>List&lt;int&gt; 专用热循环（直调 span 解析；向既有列表填充）。</summary>
            public void ParseInt32ListFast(List<int> list)
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
                    list.Add(ReadInt32Token());

                    SkipWhitespace();
                    byte c = (byte)Peek();
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

            /// <summary>float[] 专用热循环（直调 span 解析）。</summary>
            public float[] ParseSingleArrayFast()
            {
                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<float>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(ReadSingleToken());

                        SkipWhitespace();
                        byte c = (byte)Peek();
                        if (c == (byte)',') { _pos++; continue; }
                        if (c == (byte)']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;float&gt; 专用热循环。</summary>
            public void ParseSingleListFast(List<float> list)
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
                    list.Add(ReadSingleToken());

                    SkipWhitespace();
                    byte c = (byte)Peek();
                    if (c == (byte)',') { _pos++; continue; }
                    if (c == (byte)']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>double[] 专用热循环。</summary>
            public double[] ParseDoubleArrayFast()
            {
                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<double>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(ReadDoubleToken());

                        SkipWhitespace();
                        byte c = (byte)Peek();
                        if (c == (byte)',') { _pos++; continue; }
                        if (c == (byte)']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;double&gt; 专用热循环。</summary>
            public void ParseDoubleListFast(List<double> list)
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
                    list.Add(ReadDoubleToken());

                    SkipWhitespace();
                    byte c = (byte)Peek();
                    if (c == (byte)',') { _pos++; continue; }
                    if (c == (byte)']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>long[] 专用热循环。</summary>
            public long[] ParseInt64ArrayFast()
            {
                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<long>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(ReadInt64Token());

                        SkipWhitespace();
                        byte c = (byte)Peek();
                        if (c == (byte)',') { _pos++; continue; }
                        if (c == (byte)']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;long&gt; 专用热循环。</summary>
            public void ParseInt64ListFast(List<long> list)
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
                    list.Add(ReadInt64Token());

                    SkipWhitespace();
                    byte c = (byte)Peek();
                    if (c == (byte)',') { _pos++; continue; }
                    if (c == (byte)']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>string[] 专用热循环（含 null 字面量）。</summary>
            public string[] ParseStringArrayFast()
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
                        SkipWhitespace();
                        tmp.Add(Peek() == (byte)'n' && MatchLiteral("null") ? null : ReadString());

                        SkipWhitespace();
                        byte c = (byte)Peek();
                        if (c == (byte)',') { _pos++; continue; }
                        if (c == (byte)']') { _pos++; break; }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>List&lt;string&gt; 专用热循环（含 null 字面量）。</summary>
            public void ParseStringListFast(List<string> list)
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
                    SkipWhitespace();
                    list.Add(Peek() == (byte)'n' && MatchLiteral("null") ? null : ReadString());

                    SkipWhitespace();
                    byte c = (byte)Peek();
                    if (c == (byte)',') { _pos++; continue; }
                    if (c == (byte)']') { _pos++; return; }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>int token：直取（科学计数法/越界回退 double 整值校验，兼容 "1e2" 类输入）。</summary>
            private int ReadInt32Token()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseInt64Bytes(s, out long v))
                {
                    if (v >= int.MinValue && v <= int.MaxValue) return (int)v;
                    Throw(StringUtility.Format("'{0}' is out of range for 'int'.", EncodeSpan(s)));
                }

                if (TryParseDoubleBytes(s, out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) &&
                    d >= int.MinValue && d <= int.MaxValue)
                {
                    return (int)d;
                }

                Throw(StringUtility.Format("'{0}' is not a valid integer for 'int'.", EncodeSpan(s)));
                return 0;
            }

            /// <summary>long token：直取（科学计数法/溢出回退 double 整值校验）。</summary>
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

            /// <summary>float token：直取。</summary>
            private float ReadSingleToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseSingleBytes(s, out float f)) return f;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'float'.", EncodeSpan(s)));
                return 0;
            }

            /// <summary>double token：直取。</summary>
            private double ReadDoubleToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseDoubleBytes(s, out double d)) return d;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'double'.", EncodeSpan(s)));
                return 0;
            }

            #endregion

            /// <summary>读取带引号字符串的原始字节 span（不含引号），跳过转义对。</summary>
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

            private static MemberMatch MatchByName(ReflectionCache.TypeMeta meta, string escapedKey)
            {
                var fields = meta.DeserializeFields;
                for (int i = 0; i < fields.Length; i++)
                {
                    string[] names = fields[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (names[j] == escapedKey) return new MemberMatch(fields[i].Field, null);
                    }
                }

                var properties = meta.DeserializeProperties;
                for (int i = 0; i < properties.Length; i++)
                {
                    string[] names = properties[i].Names;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (names[j] == escapedKey) return new MemberMatch(null, properties[i].Property);
                    }
                }

                return default;
            }

            /// <summary>反转义（标准转义对、\uXXXX；原始段手动解码 UTF8，无效序列 → U+FFFD）。ASCII 快路径直取。</summary>
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
                        catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException)
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

            /// <summary>带引号数值（历史格式）：string → 字节 span 解析（ASCII ≤64 字符经栈缓冲零堆分配）。</summary>
            private object ParseQuotedNumberBytes(Type type, string s)
            {
                if (s.Length > 0 && s.Length <= NUMBER_CHAR_BUFFER)
                {
                    Span<byte> buffer = stackalloc byte[NUMBER_CHAR_BUFFER];
                    int i = 0;
                    while (i < s.Length)
                    {
                        char ch = s[i];
                        if (ch > 0x7F) break; // 数值 token 必为 ASCII
                        buffer[i] = (byte)ch;
                        i++;
                    }

                    if (i == s.Length)
                    {
                        return ParseNumberSpanBytes(type, buffer.Slice(0, s.Length));
                    }
                }

                return ParseNumberSpanBytes(type, Encoding.UTF8.GetBytes(s));
            }

            private object TryParseIntegralBytes(Type type, ReadOnlySpan<byte> s)
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

            // ===== 手工整数解析（ASCII 数字循环，零分配；溢出预判在乘法之前） =====

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

                // long 范围上限（负数侧允许到 9223372036854775808 = long.MinValue 绝对值）
                ulong limit = negative ? 9223372036854775808UL : 9223372036854775807UL;

                ulong acc = 0;
                while (i < s.Length)
                {
                    byte c = s[i];
                    if (c < (byte)'0' || c > (byte)'9') return false;

                    ulong digit = (uint)(c - '0');
                    // 溢出预判必须在乘法之前：先乘后查会因无符号回绕绕过检查
                    if (acc > (ulong.MaxValue - digit) / 10) return false;

                    acc = acc * 10 + digit;
                    if (acc > limit) return false;
                    i++;
                }

                // 负数侧 acc 可达 9223372036854775808UL（long.MinValue 绝对值）——超出 long.MaxValue，
                // 转换需显式 unchecked（0 - acc 回绕即为 long.MinValue，与取负语义一致）
                value = negative ? unchecked((long)(0 - acc)) : (long)acc;
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

            /// <summary>将 ASCII 数值 token 复制进调用方栈缓冲；超长/非 ASCII 返回 -1（调用方回退字符串路径）。</summary>
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

            private static bool IsDelimiter(byte c)
            {
                return c == (byte)',' || c == (byte)'}' || c == (byte)']' ||
                       c == 0x20 || c == 0x09 || c == 0x0A || c == 0x0D;
            }

            #endregion
        }
    }
}
