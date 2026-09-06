using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 开放寻址字符串到 int 的零分配 HashMap（桶链 + ArrayPool 租借，Ordinal 判等）。
    /// <para>struct 语义——必须存储于可变字段后调用（方法直接改写字段状态）；Dispose 后归还全部内部数组。</para>
    /// </summary>
    internal struct StringOpenHashMap
    {
        #region 常量 [CONSTANTS]

        private const int MIN_CAPACITY = 8;

        #endregion

        #region 字段 [FIELDS]

        private int[] _buckets;
        private string[] _keys;
        private int[] _values;
        private int[] _next;
        private int _count;
        private int _freeList;
        private int _mask;
        private int _allocCount;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取已存储条目数量。
        /// </summary>
        public int Count => _count;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="StringOpenHashMap"/> 的新实例。
        /// </summary>
        /// <param name="capacity">预估容量（自动取 2 的幂并向上对齐）。</param>
        public StringOpenHashMap(int capacity)
        {
            int cap = NextPowerOf2(Math.Max(capacity, MIN_CAPACITY));
            _mask = cap - 1;
            _buckets = ArrayPool<int>.Shared.Rent(cap);
            _keys = ArrayPool<string>.Shared.Rent(cap);
            _values = ArrayPool<int>.Shared.Rent(cap);
            _next = ArrayPool<int>.Shared.Rent(cap);
            Array.Clear(_buckets, 0, _buckets.Length);
            Array.Clear(_keys, 0, _keys.Length);
            Array.Clear(_values, 0, _values.Length);
            Array.Clear(_next, 0, _next.Length);
            _count = 0;
            _freeList = 0;
            _allocCount = 0;
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 尝试获取键对应的值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">找到的值。</param>
        /// <returns>是否存在。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out int value)
        {
            if (_buckets == null || key == null)
            {
                value = -1;
                return false;
            }

            int hash = key.GetHashCode() & 0x7FFFFFFF;
            int i = _buckets[hash & _mask];
            while (i > 0)
            {
                int idx = i - 1;
                if (string.Equals(_keys[idx], key, StringComparison.Ordinal))
                {
                    value = _values[idx];
                    return true;
                }

                i = _next[idx];
            }

            value = -1;
            return false;
        }

        /// <summary>
        /// 是否包含指定键。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>是否包含。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(string key)
        {
            return TryGetValue(key, out _);
        }

        /// <summary>
        /// 添加或更新键值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(string key, int value)
        {
            if (key == null)
            {
                return;
            }

            if (_count >= ((_mask + 1) * 3 >> 2))
            {
                Grow();
            }

            int hash = key.GetHashCode() & 0x7FFFFFFF;
            int bucket = hash & _mask;
            int i = _buckets[bucket];
            while (i > 0)
            {
                int ei = i - 1;
                if (string.Equals(_keys[ei], key, StringComparison.Ordinal))
                {
                    _values[ei] = value;
                    return;
                }

                i = _next[ei];
            }

            int idx;
            if (_freeList > 0)
            {
                idx = _freeList - 1;
                _freeList = _next[idx];
            }
            else
            {
                if (_allocCount > _mask)
                {
                    Grow();
                    bucket = hash & _mask;
                }

                idx = _allocCount++;
            }

            _keys[idx] = key;
            _values[idx] = value;
            _next[idx] = _buckets[bucket];
            _buckets[bucket] = idx + 1;
            _count++;
        }

        /// <summary>
        /// 移除指定键。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>是否移除成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(string key)
        {
            if (_buckets == null || key == null)
            {
                return false;
            }

            int hash = key.GetHashCode() & 0x7FFFFFFF;
            int bucket = hash & _mask;
            int prev = 0;
            int i = _buckets[bucket];
            while (i > 0)
            {
                int idx = i - 1;
                if (string.Equals(_keys[idx], key, StringComparison.Ordinal))
                {
                    if (prev == 0)
                    {
                        _buckets[bucket] = _next[idx];
                    }
                    else
                    {
                        _next[prev - 1] = _next[idx];
                    }

                    _keys[idx] = null;
                    _values[idx] = -1;
                    _next[idx] = _freeList;
                    _freeList = idx + 1;
                    _count--;
                    return true;
                }

                prev = i;
                i = _next[idx];
            }

            return false;
        }

        /// <summary>
        /// 清空所有条目（保留内部数组容量）。
        /// </summary>
        public void Clear()
        {
            if (_buckets == null)
            {
                return;
            }

            int cap = _mask + 1;
            Array.Clear(_buckets, 0, cap);
            Array.Clear(_keys, 0, cap);
            Array.Clear(_values, 0, cap);
            Array.Clear(_next, 0, cap);
            _count = 0;
            _freeList = 0;
            _allocCount = 0;
        }

        /// <summary>
        /// 归还全部内部数组到共享 ArrayPool。
        /// </summary>
        public void Dispose()
        {
            if (_buckets != null)
            {
                ArrayPool<int>.Shared.Return(_buckets, true);
            }

            if (_keys != null)
            {
                ArrayPool<string>.Shared.Return(_keys, true);
            }

            if (_values != null)
            {
                ArrayPool<int>.Shared.Return(_values, true);
            }

            if (_next != null)
            {
                ArrayPool<int>.Shared.Return(_next, true);
            }

            _buckets = null;
            _keys = null;
            _values = null;
            _next = null;
            _count = 0;
            _freeList = 0;
            _mask = 0;
            _allocCount = 0;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void Grow()
        {
            int newCap = (_mask + 1) << 1;
            if (newCap < MIN_CAPACITY)
            {
                newCap = MIN_CAPACITY;
            }

            int newMask = newCap - 1;
            int[] newBuckets = ArrayPool<int>.Shared.Rent(newCap);
            string[] newKeys = ArrayPool<string>.Shared.Rent(newCap);
            int[] newValues = ArrayPool<int>.Shared.Rent(newCap);
            int[] newNext = ArrayPool<int>.Shared.Rent(newCap);
            Array.Clear(newBuckets, 0, newBuckets.Length);
            Array.Clear(newKeys, 0, newKeys.Length);
            Array.Clear(newValues, 0, newValues.Length);
            Array.Clear(newNext, 0, newNext.Length);

            int newAlloc = 0;
            int oldCap = _mask + 1;
            for (int b = 0; b < oldCap; b++)
            {
                int i = _buckets[b];
                while (i > 0)
                {
                    int old = i - 1;
                    int ni = newAlloc++;
                    newKeys[ni] = _keys[old];
                    newValues[ni] = _values[old];
                    int hash = newKeys[ni].GetHashCode() & 0x7FFFFFFF;
                    int nb = hash & newMask;
                    newNext[ni] = newBuckets[nb];
                    newBuckets[nb] = ni + 1;
                    i = _next[old];
                }
            }

            ArrayPool<int>.Shared.Return(_buckets, true);
            ArrayPool<string>.Shared.Return(_keys, true);
            ArrayPool<int>.Shared.Return(_values, true);
            ArrayPool<int>.Shared.Return(_next, true);

            _buckets = newBuckets;
            _keys = newKeys;
            _values = newValues;
            _next = newNext;
            _mask = newMask;
            _allocCount = newAlloc;
            _freeList = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOf2(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        #endregion
    }
}
