using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 洗牌与无放回抽样的原语：框架内所有"打乱顺序 / 抽 k 个不重复"都走这里。
    /// <para>循环只写一遍，随机源以 <c>ref RandomSource</c> 传入：走全局流就用
    /// <see cref="RandomUtility.SharedStream"/>，要"同一种子同一结果"就自备
    /// <see cref="RandomUtility.CreateSeeded"/>。</para>
    /// </summary>
    public static class ShuffleUtility
    {
        /// <summary>就地 Fisher–Yates 打乱 <paramref name="list"/> 的前 <paramref name="count"/> 项（均匀置换）。</summary>
        public static void Shuffle<T>(IList<T> list, int count)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if ((uint)count > (uint)list.Count)
                throw new ArgumentOutOfRangeException(nameof(count), count, "不得超出列表长度");

            Shuffle(list, count, ref RandomUtility.SharedStream());
        }

        /// <summary>就地打乱整个列表。</summary>
        public static void Shuffle<T>(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            Shuffle(list, list.Count);
        }

        /// <summary><see cref="Shuffle{T}(IList{T},int)"/> 的显式随机源版本；调用后流被推进。</summary>
        public static void Shuffle<T>(IList<T> list, int count, ref RandomSource rng)
        {
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                if (j == i) continue;

                // 手动交换：元组解构在旧版 Mono 上不保证被优化掉
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// 从 <c>pool[0..n)</c> 中无放回抽 <paramref name="count"/> 个，结果落在 <c>pool[0..count)</c>。
        /// <para>与 <see cref="Shuffle{T}(IList{T},int)"/> 不是一回事：那条是前缀内部的置换，
        /// 这条每次把选中项换到已抽区边界，因此 <c>count ≪ n</c> 时也只花 <c>O(count)</c> 次交换。</para>
        /// </summary>
        public static void DrawIndices(int[] pool, int count, ref RandomSource rng)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if ((uint)count > (uint)pool.Length)
                throw new ArgumentOutOfRangeException(nameof(count), count, "不得超出样本池长度");

            for (int i = 0; i < count; i++)
            {
                int j = rng.NextInt(i, pool.Length);
                int tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }
        }

        /// <summary><see cref="DrawIndices(int[],int,ref RandomSource)"/> 的全局流版本。</summary>
        public static void DrawIndices(int[] pool, int count)
        {
            DrawIndices(pool, count, ref RandomUtility.SharedStream());
        }
    }
}
