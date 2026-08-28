using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池策略枚举。
    /// </summary>
    public enum EPoolPolicy : byte
    {
        /// <summary>
        /// 固定容量，超出软上限立即裁剪。
        /// </summary>
        Fixed = 0,

        /// <summary>
        /// 突发容忍，空闲超时才裁剪。
        /// </summary>
        Burst = 1,

        /// <summary>
        /// 粘性保留，不主动回收。
        /// </summary>
        Sticky = 2
    }

    /// <summary>
    /// 池化对象生成上下文。
    /// </summary>
    public readonly struct PoolSpawnContext
    {
        /// <summary>
        /// 资源地址。
        /// </summary>
        public readonly string Location;

        /// <summary>
        /// 分组名称。
        /// </summary>
        public readonly string Group;

        /// <summary>
        /// 父级 Transform。
        /// </summary>
        public readonly Transform Parent;

        /// <summary>
        /// 生成帧号。
        /// </summary>
        public readonly uint SpawnFrame;

        /// <summary>
        /// 初始化 <see cref="PoolSpawnContext"/> 的新实例。
        /// </summary>
        public PoolSpawnContext(string location, string group, Transform parent, uint spawnFrame)
        {
            Location = location;
            Group = group;
            Parent = parent;
            SpawnFrame = spawnFrame;
        }
    }

    /// <summary>
    /// 可池化对象接口。
    /// </summary>
    public interface IObjectPoolable
    {
        /// <summary>
        /// 对象从池中获取时调用。
        /// </summary>
        /// <param name="context">生成上下文。</param>
        void OnSpawn(in PoolSpawnContext context);

        /// <summary>
        /// 对象归还池中时调用。
        /// </summary>
        void OnDespawn();

        /// <summary>
        /// 对象从池中永久销毁时调用。
        /// </summary>
        void OnPooledDestroy();
    }

    /// <summary>
    /// 全局统计快照。
    /// </summary>
    public readonly struct ObjectPoolSummarySnapshot
    {
        /// <summary>
        /// 获取是否就绪。
        /// </summary>
        public readonly bool IsReady;

        /// <summary>
        /// 获取池数量。
        /// </summary>
        public readonly int PoolCount;

        /// <summary>
        /// 获取已加载预制体数量。
        /// </summary>
        public readonly int LoadedPrefabCount;

        /// <summary>
        /// 获取总实例数量。
        /// </summary>
        public readonly int TotalInstanceCount;

        /// <summary>
        /// 获取活跃实例数量。
        /// </summary>
        public readonly int ActiveInstanceCount;

        /// <summary>
        /// 获取非活跃实例数量。
        /// </summary>
        public readonly int InactiveInstanceCount;

        /// <summary>
        /// 获取待维护数量。
        /// </summary>
        public readonly int PendingMaintenanceCount;

        /// <summary>
        /// 初始化 <see cref="ObjectPoolSummarySnapshot"/> 的新实例。
        /// </summary>
        public ObjectPoolSummarySnapshot(
            bool isReady,
            int poolCount,
            int loadedPrefabCount,
            int totalInstanceCount,
            int activeInstanceCount,
            int inactiveInstanceCount,
            int pendingMaintenanceCount)
        {
            IsReady = isReady;
            PoolCount = poolCount;
            LoadedPrefabCount = loadedPrefabCount;
            TotalInstanceCount = totalInstanceCount;
            ActiveInstanceCount = activeInstanceCount;
            InactiveInstanceCount = inactiveInstanceCount;
            PendingMaintenanceCount = pendingMaintenanceCount;
        }
    }

    /// <summary>
    /// 实例级快照。
    /// </summary>
    public sealed class ObjectPoolInstanceSnapshot : MemoryObject
    {
        /// <summary>
        /// 实例名称。
        /// </summary>
        public string instanceName;

        /// <summary>
        /// 是否活跃。
        /// </summary>
        public bool isActive;

        /// <summary>
        /// 空闲时长。
        /// </summary>
        public float idleDuration;

        /// <summary>
        /// 生命周期时长。
        /// </summary>
        public float lifeDuration;

        /// <summary>
        /// 游戏对象引用。
        /// </summary>
        public GameObject gameObject;

        /// <summary>
        /// 清理快照。
        /// </summary>
        public override void Clear()
        {
            instanceName = null;
            isActive = false;
            idleDuration = 0f;
            lifeDuration = 0f;
            gameObject = null;
        }
    }

    /// <summary>
    /// 单池快照。
    /// </summary>
    public sealed class ObjectPoolSnapshot : MemoryObject
    {
        #region 字段 [FIELDS]

        /// <summary>
        /// 条目名称。
        /// </summary>
        public string entryName;

        /// <summary>
        /// 分组名称。
        /// </summary>
        public string group;

        /// <summary>
        /// 资源地址。
        /// </summary>
        public string location;

        /// <summary>
        /// 池策略。
        /// </summary>
        public EPoolPolicy policy;

        /// <summary>
        /// 最小空闲数量。
        /// </summary>
        public int minIdle;

        /// <summary>
        /// 保留目标。
        /// </summary>
        public int retainTarget;

        /// <summary>
        /// 软容量。
        /// </summary>
        public int softCapacity;

        /// <summary>
        /// 硬容量。
        /// </summary>
        public int hardCapacity;

        /// <summary>
        /// 是否卸载预制体。
        /// </summary>
        public bool unloadPrefab;

        /// <summary>
        /// 总数量。
        /// </summary>
        public int totalCount;

        /// <summary>
        /// 活跃数量。
        /// </summary>
        public int activeCount;

        /// <summary>
        /// 非活跃数量。
        /// </summary>
        public int inactiveCount;

        /// <summary>
        /// 预制体是否已加载。
        /// </summary>
        public bool prefabLoaded;

        /// <summary>
        /// 距下次维护的秒数。
        /// </summary>
        public float nextMaintenanceIn;

        /// <summary>
        /// 生成次数。
        /// </summary>
        public int spawnCount;

        /// <summary>
        /// 回收次数。
        /// </summary>
        public int despawnCount;

        /// <summary>
        /// 命中次数。
        /// </summary>
        public int hitCount;

        /// <summary>
        /// 未命中次数。
        /// </summary>
        public int missCount;

        /// <summary>
        /// 扩展次数。
        /// </summary>
        public int expandCount;

        /// <summary>
        /// 销毁次数。
        /// </summary>
        public int destroyCount;

        /// <summary>
        /// 峰值活跃数。
        /// </summary>
        public int peakActive;

        /// <summary>
        /// 实例列表。
        /// </summary>
        internal readonly List<ObjectPoolInstanceSnapshot> instances = new List<ObjectPoolInstanceSnapshot>(16);

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取实例数量。
        /// </summary>
        public int InstanceCount => instances.Count;

        /// <summary>
        /// 获取指定索引的实例快照。
        /// </summary>
        /// <param name="index">索引。</param>
        /// <returns>实例快照。</returns>
        public ObjectPoolInstanceSnapshot GetInstance(int index)
        {
            return (uint)index < (uint)instances.Count ? instances[index] : null;
        }

        #endregion

        #region 方法 [METHODS]

        /// <summary>
        /// 清理快照。
        /// </summary>
        public override void Clear()
        {
            entryName = null;
            group = null;
            location = null;
            policy = default;
            minIdle = 0;
            retainTarget = 0;
            softCapacity = 0;
            hardCapacity = 0;
            unloadPrefab = false;
            totalCount = 0;
            activeCount = 0;
            inactiveCount = 0;
            prefabLoaded = false;
            nextMaintenanceIn = 0f;
            spawnCount = 0;
            despawnCount = 0;
            hitCount = 0;
            missCount = 0;
            expandCount = 0;
            destroyCount = 0;
            peakActive = 0;
            ClearInstances();
        }

        internal void ClearInstances()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                MemoryPool.Release(instances[i]);
            }

            instances.Clear();
        }

        #endregion
    }

    /// <summary>
    /// 池化对象句柄，附着在池化 GameObject 上，提供代系校验。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectPoolHandle : MonoBehaviour
    {
        #region 字段 [FIELDS]

        private RuntimeObjectPool _owner;
        private int _slotIndex = -1;
        private uint _generation;

        #endregion

        #region 内部属性 [INTERNAL PROPERTIES]

        internal int SlotIndex => _slotIndex;
        internal uint Generation => _generation;

        #endregion

        #region 内部方法 [INTERNAL METHODS]

        internal void Bind(RuntimeObjectPool owner, int slotIndex, uint generation)
        {
            _owner = owner;
            _slotIndex = slotIndex;
            _generation = generation;
        }

        internal void Detach()
        {
            _owner = null;
            _slotIndex = -1;
            _generation = 0;
        }

        internal bool TryRelease()
        {
            return _owner != null && _owner.ReleaseFromHandle(this);
        }

        #endregion

        #region Unity 回调 [UNITY CALLBACKS]

        private void OnDestroy()
        {
            if (_owner != null)
            {
                _owner.NotifyHandleDestroyed(_slotIndex, _generation);
            }
        }

        #endregion
    }
}
