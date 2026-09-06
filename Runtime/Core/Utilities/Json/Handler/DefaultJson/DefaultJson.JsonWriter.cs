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
        /// 统一序列化写入器（string / UTF8 字节双路径的单一结构实现）。
        /// </summary>
        /// <remarks>
        /// <para><b>单一来源</b>：分派、容器遍历、引用环/深度守卫、反射成员遍历、Unity 结构体直写、
        /// 类型化基元数组快路径——全部只实现一次；值的编码差异（char / UTF8、转义、数字格式化）
        /// 下沉到 <see cref="IJsonSink"/> 的两个实现。</para>
        /// <para><b>引用环</b>：对齐 <see cref="NewtonsoftJsonHandler"/> 既定的 ReferenceLoopHandling.Ignore 语义
        /// ——跳过构成环的成员/元素（不抛错、不无限递归）；深度上限软截断（跳过+警告）。</para>
        /// <para><b>输出契约</b>：字节路径紧凑格式，与字符串路径紧凑格式 UTF8 编码逐字节等价；
        /// readable 缩进仅字符串入口可达（字节入口不暴露，契约保持）。</para>
        /// <para><b>AOT 约束</b>：无表达式树/Reflection.Emit；Sink 为 struct 经 ref 传递（受约束调用无装箱），
        /// 接口按"每值"粒度分发，开销由原语内部工作量摊薄。</para>
        /// </remarks>
        internal static class JsonWriter<TSink> where TSink : struct, IJsonSink
        {
            #region 入口 [ENTRY]

            /// <summary>
            /// 统一写入入口（Sink 由调用方构造后传入；LoopGuard 由调用方管理）。
            /// 字符串/字节路径的差异（池化 builder / scratch 租还）在 DefaultJson 入口处理，
            /// 结构写入逻辑在此单一实现。
            /// </summary>
            internal static void WriteAll(ref TSink sink, object obj, bool removeNulls, bool readable, int depthLimit)
            {
                WriteValue(ref sink, obj, removeNulls, readable, 0, depthLimit);
            }

            #endregion

            #region 值分派 [VALUE DISPATCH]

            private static void WriteValue(ref TSink sink, object value, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                // 安全网：成员/元素处已按 LoopGuard.WouldExceedDepth 软截断，此处仅兜底未守卫路径。
                // 标量无递归风险，任何深度都照常写值。
                if (depth >= depthLimit && !LoopGuard.IsScalarValue(value))
                {
                    sink.WriteAscii("null");
                    return;
                }

                if (value == null)
                {
                    sink.WriteAscii("null");
                    return;
                }

                // Unity 伪 null（已销毁对象）按 null 输出，防止反射进原生侧对象
                if (value is UnityEngine.Object unityObject && unityObject == null)
                {
                    sink.WriteAscii("null");
                    return;
                }

                Type type = value.GetType();

                // 基元/字符串/枚举/已知 BCL 值类型：直接写值（跳过反射元数据查找——数组/集合元素热路径）
                if (type.IsPrimitive || type == typeof(string) || type.IsEnum ||
                    type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
                    type == typeof(TimeSpan) || type == typeof(Guid))
                {
                    WriteSimpleValue(ref sink, value);
                    return;
                }

                // 常见 Unity 结构体直写快路径（绕过反射 FieldInfo.SetValue 装箱）。
                // 必须在容器/反射分发之前——Vector3 等既非基元也非 BCL 值类型，放 WriteSimpleValue 内永远不可达。
                if (TryWriteUnityStruct(ref sink, value)) return;

                // 预序列化回调（元数据走缓存）
                var meta = ReflectionCache.Get(type);
                foreach (MethodInfo info in meta.BeforeSerializeMethods)
                {
                    info.Invoke(value, null);
                }

                if (type.IsArray)
                {
                    WriteArray(ref sink, (Array)value, removeNulls, readable, depth, depthLimit);
                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    WriteDictionary(ref sink, (IDictionary)value, type, removeNulls, readable, depth, depthLimit);
                    return;
                }

                if (value is IList list)
                {
                    WriteList(ref sink, list, removeNulls, readable, depth, depthLimit);
                    return;
                }

                WriteObject(ref sink, value, type, meta, removeNulls, readable, depth, depthLimit);
            }

            /// <summary>写入简单值（基元/字符串/枚举/已知可转换类型）。不可处理类型抛错。</summary>
            private static void WriteSimpleValue(ref TSink sink, object value)
            {
                switch (value)
                {
                    case bool b:
                        sink.WriteAscii(b ? "true" : "false");
                        return;
                    case char c:
                        sink.WriteEscaped(c);
                        return;
                    case string s:
                        sink.WriteEscaped(s);
                        return;
                    case float f:
                        WriteFloat(ref sink, f);
                        return;
                    case double d:
                        WriteDouble(ref sink, d);
                        return;
                    case decimal m:
                        sink.WriteAscii(m.ToString(CultureInfo.InvariantCulture));
                        return;
                    case int i:
                        sink.WriteInt64(i);
                        return;
                    case long l:
                        sink.WriteInt64(l);
                        return;
                    case uint ui:
                        sink.WriteUInt64(ui);
                        return;
                    case ulong ul:
                        sink.WriteUInt64(ul);
                        return;
                    case byte by:
                        sink.WriteUInt64(by);
                        return;
                    case sbyte sbv:
                        sink.WriteInt64(sbv);
                        return;
                    case short sh:
                        sink.WriteInt64(sh);
                        return;
                    case ushort ush:
                        sink.WriteUInt64(ush);
                        return;
                    case DateTime dt:
                        sink.WriteEscaped(dt.ToString("o", CultureInfo.InvariantCulture));
                        return;
                    case DateTimeOffset dto:
                        sink.WriteEscaped(dto.ToString("o", CultureInfo.InvariantCulture));
                        return;
                    case TimeSpan ts:
                        sink.WriteEscaped(ts.ToString("c", CultureInfo.InvariantCulture));
                        return;
                    case Guid g:
                        sink.WriteEscaped(g.ToString("D"));
                        return;
                    default:
                        Type type = value.GetType();
                        if (type.IsEnum)
                        {
                            // 枚举以名称字符串输出（与解析端对称；数值枚举解析端同样支持）
                            sink.WriteEscaped(value.ToString());
                            return;
                        }

                        throw new GameException(StringUtility.Format(
                            "Type '{0}' is not a simple value and cannot be written by WriteSimpleValue.", type.FullName));
                }
            }

            #region Unity 结构体直写快路径 [UNITY STRUCT FAST PATH]

            /// <summary>尝试直写常见 Unity 结构体（绕过反射）。返回 true 表示已处理。</summary>
            private static bool TryWriteUnityStruct(ref TSink sink, object value)
            {
                switch (value)
                {
                    case Vector2 v2:
                        sink.WriteAscii("{\"x\":");
                        WriteFloat(ref sink, v2.x);
                        sink.WriteAscii(",\"y\":");
                        WriteFloat(ref sink, v2.y);
                        sink.WriteAscii("}");
                        return true;
                    case Vector3 v3:
                        sink.WriteAscii("{\"x\":");
                        WriteFloat(ref sink, v3.x);
                        sink.WriteAscii(",\"y\":");
                        WriteFloat(ref sink, v3.y);
                        sink.WriteAscii(",\"z\":");
                        WriteFloat(ref sink, v3.z);
                        sink.WriteAscii("}");
                        return true;
                    case Vector4 v4:
                        sink.WriteAscii("{\"x\":");
                        WriteFloat(ref sink, v4.x);
                        sink.WriteAscii(",\"y\":");
                        WriteFloat(ref sink, v4.y);
                        sink.WriteAscii(",\"z\":");
                        WriteFloat(ref sink, v4.z);
                        sink.WriteAscii(",\"w\":");
                        WriteFloat(ref sink, v4.w);
                        sink.WriteAscii("}");
                        return true;
                    case Color col:
                        sink.WriteAscii("{\"r\":");
                        WriteFloat(ref sink, col.r);
                        sink.WriteAscii(",\"g\":");
                        WriteFloat(ref sink, col.g);
                        sink.WriteAscii(",\"b\":");
                        WriteFloat(ref sink, col.b);
                        sink.WriteAscii(",\"a\":");
                        WriteFloat(ref sink, col.a);
                        sink.WriteAscii("}");
                        return true;
                    case Quaternion q:
                        sink.WriteAscii("{\"x\":");
                        WriteFloat(ref sink, q.x);
                        sink.WriteAscii(",\"y\":");
                        WriteFloat(ref sink, q.y);
                        sink.WriteAscii(",\"z\":");
                        WriteFloat(ref sink, q.z);
                        sink.WriteAscii(",\"w\":");
                        WriteFloat(ref sink, q.w);
                        sink.WriteAscii("}");
                        return true;
                    default:
                        return false;
                }
            }

            #endregion

            private static void WriteFloat(ref TSink sink, float f)
            {
                if (float.IsNaN(f)) sink.WriteAscii("NaN");
                else if (float.IsPositiveInfinity(f)) sink.WriteAscii("Infinity");
                else if (float.IsNegativeInfinity(f)) sink.WriteAscii("-Infinity");
                else if (f == MathF.Truncate(f) && MathF.Abs(f) < 1e15f) sink.WriteInt64((long)f); // 整值快路径（<2^53 转换精确）
                else sink.WriteAscii(f.ToString("R", CultureInfo.InvariantCulture));
            }

            private static void WriteDouble(ref TSink sink, double d)
            {
                if (double.IsNaN(d)) sink.WriteAscii("NaN");
                else if (double.IsPositiveInfinity(d)) sink.WriteAscii("Infinity");
                else if (double.IsNegativeInfinity(d)) sink.WriteAscii("-Infinity");
                else if (d == Math.Truncate(d) && Math.Abs(d) < 1e15) sink.WriteInt64((long)d); // 整值快路径
                else sink.WriteAscii(d.ToString("R", CultureInfo.InvariantCulture));
            }

            #endregion

            #region 容器 [CONTAINERS]

            private static void WriteArray(ref TSink sink, Array array, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                if (array.Length == 0)
                {
                    sink.WriteAscii("[]");
                    return;
                }

                // 类型化基元数组快速路径：具体类型模式匹配（AOT 安全），消除逐元素 Array.GetValue 装箱与值分派。
                // 单一实现（此前 string/byte 双 Writer 各一份；ref 参数不可被 lambda 捕获，故为显式内联循环）
                switch (array)
                {
                    case int[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case long[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case float[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            WriteFloat(ref sink, a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case double[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            WriteDouble(ref sink, a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case bool[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteAscii(a[i] ? "true" : "false");
                        }

                        sink.WriteAscii(']');
                        return;
                    case uint[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteUInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case ulong[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteUInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case short[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case ushort[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteUInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case byte[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteUInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case sbyte[] a:
                        sink.WriteAscii('[');
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteInt64(a[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                }

                sink.WriteAscii('[');
                LoopGuard.PushReference(array);
                for (int i = 0; i < array.Length; i++)
                {
                    object element = array.GetValue(i);
                    if (LoopGuard.IsSerializingReference(element)) continue; // 引用环：跳过元素
                    if (LoopGuard.WouldExceedDepth(element, depth, depthLimit)) continue; // 深度超限：软截断

                    if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                    WriteValue(ref sink, element, removeNulls, readable, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                sink.WriteAscii(']');
            }

            private static void WriteList(ref TSink sink, IList list, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                if (list.Count == 0)
                {
                    sink.WriteAscii("[]");
                    return;
                }

                // 类型化列表快速路径（高频类型；字符串列表免除逐元素值分派）
                switch (list)
                {
                    case List<int> l:
                        sink.WriteAscii('[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteInt64(l[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case List<long> l:
                        sink.WriteAscii('[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteInt64(l[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case List<float> l:
                        sink.WriteAscii('[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            WriteFloat(ref sink, l[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case List<double> l:
                        sink.WriteAscii('[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            WriteDouble(ref sink, l[i]);
                        }

                        sink.WriteAscii(']');
                        return;
                    case List<bool> l:
                        sink.WriteAscii('[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            sink.WriteAscii(l[i] ? "true" : "false");
                        }

                        sink.WriteAscii(']');
                        return;
                    case List<string> l:
                        sink.WriteAscii('[');
                        for (int i = 0; i < l.Count; i++)
                        {
                            if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                            string s = l[i];
                            if (s == null) sink.WriteAscii("null");
                            else sink.WriteEscaped(s);
                        }

                        sink.WriteAscii(']');
                        return;
                }

                sink.WriteAscii('[');
                LoopGuard.PushReference(list);
                for (int i = 0; i < list.Count; i++)
                {
                    object element = list[i];
                    if (LoopGuard.IsSerializingReference(element)) continue;
                    if (LoopGuard.WouldExceedDepth(element, depth, depthLimit)) continue;

                    if (i > 0) sink.WriteAscii(readable ? ", " : ",");
                    WriteValue(ref sink, element, removeNulls, readable, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                sink.WriteAscii(']');
            }

            /// <summary>
            /// 字典序列化：简单 key 输出标准 JSON 对象格式；
            /// 复杂 key 回退 legacy 条目数组格式，解析端两种格式都接受。
            /// </summary>
            private static void WriteDictionary(ref TSink sink, IDictionary dictionary, Type dictType, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                if (dictionary.Count == 0)
                {
                    sink.WriteAscii("{}");
                    return;
                }

                Type keyType = GenericArgsCache.Get(dictType)[0];
                if (!JsonTypeUtil.IsStandardDictionaryKey(keyType))
                {
                    WriteDictionaryLegacy(ref sink, dictionary, removeNulls, readable, depth, depthLimit);
                    return;
                }

                sink.WriteAscii('{');
                LoopGuard.PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (LoopGuard.IsSerializingReference(entry.Value)) continue;
                    if (LoopGuard.WouldExceedDepth(entry.Value, depth, depthLimit)) continue;

                    if (isFirst) isFirst = false;
                    else sink.WriteAscii(',');

                    if (readable) sink.WriteIndent(depth + 1);

                    WriteDictionaryKey(ref sink, entry.Key, keyType);
                    sink.WriteAscii(':');
                    if (readable) sink.WriteAscii(' ');
                    WriteValue(ref sink, entry.Value, removeNulls, readable, depth + 1, depthLimit);
                }

                LoopGuard.PopReference();
                if (readable) sink.WriteIndent(depth);
                sink.WriteAscii('}');
            }

            private static void WriteDictionaryKey(ref TSink sink, object key, Type keyType)
            {
                if (key is string keyString)
                {
                    sink.WriteEscaped(keyString);
                }
                else if (keyType.IsEnum)
                {
                    sink.WriteEscaped(key.ToString());
                }
                else
                {
                    // 数值/bool/char/Guid/DateTime 等：字符串化的标准 key
                    sink.WriteEscaped(Convert.ToString(key, CultureInfo.InvariantCulture));
                }
            }

            private static void WriteDictionaryLegacy(ref TSink sink, IDictionary dictionary, bool removeNulls, bool readable, int depth, int depthLimit)
            {
                sink.WriteAscii('[');
                LoopGuard.PushReference(dictionary);
                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (LoopGuard.IsSerializingReference(entry.Value)) continue;
                    if (LoopGuard.WouldExceedDepth(entry.Value, depth, depthLimit)) continue;

                    if (isFirst) isFirst = false;
                    else sink.WriteAscii(',');

                    if (readable) sink.WriteIndent(depth + 1);

                    sink.WriteAscii("{\"" + TypeConverter.KeyMember + "\":");
                    WriteValue(ref sink, entry.Key, removeNulls, readable, depth + 1, depthLimit);
                    sink.WriteAscii(",\"" + TypeConverter.ValueMember + "\":");
                    WriteValue(ref sink, entry.Value, removeNulls, readable, depth + 1, depthLimit);
                    sink.WriteAscii("}");
                }

                LoopGuard.PopReference();
                if (readable) sink.WriteIndent(depth);
                sink.WriteAscii(']');
            }

            #endregion

            #region 对象 [OBJECTS]

            private static void WriteObject(ref TSink sink, object obj, Type type, ReflectionCache.TypeMeta meta, bool removeNulls, bool readable, int depth, int depthLimit)
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
                    sink.WriteIndent(depth);
                }

                sink.WriteAscii('{');

                bool isFirst = true;

                for (int i = 0; i < fields.Length; i++)
                {
                    object value = fields[i].Field.GetValue(obj);
                    if (value == null && removeNulls) continue;

                    // 引用环：跳过整个成员（名称+值），对齐 Newtonsoft ReferenceLoopHandling.Ignore
                    if (LoopGuard.IsSerializingReference(value)) continue;
                    if (LoopGuard.WouldExceedDepth(value, depth, depthLimit)) continue;

                    if (isFirst) isFirst = false;
                    else sink.WriteAscii(',');

                    if (readable) sink.WriteIndent(depth + 1);

                    sink.WriteEscaped(fields[i].Name);
                    sink.WriteAscii(':');
                    if (readable) sink.WriteAscii(' ');
                    WriteValue(ref sink, value, removeNulls, readable, depth + 1, depthLimit);
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
                    if (LoopGuard.WouldExceedDepth(value, depth, depthLimit)) continue;

                    if (isFirst) isFirst = false;
                    else sink.WriteAscii(',');

                    if (readable) sink.WriteIndent(depth + 1);

                    sink.WriteEscaped(properties[i].Name);
                    sink.WriteAscii(':');
                    if (readable) sink.WriteAscii(' ');
                    WriteValue(ref sink, value, removeNulls, readable, depth + 1, depthLimit);
                }

                if (readable) sink.WriteIndent(depth);
                sink.WriteAscii('}');
                LoopGuard.PopReference();
            }

            #endregion
        }
    }
}
