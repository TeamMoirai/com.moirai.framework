using System.Collections.Generic;
using System.Linq;

namespace Moirai.Atropos.Attributes.Editor.Utils
{
    /// <summary>
    /// LINQ 扩展方法集合。
    /// </summary>
    public static class LinqExtend
    {
        /// <summary>
        /// 为可枚举序列中的每个元素附加其索引，产出（元素值, 索引）元组序列。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="enumerable">源可枚举序列。</param>
        /// <param name="startIndex">起始索引偏移量，默认为 0。</param>
        /// <returns>包含元素值及其索引（从 <paramref name="startIndex"/> 开始计数）的元组序列。</returns>
        public static IEnumerable<(T value, int index)> WithIndex<T>(this IEnumerable<T> enumerable, int startIndex = 0)
            => enumerable.Select((source, index) => (value: source, index: index + startIndex));
    }
}