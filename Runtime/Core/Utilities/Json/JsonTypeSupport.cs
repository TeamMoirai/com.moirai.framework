using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Events;

namespace Moirai.Atropos
{
    /// <summary>
    /// JSON 成员类型可序列化性判定（允许列表）：只有能写出、也能原样读回的成员类型才参与序列化。
    /// </summary>
    /// <remarks>
    /// 与旧的"黑名单 + 反射兜底"相对：兜底反射会把不是数据的类型写成内部结构（<c>LinkedList</c> 的节点链、
    /// <c>StringBuilder</c> 的字符块），把接口/抽象成员写成不带类型名的对象（读回时无法构造），
    /// 对委托与多维数组则直接抛错。静默写歪比报错更坏——存档看起来是成功的。
    /// 判据只决定**成员是否入选**：被排除的成员写侧不出现在 JSON，读侧按未知字段忽略，两侧对称；
    /// 类型自身无可序列化成员仍由 <c>DefaultJson</c> 抛 <see cref="GameException"/>，不静默产出 <c>{}</c>。
    /// </remarks>
    public static class JsonTypeSupport
    {
        /// <summary>读侧能还原的泛型集合定义（与 <c>DefaultJson.JsonReader</c> 的分派一致）。</summary>
        private static readonly HashSet<Type> s_SupportedGenericDefinitions = new HashSet<Type>
        {
            typeof(List<>),
            typeof(IList<>),
            typeof(IReadOnlyList<>),
            typeof(Dictionary<,>),
            typeof(IDictionary<,>),
            typeof(IReadOnlyDictionary<,>),
        };

        /// <summary>
        /// 以 JSON 数组承载的泛型集合定义：写侧按顺序展开、读侧按顺序回填。
        /// </summary>
        /// <remarks>
        /// <c>Stack&lt;T&gt;</c> 的枚举序是自顶向下，读侧须逆序压栈才能还原成同构的栈；
        /// <see cref="System.Collections.Generic.ISet{T}"/> 一律落到 <c>HashSet&lt;T&gt;</c> 实例。
        /// 接口形态（<c>IList&lt;T&gt;</c>/<c>IReadOnlyList&lt;T&gt;</c> 等）落到 <c>List&lt;T&gt;</c> 具体实例。
        /// </remarks>
        internal static readonly HashSet<Type> s_ArrayBackedDefinitions = new HashSet<Type>
        {
            typeof(HashSet<>),
            typeof(ISet<>),
            typeof(SortedSet<>),
            typeof(Queue<>),
            typeof(Stack<>),
            typeof(LinkedList<>),
            typeof(IList<>),
            typeof(ICollection<>),
            typeof(IEnumerable<>),
            typeof(IReadOnlyCollection<>),
            typeof(IReadOnlyList<>),
        };

        /// <summary>
        /// 取集合类型的元素类型（数组取 <c>GetElementType()</c>，泛型集合取末位类型参数）。
        /// </summary>
        /// <param name="type">集合类型。</param>
        /// <returns>元素类型；不是集合返回 <c>null</c>。</returns>
        internal static Type GetCollectionElementType(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return type.GetArrayRank() == 1 ? type.GetElementType() : null;

            if (!type.IsGenericType) return null;

            Type definition = type.GetGenericTypeDefinition();
            if (!s_SupportedGenericDefinitions.Contains(definition) && !s_ArrayBackedDefinitions.Contains(definition)) return null;

            Type[] args = type.GetGenericArguments();
            return args[args.Length - 1];
        }

        /// <summary>
        /// 判定类型是否以 JSON 数组承载（写侧分派与读侧构造共用）。
        /// </summary>
        /// <param name="definition">泛型类型定义。</param>
        /// <returns>数组承载返回 <c>true</c>。</returns>
        internal static bool IsArrayBackedDefinition(Type definition)
        {
            return definition != null && s_ArrayBackedDefinitions.Contains(definition);
        }

        /// <summary>BCL 程序集前缀：其中的具体类型只认显式列举的标量与集合，其余不反射其内部字段。</summary>
        private static readonly string[] s_BclAssemblyPrefixes =
        {
            "System", "mscorlib", "netstandard", "Mono.",
        };

        /// <summary>
        /// 判定成员类型能否被序列化并原样读回。
        /// </summary>
        /// <param name="type">成员声明类型。</param>
        /// <returns>入选返回 <c>true</c>；不参与序列化返回 <c>false</c>。</returns>
        public static bool IsSupportedMemberType(Type type)
        {
            if (type == null) return false;

            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return true;

            // 写出侧直写的 BCL 值类型（与 DefaultJson.WriteValue 的直写分支对齐）
            if (type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
                type == typeof(TimeSpan) || type == typeof(Guid)) return true;

            if (IsNotDataType(type)) return false;

            if (type.IsArray)
            {
                // 多维数组不入选：写出侧要嵌套展开、读侧要按各维长度重建，两侧尚未对接（今天直接 ArgumentException）。
                // 在实现之前保持"不入选"，不能让存盘在这里抛错。
                return type.GetArrayRank() == 1 && IsSupportedMemberType(type.GetElementType());
            }

            // 泛型集合：定义须在名单内（含以数组承载的接口形态，如 IList<T>/IReadOnlyCollection<T>），元素类型也要入选
            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (s_SupportedGenericDefinitions.Contains(definition) || IsArrayBackedDefinition(definition))
                {
                    Type[] args = type.GetGenericArguments();
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (!IsSupportedMemberType(args[i])) return false;
                    }

                    return true;
                }
            }

            // 其余接口/抽象/开放泛型：JSON 里没有类型名，读回时构造不出实例
            if (type.IsInterface || type.IsAbstract || type.IsGenericTypeDefinition) return false;

            // BCL 的其余类型（StringBuilder、Tuple、ValueTuple、KeyValuePair、CancellationToken、Stream、Task…）
            // 不是数据契约：公开字段面为空或全是私有实现细节，反射出来是空对象或内部结构。
            if (IsBclAssembly(type.Assembly)) return false;

            // 引擎侧与项目侧的具体类型（Vector3/Color/Quaternion 等结构体、各 POCO）按反射成员序列化
            return true;
        }

        /// <summary>
        /// 判定类型是否属于"装的不是数据"的家族（各 handler 共用的排除契约）。
        /// </summary>
        /// <param name="type">待判定的类型。</param>
        /// <returns>属于非数据家族返回 <c>true</c>。</returns>
        public static bool IsNotDataType(Type type)
        {
            if (type == null) return false;
            if (type == typeof(object)) return true;

            return typeof(Delegate).IsAssignableFrom(type) ||
                   typeof(UnityEventBase).IsAssignableFrom(type) ||
                   typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                   type == typeof(IntPtr) || type == typeof(UIntPtr) ||
                   typeof(MemberInfo).IsAssignableFrom(type) || type == typeof(Type) ||
                   typeof(MarshalByRefObject).IsAssignableFrom(type);
        }

        /// <summary>
        /// 判定程序集是否属于 BCL（其具体类型默认不入允许列表）。
        /// </summary>
        /// <param name="assembly">类型所在程序集。</param>
        /// <returns>BCL 程序集返回 <c>true</c>。</returns>
        private static bool IsBclAssembly(Assembly assembly)
        {
            string name = assembly?.GetName().Name;
            if (string.IsNullOrEmpty(name)) return false;

            for (int i = 0; i < s_BclAssemblyPrefixes.Length; i++)
            {
                if (name.StartsWith(s_BclAssemblyPrefixes[i], StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
