using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 通用开放寻址哈希映射，使用 struct 键和 struct 值，零 GC 热路径。
    /// </summary>
    /// <typeparam name="TKey">键类型，必须为 struct 且实现 IEquatable。</typeparam>
    /// <typeparam name="TValue">值类型，必须为 struct。</typeparam>
    internal sealed class ResourceIndexMap<TKey, TValue>
        where TKey : struct, IEquatable<TKey>
        where TValue : struct
    {
        #region 常量 [CONSTANTS]

        private const int DEFAULT_CAPACITY = 16;

        #endregion

        #region 结构体 [STRUCTS]

        private struct Entry
        {
            public int HashCode;
            public int Next;
            public TKey Key;
            public TValue Value;
            public byte State;
        }

        #endregion

        #region 字段 [FIELDS]

        private int[] _buckets;
        private Entry[] _entries;
        private int _count;
        private int _freeHead = -1;
        private int _freeCount;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 当前有效条目数。
        /// </summary>
        public int Count => _count - _freeCount;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 清空映射。
        /// </summary>
        public void Clear()
        {
            if (_buckets != null)
            {
                for (int i = 0; i < _buckets.Length; i++)
                {
                    _buckets[i] = -1;
                }
            }

            if (_entries != null)
            {
                Array.Clear(_entries, 0, _entries.Length);
            }

            _count = 0;
            _freeHead = -1;
            _freeCount = 0;
        }

        /// <summary>
        /// 确保容量。
        /// </summary>
        /// <param name="capacity">目标容量。</param>
        public void EnsureCapacity(int capacity)
        {
            if (capacity <= 0)
            {
                return;
            }

            int target = NextPowerOfTwo(Math.Max(DEFAULT_CAPACITY, capacity));
            if (_buckets == null)
            {
                Initialize(target);
                return;
            }

            if (_entries.Length < target)
            {
                Resize(target);
            }
        }

        /// <summary>
        /// 尝试获取值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        /// <returns>是否找到。</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default;
            if (_buckets == null)
            {
                return false;
            }

            int hashCode = GetHashCode(key);
            int entryIndex = _buckets[hashCode & (_buckets.Length - 1)];
            while (entryIndex >= 0)
            {
                ref Entry entry = ref _entries[entryIndex];
                if (entry.State == 1 && entry.HashCode == hashCode && entry.Key.Equals(key))
                {
                    value = entry.Value;
                    return true;
                }

                entryIndex = entry.Next;
            }

            return false;
        }

        /// <summary>
        /// 设置键值对。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        public void Set(TKey key, TValue value)
        {
            if (_buckets == null)
            {
                Initialize(DEFAULT_CAPACITY);
            }

            int hashCode = GetHashCode(key);
            int bucket = hashCode & (_buckets.Length - 1);
            int entryIndex = _buckets[bucket];
            while (entryIndex >= 0)
            {
                ref Entry entry = ref _entries[entryIndex];
                if (entry.State == 1 && entry.HashCode == hashCode && entry.Key.Equals(key))
                {
                    entry.Value = value;
                    return;
                }

                entryIndex = entry.Next;
            }

            int newIndex;
            if (_freeHead >= 0)
            {
                newIndex = _freeHead;
                _freeHead = _entries[newIndex].Next;
                _freeCount--;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Resize(_entries.Length << 1);
                    bucket = hashCode & (_buckets.Length - 1);
                }

                newIndex = _count;
                _count++;
            }

            ref Entry newEntry = ref _entries[newIndex];
            newEntry.HashCode = hashCode;
            newEntry.Next = _buckets[bucket];
            newEntry.Key = key;
            newEntry.Value = value;
            newEntry.State = 1;
            _buckets[bucket] = newIndex;
        }

        /// <summary>
        /// 移除键。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>是否移除成功。</returns>
        public bool Remove(TKey key)
        {
            if (_buckets == null)
            {
                return false;
            }

            int hashCode = GetHashCode(key);
            int bucket = hashCode & (_buckets.Length - 1);
            int previous = -1;
            int entryIndex = _buckets[bucket];
            while (entryIndex >= 0)
            {
                ref Entry entry = ref _entries[entryIndex];
                if (entry.State == 1 && entry.HashCode == hashCode && entry.Key.Equals(key))
                {
                    if (previous < 0)
                    {
                        _buckets[bucket] = entry.Next;
                    }
                    else
                    {
                        _entries[previous].Next = entry.Next;
                    }

                    entry = default;
                    entry.Next = _freeHead;
                    _freeHead = entryIndex;
                    _freeCount++;
                    return true;
                }

                previous = entryIndex;
                entryIndex = entry.Next;
            }

            return false;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void Initialize(int capacity)
        {
            _buckets = new int[capacity];
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = -1;
            }

            _entries = new Entry[capacity];
            _count = 0;
            _freeHead = -1;
            _freeCount = 0;
        }

        private void Resize(int capacity)
        {
            int newCapacity = NextPowerOfTwo(capacity);
            Entry[] oldEntries = _entries;
            int[] newBuckets = new int[newCapacity];
            for (int i = 0; i < newBuckets.Length; i++)
            {
                newBuckets[i] = -1;
            }

            Entry[] newEntries = new Entry[newCapacity];
            Array.Copy(oldEntries, 0, newEntries, 0, _count);
            for (int i = 0; i < _count; i++)
            {
                if (newEntries[i].State != 1)
                {
                    continue;
                }

                int bucket = newEntries[i].HashCode & (newBuckets.Length - 1);
                newEntries[i].Next = newBuckets[bucket];
                newBuckets[bucket] = i;
            }

            _buckets = newBuckets;
            _entries = newEntries;
        }

        private static int GetHashCode(TKey key)
        {
            return key.GetHashCode() & 0x7fffffff;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }

        #endregion
    }

    /// <summary>
    /// 专用 ulong 到 int 的开放寻址哈希映射，使用 Murmur finalizer 混合键值，SoA 布局。
    /// </summary>
    internal sealed class ResourceUlongIntMap
    {
        #region 常量 [CONSTANTS]

        private const int DEFAULT_CAPACITY = 16;
        private const int LOAD_FACTOR_NUMERATOR = 7;
        private const int LOAD_FACTOR_DENOMINATOR = 10;

        #endregion

        #region 字段 [FIELDS]

        private ulong[] _keys;
        private int[] _values;
        private byte[] _states;
        private int _count;
        private int _occupied;
        private int _resizeThreshold;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 当前有效条目数。
        /// </summary>
        public int Count => _count;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 清空映射。
        /// </summary>
        public void Clear()
        {
            if (_states != null)
            {
                Array.Clear(_states, 0, _states.Length);
            }

            _count = 0;
            _occupied = 0;
        }

        /// <summary>
        /// 确保容量。
        /// </summary>
        /// <param name="capacity">目标容量。</param>
        public void EnsureCapacity(int capacity)
        {
            if (capacity <= 0)
            {
                return;
            }

            int target = NextPowerOfTwo(Math.Max(DEFAULT_CAPACITY, RequiredCapacity(capacity)));
            if (_keys == null)
            {
                Initialize(target);
                return;
            }

            if (_keys.Length < target)
            {
                Resize(target);
            }
        }

        /// <summary>
        /// 尝试获取值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        /// <returns>是否找到。</returns>
        public bool TryGetValue(ulong key, out int value)
        {
            value = 0;
            if (_keys == null)
            {
                return false;
            }

            int mask = _keys.Length - 1;
            int index = (int)(Mix(key) & (uint)mask);
            while (true)
            {
                byte state = _states[index];
                if (state == 0)
                {
                    return false;
                }

                if (state == 1 && _keys[index] == key)
                {
                    value = _values[index];
                    return true;
                }

                index = (index + 1) & mask;
            }
        }

        /// <summary>
        /// 设置键值对。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        public void Set(ulong key, int value)
        {
            if (_keys == null)
            {
                Initialize(DEFAULT_CAPACITY);
            }

            if (_occupied + 1 > _resizeThreshold)
            {
                Resize(_count + 1 > _resizeThreshold ? _keys.Length << 1 : _keys.Length);
            }

            int mask = _keys.Length - 1;
            int index = (int)(Mix(key) & (uint)mask);
            int firstDeleted = -1;
            while (true)
            {
                byte state = _states[index];
                if (state == 0)
                {
                    int writeIndex = firstDeleted >= 0 ? firstDeleted : index;
                    if (firstDeleted < 0)
                    {
                        _occupied++;
                    }

                    _keys[writeIndex] = key;
                    _values[writeIndex] = value;
                    _states[writeIndex] = 1;
                    _count++;
                    return;
                }

                if (state == 2)
                {
                    if (firstDeleted < 0)
                    {
                        firstDeleted = index;
                    }
                }
                else if (_keys[index] == key)
                {
                    _values[index] = value;
                    return;
                }

                index = (index + 1) & mask;
            }
        }

        /// <summary>
        /// 移除键。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>是否移除成功。</returns>
        public bool Remove(ulong key)
        {
            if (_keys == null)
            {
                return false;
            }

            int mask = _keys.Length - 1;
            int index = (int)(Mix(key) & (uint)mask);
            while (true)
            {
                byte state = _states[index];
                if (state == 0)
                {
                    return false;
                }

                if (state == 1 && _keys[index] == key)
                {
                    _states[index] = 2;
                    _values[index] = 0;
                    _count--;
                    return true;
                }

                index = (index + 1) & mask;
            }
        }

        /// <summary>
        /// 遍历所有有效键。
        /// </summary>
        /// <param name="action">回调。</param>
        public void ForEachKey(Action<ulong> action)
        {
            if (_keys == null || action == null)
            {
                return;
            }

            for (int i = 0; i < _keys.Length; i++)
            {
                if (_states[i] == 1)
                {
                    action(_keys[i]);
                }
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void Initialize(int capacity)
        {
            int actualCapacity = NextPowerOfTwo(Math.Max(DEFAULT_CAPACITY, capacity));
            _keys = new ulong[actualCapacity];
            _values = new int[actualCapacity];
            _states = new byte[actualCapacity];
            _count = 0;
            _occupied = 0;
            _resizeThreshold = Math.Max(1, actualCapacity * LOAD_FACTOR_NUMERATOR / LOAD_FACTOR_DENOMINATOR);
        }

        private void Resize(int capacity)
        {
            ulong[] oldKeys = _keys;
            int[] oldValues = _values;
            byte[] oldStates = _states;
            Initialize(capacity);
            if (oldKeys == null)
            {
                return;
            }

            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldStates[i] == 1)
                {
                    Set(oldKeys[i], oldValues[i]);
                }
            }
        }

        private static int RequiredCapacity(int capacity)
        {
            return (capacity * LOAD_FACTOR_DENOMINATOR + LOAD_FACTOR_NUMERATOR - 1) / LOAD_FACTOR_NUMERATOR;
        }

        private static uint Mix(ulong key)
        {
            unchecked
            {
                key ^= key >> 33;
                key *= 0xff51afd7ed558ccdUL;
                key ^= key >> 33;
                key *= 0xc4ceb9fe1a85ec53UL;
                key ^= key >> 33;
                return (uint)(key ^ (key >> 32));
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }

        #endregion
    }
}
