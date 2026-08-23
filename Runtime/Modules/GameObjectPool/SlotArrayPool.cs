using System;
using System.Collections.Generic;

namespace Moirai.Atropos.GameObjectPool
{
    /// <summary>
    /// 按长度分桶的通用数组池，零 GC 热路径租借。
    /// </summary>
    /// <typeparam name="T">数组元素类型。</typeparam>
    internal static class SlotArrayPool<T>
    {
        #region 常量 [CONSTANTS]

        private const int MAX_BUCKETS = 18;
        private const int MIN_BUCKET_SIZE = 1 << 4;

        #endregion

        #region 字段 [FIELDS]

        private static readonly Stack<T[]>[] s_Buckets = new Stack<T[]>[MAX_BUCKETS];

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 租借一个长度不小于 <paramref name="count"/> 的数组。
        /// </summary>
        /// <param name="count">所需最小长度。</param>
        /// <returns>租借的数组。</returns>
        public static T[] Rent(int count)
        {
            if (count <= 0)
            {
                return Array.Empty<T>();
            }

            int bucketIndex = GetBucketIndex(count);
            if (bucketIndex < 0 || bucketIndex >= MAX_BUCKETS)
            {
                return new T[count];
            }

            Stack<T[]> bucket = s_Buckets[bucketIndex];
            if (bucket != null && bucket.Count > 0)
            {
                return bucket.Pop();
            }

            int size = GetBucketSize(bucketIndex);
            return new T[size];
        }

        /// <summary>
        /// 归还数组到池中。
        /// </summary>
        /// <param name="array">要归还的数组。</param>
        /// <param name="clearArray">是否清零数组内容。</param>
        public static void Return(T[] array, bool clearArray)
        {
            if (array == null || array.Length == 0)
            {
                return;
            }

            int bucketIndex = GetBucketIndex(array.Length);
            if (bucketIndex < 0 || bucketIndex >= MAX_BUCKETS)
            {
                return;
            }

            if (clearArray)
            {
                Array.Clear(array, 0, array.Length);
            }

            Stack<T[]> bucket = s_Buckets[bucketIndex];
            if (bucket == null)
            {
                bucket = new Stack<T[]>(4);
                s_Buckets[bucketIndex] = bucket;
            }

            if (bucket.Count < 64)
            {
                bucket.Push(array);
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static int GetBucketIndex(int count)
        {
            if (count <= MIN_BUCKET_SIZE)
            {
                return 0;
            }

            int index = 0;
            int size = MIN_BUCKET_SIZE;
            while (size < count && index < MAX_BUCKETS - 1)
            {
                size <<= 1;
                index++;
            }

            return index;
        }

        private static int GetBucketSize(int bucketIndex)
        {
            int size = MIN_BUCKET_SIZE;
            for (int i = 0; i < bucketIndex; i++)
            {
                size <<= 1;
            }

            return size;
        }

        #endregion
    }
}
