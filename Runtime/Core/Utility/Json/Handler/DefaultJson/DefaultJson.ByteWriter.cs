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
        /// UTF8 字节序列化写入器。与 <see cref="Writer"/> 逻辑对称，但直接向线程本地池化
        /// byte buffer 写 UTF8 字节，供 IO/网络等字节载体场景跳过 string 中间态。
        /// </summary>
        /// <remarks>
        /// <para><b>输出契约</b>：与 <see cref="Writer.Serialize"/> 的紧凑格式输出 UTF8 编码后逐字节等价。</para>
        /// <para><b>缓冲策略</b>：每线程一块 scratch buffer（<c>[ThreadStatic]</c>，×2 扩容，异常路径不泄漏），
        /// 最终结果仅一次定长拷贝分配（与 string 版 ToString 的单次分配对等）。</para>
        /// <para><b>AOT 约束</b>：手动 UTF8 编码器（含代理对 → 4 字节序列、无效代理 → U+FFFD），
        /// 无表达式树/Emit/Utf8Formatter 依赖。</para>
        /// </remarks>
        internal static class ByteWriter
        {
            #region 变量 [VARIABLES]
            private const int INITIAL_CAPACITY = 256;

            [ThreadStatic]
            private static byte[] t_Scratch;

            // 引用环/深度守卫由共享 LoopGuard 处理（P0 消除重复）

            #endregion

            #region 入口 [ENTRY]
            /// <summary>序列化对象为 UTF8 JSON 字节（紧凑格式）。</summary>
            public static byte[] Serialize(object obj, bool removeNulls, int depthLimit)
            {
                LoopGuard.Begin();

                byte[] buf = t_Scratch ??= new byte[INITIAL_CAPACITY];
                int pos = 0;
                try
                {
                    WriteValue(ref buf, ref pos, obj, removeNulls, 0, depthLimit);
                }
                finally
                {
                    t_Scratch = buf;
                    LoopGuard.End();
                }

                byte[] result = new byte[pos];
                Array.Copy(buf, result, pos);
                return result;
            }

            #endregion

            #region 值分派 [VALUE DISPATCH]
            private static void WriteValue(ref byte[] buf, ref int pos, object value, bool removeNulls, int depth, int depthLimit)
            {
                // 安全网：成员/元素处已按 WouldExceedDepth 软截断，此处仅兜底未守卫路径（写 null 保证输出合法）。
                // 标量无递归风险，任何深度都照常写值（边界对象的字符串/数值字段不得丢失）。
                if (depth >= depthLimit && !LoopGuard.IsScalarValue(value))
                {
                    AppendAscii(ref buf, ref pos, "null");
                    return;
                }

                if (value == null)
                {
                    AppendAscii(ref buf, ref pos, "null");
                    return;
                }

                // Unity 伪 null（已销毁对象）按 null 输出，防止反射进原生侧对象
                if (value is UnityEngine.Object unityObject && unityObject == null)
                {
                    AppendAscii(ref buf, ref pos, "null");
                    return;
                }

                Type type = value.GetType();

                // 基元/字符串/枚举/已知 BCL 值类型：直接写值（跳过反射元数据查找——数组/集合元素热路径）
                if (type.IsPrimitive || type == typeof(string) || type.IsEnum ||
                    type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
                    type == typeof(TimeSpan) || type == typeof(Guid))
                {
                    WriteSimpleValue(ref buf, ref pos, value);
                    return;
                }

                // 常见 Unity 结构体直写快路径（绕过反射 FieldInfo.SetValue 装箱）。
                // 必须在容器/反射分发之前——Vector3 等既非基元也非 BCL 值类型，放 WriteSimpleValue 内永远不可达。
                if (TryWriteUnityStruct(ref buf, ref pos, value)) return;

                // 预序列化回调（元数据走缓存；仅复合类型可达此处）
                foreach (MethodInfo info in ReflectionCache.Get(type).BeforeSerializeMethods)
                {
                    info.Invoke(value, null);
                }

                if (type.IsArray)
                {
                    WriteArray(ref buf, ref pos, (Array)value, removeNulls, depth, depthLimit);
                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    WriteDictionary(ref buf, ref pos, (IDictionary)value, type, removeNulls, depth, depthLimit);
                    return;
                }

                if (value is IList list)
                {
                    WriteList(ref buf, ref pos, list, removeNulls, depth, depthLimit);
                    return;
                }

                WriteObject(ref buf, ref pos, value, type, removeNulls, depth, depthLimit);
            }

            /// <summary>写入简单值（基元/字符串/枚举）。与 string 版 Writer 的 void 语义对齐：不可处理类型抛错。</summary>
            private static void WriteSimpleValue(ref byte[] buf, ref int pos, object value)
            {
                switch (value)
                {
                    case bool b:
                        AppendAscii(ref buf, ref pos, b ? "true" : "false");
                        return;
                    case char c:
                        WriteEscapedChar(ref buf, ref pos, c);
                        return;
                    case string s:
                        WriteEscapedString(ref buf, ref pos, s);
                        return;
                    case float f:
                        WriteFloat(ref buf, ref pos, f);
                        return;
                    case double d:
                        WriteDouble(ref buf, ref pos, d);
                        return;
                    case decimal m:
                        AppendAscii(ref buf, ref pos, m.ToString(CultureInfo.InvariantCulture));
                        return;
                    case int i:
                        WriteInt64(ref buf, ref pos, i);
                        return;
                    case long l:
                        WriteInt64(ref buf, ref pos, l);
                        return;
                    case uint ui:
                        WriteUInt64(ref buf, ref pos, ui);
                        return;
                    case ulong ul:
                        WriteUInt64(ref buf, ref pos, ul);
                        return;
                    case byte by:
                        WriteUInt64(ref buf, ref pos, by);
                        return;
                    case sbyte sbv:
                        WriteInt64(ref buf, ref pos, sbv);
                        return;
                    case short sh:
                        WriteInt64(ref buf, ref pos, sh);
                        return;
                    case ushort ush:
                        WriteUInt64(ref buf, ref pos, ush);
                        return;
                    case DateTime dt:
                        WriteEscapedString(ref buf, ref pos, dt.ToString("o", CultureInfo.InvariantCulture));
                        return;
                    case DateTimeOffset dto:
                        WriteEscapedString(ref buf, ref pos, dto.ToString("o", CultureInfo.InvariantCulture));
                        return;
                    case TimeSpan ts:
                        WriteEscapedString(ref buf, ref pos, ts.ToString("c", CultureInfo.InvariantCulture));
                        return;
                    case Guid g:
                        WriteEscapedString(ref buf, ref pos, g.ToString("D"));
                        return;
                    default:
                        Type type = value.GetType();
                        if (type.IsEnum)
                        {
                            WriteEscapedString(ref buf, ref pos, value.ToString());
                            return;
                        }

                        throw new GameException(StringUtility.Format(
                            "Type '{0}' is not a simple value and cannot be written by WriteSimpleValue.", type.FullName));
                }
            }

            #region Unity 结构体直写快路径 [UNITY STRUCT FAST PATH]
            /// <summary>尝试直写常见 Unity 结构体（绕过反射）。返回 true 表示已处理。</summary>
            private static bool TryWriteUnityStruct(ref byte[] buf, ref int pos, object value)
            {
                switch (value)
                {
                    case Vector2 v2:
                        AppendAscii(ref buf, ref pos, "{\"x\":");
                        WriteFloat(ref buf, ref pos, v2.x);
                        AppendAscii(ref buf, ref pos, ",\"y\":");
                        WriteFloat(ref buf, ref pos, v2.y);
                        Append(ref buf, ref pos, (byte)'}');
                        return true;
                    case Vector3 v3:
                        AppendAscii(ref buf, ref pos, "{\"x\":");
                        WriteFloat(ref buf, ref pos, v3.x);
                        AppendAscii(ref buf, ref pos, ",\"y\":");
                        WriteFloat(ref buf, ref pos, v3.y);
                        AppendAscii(ref buf, ref pos, ",\"z\":");
                        WriteFloat(ref buf, ref pos, v3.z);
                        Append(ref buf, ref pos, (byte)'}');
                        return true;
                    case Vector4 v4:
                        AppendAscii(ref buf, ref pos, "{\"x\":");
                        WriteFloat(ref buf, ref pos, v4.x);
                        AppendAscii(ref buf, ref pos, ",\"y\":");
                        WriteFloat(ref buf, ref pos, v4.y);
                        AppendAscii(ref buf, ref pos, ",\"z\":");
                        WriteFloat(ref buf, ref pos, v4.z);
                        AppendAscii(ref buf, ref pos, ",\"w\":");
                        WriteFloat(ref buf, ref pos, v4.w);
                        Append(ref buf, ref pos, (byte)'}');
                        return true;
                    case Color col:
                        AppendAscii(ref buf, ref pos, "{\"r\":");
                        WriteFloat(ref buf, ref pos, col.r);
                        AppendAscii(ref buf, ref pos, ",\"g\":");
                        WriteFloat(ref buf, ref pos, col.g);
                        AppendAscii(ref buf, ref pos, ",\"b\":");
                        WriteFloat(ref buf, ref pos, col.b);
                        AppendAscii(ref buf, ref pos, ",\"a\":");
                        WriteFloat(ref buf, ref pos, col.a);
                        Append(ref buf, ref pos, (byte)'}');
                        return true;
                    case Quaternion q:
                        AppendAscii(ref buf, ref pos, "{\"x\":");
                        WriteFloat(ref buf, ref pos, q.x);
                        AppendAscii(ref buf, ref pos, ",\"y\":");
                        WriteFloat(ref buf, ref pos, q.y);
                        AppendAscii(ref buf, ref pos, ",\"z\":");
                        WriteFloat(ref buf, ref pos, q.z);
                        AppendAscii(ref buf, ref pos, ",\"w\":");
                        WriteFloat(ref buf, ref pos, q.w);
                        Append(ref buf, ref pos, (byte)'}');
                        return true;
                    default:
                        return false;
                }
            }

            #endregion

            private static void WriteFloat(ref byte[] buf, ref int pos, float f)
            {
                if (float.IsNaN(f)) AppendAscii(ref buf, ref pos, "NaN");
                else if (float.IsPositiveInfinity(f)) AppendAscii(ref buf, ref pos, "Infinity");
                else if (float.IsNegativeInfinity(f)) AppendAscii(ref buf, ref pos, "-Infinity");
                else if (f == MathF.Truncate(f) && MathF.Abs(f) < 1e15f) WriteInt64(ref buf, ref pos, (long)f); // 整值快路径（跳过昂贵的 "R" 格式化）
                else AppendAscii(ref buf, ref pos, f.ToString("R", CultureInfo.InvariantCulture));
            }

            private static void WriteDouble(ref byte[] buf, ref int pos, double d)
            {
                if (double.IsNaN(d)) AppendAscii(ref buf, ref pos, "NaN");
                else if (double.IsPositiveInfinity(d)) AppendAscii(ref buf, ref pos, "Infinity");
                else if (double.IsNegativeInfinity(d)) AppendAscii(ref buf, ref pos, "-Infinity");
                else if (d == Math.Truncate(d) && Math.Abs(d) < 1e15) WriteInt64(ref buf, ref pos, (long)d); // 整值快路径
                else AppendAscii(ref buf, ref pos, d.ToString("R", CultureInfo.InvariantCulture));
            }

            // ===== 手工整数格式化（零分配；数字低位在前写入后原地反转） =====

            private static void WriteInt64(ref byte[] buf, ref int pos, long v)
            {
                Ensure(ref buf, pos, 21); // '-' + 20 位数字
                if (v < 0)
                {
                    buf[pos++] = (byte)'-';
                    // long.MinValue 取负溢出，经 unchecked 转 ulong 绝对值处理
                    WriteUInt64Digits(ref buf, ref pos, unchecked((ulong)(-(v + 1)) + 1UL));
                    return;
                }

                WriteUInt64Digits(ref buf, ref pos, (ulong)v);
            }

            private static void WriteUInt64(ref byte[] buf, ref int pos, ulong v)
            {
                Ensure(ref buf, pos, 20);
                WriteUInt64Digits(ref buf, ref pos, v);
            }

            private static void WriteUInt64Digits(ref byte[] buf, ref int pos, ulong v)
            {
                if (v == 0)
                {
                    buf[pos++] = (byte)'0';
                    return;
                }

                int digitStart = pos;
                while (v >= 10)
                {
                    buf[pos++] = (byte)('0' + (v % 10));
                    v /= 10;
                }

                buf[pos++] = (byte)('0' + v);

                // 原地反转数字序列（低位在前 → 高位在前）
                int left = digitStart, right = pos - 1;
                while (left < right)
                {
                    byte tmp = buf[left];
                    buf[left] = buf[right];
                    buf[right] = tmp;
                    left++;
                    right--;
                }
            }

            #endregion

            #region 容器 [CONTAINERS]
            private static void WriteArray(ref byte[] buf, ref int pos, Array array, bool removeNulls, int depth, int depthLimit)
            {
                if (array.Length == 0)
                {
                    AppendAscii(ref buf, ref pos, "[]");
                    return;
                }

                // 类型化基元数组快速路径：具体类型模式匹配（AOT 安全），消除逐元素 Array.GetValue 装箱与值分派。
                // 注意：此处为显式内联循环而非委托骨架（string 版 Writer 的 WritePrimitiveArray 模式）——
                // ref byte[]/ref int 参数无法被 lambda 捕获（CS1628），委托仅适用于无 ref 参数的 string 路径。
                // 两版骨架语义等价，修改分隔符/守卫行为时必须双侧同步。
                switch (array)
                {
                    case int[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case long[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case float[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteFloat(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case double[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteDouble(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case bool[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            AppendAscii(ref buf, ref pos, a[i] ? "true" : "false");
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case uint[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteUInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case ulong[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteUInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case short[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case ushort[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteUInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case byte[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteUInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case sbyte[] a:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteInt64(ref buf, ref pos, a[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                }

                Append(ref buf, ref pos, (byte)'[');
                LoopGuard.PushReference(array);
                for (int i = 0; i < array.Length; i++)
                {
                    object element = array.GetValue(i);
                    if (LoopGuard.IsSerializingReference(element)) continue; // 引用环：跳过元素
                    if (LoopGuard.WouldExceedDepth(element, depth)) continue; // 深度超限：软截断

                    if (i > 0) Append(ref buf, ref pos, (byte)',');
                    WriteValue(ref buf, ref pos, element, removeNulls, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                Append(ref buf, ref pos, (byte)']');
            }

            private static void WriteList(ref byte[] buf, ref int pos, IList list, bool removeNulls, int depth, int depthLimit)
            {
                if (list.Count == 0)
                {
                    AppendAscii(ref buf, ref pos, "[]");
                    return;
                }

                // 类型化基元列表快速路径：消除逐元素接口索引器装箱与值分派
                switch (list)
                {
                    case List<int> l:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteInt64(ref buf, ref pos, l[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case List<long> l:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteInt64(ref buf, ref pos, l[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case List<float> l:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteFloat(ref buf, ref pos, l[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case List<double> l:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            WriteDouble(ref buf, ref pos, l[i]);
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                    case List<bool> l:
                    {
                        Append(ref buf, ref pos, (byte)'[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) Append(ref buf, ref pos, (byte)',');
                            AppendAscii(ref buf, ref pos, l[i] ? "true" : "false");
                        }

                        Append(ref buf, ref pos, (byte)']');
                        return;
                    }
                }

                Append(ref buf, ref pos, (byte)'[');
                LoopGuard.PushReference(list);
                for (int i = 0; i < list.Count; i++)
                {
                    object element = list[i];
                    if (LoopGuard.IsSerializingReference(element)) continue; // 引用环：跳过元素
                    if (LoopGuard.WouldExceedDepth(element, depth)) continue; // 深度超限：软截断

                    if (i > 0) Append(ref buf, ref pos, (byte)',');
                    WriteValue(ref buf, ref pos, element, removeNulls, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                Append(ref buf, ref pos, (byte)']');
            }

            private static void WriteDictionary(ref byte[] buf, ref int pos, IDictionary dictionary, Type dictType, bool removeNulls, int depth, int depthLimit)
            {
                if (dictionary.Count == 0)
                {
                    AppendAscii(ref buf, ref pos, "{}");
                    return;
                }

                Type keyType = dictType.GetGenericArguments()[0];
                if (!JsonTypeUtil.IsStandardDictionaryKey(keyType))
                {
                    WriteDictionaryLegacy(ref buf, ref pos, dictionary, removeNulls, depth, depthLimit);
                    return;
                }

                Append(ref buf, ref pos, (byte)'{');
                LoopGuard.PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    // 引用环（值指向字典自身或其祖先）：跳过该键值对
                    if (LoopGuard.IsSerializingReference(entry.Value)) continue;
                    if (LoopGuard.WouldExceedDepth(entry.Value, depth)) continue; // 深度超限：软截断

                    if (isFirst) isFirst = false;
                    else Append(ref buf, ref pos, (byte)',');

                    WriteDictionaryKey(ref buf, ref pos, entry.Key, keyType);
                    Append(ref buf, ref pos, (byte)':');
                    WriteValue(ref buf, ref pos, entry.Value, removeNulls, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                Append(ref buf, ref pos, (byte)'}');
            }

            private static void WriteDictionaryKey(ref byte[] buf, ref int pos, object key, Type keyType)
            {
                if (key is string keyString)
                {
                    WriteEscapedString(ref buf, ref pos, keyString);
                }
                else if (keyType.IsEnum)
                {
                    WriteEscapedString(ref buf, ref pos, key.ToString());
                }
                else
                {
                    WriteEscapedString(ref buf, ref pos, Convert.ToString(key, CultureInfo.InvariantCulture));
                }
            }

            private static void WriteDictionaryLegacy(ref byte[] buf, ref int pos, IDictionary dictionary, bool removeNulls, int depth, int depthLimit)
            {
                Append(ref buf, ref pos, (byte)'[');
                LoopGuard.PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (LoopGuard.IsSerializingReference(entry.Value)) continue;
                    if (LoopGuard.WouldExceedDepth(entry.Value, depth)) continue;

                    if (isFirst) isFirst = false;
                    else Append(ref buf, ref pos, (byte)',');

                    AppendAscii(ref buf, ref pos, "{\"" + TypeConverter.KeyMember + "\":");
                    WriteValue(ref buf, ref pos, entry.Key, removeNulls, depth + 1, depthLimit);
                    AppendAscii(ref buf, ref pos, ",\"" + TypeConverter.ValueMember + "\":");
                    WriteValue(ref buf, ref pos, entry.Value, removeNulls, depth + 1, depthLimit);
                    Append(ref buf, ref pos, (byte)'}');
                }

                LoopGuard.PopReference();
                Append(ref buf, ref pos, (byte)']');
            }

            #endregion

            #region 对象 [OBJECTS]
            private static void WriteObject(ref byte[] buf, ref int pos, object obj, Type type, bool removeNulls, int depth, int depthLimit)
            {
                var meta = ReflectionCache.Get(type);
                var fields = meta.SerializeFields;
                var properties = meta.SerializeProperties;

                if (fields.Length == 0 && properties.Length == 0)
                {
                    throw new GameException(StringUtility.Format(
                        "Type '{0}' has no serializable fields or properties. If it represents a value-like type, expose members or mark them with [JsonSerialize].", type.FullName));
                }

                // 容器入口压栈（值类型对象无环可能，不入栈）；使自引用根对象可被成员检查识别
                if (!type.IsValueType) LoopGuard.PushReference(obj);

                Append(ref buf, ref pos, (byte)'{');

                bool isFirst = true;

                for (int i = 0; i < fields.Length; i++)
                {
                    object value = fields[i].Field.GetValue(obj);
                    if (value == null && removeNulls) continue;

                    // 引用环：跳过整个成员（名称+值），对齐 Newtonsoft ReferenceLoopHandling.Ignore
                    if (LoopGuard.IsSerializingReference(value)) continue;
                    if (LoopGuard.WouldExceedDepth(value, depth)) continue; // 深度超限：软截断

                    if (isFirst) isFirst = false;
                    else Append(ref buf, ref pos, (byte)',');

                    WriteEscapedString(ref buf, ref pos, fields[i].Name);
                    Append(ref buf, ref pos, (byte)':');
                    WriteValue(ref buf, ref pos, value, removeNulls, depth + 1, depthLimit);
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
                    if (LoopGuard.IsSerializingReference(value)) continue;
                    if (LoopGuard.WouldExceedDepth(value, depth)) continue; // 深度超限：软截断

                    if (isFirst) isFirst = false;
                    else Append(ref buf, ref pos, (byte)',');

                    WriteEscapedString(ref buf, ref pos, properties[i].Name);
                    Append(ref buf, ref pos, (byte)':');
                    WriteValue(ref buf, ref pos, value, removeNulls, depth + 1, depthLimit);
                }

                Append(ref buf, ref pos, (byte)'}');
                if (!type.IsValueType) LoopGuard.PopReference();
            }

            #endregion

            #region 字符串转义与 UTF8 编码 [ESCAPING/UTF8]
            /// <summary>写入带引号的转义字符串（逐字符转义 + 手动 UTF8 编码，含代理对）。一次性预留缓冲，循环内零边界检查调用。</summary>
            private static void WriteEscapedString(ref byte[] buf, ref int pos, string s)
            {
                // 最坏情况：每个字符转义为 6 字节（\uXXXX）+ 两侧引号；一次 Ensure 后循环内直接写
                Ensure(ref buf, pos, s.Length * 6 + 2);
                buf[pos++] = (byte)'"';

                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '\\':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'\\';
                            break;
                        case '"':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'"';
                            break;
                        case '\b':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'b';
                            break;
                        case '\f':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'f';
                            break;
                        case '\n':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'n';
                            break;
                        case '\r':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'r';
                            break;
                        case '\t':
                            buf[pos++] = (byte)'\\';
                            buf[pos++] = (byte)'t';
                            break;
                        default:
                            if (c < ' ')
                            {
                                buf[pos++] = (byte)'\\';
                                buf[pos++] = (byte)'u';
                                AppendHex4(ref buf, ref pos, c);
                            }
                            else if (c < 0x80)
                            {
                                buf[pos++] = (byte)c;
                            }
                            else if (char.IsHighSurrogate(c))
                            {
                                // 代理对：与低位代理合并为码点编码；孤立代理替换为 U+FFFD
                                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                                {
                                    WriteUtf8Rune(ref buf, ref pos, CodePointFromSurrogatePair(c, s[i + 1]));
                                    i++;
                                }
                                else
                                {
                                    WriteUtf8Rune(ref buf, ref pos, 0xFFFD);
                                }
                            }
                            else if (char.IsLowSurrogate(c))
                            {
                                WriteUtf8Rune(ref buf, ref pos, 0xFFFD);
                            }
                            else
                            {
                                WriteUtf8Rune(ref buf, ref pos, c);
                            }

                            break;
                    }
                }

                buf[pos++] = (byte)'"';
            }

            private static void WriteEscapedChar(ref byte[] buf, ref int pos, char c)
            {
                Append(ref buf, ref pos, (byte)'"');
                switch (c)
                {
                    case '\\':
                        AppendAscii(ref buf, ref pos, "\\\\");
                        break;
                    case '"':
                        AppendAscii(ref buf, ref pos, "\\\"");
                        break;
                    case '\b':
                        AppendAscii(ref buf, ref pos, "\\b");
                        break;
                    case '\f':
                        AppendAscii(ref buf, ref pos, "\\f");
                        break;
                    case '\n':
                        AppendAscii(ref buf, ref pos, "\\n");
                        break;
                    case '\r':
                        AppendAscii(ref buf, ref pos, "\\r");
                        break;
                    case '\t':
                        AppendAscii(ref buf, ref pos, "\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            AppendAscii(ref buf, ref pos, "\\u");
                            AppendHex4(ref buf, ref pos, c);
                        }
                        else if (char.IsSurrogate(c))
                        {
                            WriteUtf8Rune(ref buf, ref pos, 0xFFFD);
                        }
                        else
                        {
                            WriteUtf8Rune(ref buf, ref pos, c);
                        }

                        break;
                }

                Append(ref buf, ref pos, (byte)'"');
            }

            private static uint CodePointFromSurrogatePair(char high, char low)
            {
                return 0x10000u + ((uint)(high - 0xD800) << 10) + (uint)(low - 0xDC00);
            }

            /// <summary>将码点（≤0x10FFFF）编码为 UTF8（1-4 字节）。</summary>
            private static void WriteUtf8Rune(ref byte[] buf, ref int pos, uint rune)
            {
                if (rune < 0x80)
                {
                    Append(ref buf, ref pos, (byte)rune);
                }
                else if (rune < 0x800)
                {
                    Ensure(ref buf, pos, 2);
                    buf[pos++] = (byte)(0xC0 | (rune >> 6));
                    buf[pos++] = (byte)(0x80 | (rune & 0x3F));
                }
                else if (rune < 0x10000)
                {
                    Ensure(ref buf, pos, 3);
                    buf[pos++] = (byte)(0xE0 | (rune >> 12));
                    buf[pos++] = (byte)(0x80 | ((rune >> 6) & 0x3F));
                    buf[pos++] = (byte)(0x80 | (rune & 0x3F));
                }
                else
                {
                    Ensure(ref buf, pos, 4);
                    buf[pos++] = (byte)(0xF0 | (rune >> 18));
                    buf[pos++] = (byte)(0x80 | ((rune >> 12) & 0x3F));
                    buf[pos++] = (byte)(0x80 | ((rune >> 6) & 0x3F));
                    buf[pos++] = (byte)(0x80 | (rune & 0x3F));
                }
            }

            /// <summary>写入 4 位大写十六进制（\uXXXX 转义用，纯 ASCII）。</summary>
            private static void AppendHex4(ref byte[] buf, ref int pos, char c)
            {
                Ensure(ref buf, pos, 4);
                uint v = c;
                for (int shift = 12; shift >= 0; shift -= 4)
                {
                    uint nibble = (v >> shift) & 0xF;
                    buf[pos++] = (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
                }
            }

            #endregion

            #region 缓冲工具 [BUFFER UTILITIES]
            private static void Ensure(ref byte[] buf, int pos, int count)
            {
                int required = pos + count;
                if (required <= buf.Length) return;

                int newSize = buf.Length * 2;
                while (newSize < required) newSize *= 2;

                var grown = new byte[newSize];
                Array.Copy(buf, grown, pos);
                buf = grown;
            }

            private static void Append(ref byte[] buf, ref int pos, byte b)
            {
                if (pos < buf.Length)
                {
                    buf[pos++] = b;
                    return;
                }

                Ensure(ref buf, pos, 1);
                buf[pos++] = b;
            }

            /// <summary>追加 ASCII 文本（数字/标量字面量等保证 ASCII 的内容；一次性预留，循环内直接写）。</summary>
            private static void AppendAscii(ref byte[] buf, ref int pos, string s)
            {
                if (string.IsNullOrEmpty(s)) return;

                Ensure(ref buf, pos, s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c < 0x80)
                    {
                        buf[pos++] = (byte)c;
                    }
                    else
                    {
                        WriteUtf8Rune(ref buf, ref pos, c);
                    }
                }
            }

            #endregion
        }
    }
}
