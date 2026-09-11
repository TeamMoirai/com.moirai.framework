using System;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池化实例注册表：GameObject 引用 → (池, Slot) 的零分配反向映射。
    /// <para>代系由 Slot 独占维护（租期级），本表仅负责实例身份解析；主线程单线程访问，无需同步。</para>
    /// </summary>
    internal sealed class PooledInstanceRegistry : IDisposable
    {
        #region 结构体 [STRUCTS]

        private struct Entry
        {
            public RuntimeGameObjectPool Pool;
            public int SlotIndex;
            public int NextFree;
        }

        #endregion

        #region 字段 [FIELDS]

        private Entry[] _entries;
        private int _freeHead;
        private int _allocCount;
        private int _count;
        private ReferenceOpenHashMap _map;

        #endregion

        #region 构造 [CONSTRUCTOR]

        public PooledInstanceRegistry(int capacity = 64)
        {
            _entries = new Entry[capacity];
            _freeHead = -1;
            _allocCount = 0;
            _count = 0;
            _map = new ReferenceOpenHashMap(capacity);
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        public void Register(GameObject instance, RuntimeGameObjectPool pool, int slotIndex)
        {
            if (instance == null)
            {
                return;
            }

            if (_map.TryGetValue(instance, out int existing))
            {
                _entries[existing].Pool = pool;
                _entries[existing].SlotIndex = slotIndex;
                return;
            }

            int index = AllocEntry();
            _entries[index].Pool = pool;
            _entries[index].SlotIndex = slotIndex;
            _map.AddOrUpdate(instance, index);
            _count++;
        }

        public void Unregister(GameObject instance)
        {
            // 真 null 才跳过：Unity 假空（外部 Destroy）仍持有托管引用，必须移除条目防泄漏。
            if (ReferenceEquals(instance, null))
            {
                return;
            }

            if (!_map.TryGetValue(instance, out int index))
            {
                return;
            }

            _map.Remove(instance);
            FreeEntry(index);
            _count--;
        }

        public bool TryResolve(GameObject instance, out RuntimeGameObjectPool pool, out int slotIndex)
        {
            pool = null;
            slotIndex = -1;
            if (instance == null)
            {
                return false;
            }

            if (!_map.TryGetValue(instance, out int index))
            {
                return false;
            }

            Entry entry = _entries[index];
            pool = entry.Pool;
            slotIndex = entry.SlotIndex;
            return pool != null;
        }

        public void Clear()
        {
            _map.Clear();
            Array.Clear(_entries, 0, _allocCount);
            _freeHead = -1;
            _allocCount = 0;
            _count = 0;
        }

        public void Dispose()
        {
            Clear();
            _map.Dispose();
            _entries = null;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private int AllocEntry()
        {
            if (_freeHead >= 0)
            {
                int index = _freeHead;
                _freeHead = _entries[index].NextFree;
                _entries[index] = default;
                _entries[index].NextFree = -1;
                return index;
            }

            if (_allocCount >= _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length << 1);
            }

            int allocated = _allocCount++;
            _entries[allocated] = default;
            _entries[allocated].NextFree = -1;
            return allocated;
        }

        private void FreeEntry(int index)
        {
            _entries[index] = default;
            _entries[index].NextFree = _freeHead;
            _freeHead = index;
        }

        #endregion
    }
}
