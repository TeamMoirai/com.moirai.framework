using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 名称↔ID 的计数字典注册表：packed resource key 的三个组成轴（package / location / type）共用这一份实现。
    /// </summary>
    /// <remarks>
    /// 零分配是硬约束：不实现任何接口（免受 struct 实参装箱），不暴露 <see cref="IEnumerable{T}"/> 或 foreach 枚举（枚举器装箱即分配）。
    /// 比较器用默认而非 <see cref="IEqualityComparer{T}"/> 字段：<see cref="string"/> 与 <see cref="Type"/> 的默认比较即序数/引用语义。
    /// </remarks>
    /// <typeparam name="TValue">被登记的键类型（<see cref="string"/> 或 <see cref="Type"/>）。</typeparam>
    internal sealed class ResourceNameRegistry<TValue> where TValue : class
    {
        private readonly Dictionary<TValue, int> _ids = new Dictionary<TValue, int>();
        private readonly Stack<int> _freeIds = new Stack<int>();
        private readonly int _maxId;
        private readonly TValue _miss;

        private TValue[] _values;
        private int[] _refCounts;
        private int _nextId = 1;

        /// <param name="maxId">该轴在 packed key 里能表达的最大 id，超出即抛。</param>
        /// <param name="miss">按 id 取不到名称时回什么（字符串轴回 <see cref="string.Empty"/>，类型轴回 null）。</param>
        public ResourceNameRegistry(int maxId, TValue miss)
        {
            _maxId = maxId;
            _miss = miss;
        }

        /// <summary>取 id，不存在则登记并返回新 id。归一化由调用方在此之前完成。</summary>
        public int GetOrAdd(TValue value)
        {
            if (_ids.TryGetValue(value, out int id))
            {
                return id;
            }

            id = AllocateId();
            _ids.Add(value, id);
            EnsureSlot(id);
            _values[id] = value;
            return id;
        }

        /// <summary>只读查询：不登记、不分配。packed key 的非驻留取键路径走这里。</summary>
        public bool TryGetId(TValue value, out int id) => _ids.TryGetValue(value, out id);

        /// <summary>按 id 取名。越界或该槽位已被回收时回 <paramref name="miss"/>。</summary>
        public TValue GetValue(int id)
        {
            if (_values == null || id <= 0 || id >= _values.Length)
            {
                return _miss;
            }

            TValue value = _values[id];
            return value ?? _miss;
        }

        /// <summary>记录一条持有该键的资源时 +1。</summary>
        public void Retain(int id)
        {
            if (_refCounts == null || id <= 0 || id >= _refCounts.Length)
            {
                return;
            }

            _refCounts[id]++;
        }

        /// <summary>
        /// 记录释放时计数 -1；归零则摘掉名字与槽位并把 id 压回空闲栈，供同名键复用。
        /// </summary>
        public void Release(int id)
        {
            if (!DecrementToZero(id))
            {
                return;
            }

            TValue value = id < _values.Length ? _values[id] : null;
            if (value == null)
            {
                return;
            }

            _ids.Remove(value);
            _values[id] = null;
            _freeIds.Push(id);
        }

        /// <summary>
        /// 只把计数减一，不摘字典、不回收 id。
        /// </summary>
        /// <remarks>整表清空（后端重置）走这条而非 <see cref="Release"/>：避免逐条回收，也避免遍历别表键时反向改动本表字典。</remarks>
        public void DecrementOnly(int id)
        {
            if (_refCounts == null || id <= 0 || id >= _refCounts.Length || _refCounts[id] <= 0)
            {
                return;
            }

            _refCounts[id]--;
        }

        private bool DecrementToZero(int id)
        {
            if (_refCounts == null || id <= 0 || id >= _refCounts.Length || _refCounts[id] <= 0)
            {
                return false;
            }

            _refCounts[id]--;
            return _refCounts[id] == 0;
        }

        private int AllocateId()
        {
            while (_freeIds.Count > 0)
            {
                int freeId = _freeIds.Pop();
                if (freeId > 0 && freeId <= _maxId)
                {
                    return freeId;
                }
            }

            if (_nextId <= 0 || _nextId > _maxId)
            {
                throw new GameException("Resource key id range exceeded.");
            }

            return _nextId++;
        }

        private void EnsureSlot(int id)
        {
            EnsureArray(ref _values, id);
            EnsureArray(ref _refCounts, id);
        }

        private static void EnsureArray<T>(ref T[] array, int index)
        {
            if (array == null)
            {
                array = new T[Math.Max(16, index + 1)];
                return;
            }

            if (index < array.Length)
            {
                return;
            }

            Array.Resize(ref array, Math.Max(index + 1, array.Length << 1));
        }
    }
}
