using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        /// <summary>
        /// 序列化写入器。单遍直写（无中间列表/子字符串）、区域性固定（InvariantCulture）、标准 JSON 输出。
        /// </summary>
        /// <remarks>
        /// <para><b>商业级保证</b>：数值全部以 <see cref="CultureInfo.InvariantCulture"/> 输出（浮点用 "R" 往返格式），
        /// 杜绝区域性小数点损坏；<see cref="Dictionary{TKey,TValue}"/> 以标准 JSON 对象格式输出（字符串 key），
        /// 复杂 key 回退 legacy 条目数组格式；<see cref="DateTime"/>/<see cref="Guid"/>/<see cref="TimeSpan"/> 等
        /// 无公开字段类型显式转为字符串，不再静默输出 "{}"。</para>
        /// <para><b>引用环</b>：按 <see cref="NewtonsoftJsonHandler"/> 既定的 ReferenceLoopHandling.Ignore 语义
        /// 跳过构成环的成员/元素（不抛错、不无限递归）；深度上限仅作为真深嵌套 DAG 的栈安全兜底。</para>
        /// <para><b>AOT 约束</b>：不使用表达式树/Reflection.Emit（IL2CPP 不支持），反射开销由
        /// <see cref="ReflectionCache"/> 元数据缓存吸收。</para>
        /// </remarks>
        internal static class Writer
        {
            #region 引用环/深度守卫 [REFERENCE LOOP / DEPTH GUARD]

            /// <summary>序列化中的引用类型对象栈（线程本地复用；深度 ≤ maxDepth，线性扫描即可，无需 HashSet）。</summary>
            [ThreadStatic]
            private static List<object> t_RefStack;

            /// <summary>深度告警已发出标志（每次 Serialize 重置，避免刷屏）。</summary>
            [ThreadStatic]
            private static bool t_DepthWarned;

            /// <summary>
            /// 该引用是否正在序列化中（构成引用环）。null/值类型/字符串不参与跟踪（无环可能）。
            /// 调用方（成员/元素发射处）应跳过该成员/元素。
            /// </summary>
            private static bool IsSerializingReference(object value)
            {
                if (value == null) return false;
                Type t = value.GetType();
                if (t.IsValueType || t == typeof(string)) return false;

                var stack = t_RefStack;
                if (stack == null) return false;

                for (int i = 0; i < stack.Count; i++)
                {
                    if (ReferenceEquals(stack[i], value)) return true;
                }

                return false;
            }

            /// <summary>容器（对象/数组/列表/字典）入口压栈。调用方已通过 IsSerializingReference 检查，此处置信非环。</summary>
            private static void PushReference(object container)
            {
                (t_RefStack ??= new List<object>(maxDepth + 2)).Add(container);
            }

            /// <summary>容器出口弹栈（正常路径配对；异常路径由 Serialize 的 finally 清栈兜底）。</summary>
            private static void PopReference()
            {
                var stack = t_RefStack;
                if (stack != null && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }

            /// <summary>是否为标量值（写入无递归风险，深度守卫不适用）。</summary>
            private static bool IsScalarValue(object value)
            {
                if (value == null || value is string) return true;
                Type t = value.GetType();
                return t.IsPrimitive || t.IsEnum ||
                       t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) ||
                       t == typeof(TimeSpan) || t == typeof(Guid);
            }

            /// <summary>
            /// 子级复合值是否会被深度守卫截断（父级深度 + 子级深度超限）。
            /// 命中时调用方应跳过整个成员/元素（名称+值），保持输出 JSON 合法；仅告警一次。
            /// </summary>
            private static bool WouldExceedDepth(object childValue, int parentDepth)
            {
                if (parentDepth + 1 < maxDepth || IsScalarValue(childValue)) return false;

                if (!t_DepthWarned)
                {
                    t_DepthWarned = true;
                    Log.Warning(StringUtility.Format(
                        "[DefaultJson] Serialization depth exceeded the limit of {0}. Members beyond the limit are skipped (reference loop via value-type boxing or nesting too deep).", maxDepth));
                }

                return true;
            }

            #endregion

            #region 入口 [ENTRY]

            /// <summary>序列化对象为 JSON 字符串。</summary>
            public static string Serialize(object obj, bool removeNulls, bool readable)
            {
                t_RefStack?.Clear(); // 跨调用残留清理（异常路径可能未配对弹出）
                t_DepthWarned = false;

                StringHandler.IStringBuilder sb = StringUtility.CreateStringBuilder();
                try
                {
                    WriteValue(sb, obj, removeNulls, readable, 0);
                    return sb.ToStringAndDispose();
                }
                catch
                {
                    sb.Dispose(); // 异常路径也要归还池，避免池化 builder 泄漏
                    throw;
                }
                finally
                {
                    t_RefStack?.Clear();
                }
            }

            #endregion

            #region 值分派 [VALUE DISPATCH]

            private static void WriteValue(StringHandler.IStringBuilder sb, object value, bool removeNulls, bool readable, int depth)
            {
                // 安全网：成员/元素处已按 WouldExceedDepth 软截断，此处仅兜底未守卫路径（写 null 保证输出合法）。
                // 标量无递归风险，任何深度都照常写值（边界对象的字符串/数值字段不得丢失）。
                if (depth >= maxDepth && !IsScalarValue(value))
                {
                    sb.Append("null");
                    return;
                }

                if (value == null)
                {
                    sb.Append("null");
                    return;
                }

                // Unity 伪 null（已销毁对象）按 null 输出，防止反射进原生侧对象
                if (value is UnityEngine.Object unityObject && unityObject == null)
                {
                    sb.Append("null");
                    return;
                }

                Type type = value.GetType();

                // 基元/字符串/枚举/已知 BCL 值类型：直接写值（跳过反射元数据查找——数组/集合元素热路径）
                if (type.IsPrimitive || type == typeof(string) || type.IsEnum ||
                    type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
                    type == typeof(TimeSpan) || type == typeof(Guid))
                {
                    WriteSimpleValue(sb, value);
                    return;
                }

                // 预序列化回调（元数据走缓存）
                var meta = ReflectionCache.Get(type);
                foreach (MethodInfo info in meta.BeforeSerializeMethods)
                {
                    info.Invoke(value, null);
                }

                if (type.IsArray)
                {
                    WriteArray(sb, (Array)value, removeNulls, readable, depth);
                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    WriteDictionary(sb, (IDictionary)value, type, removeNulls, readable, depth);
                    return;
                }

                if (value is IList list)
                {
                    WriteList(sb, list, removeNulls, readable, depth);
                    return;
                }

                WriteObject(sb, value, type, meta, removeNulls, readable, depth);
            }

            /// <summary>
            /// 写入简单值（基元/字符串/枚举/已知可转换类型）。返回 false 表示非简单值，交由容器/对象路径处理。
            /// </summary>
            private static bool WriteSimpleValue(StringHandler.IStringBuilder sb, object value)
            {
                switch (value)
                {
                    case bool b:
                        sb.Append(b ? "true" : "false");
                        return true;
                    case char c:
                        WriteEscapedString(sb, c.ToString());
                        return true;
                    case string s:
                        WriteEscapedString(sb, s);
                        return true;
                    case float f:
                        WriteFloat(sb, f);
                        return true;
                    case double d:
                        WriteDouble(sb, d);
                        return true;
                    case decimal m:
                        sb.Append(m.ToString(CultureInfo.InvariantCulture));
                        return true;
                    case int i:
                        WriteInt64(sb, i);
                        return true;
                    case long l:
                        WriteInt64(sb, l);
                        return true;
                    case uint ui:
                        WriteUInt64(sb, ui);
                        return true;
                    case ulong ul:
                        WriteUInt64(sb, ul);
                        return true;
                    case byte by:
                        WriteUInt64(sb, by);
                        return true;
                    case sbyte sbv:
                        WriteInt64(sb, sbv);
                        return true;
                    case short sh:
                        WriteInt64(sb, sh);
                        return true;
                    case ushort ush:
                        WriteUInt64(sb, ush);
                        return true;
                    case DateTime dt:
                        WriteEscapedString(sb, dt.ToString("o", CultureInfo.InvariantCulture));
                        return true;
                    case DateTimeOffset dto:
                        WriteEscapedString(sb, dto.ToString("o", CultureInfo.InvariantCulture));
                        return true;
                    case TimeSpan ts:
                        WriteEscapedString(sb, ts.ToString("c", CultureInfo.InvariantCulture));
                        return true;
                    case Guid g:
                        WriteEscapedString(sb, g.ToString("D"));
                        return true;
                    default:
                        Type type = value.GetType();
                        if (type.IsEnum)
                        {
                            // 枚举以名称字符串输出（与解析端对称；数值枚举解析端同样支持）
                            WriteEscapedString(sb, value.ToString());
                            return true;
                        }

                        return false;
                }
            }

            private static void WriteFloat(StringHandler.IStringBuilder sb, float f)
            {
                if (float.IsNaN(f)) sb.Append("NaN");
                else if (float.IsPositiveInfinity(f)) sb.Append("Infinity");
                else if (float.IsNegativeInfinity(f)) sb.Append("-Infinity");
                else if (f == MathF.Truncate(f) && MathF.Abs(f) < 1e15f) WriteInt64(sb, (long)f); // 整值快路径（跳过昂贵的 "R" 格式化）
                else sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
            }

            private static void WriteDouble(StringHandler.IStringBuilder sb, double d)
            {
                if (double.IsNaN(d)) sb.Append("NaN");
                else if (double.IsPositiveInfinity(d)) sb.Append("Infinity");
                else if (double.IsNegativeInfinity(d)) sb.Append("-Infinity");
                else if (d == Math.Truncate(d) && Math.Abs(d) < 1e15) WriteInt64(sb, (long)d); // 整值快路径
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }

            // ===== 手工整数格式化（栈缓冲 + 单次 span 追加，零分配；免去 ToString 分配与接口逐段调用） =====

            private static void WriteInt64(StringHandler.IStringBuilder sb, long v)
            {
                Span<char> buffer = stackalloc char[21]; // '-' + 20 位
                int pos = 0;

                ulong digits;
                if (v < 0)
                {
                    buffer[pos++] = '-';
                    digits = v == long.MinValue ? unchecked((ulong)(-(v + 1)) + 1UL) : (ulong)(-v);
                }
                else
                {
                    digits = (ulong)v;
                }

                pos = FormatDigits(buffer, pos, digits);
                sb.Append((ReadOnlySpan<char>)buffer.Slice(0, pos));
            }

            private static void WriteUInt64(StringHandler.IStringBuilder sb, ulong v)
            {
                Span<char> buffer = stackalloc char[20];
                int pos = FormatDigits(buffer, 0, v);
                sb.Append((ReadOnlySpan<char>)buffer.Slice(0, pos));
            }

            /// <summary>数字低位在前写入后原地反转。返回写入后的长度。</summary>
            private static int FormatDigits(Span<char> buffer, int pos, ulong v)
            {
                if (v == 0)
                {
                    buffer[pos++] = '0';
                    return pos;
                }

                int digitStart = pos;
                while (v >= 10)
                {
                    buffer[pos++] = (char)('0' + (v % 10));
                    v /= 10;
                }

                buffer[pos++] = (char)('0' + v);

                int left = digitStart, right = pos - 1;
                while (left < right)
                {
                    char tmp = buffer[left];
                    buffer[left] = buffer[right];
                    buffer[right] = tmp;
                    left++;
                    right--;
                }

                return pos;
            }

            #endregion

            #region 容器 [CONTAINERS]

            private static void WriteArray(StringHandler.IStringBuilder sb, Array array, bool removeNulls, bool readable, int depth)
            {
                if (array.Length == 0)
                {
                    sb.Append("[]");
                    return;
                }

                // 类型化基元数组快速路径：具体类型模式匹配（AOT 安全），消除逐元素 Array.GetValue 装箱与值分派
                switch (array)
                {
                    case int[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteInt64(sb, a[i]));
                        return;
                    case long[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteInt64(sb, a[i]));
                        return;
                    case float[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteFloat(sb, a[i]));
                        return;
                    case double[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteDouble(sb, a[i]));
                        return;
                    case bool[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => sb.Append(a[i] ? "true" : "false"));
                        return;
                    case uint[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteUInt64(sb, a[i]));
                        return;
                    case ulong[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteUInt64(sb, a[i]));
                        return;
                    case short[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteInt64(sb, a[i]));
                        return;
                    case ushort[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteUInt64(sb, a[i]));
                        return;
                    case byte[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteUInt64(sb, a[i]));
                        return;
                    case sbyte[] a:
                        WritePrimitiveArray(sb, a.Length, readable, i => WriteInt64(sb, a[i]));
                        return;
                }

                sb.Append('[');
                PushReference(array);
                for (int i = 0; i < array.Length; i++)
                {
                    object element = array.GetValue(i);
                    if (IsSerializingReference(element)) continue; // 引用环：跳过元素
                    if (WouldExceedDepth(element, depth)) continue; // 深度超限：软截断

                    if (i > 0) sb.Append(readable ? ", " : ",");
                    WriteValue(sb, element, removeNulls, readable, depth + 1);
                }

                PopReference();
                sb.Append(']');
            }

            /// <summary>类型化基元数组写入骨架（分隔符处理）。</summary>
            private static void WritePrimitiveArray(StringHandler.IStringBuilder sb, int count, bool readable, Action<int> write)
            {
                sb.Append('[');
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append(readable ? ", " : ",");
                    write(i);
                }

                sb.Append(']');
            }

            private static void WriteList(StringHandler.IStringBuilder sb, IList list, bool removeNulls, bool readable, int depth)
            {
                if (list.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }

                // 类型化基元列表快速路径：消除逐元素接口索引器装箱与值分派
                switch (list)
                {
                    case List<int> l:
                        WritePrimitiveArray(sb, l.Count, readable, i => WriteInt64(sb, l[i]));
                        return;
                    case List<long> l:
                        WritePrimitiveArray(sb, l.Count, readable, i => WriteInt64(sb, l[i]));
                        return;
                    case List<float> l:
                        WritePrimitiveArray(sb, l.Count, readable, i => WriteFloat(sb, l[i]));
                        return;
                    case List<double> l:
                        WritePrimitiveArray(sb, l.Count, readable, i => WriteDouble(sb, l[i]));
                        return;
                    case List<bool> l:
                        WritePrimitiveArray(sb, l.Count, readable, i => sb.Append(l[i] ? "true" : "false"));
                        return;
                }

                sb.Append('[');
                PushReference(list);
                for (int i = 0; i < list.Count; i++)
                {
                    object element = list[i];
                    if (IsSerializingReference(element)) continue; // 引用环：跳过元素
                    if (WouldExceedDepth(element, depth)) continue; // 深度超限：软截断

                    if (i > 0) sb.Append(readable ? ", " : ",");
                    WriteValue(sb, element, removeNulls, readable, depth + 1);
                }

                PopReference();
                sb.Append(']');
            }

            /// <summary>
            /// 字典序列化：简单 key（字符串/枚举/数值/bool/char/Guid/DateTime 等）输出标准 JSON 对象格式；
            /// 复杂 key 回退 legacy 条目数组格式（[{"key":..,"value":..}]），解析端两种格式都接受。
            /// </summary>
            private static void WriteDictionary(StringHandler.IStringBuilder sb, IDictionary dictionary, Type dictType, bool removeNulls, bool readable, int depth)
            {
                if (dictionary.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }

                Type keyType = dictType.GetGenericArguments()[0];
                if (!IsStandardDictionaryKey(keyType))
                {
                    WriteDictionaryLegacy(sb, dictionary, keyType, removeNulls, readable, depth);
                    return;
                }

                sb.Append('{');
                PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    // 引用环（值指向字典自身或其祖先）：跳过该键值对
                    if (IsSerializingReference(entry.Value)) continue;
                    if (WouldExceedDepth(entry.Value, depth)) continue; // 深度超限：软截断

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    WriteDictionaryKey(sb, entry.Key, keyType);
                    sb.Append(':');
                    if (readable) sb.Append(' ');
                    WriteValue(sb, entry.Value, removeNulls, readable, depth + 1);
                }

                PopReference();
                if (readable) sb.Append("\r\n").Append('\t', depth);
                sb.Append('}');
            }

            private static void WriteDictionaryKey(StringHandler.IStringBuilder sb, object key, Type keyType)
            {
                if (key is string keyString)
                {
                    WriteEscapedString(sb, keyString);
                }
                else if (keyType.IsEnum)
                {
                    WriteEscapedString(sb, key.ToString());
                }
                else
                {
                    // 数值/bool/char/Guid/DateTime 等：字符串化的标准 key
                    WriteEscapedString(sb, Convert.ToString(key, CultureInfo.InvariantCulture));
                }
            }

            private static void WriteDictionaryLegacy(StringHandler.IStringBuilder sb, IDictionary dictionary, Type keyType, bool removeNulls, bool readable, int depth)
            {
                sb.Append('[');
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    sb.Append('{');
                    sb.Append("\"key\":");
                    WriteValue(sb, entry.Key, removeNulls, readable, depth + 1);
                    sb.Append(",\"value\":");
                    WriteValue(sb, entry.Value, removeNulls, readable, depth + 1);
                    sb.Append('}');
                }

                if (readable) sb.Append("\r\n").Append('\t', depth);
                sb.Append(']');
            }

            /// <summary>能否作为标准 JSON 对象 key 输出（字符串化后可无损还原）。</summary>
            internal static bool IsStandardDictionaryKey(Type keyType)
            {
                return keyType == typeof(string) ||
                       keyType == typeof(char) ||
                       keyType == typeof(bool) ||
                       keyType.IsEnum ||
                       keyType == typeof(byte) || keyType == typeof(sbyte) ||
                       keyType == typeof(short) || keyType == typeof(ushort) ||
                       keyType == typeof(int) || keyType == typeof(uint) ||
                       keyType == typeof(long) || keyType == typeof(ulong) ||
                       keyType == typeof(float) || keyType == typeof(double) || keyType == typeof(decimal) ||
                       keyType == typeof(Guid) || keyType == typeof(DateTime) || keyType == typeof(DateTimeOffset) ||
                       keyType == typeof(TimeSpan);
            }

            #endregion

            #region 对象 [OBJECTS]

            private static void WriteObject(StringHandler.IStringBuilder sb, object obj, Type type, ReflectionCache.TypeMeta meta, bool removeNulls, bool readable, int depth)
            {
                var fields = meta.SerializeFields;
                var properties = meta.SerializeProperties;

                if (fields.Length == 0 && properties.Length == 0)
                {
                    // 显式失败而非静默输出 "{}"（如无公开成员的自定义类型）
                    throw new GameException(StringUtility.Format(
                        "Type '{0}' has no serializable fields or properties. If it represents a value-like type, expose members or mark them with [JsonSerialize].", type.FullName));
                }

                // 容器入口压栈（值类型对象无环可能，不入栈）；使自引用根对象可被成员检查识别
                if (!type.IsValueType) PushReference(obj);

                // 可读模式：对象起始换行缩进（保持既有 pretty 输出风格）
                if (readable && depth > 0)
                {
                    sb.Append("\r\n").Append('\t', depth);
                }

                sb.Append('{');

                bool isFirst = true;

                for (int i = 0; i < fields.Length; i++)
                {
                    object value = fields[i].Field.GetValue(obj);
                    if (value == null && removeNulls) continue;

                    // 引用环：跳过整个成员（名称+值），对齐 Newtonsoft ReferenceLoopHandling.Ignore
                    if (IsSerializingReference(value)) continue;
                    if (WouldExceedDepth(value, depth)) continue; // 深度超限：软截断

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    WriteEscapedString(sb, fields[i].Name);
                    sb.Append(':');
                    if (readable) sb.Append(' ');
                    WriteValue(sb, value, removeNulls, readable, depth + 1);
                }

                for (int i = 0; i < properties.Length; i++)
                {
                    object value;
                    try
                    {
                        value = properties[i].Property.GetValue(obj);
                    }
                    catch (Exception e)
                    {
                        throw new GameException(StringUtility.Format(
                            "Failed to read property '{0}' of type '{1}'.", properties[i].Property.Name, type.FullName), e);
                    }

                    if (value == null && removeNulls) continue;

                    // 引用环：跳过整个成员（名称+值）
                    if (IsSerializingReference(value)) continue;
                    if (WouldExceedDepth(value, depth)) continue; // 深度超限：软截断

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    WriteEscapedString(sb, properties[i].Name);
                    sb.Append(':');
                    if (readable) sb.Append(' ');
                    WriteValue(sb, value, removeNulls, readable, depth + 1);
                }

                if (readable) sb.Append("\r\n").Append('\t', depth);
                sb.Append('}');
                if (!type.IsValueType) PopReference();
            }

            #endregion

            #region 字符串转义 [ESCAPING]

            /// <summary>写入带引号的转义字符串。无转义字符的常见路径整串单次追加（免逐字符接口调用）。</summary>
            private static void WriteEscapedString(StringHandler.IStringBuilder sb, string s)
            {
                if (!NeedsEscape(s))
                {
                    sb.Append('"').Append(s).Append('"');
                    return;
                }

                sb.Append('"');
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\b':
                            sb.Append("\\b");
                            break;
                        case '\f':
                            sb.Append("\\f");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u").Append(((int)c).ToString("X4"));
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

            /// <summary>是否含需转义字符（引号/反斜杠/控制字符）。</summary>
            private static bool NeedsEscape(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;

                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '"' || c == '\\' || c < ' ') return true;
                }

                return false;
            }

            #endregion
        }
    }
}
