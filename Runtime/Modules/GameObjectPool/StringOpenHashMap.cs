using System;

namespace Moirai.Atropos.GameObjectPool
{
    /// <summary>
    /// 开放寻址字符串到 int 的零分配 HashMap。
    /// </summary>
    internal sealed class StringOpenHashMap : IDisposable
    {
        #region 常量 [CONSTANTS]

        private const float LOAD_FACTOR = 0.75f;

        #endregion

        #region 字段 [FIELDS]

        private string[] _keys;
        private int[] _values;
        private int _capacity;
        private int _count;
        private bool _disposed;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取已存储条目数量。
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// 获取是否已释放。
        /// </summary>
        public bool IsDisposed => _disposed;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="StringOpenHashMap"/> 的新实例。
        /// </summary>
        /// <param name="initialCapacity">初始容量。</param>
        public StringOpenHashMap(int initialCapacity)
        {
            _capacity = ToPowerOfTwo(Math.Max(8, initialCapacity));
            _keys = new string[_capacity];
            _values = new int[_capacity];
            _count = 0;
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 尝试获取值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">找到的值。</param>
        /// <returns>是否存在。</returns>
        public bool TryGetValue(string key, out int value)
        {
            int index = FindSlot(key);
            if (index >= 0 && _keys[index] != null)
            {
                value = _values[index];
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>
        /// 是否包含指定键。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>是否包含。</returns>
        public bool ContainsKey(string key)
        {
            int index = FindSlot(key);
            return index >= 0 && _keys[index] != null;
        }

        /// <summary>
        /// 添加或更新值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        public void AddOrUpdate(string key, int value)
        {
            if (_count >= _capacity * LOAD_FACTOR)
            {
                Grow();
            }

            int index = FindSlot(key);
            if (_keys[index] == null)
            {
                _count++;
            }

            _keys[index] = key;
            _values[index] = value;
        }

        /// <summary>
        /// 清空所有条目。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_keys, 0, _capacity);
            Array.Clear(_values, 0, _capacity);
            _count = 0;
        }

        /// <summary>
        /// 释放内部数组。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _keys = null;
            _values = null;
            _capacity = 0;
            _count = 0;
            _disposed = true;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private int FindSlot(string key)
        {
            int hash = GetStableHashCode(key) & 0x7FFFFFFF;
            int mask = _capacity - 1;
            int index = hash & mask;

            while (true)
            {
                string existing = _keys[index];
                if (existing == null)
                {
                    return index;
                }

                if (string.Equals(existing, key, StringComparison.Ordinal))
                {
                    return index;
                }

                index = (index + 1) & mask;
            }
        }

        private void Grow()
        {
            string[] oldKeys = _keys;
            int[] oldValues = _values;
            int oldCapacity = _capacity;

            _capacity <<= 1;
            _keys = new string[_capacity];
            _values = new int[_capacity];
            _count = 0;

            for (int i = 0; i < oldCapacity; i++)
            {
                if (oldKeys[i] != null)
                {
                    AddOrUpdate(oldKeys[i], oldValues[i]);
                }
            }
        }

        private static int ToPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }

        private static int GetStableHashCode(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }

            unchecked
            {
                int hash = 5381;
                for (int i = 0; i < s.Length; i++)
                {
                    hash = ((hash << 5) + hash) ^ s[i];
                }

                return hash;
            }
        }

        #endregion
    }
}
