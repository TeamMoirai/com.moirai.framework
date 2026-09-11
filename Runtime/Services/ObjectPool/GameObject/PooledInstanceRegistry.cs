using System;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池化实例注册表：GameObject 引用 → (池, Slot, 代系) 的零分配反向映射。
    /// <para>替代 MonoBehaviour Handle：Despawn(GameObject) 经引用哈希解析，无需 GetComponent。</para>
    /// </summary>
    internal sealed class PooledInstanceRegistry : IDisposable
    {
        #region 结构体 [STRUCTS]

        private struct Entry
        {
            public RuntimeGameObjectPool Pool;
            public int SlotIndex;
            public uint Generation;
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

        public void Register(GameObject instance, RuntimeGameObjectPool pool, int slotIndex, uint generation)
        {
            if (instance == null)
            {
                return;
            }

            if (_map.TryGetValue(instance, out int existing))
            {
                _entries[existing].Pool = pool;
                _entries[existing].SlotIndex = slotIndex;
                _entries[existing].Generation = generation;
                return;
            }

            int index = AllocEntry();
            _entries[index].Pool = pool;
            _entries[index].SlotIndex = slotIndex;
            _entries[index].Generation = generation;
            _map.AddOrUpdate(instance, index);
            _count++;
        }

        public void Unregister(GameObject instance)
        {
            if (instance == null)
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

        public bool TryResolve(GameObject instance, out RuntimeGameObjectPool pool, out int slotIndex, out uint generation)
        {
            pool = null;
            slotIndex = -1;
            generation = 0;
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
            generation = entry.Generation;
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
