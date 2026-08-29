using System;
using System.Collections.Generic;
using System.Globalization;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        /// <summary>
        /// 引用环与深度守卫（Writer / ByteWriter 共享）。
        /// </summary>
        /// <remarks>
        /// <para><b>设计</b>：ThreadStatic 引用栈 + 容器入口压栈/出口弹栈 + 成员处只查不压。
        /// 值类型/字符串/null 不参与跟踪（无环可能）。</para>
        /// <para><b>引用环</b>：对齐 <see cref="NewtonsoftJsonHandler"/> 既定的 ReferenceLoopHandling.Ignore 语义
        /// ——跳过构成环的成员/元素（不抛错、不无限递归）。</para>
        /// <para><b>深度守卫</b>：超限成员软截断（跳过+警告，不抛错）；标量豁免（无递归风险，任何深度照常写值）。</para>
        /// </remarks>
        internal static class LoopGuard
        {
            /// <summary>序列化中的引用类型对象栈（线程本地复用）。</summary>
            [ThreadStatic]
            public static List<object> RefStack;

            /// <summary>深度告警已发出标志（每次 Serialize 重置，避免刷屏）。</summary>
            [ThreadStatic]
            public static bool DepthWarned;

            /// <summary>开始一次序列化（清栈 + 重置告警）。</summary>
            public static void Begin()
            {
                RefStack?.Clear();
                DepthWarned = false;
            }

            /// <summary>结束一次序列化（清栈兜底，异常路径未配对弹出也能恢复）。</summary>
            public static void End()
            {
                RefStack?.Clear();
            }

            /// <summary>
            /// 该引用是否正在序列化中（构成引用环）。
            /// null/值类型/字符串不参与跟踪（无环可能）。调用方应跳过该成员/元素。
            /// </summary>
            public static bool IsSerializingReference(object value)
            {
                if (value == null) return false;
                Type t = value.GetType();
                if (t.IsValueType || t == typeof(string)) return false;

                var stack = RefStack;
                if (stack == null) return false;

                for (int i = 0; i < stack.Count; i++)
                {
                    if (ReferenceEquals(stack[i], value)) return true;
                }

                return false;
            }

            /// <summary>容器入口压栈（对象/数组/列表/字典；值类型不入栈）。</summary>
            public static void PushReference(object container)
            {
                if (container == null) return;
                Type t = container.GetType();
                if (t.IsValueType) return;

                (RefStack ??= new List<object>(maxDepth + 2)).Add(container);
            }

            /// <summary>容器出口弹栈。</summary>
            public static void PopReference()
            {
                var stack = RefStack;
                if (stack != null && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }

            /// <summary>是否为标量值（写入无递归风险，深度守卫不适用）。</summary>
            public static bool IsScalarValue(object value)
            {
                if (value == null || value is string) return true;
                Type t = value.GetType();
                return t.IsPrimitive || t.IsEnum ||
                       t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) ||
                       t == typeof(TimeSpan) || t == typeof(Guid);
            }

            /// <summary>
            /// 子级复合值是否会被深度守卫截断。
            /// 命中时调用方应跳过整个成员/元素，保持输出合法；仅告警一次。
            /// depthLimit 由调用方按次传入（与 Writer 的安全网同源，不读静态 maxDepth——多 handler 实例各自配置时保持一致语义）。
            /// </summary>
            public static bool WouldExceedDepth(object childValue, int parentDepth, int depthLimit)
            {
                if (parentDepth + 1 < depthLimit || IsScalarValue(childValue)) return false;

                if (!DepthWarned)
                {
                    DepthWarned = true;
                    LogUtility.Warning("[DefaultJson] Serialization depth exceeded the limit of {0}. Members beyond the limit are skipped.", depthLimit);
                }

                return true;
            }
        }

        /// <summary>
        /// 共享类型工具（Writer / ByteWriter 共享）。
        /// </summary>
        internal static class JsonTypeUtil
        {
            /// <summary>能否作为标准 JSON 对象 key 输出（字符串化后可无损还原）。</summary>
            public static bool IsStandardDictionaryKey(Type keyType)
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
        }

        /// <summary>
        /// 泛型参数缓存（Writer / Reader 共享）。
        /// </summary>
        /// <remarks>
        /// <para><c>GetGenericArguments()</c> 与 <c>GenericTypeArguments</c> 每次调用分配新 <c>Type[]</c>；
        /// 字典/列表键值类型集合有限（按类型收敛），缓存后同类型重复序列化/解析零分配。</para>
        /// </remarks>
        internal static class GenericArgsCache
        {
            private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type[]> s_Cache =
                new System.Collections.Concurrent.ConcurrentDictionary<Type, Type[]>();

            /// <summary>
            /// 获取泛型类型的类型参数（缓存命中零分配）。
            /// </summary>
            /// <param name="type">泛型类型。</param>
            /// <returns>类型参数数组（缓存实例，调用方不得修改）。</returns>
            public static Type[] Get(Type type)
            {
                return s_Cache.GetOrAdd(type, static t => t.GetGenericArguments());
            }
        }

        /// <summary>
        /// 共享类型转换（Reader / ByteReader 共享）。
        /// 所有方法接收 string 参数——字节路径的 Reader 在物化字符串后调用。
        /// </summary>
        internal static class TypeConverter
        {
            /// <summary>legacy 字典格式的成员名常量。</summary>
            public const string KeyMember = "key";

            /// <summary>legacy 字典格式的成员名常量。</summary>
            public const string ValueMember = "value";

            /// <summary>
            /// 尝试将字符串转换为非数值目标类型（string/char/bool/枚举/Guid/DateTime/TimeSpan/DateTimeOffset）。
            /// 返回 false 表示目标类型为数值——调用方需走各自的 span 数值解析路径（char/byte span 差异不适合共享）。
            /// 不匹配的值会抛 <see cref="GameException"/>（不是返回 false）。
            /// </summary>
            public static bool TryConvertFromString(string s, Type type, out object result)
            {
                if (type == typeof(string) || type == typeof(object)) { result = s; return true; }
                if (type == typeof(char)) { result = s.Length > 0 && s != "null" ? (object)s[0] : '\0'; return true; }
                if (type == typeof(bool)) { result = ParseBooleanString(s); return true; }

                if (type.IsEnum)
                {
                    if (Enum.TryParse(type, s, false, out object enumValue)) { result = enumValue; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid name or value for enum '{1}'.", s, type.Name));
                }

                if (type == typeof(Guid))
                {
                    if (Guid.TryParse(s, out Guid guid)) { result = guid; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid Guid.", s));
                }

                if (type == typeof(DateTime))
                {
                    if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt)) { result = dt; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid DateTime.", s));
                }

                if (type == typeof(DateTimeOffset))
                {
                    if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset dto)) { result = dto; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid DateTimeOffset.", s));
                }

                if (type == typeof(TimeSpan))
                {
                    if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out TimeSpan ts)) { result = ts; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid TimeSpan.", s));
                }

                // 数值类型：交由调用方的 span 解析（char/byte span 差异不适合共享）
                result = null;
                return false;
            }

            public static bool ParseBooleanString(string s)
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

            /// <summary>
            /// 尝试将字符串转换为非数值字典 key 类型（string/char/bool/枚举/Guid）。
            /// 返回 false 表示 key 为数值类型——调用方需走各自的 span 数值解析。
            /// 不匹配的值会抛 <see cref="GameException"/>。
            /// </summary>
            public static bool TryConvertDictionaryKey(string s, Type keyType, out object result)
            {
                if (keyType == typeof(string)) { result = s; return true; }
                if (keyType == typeof(char)) { result = s.Length > 0 ? (object)s[0] : '\0'; return true; }
                if (keyType == typeof(bool)) { result = ParseBooleanString(s); return true; }

                if (keyType.IsEnum)
                {
                    if (Enum.TryParse(keyType, s, false, out object v)) { result = v; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid dictionary key for enum '{1}'.", s, keyType.Name));
                }

                if (keyType == typeof(Guid))
                {
                    if (Guid.TryParse(s, out Guid guid)) { result = guid; return true; }
                    Throw(StringUtility.Format("'{0}' is not a valid Guid dictionary key.", s));
                }

                // 数值 key：调用方负责 span 解析
                result = null;
                return false;
            }

            /// <summary>共享 Throw（标注 [DoesNotReturn]，消除调用方不可达警告）。</summary>
            [System.Diagnostics.CodeAnalysis.DoesNotReturn]
            private static void Throw(string message)
            {
                throw new GameException(message);
            }
        }
    }
}
