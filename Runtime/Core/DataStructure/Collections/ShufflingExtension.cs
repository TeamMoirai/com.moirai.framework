using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.Collections
{
    /// <summary>
    /// 为 <see cref="IReadOnlyList{T}"/> 提供洗牌与随机采样扩展。
    /// 所有方法均线程安全（随机源走 <see cref="RandomUtility"/> 的每线程流，且可统一播种复现）。
    /// </summary>
    public static class ShufflingExtension
    {
        /// <summary>
        /// 原地洗牌（Fisher–Yates 算法），修改原列表顺序。
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">待洗牌列表，不能为 null</param>
        /// <exception cref="ArgumentNullException">list 为 null</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Shuffle<T>(this IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));

            ShuffleUtility.Shuffle(list, list.Count);
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
            ShuffleUtility.Shuffle(copy, copy.Count);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T RandomElement<T>(this IReadOnlyList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new InvalidOperationException("Cannot get random element from empty list.");

            return list[RandomUtility.NextInt(list.Count)];
        }

        /// <summary>
        /// 无放回采样 <paramref name="count"/> 个元素，返回新列表。
        /// <para>count ≥ 列表长度时，返回完整洗牌副本。</para>
        /// <para>根据 count 与 n 的比例自适应选择最优算法路径。</para>
        /// </summary>
        public static List<T> RandomElements<T>(this IReadOnlyList<T> list, int count)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be non-negative.");

            int n = list.Count;

            // --- 快速路径 ---
            if (count == 0 || n == 0) return new List<T>();
            if (count == 1) return new List<T>(1) { list[RandomUtility.NextInt(n)] };
            if (count >= n) return list.Shuffled();

            // --- 自适应路径选择 ---
            //
            // 策略 A（HashSet 拒绝采样）:
            //   适合 count << n 的场景。
            //   内存 O(count)，时间期望 O(count)，无需分配 O(n) 数组。
            //   注意：Mono 的 HashSet 遍历顺序 ≠ 插入顺序，
            //         因此必须按插入顺序写入 result，不能遍历 HashSet。
            //
            // 策略 B（部分 Fisher-Yates）:
            //   适合 count 接近 n 的场景。
            //   内存 O(n)，时间 O(count)，但需预分配索引数组。
            //
            // 分界线取 n / 3，经实验在两者之间取得较好平衡。

            return count < n / 3
                ? SampleByRejection(list, n, count)
                : SampleByPartialShuffle(list, n, count);
        }

        /// <summary>
        /// HashSet 拒绝采样：平均 O(count) 时间，O(count) 空间。
        /// 利用 HashSet.Add 的返回值判断碰撞，do-while 在 count≪n 时几乎只执行一次。
        /// </summary>
        /// <remarks>内部实现 — 拒绝采样（小 count 适用）</remarks>
        private static List<T> SampleByRejection<T>(IReadOnlyList<T> list, int n, int count)
        {
            var selected = new HashSet<int>();
            var result = new List<T>(count);

            for (int i = 0; i < count; i++)
            {
                int idx;
                // HashSet.Add 返回 false 表示已存在 → 重新采样
                // 当 count ≪ n 时碰撞概率 ≈ count/n → 趋近于 0
                do
                {
                    idx = RandomUtility.NextInt(n);
                } while (!selected.Add(idx));

                result.Add(list[idx]);
            }

            return result;
        }

        /// <summary>
        /// 部分 Fisher-Yates：只对前 count 个位置做随机交换。
        /// 时间 O(count)，空间 O(n)（索引数组）。
        /// </summary>
        /// <remarks>内部实现 — 部分 Fisher-Yates（大 count 适用）</remarks>
        private static List<T> SampleByPartialShuffle<T>(IReadOnlyList<T> list, int n, int count)
        {
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;

            ShuffleUtility.DrawIndices(indices, count);

            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(list[indices[i]]);
            }

            return result;
        }
    }
}
