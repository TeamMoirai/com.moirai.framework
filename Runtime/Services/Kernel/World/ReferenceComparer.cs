using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于引用相等性的比较器。
    /// <para>使用 <see cref="object.ReferenceEquals"/> 判断相等，并返回运行时默认（引用）哈希码，适用于以对象实例为键的字典等场景。</para>
    /// </summary>
    /// <typeparam name="T">参与比较的引用类型。</typeparam>
    internal sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        /// <summary>
        /// 获取比较器单例实例。
        /// </summary>
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        private ReferenceComparer() { }

        /// <summary>
        /// 判断两个对象是否为同一引用。
        /// </summary>
        /// <param name="x">待比较的第一个对象。</param>
        /// <param name="y">待比较的第二个对象。</param>
        /// <returns>两者为同一引用（或均为 <c>null</c>）返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        /// <summary>
        /// 获取对象的运行时默认（引用）哈希码。
        /// </summary>
        /// <param name="obj">待计算哈希码的对象。</param>
        /// <returns>对象的引用哈希码。</returns>
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
