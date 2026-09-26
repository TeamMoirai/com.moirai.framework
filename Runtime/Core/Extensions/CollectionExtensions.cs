using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 为 <see cref="ICollection{T}"/> 提供批量操作扩展方法。
    /// </summary>
    public static class CollectionExtensions
    {
        /// <summary>
        /// 向集合中批量添加元素。
        /// </summary>
        public static void AddRange<T>(this ICollection<T> source, IEnumerable<T> items)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (items == null) throw new ArgumentNullException(nameof(items));

            // List<T>.AddRange 内部一次性扩容，最优路径
            if (source is List<T> list)
            {
                list.AddRange(items);
                return;
            }

            // HashSet<T>.UnionWith 语义一致且内部已优化
            if (source is HashSet<T> set)
            {
                set.UnionWith(items);
                return;
            }

            foreach (T item in items)
            {
                source.Add(item);
            }
        }

        /// <summary>
        /// 从集合中批量移除元素，返回实际移除数量。
        /// </summary>
        public static int RemoveRange<T>(this ICollection<T> source, IEnumerable<T> items)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (items == null) throw new ArgumentNullException(nameof(items));

            // List<T> 最优：用 RemoveAll 一次 O(n) 扫描
            if (source is List<T> list)
            {
                var toRemove = new HashSet<T>(items);
                if (toRemove.Count == 0) return 0;
                int before = list.Count;
                list.RemoveAll(toRemove.Contains);
                return before - list.Count;
            }

            // 通用路径
            int removed = 0;
            foreach (T item in items)
            {
                if (source.Remove(item))
                    removed++;
            }
            return removed;
        }

        /// <summary>
        /// 用新元素序列替换集合中的所有现有元素。
        /// </summary>
        public static void Replace<T>(this ICollection<T> source, IEnumerable<T> items)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (items == null) throw new ArgumentNullException(nameof(items));

            // 自引用安全：source 和 items 是同一对象时必须先复制
            if (ReferenceEquals(source, items))
            {
                var snapshot = new List<T>(source);
                source.Clear();
                AddRange(source, snapshot);
                return;
            }

            source.Clear();
            AddRange(source, items);
        }
    }
}