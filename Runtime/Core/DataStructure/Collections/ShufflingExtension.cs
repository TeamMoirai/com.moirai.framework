using System;
using System.Collections.Generic;
using System.Threading;

namespace Moirai.Atropos.Collections
{
    /// <summary>
    /// 为 <see cref="IReadOnlyList{T}"/> 提供洗牌与随机采样扩展。
    /// 所有方法均线程安全（每个线程独立随机数生成器）。
    /// </summary>
    public static class ShufflingExtension
    {
        [ThreadStatic]
        private static Random s_Rng;

        private static Random Rng
        {
            get
            {
                if (s_Rng == null)
                {
                    // 种子组合：时间戳 + 线程ID + Guid 哈希（进一步提高随机性）
                    int seed = Environment.TickCount
                               ^ Thread.CurrentThread.ManagedThreadId
                               ^ Guid.NewGuid().GetHashCode();
                    s_Rng = new Random(seed);
                }
                return s_Rng;
            }
        }

        /// <summary>
        /// 原地洗牌（Fisher–Yates 算法），修改原列表顺序。
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">待洗牌列表，不能为 null</param>
        /// <exception cref="ArgumentNullException">list 为 null</exception>
        public static void Shuffle<T>(this IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// 返回一个新列表，包含原列表元素经洗牌后的顺序（原列表不变）。
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">原始列表，不能为 null</param>
        /// <returns>新顺序的列表</returns>
        /// <exception cref="ArgumentNullException">list 为 null</exception>
        public static List<T> Shuffled<T>(this IReadOnlyList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            var copy = new List<T>(list);
            copy.Shuffle();
            return copy;
        }

        /// <summary>
        /// 从列表中随机选取一个元素。
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">列表，不能为 null 且不能为空</param>
        /// <returns>随机元素</returns>
        /// <exception cref="ArgumentNullException">list 为 null</exception>
        /// <exception cref="InvalidOperationException">列表为空</exception>
        public static T RandomElement<T>(this IReadOnlyList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new InvalidOperationException("Cannot get random element from empty list.");
            return list[Rng.Next(list.Count)];
        }

        /// <summary>
        /// 随机选取指定数量的元素（无放回采样），返回新列表。
        /// 若 <paramref name="count"/> 大于等于列表长度，则返回所有元素的随机顺序（即洗牌后的完整列表）。
        /// 若 <paramref name="count"/> 为 0，返回空列表。
        /// </summary>
        /// <remarks>
        /// 该方法分配 O(n) 的临时索引数组（n 为列表长度），当 n 极大且 count 极小时，可考虑使用其他专用采样算法，
        /// 但对于绝大多数应用场景，此实现的简洁性和性能已足够优秀。
        /// </remarks>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">列表，不能为 null</param>
        /// <param name="count">需要选取的元素数量</param>
        /// <returns>包含随机选取元素的列表</returns>
        /// <exception cref="ArgumentNullException">list 为 null</exception>
        /// <exception cref="ArgumentOutOfRangeException">count 为负数</exception>
        public static List<T> RandomElements<T>(this IReadOnlyList<T> list, int count)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            int n = list.Count;
            if (count == 0) return new List<T>();

            // 若请求数量 >= 总数，直接返回完整洗牌副本（避免索引数组的额外开销）
            if (count >= n)
            {
                var copy = new List<T>(list);
                copy.Shuffle();
                return copy;
            }

            // 部分洗牌：使用索引数组，只对前 count 个位置进行随机交换
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;

            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                int j = Rng.Next(i, n);
                (indices[i], indices[j]) = (indices[j], indices[i]);
                result.Add(list[indices[i]]);
            }
            return result;
        }
    }
}