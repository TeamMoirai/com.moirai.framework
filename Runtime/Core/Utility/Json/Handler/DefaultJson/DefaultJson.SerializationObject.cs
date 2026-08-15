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
        /// 跳过构成环的成员/元素（不抛错、不无限递归）；深度上限仅作为真深嵌套 DAG 的栈安全兜底。
        /// 守卫逻辑由 <see cref="LoopGuard"/> 共享。</para>
        /// <para><b>AOT 约束</b>：不使用表达式树/Reflection.Emit（IL2CPP 不支持），反射开销由
        /// <see cref="ReflectionCache"/> 元数据缓存吸收。</para>
        /// </remarks>
        internal static class Writer
        {
            #region 入口 [ENTRY]

            /// <summary>序列化对象为 JSON 字符串。</summary>
            public static string Serialize(object obj, bool removeNulls, bool readable, int depthLimit)
            {
                LoopGuard.Begin();

                StringHandler.IStringBuilder sb = StringUtility.CreateStringBuilder();
                try
                {
                    WriteValue(sb, obj, removeNulls, readable, 0, depthLimit);
                    return sb.ToStringAndDispose();
                }
                catch
                {
                    sb.Dispose();
                    throw;
                }
                finally
                {
                    LoopGuard.End();
                }
            }

            #endregion

            #region 值分派 [VALUE DISPATCH]

            private static void WriteValue(StringHandler.IStringBuilder sb, object value, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                // 安全网：成员/元素处已按 LoopGuard.WouldExceedDepth 软截断，此处仅兜底未守卫路径。
                // 标量无递归风险，任何深度都照常写值。
                if (depth >= depthLimit && !LoopGuard.IsScalarValue(value))
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

                // 常见 Unity 结构体直写快路径（绕过反射 FieldInfo.SetValue 装箱）。
                // 必须在容器/反射分发之前——Vector3 等既非基元也非 BCL 值类型，放 WriteSimpleValue 内永远不可达。
                if (TryWriteUnityStruct(sb, value)) return;

                // 预序列化回调（元数据走缓存）
                var meta = ReflectionCache.Get(type);
                foreach (MethodInfo info in meta.BeforeSerializeMethods)
                {
                    info.Invoke(value, null);
                }

                if (type.IsArray)
                {
                    WriteArray(sb, (Array)value, removeNulls, readable, depth, depthLimit);
                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    WriteDictionary(sb, (IDictionary)value, type, removeNulls, readable, depth, depthLimit);
                    return;
                }

                if (value is IList list)
                {
                    WriteList(sb, list, removeNulls, readable, depth, depthLimit);
                    return;
                }

                WriteObject(sb, value, type, meta, removeNulls, readable, depth, depthLimit);
            }

            /// <summary>写入简单值（基元/字符串/枚举/已知可转换类型）。</summary>
            private static void WriteSimpleValue(StringHandler.IStringBuilder sb, object value)
            {
                switch (value)
                {
                    case bool b:
                        sb.Append(b ? "true" : "false");
                        return;
                    case char c:
                        WriteEscapedString(sb, c.ToString());
                        return;
                    case string s:
                        WriteEscapedString(sb, s);
                        return;
                    case float f:
                        WriteFloat(sb, f);
                        return;
                    case double d:
                        WriteDouble(sb, d);
                        return;
                    case decimal m:
                        sb.Append(m.ToString(CultureInfo.InvariantCulture));
                        return;
                    case int i:
                        WriteInt64(sb, i);
                        return;
                    case long l:
                        WriteInt64(sb, l);
                        return;
                    case uint ui:
                        WriteUInt64(sb, ui);
                        return;
                    case ulong ul:
                        WriteUInt64(sb, ul);
                        return;
                    case byte by:
                        WriteUInt64(sb, by);
                        return;
                    case sbyte sbv:
                        WriteInt64(sb, sbv);
                        return;
                    case short sh:
                        WriteInt64(sb, sh);
                        return;
                    case ushort ush:
                        WriteUInt64(sb, ush);
                        return;
                    case DateTime dt:
                        WriteEscapedString(sb, dt.ToString("o", CultureInfo.InvariantCulture));
                        return;
                    case DateTimeOffset dto:
                        WriteEscapedString(sb, dto.ToString("o", CultureInfo.InvariantCulture));
                        return;
                    case TimeSpan ts:
                        WriteEscapedString(sb, ts.ToString("c", CultureInfo.InvariantCulture));
                        return;
                    case Guid g:
                        WriteEscapedString(sb, g.ToString("D"));
                        return;
                    default:
                        Type type = value.GetType();
                        if (type.IsEnum)
                        {
                            WriteEscapedString(sb, value.ToString());
                            return;
                        }

                        // 常见 Unity 结构体直写快路径（P4：绕过反射 FieldInfo.SetValue）
                        if (TryWriteUnityStruct(sb, value)) return;

                        throw new GameException(StringUtility.Format(
                            "Type '{0}' is not a simple value and cannot be written by WriteSimpleValue.", type.FullName));
                }
            }

            #region Unity 结构体直写快路径 [UNITY STRUCT FAST PATH]

            /// <summary>尝试直写常见 Unity 结构体（绕过反射）。返回 true 表示已处理。</summary>
            private static bool TryWriteUnityStruct(StringHandler.IStringBuilder sb, object value)
            {
                switch (value)
                {
                    case Vector2 v2:
                        sb.Append("{\"x\":");
                        WriteFloat(sb, v2.x);
                        sb.Append(",\"y\":");
                        WriteFloat(sb, v2.y);
                        sb.Append('}');
                        return true;
                    case Vector3 v3:
                        sb.Append("{\"x\":");
                        WriteFloat(sb, v3.x);
                        sb.Append(",\"y\":");
                        WriteFloat(sb, v3.y);
                        sb.Append(",\"z\":");
                        WriteFloat(sb, v3.z);
                        sb.Append('}');
                        return true;
                    case Vector4 v4:
                        sb.Append("{\"x\":");
                        WriteFloat(sb, v4.x);
                        sb.Append(",\"y\":");
                        WriteFloat(sb, v4.y);
                        sb.Append(",\"z\":");
                        WriteFloat(sb, v4.z);
                        sb.Append(",\"w\":");
                        WriteFloat(sb, v4.w);
                        sb.Append('}');
                        return true;
                    case Color col:
                        sb.Append("{\"r\":");
                        WriteFloat(sb, col.r);
                        sb.Append(",\"g\":");
                        WriteFloat(sb, col.g);
                        sb.Append(",\"b\":");
                        WriteFloat(sb, col.b);
                        sb.Append(",\"a\":");
                        WriteFloat(sb, col.a);
                        sb.Append('}');
                        return true;
                    case Quaternion q:
                        sb.Append("{\"x\":");
                        WriteFloat(sb, q.x);
                        sb.Append(",\"y\":");
                        WriteFloat(sb, q.y);
                        sb.Append(",\"z\":");
                        WriteFloat(sb, q.z);
                        sb.Append(",\"w\":");
                        WriteFloat(sb, q.w);
                        sb.Append('}');
                        return true;
                    default:
                        return false;
                }
            }

            #endregion

            private static void WriteFloat(StringHandler.IStringBuilder sb, float f)
            {
                if (float.IsNaN(f)) sb.Append("NaN");
                else if (float.IsPositiveInfinity(f)) sb.Append("Infinity");
                else if (float.IsNegativeInfinity(f)) sb.Append("-Infinity");
                else if (f == MathF.Truncate(f) && MathF.Abs(f) < 1e15f) WriteInt64(sb, (long)f);
                else sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
            }

            private static void WriteDouble(StringHandler.IStringBuilder sb, double d)
            {
                if (double.IsNaN(d)) sb.Append("NaN");
                else if (double.IsPositiveInfinity(d)) sb.Append("Infinity");
                else if (double.IsNegativeInfinity(d)) sb.Append("-Infinity");
                else if (d == Math.Truncate(d) && Math.Abs(d) < 1e15) WriteInt64(sb, (long)d);
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }

            // ===== 手工整数格式化（栈缓冲 + 单次 span 追加，零分配） =====

            private static void WriteInt64(StringHandler.IStringBuilder sb, long v)
            {
                Span<char> buffer = stackalloc char[21];
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

            private static void WriteArray(StringHandler.IStringBuilder sb, Array array, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                if (array.Length == 0)
                {
                    sb.Append("[]");
                    return;
                }

                // 类型化基元数组快速路径
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
                LoopGuard.PushReference(array);
                for (int i = 0; i < array.Length; i++)
                {
                    object element = array.GetValue(i);
                    if (LoopGuard.IsSerializingReference(element)) continue;
                    if (LoopGuard.WouldExceedDepth(element, depth)) continue;

                    if (i > 0) sb.Append(readable ? ", " : ",");
                    WriteValue(sb, element, removeNulls, readable, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
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

            private static void WriteList(StringHandler.IStringBuilder sb, IList list, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                if (list.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }

                // 类型化基元列表快速路径
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
                LoopGuard.PushReference(list);
                for (int i = 0; i < list.Count; i++)
                {
                    object element = list[i];
                    if (LoopGuard.IsSerializingReference(element)) continue;
                    if (LoopGuard.WouldExceedDepth(element, depth)) continue;

                    if (i > 0) sb.Append(readable ? ", " : ",");
                    WriteValue(sb, element, removeNulls, readable, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                sb.Append(']');
            }

            /// <summary>
            /// 字典序列化：简单 key 输出标准 JSON 对象格式；
            /// 复杂 key 回退 legacy 条目数组格式，解析端两种格式都接受。
            /// </summary>
            private static void WriteDictionary(StringHandler.IStringBuilder sb, IDictionary dictionary, Type dictType, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                if (dictionary.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }

                Type keyType = dictType.GetGenericArguments()[0];
                if (!JsonTypeUtil.IsStandardDictionaryKey(keyType))
                {
                    WriteDictionaryLegacy(sb, dictionary, removeNulls, readable, depth, depthLimit);
                    return;
                }

                sb.Append('{');
                LoopGuard.PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (LoopGuard.IsSerializingReference(entry.Value)) continue;
                    if (LoopGuard.WouldExceedDepth(entry.Value, depth)) continue;

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    WriteDictionaryKey(sb, entry.Key, keyType);
                    sb.Append(':');
                    if (readable) sb.Append(' ');
                    WriteValue(sb, entry.Value, removeNulls, readable, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
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
                    WriteEscapedString(sb, Convert.ToString(key, CultureInfo.InvariantCulture));
                }
            }

            private static void WriteDictionaryLegacy(StringHandler.IStringBuilder sb, IDictionary dictionary, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                sb.Append('[');
                LoopGuard.PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (LoopGuard.IsSerializingReference(entry.Value)) continue;
                    if (LoopGuard.WouldExceedDepth(entry.Value, depth)) continue;

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    sb.Append('{');
                    sb.Append("\"" + TypeConverter.KeyMember + "\":");
                    WriteValue(sb, entry.Key, removeNulls, readable, depth + 1, depthLimit);
                    sb.Append(",\"" + TypeConverter.ValueMember + "\":");
                    WriteValue(sb, entry.Value, removeNulls, readable, depth + 1, depthLimit);
                    sb.Append('}');
                }

                LoopGuard.PopReference();
                if (readable) sb.Append("\r\n").Append('\t', depth);
                sb.Append(']');
            }

            #endregion

            #region 对象 [OBJECTS]

            private static void WriteObject(StringHandler.IStringBuilder sb, object obj, Type type, ReflectionCache.TypeMeta meta, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                var fields = meta.SerializeFields;
                var properties = meta.SerializeProperties;

                if (fields.Length == 0 && properties.Length == 0)
                {
                    throw new GameException(StringUtility.Format(
                        "Type '{0}' has no serializable fields or properties. If it represents a value-like type, expose members or mark them with [JsonSerialize].", type.FullName));
                }

                LoopGuard.PushReference(obj);

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

                    if (LoopGuard.IsSerializingReference(value)) continue;
                    if (LoopGuard.WouldExceedDepth(value, depth)) continue;

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    WriteEscapedString(sb, fields[i].Name);
                    sb.Append(':');
                    if (readable) sb.Append(' ');
                    WriteValue(sb, value, removeNulls, readable, depth + 1, depthLimit);
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

                    if (LoopGuard.IsSerializingReference(value)) continue;
                    if (LoopGuard.WouldExceedDepth(value, depth)) continue;

                    if (isFirst) isFirst = false;
                    else sb.Append(',');

                    if (readable) sb.Append("\r\n").Append('\t', depth + 1);

                    WriteEscapedString(sb, properties[i].Name);
                    sb.Append(':');
                    if (readable) sb.Append(' ');
                    WriteValue(sb, value, removeNulls, readable, depth + 1, depthLimit);
                }

                if (readable) sb.Append("\r\n").Append('\t', depth);
                sb.Append('}');
                LoopGuard.PopReference();
            }

            #endregion

            #region 字符串转义 [ESCAPING]

            /// <summary>写入带引号的转义字符串。无转义字符的常见路径整串单次追加。</summary>
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
