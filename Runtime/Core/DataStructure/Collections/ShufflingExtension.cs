using System;
using System.Collections.Generic;
using System.Linq;

namespace Moirai.Atropos.Collections
{
    /// <summary>
    /// 用于 <see cref="IReadOnlyList{T}"/> 的洗牌扩展
    /// </summary>
    public static class ShufflingExtension
    {
        private static readonly Random s_Rng = new Random();

        /// <summary>
        /// Fisher–Yates 洗牌算法
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = s_Rng.Next(n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }

        /// <summary>
        /// 从列表中随机返回一个元素
        /// </summary>
        /// <param name="list"></param>
        public static T Random<T>(this IReadOnlyList<T> list)
        {
            return list[s_Rng.Next(list.Count)];
        }

        public static T Last<T>(this IReadOnlyList<T> list)
        {
            return list[^1];
        }

        public static List<T> GetRandomElements<T>(this List<T> list, int elementsCount)
        {
            return list.OrderBy(arg => Guid.NewGuid())
                .Take(list.Count < elementsCount ? list.Count : elementsCount)
                .ToList();
        }
    }
}