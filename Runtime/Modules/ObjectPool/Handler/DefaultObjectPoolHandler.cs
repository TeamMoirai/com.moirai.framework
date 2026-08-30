using System;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 默认通用池后端配置（分页槽位 + 按名链 + 最小堆维护调度）。当前无专有数据字段。
    /// </summary>
    [Serializable]
    public sealed class DefaultObjectPoolHandlerConfig : ObjectPoolServiceHandlerConfig
    {
        /// <inheritdoc />
        public override ObjectPoolServiceHandler CreateHandler()
        {
            return new DefaultObjectPoolHandler();
        }
    }

    /// <summary>
    /// 通用对象池默认处理器：分页槽位存储 + 按名复用链 + 引用计数 + 最小堆维护调度。
    /// <para><see cref="ObjectPoolServiceHandler"/> 的内置实现，池体为内嵌 <see cref="ObjectPool{T}"/>。</para>
    /// <para>由 <see cref="DefaultObjectPoolHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化——运行时字段无需 [NonSerialized] 标注）。</para>
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class DefaultObjectPoolHandler : ObjectPoolServiceHandler
    {
        #region 常量 [CONSTANTS]

        private const int INIT_POOL_ARRAY_CAPACITY = 8;
        private const float DEFAULT_AUTO_RELEASE_INTERVAL = float.MaxValue;
        private const int DEFAULT_CAPACITY = int.MaxValue;
        private const float DEFAULT_EXPIRE_TIME = float.MaxValue;

        #endregion

        #region 字段 [FIELDS]

        // struct 哈希表/调度器必须存于可变字段（方法直接改写字段状态），禁止 readonly。
        private PoolMaintenanceScheduler _scheduler;
        private OpenHashMap<ObjectPoolKey> _poolMap;
        private ObjectPoolBase[] _pools;
        private int _poolCount;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取池数量。
        /// </summary>
        public override int Count => _poolMap.Count;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化（幂等：无条件重建全部运行时状态）。
        /// </summary>
        protected override void OnInit()
        {
            _scheduler = new PoolMaintenanceScheduler();
            _poolMap = new OpenHashMap<ObjectPoolKey>(INIT_POOL_ARRAY_CAPACITY);
            _pools = new ObjectPoolBase[INIT_POOL_ARRAY_CAPACITY];
            _poolCount = 0;
            Application.lowMemory += OnLowMemory;
        }

        /// <summary>
        /// 处理器关闭。
        /// </summary>
        protected override void OnShutdown()
        {
            Application.lowMemory -= OnLowMemory;

            for (int i = _poolCount - 1; i >= 0; i--)
            {
                _pools[i].Shutdown();
            }

            _scheduler.Clear();
            _poolMap.Dispose();
            Array.Clear(_pools, 0, _poolCount);
            _poolCount = 0;
        }

        /// <summary>
        /// 每帧轮询——处理到期的池维护操作。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            _scheduler.ProcessDue(Time.realtimeSinceStartup);
        }

        #endregion

        #region 池管理 [POOL MANAGEMENT]

        /// <summary>
        /// 是否存在指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>是否存在。</returns>
        public override bool HasObjectPool<T>(string name)
        {
            return _poolMap.ContainsKey(new ObjectPoolKey(typeof(T), name));
        }

        /// <summary>
        /// 获取指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>池实例；不存在返回 null。</returns>
        public override IObjectPool<T> GetObjectPool<T>(string name)
        {
            return (IObjectPool<T>)InternalGetPool(new ObjectPoolKey(typeof(T), name));
        }

        /// <summary>
        /// 获取或创建指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="options">创建选项（已存在时忽略）。</param>
        /// <returns>池实例。</returns>
        public override IObjectPool<T> GetOrCreatePool<T>(ObjectPoolCreateOptions options)
        {
            ObjectPoolKey key = new ObjectPoolKey(typeof(T), options.Name);
            if (_poolMap.TryGetValue(key, out int idx))
            {
                return (IObjectPool<T>)_pools[idx];
            }

            ObjectPool<T> pool = new ObjectPool<T>(
                _scheduler,
                options.Name ?? string.Empty,
                options.AllowMultiSpawn,
                options.AutoReleaseInterval ?? DEFAULT_AUTO_RELEASE_INTERVAL,
                options.Capacity ?? DEFAULT_CAPACITY,
                options.ExpireTime ?? DEFAULT_EXPIRE_TIME,
                options.Priority);

            int storageIndex = _poolCount;
            if (storageIndex >= _pools.Length)
            {
                ObjectPoolBase[] newArr = new ObjectPoolBase[_pools.Length * 2];
                Array.Copy(_pools, 0, newArr, 0, _poolCount);
                _pools = newArr;
            }

            _pools[storageIndex] = pool;
            _poolCount++;
            _poolMap.AddOrUpdate(key, storageIndex);
            return pool;
        }

        /// <summary>
        /// 销毁指定类型的池（释放其全部对象）。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>是否销毁成功。</returns>
        public override bool DestroyObjectPool<T>(string name)
        {
            return InternalDestroyPool(new ObjectPoolKey(typeof(T), name));
        }

        #endregion

        #region 释放 [RELEASE]

        /// <summary>
        /// 释放所有池的全部可释放对象。
        /// </summary>
        public override void Release()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i].Release();
            }
        }

        /// <summary>
        /// 释放所有池的全部未使用且可释放的对象。
        /// </summary>
        public override void ReleaseAllUnused()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i].ReleaseAllUnused();
            }
        }

        #endregion

        #region 调试 [DEBUG]

        /// <summary>
        /// 获取全部池（按优先级可选排序）填充到结果数组。
        /// </summary>
        /// <param name="sort">是否按优先级降序排序。</param>
        /// <param name="results">结果数组。</param>
        /// <returns>池总数（可能超出数组容量）。</returns>
        public override int GetAllObjectPools(bool sort, ObjectPoolBase[] results)
        {
            if (results == null)
            {
                LogUtility.Error("Results is invalid.");
                return 0;
            }

            int count = _poolCount;
            int copy = results.Length < count ? results.Length : count;
            if (sort)
            {
                for (int i = 0; i < copy; i++)
                {
                    results[i] = _pools[i];
                }

                for (int i = 1; i < copy; i++)
                {
                    ObjectPoolBase key = results[i];
                    int keyPriority = key.Priority;
                    int j = i - 1;
                    while (j >= 0 && results[j].Priority > keyPriority)
                    {
                        results[j + 1] = results[j];
                        j--;
                    }

                    results[j + 1] = key;
                }
            }
            else
            {
                Array.Copy(_pools, 0, results, 0, copy);
            }

            return count;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private ObjectPoolBase InternalGetPool(ObjectPoolKey key)
        {
            if (_poolMap.TryGetValue(key, out int idx))
            {
                return _pools[idx];
            }

            return null;
        }

        private bool InternalDestroyPool(ObjectPoolKey key)
        {
            if (!_poolMap.TryGetValue(key, out int idx))
            {
                return false;
            }

            ObjectPoolBase pool = _pools[idx];
            _scheduler.Remove(pool);
            pool.Shutdown();

            int lastIndex = _poolCount - 1;
            if (idx < lastIndex)
            {
                ObjectPoolBase lastPool = _pools[lastIndex];
                _pools[idx] = lastPool;
                _poolMap.AddOrUpdate(new ObjectPoolKey(lastPool.ObjectType, lastPool.Name), idx);
            }

            _pools[lastIndex] = null;
            _poolCount--;
            _poolMap.Remove(key);
            return true;
        }

        private void OnLowMemory()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i].ExecuteMaintenance(now, true);
            }
        }

        #endregion

        #region 内部池实现 [INTERNAL POOL IMPLEMENTATION]

        /// <summary>
        /// 通用池运行时实现：分页槽位 + 引用计数 + 按名复用链 + FIFO 未用链 + 堆调度维护。
        /// </summary>
        private sealed class ObjectPool<T> : ObjectPoolBase, IObjectPool<T> where T : ObjectBase
        {
            #region 常量 [CONSTANTS]

            private const int RELEASES_PER_WAKE = 8;
            private const float MAINTENANCE_WAKE_INTERVAL = 0.001f;

            #endregion

            #region 结构体 [STRUCTS]

            private struct ObjectSlot
            {
                public T Obj;
                public int SpawnCount;
                public float LastUseTime;
                public int PrevAvailable;
                public int NextAvailable;
                public int PrevUnused;
                public int NextUnused;
                public int PrevAll;
                public int NextAll;
                public byte Flags;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public bool IsAlive() => (Flags & 1) != 0;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void SetAlive(bool alive)
                {
                    Flags = alive ? (byte)1 : (byte)0;
                }
            }

            #endregion

            #region 字段 [FIELDS]

            private readonly PoolMaintenanceScheduler _scheduler;
            private readonly bool _allowMultiSpawn;
            private readonly MemoryPoolHandle _objectMemoryPoolHandle;

            private PoolSlotStorage<ObjectSlot> _storage;

            private ReferenceOpenHashMap _targetMap;
            private OpenHashMap<string> _availableNameHeadMap;
            private OpenHashMap<string> _allNameHeadMap;
            private bool _hasNameMap;

            private int _unnamedAvailableHead;
            private int _unnamedAllHead;
            private int _unusedHead;
            private int _unusedTail;
            private int _unusedCount;

            private float _autoReleaseInterval;
            private int _capacity;
            private float _expireTime;
            private int _priority;
            private float _overCapacitySince;
            private int _pendingReleaseCount;
            private bool _isShuttingDown;

            #endregion

            #region 构造 [CONSTRUCTOR]

            public ObjectPool(PoolMaintenanceScheduler scheduler, string name, bool allowMultiSpawn,
                float autoReleaseInterval, int capacity, float expireTime, int priority)
                : base(name)
            {
                _scheduler = scheduler;
                _allowMultiSpawn = allowMultiSpawn;
                _objectMemoryPoolHandle = MemoryPool.GetHandle(typeof(T));
                _storage.Initialize();
                _targetMap = new ReferenceOpenHashMap(16);
                _autoReleaseInterval = autoReleaseInterval;
                _capacity = capacity;
                _expireTime = expireTime;
                _priority = priority;
                _overCapacitySince = -1f;
                _pendingReleaseCount = 0;
                _unnamedAvailableHead = -1;
                _unnamedAllHead = -1;
                _unusedHead = -1;
                _unusedTail = -1;
                _unusedCount = 0;
                _isShuttingDown = false;
            }

            #endregion

            #region 属性 [PROPERTIES]

            /// <summary>
            /// 获取对象类型。
            /// </summary>
            public override Type ObjectType => typeof(T);

            /// <summary>
            /// 获取池内对象总数。
            /// </summary>
            public override int Count => _targetMap.Count;

            /// <summary>
            /// 获取是否允许引用计数复用。
            /// </summary>
            public override bool AllowMultiSpawn => _allowMultiSpawn;

            /// <summary>
            /// 获取或设置超容自动释放间隔（秒）。
            /// </summary>
            public override float AutoReleaseInterval
            {
                get => _autoReleaseInterval;
                set
                {
                    if (value < 0f)
                    {
                        LogUtility.Error("AutoReleaseInterval is invalid.");
                        return;
                    }

                    _autoReleaseInterval = value;
                    RefreshMaintenance();
                }
            }

            /// <summary>
            /// 获取或设置池容量。
            /// </summary>
            public override int Capacity
            {
                get => _capacity;
                set
                {
                    if (value < 0)
                    {
                        LogUtility.Error("Capacity is invalid.");
                        return;
                    }

                    _capacity = value;
                    if (Count > _capacity)
                    {
                        MarkRelease(Count - _capacity);
                    }

                    RefreshMaintenance();
                }
            }

            /// <summary>
            /// 获取或设置空闲过期时间（秒）。
            /// </summary>
            public override float ExpireTime
            {
                get => _expireTime;
                set
                {
                    if (value < 0f)
                    {
                        LogUtility.Error("ExpireTime is invalid.");
                        return;
                    }

                    _expireTime = value;
                    RefreshMaintenance();
                }
            }

            /// <summary>
            /// 获取或设置池优先级。
            /// </summary>
            public override int Priority
            {
                get => _priority;
                set => _priority = value;
            }

            private bool TrackLastUseTime => _expireTime < float.MaxValue;

            #endregion

            #region 注册与取用 [REGISTER & SPAWN]

            /// <summary>
            /// 注册对象入池。
            /// </summary>
            /// <param name="obj">池化对象。</param>
            /// <param name="spawned">是否立即取用。</param>
            /// <returns>是否成功。</returns>
            public bool Register(T obj, bool spawned)
            {
                if (obj == null || obj.Target == null)
                {
                    LogUtility.Error("Object or target is invalid in pool '{0}'.", FullName);
                    if (obj != null)
                    {
                        RecycleObject(obj);
                    }

                    return false;
                }

                if (_targetMap.TryGetValue(obj.Target, out int existingIdx)
                    && _storage.IsValidIndex(existingIdx)
                    && _storage.GetSlotRef(existingIdx).IsAlive())
                {
                    LogUtility.Error("Target '{0}' is already registered in pool '{1}'.",
                        obj.Target.GetType().FullName, FullName);
                    RecycleObject(obj);
                    return false;
                }

                if (!EnsureRegisterCapacity())
                {
                    LogUtility.Error("Object pool '{0}' capacity is full.", FullName);
                    RecycleObject(obj);
                    return false;
                }

                int idx = _storage.AllocSlot();
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                slot = default;
                slot.Obj = obj;
                slot.SpawnCount = spawned ? 1 : 0;
                slot.LastUseTime = 0f;
                slot.PrevAvailable = -1;
                slot.NextAvailable = -1;
                slot.PrevUnused = -1;
                slot.NextUnused = -1;
                slot.PrevAll = -1;
                slot.NextAll = -1;
                slot.SetAlive(true);

                _targetMap.AddOrUpdate(obj.Target, idx);
                if (_allowMultiSpawn)
                {
                    AddToAllNameChain(idx);
                }

                if (TrackLastUseTime)
                {
                    float now = Time.realtimeSinceStartup;
                    slot.LastUseTime = now;
                    obj.LastUseTime = now;
                }

                if (spawned)
                {
                    obj.OnSpawn();
                }
                else
                {
                    MarkSlotAvailable(idx);
                }

                RefreshMaintenance();
                ValidateState();
                return true;
            }

            /// <summary>
            /// 取用一个对象（无名）。
            /// </summary>
            /// <returns>对象；无可复用对象返回 null。</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Spawn() => Spawn(string.Empty);

            /// <summary>
            /// 按名取用一个对象。
            /// </summary>
            /// <param name="name">对象名称。</param>
            /// <returns>对象；无可复用对象返回 null。</returns>
            public T Spawn(string name)
            {
                if (name == null)
                {
                    name = string.Empty;
                }

                if (_allowMultiSpawn)
                {
                    return SpawnAny(name);
                }

                int head = FindAvailableByName(name);
                if (head < 0)
                {
                    return null;
                }

                SpawnSlot(head);
                return _storage.GetSlotRef(head).Obj;
            }

            /// <summary>
            /// 归还对象。
            /// </summary>
            /// <param name="obj">池化对象。</param>
            public void Despawn(T obj)
            {
                if (obj == null || obj.Target == null)
                {
                    return;
                }

                if (!_targetMap.TryGetValue(obj.Target, out int idx))
                {
                    if (_isShuttingDown)
                    {
                        return;
                    }

                    LogUtility.Error("Cannot find target in pool '{0}', type='{1}'.",
                        Name, obj.Target.GetType().FullName);
                    return;
                }

                DespawnSlot(idx);
            }

            /// <summary>
            /// 按引用目标归还对象。
            /// </summary>
            /// <param name="target">引用目标。</param>
            public void DespawnTarget(object target)
            {
                if (target == null)
                {
                    return;
                }

                if (!_targetMap.TryGetValue(target, out int idx))
                {
                    if (_isShuttingDown)
                    {
                        return;
                    }

                    LogUtility.Error("Cannot find target in pool '{0}', type='{1}'.",
                        Name, target.GetType().FullName);
                    return;
                }

                DespawnSlot(idx);
            }

            #endregion

            #region 释放 [RELEASE]

            /// <summary>
            /// 释放全部可释放对象。
            /// </summary>
            public override void Release()
            {
                ReleaseAllUnused();
            }

            /// <summary>
            /// 释放指定数量的空闲对象。
            /// </summary>
            /// <param name="toReleaseCount">释放数量。</param>
            public override void Release(int toReleaseCount)
            {
                if (toReleaseCount <= 0)
                {
                    return;
                }

                int released = ReleaseUnused(toReleaseCount, false, float.MinValue);
                _pendingReleaseCount = Math.Max(0, _pendingReleaseCount - released);
                RefreshMaintenance();
                if (released > 0)
                {
                    ValidateState();
                }
            }

            /// <summary>
            /// 释放全部未使用且可释放的对象。
            /// </summary>
            public override void ReleaseAllUnused()
            {
                int released = 0;
                int current = _unusedHead;
                while (current >= 0)
                {
                    int next = _storage.GetSlotRef(current).NextUnused;
                    if (CanReleaseSlot(ref _storage.GetSlotRef(current)))
                    {
                        ReleaseSlot(current);
                        released++;
                    }

                    current = next;
                }

                _pendingReleaseCount = 0;
                RefreshMaintenance();
                if (released > 0)
                {
                    ValidateState();
                }
            }

            #endregion

            #region 维护调度 [MAINTENANCE SCHEDULING]

            /// <summary>
            /// 执行一次维护操作：超容间隔释放 → 预算释放 → 过期释放（每次唤醒最多 RELEASES_PER_WAKE 个）。
            /// </summary>
            /// <param name="now">当前实时时钟。</param>
            /// <param name="lowMemory">是否为低内存强制维护。</param>
            public override void ExecuteMaintenance(float now, bool lowMemory)
            {
                if (lowMemory)
                {
                    ReleaseAllUnused();
                    return;
                }

                // 超容持续达到间隔 → 标记超出部分待释放（"连续超容"计时，回落即重置）。
                if (_overCapacitySince >= 0f && now - _overCapacitySince >= _autoReleaseInterval)
                {
                    _overCapacitySince = now;
                    MarkRelease(Count - _capacity);
                }

                if (_pendingReleaseCount > 0)
                {
                    // 预算钳制到待释放数——待释放计数是"需要"而非"可以"，超发会多杀对象。
                    int releaseBudget = Math.Min(RELEASES_PER_WAKE, _pendingReleaseCount);
                    int released = ReleaseUnused(releaseBudget, false, float.MinValue);
                    _pendingReleaseCount = Math.Max(0, _pendingReleaseCount - released);
                }
                else if (_expireTime < float.MaxValue && _unusedCount > 0)
                {
                    ReleaseUnused(RELEASES_PER_WAKE, true, now - _expireTime);
                }

                RefreshMaintenance();
            }

            internal override void Shutdown()
            {
                _isShuttingDown = true;
                int slotCount = _storage.SlotCount;
                for (int i = 0; i < slotCount; i++)
                {
                    ref ObjectSlot slot = ref _storage.GetSlotRef(i);
                    if (!slot.IsAlive())
                    {
                        continue;
                    }

                    slot.Obj.Release(true);
                    RecycleObject(slot.Obj);
                    slot.Obj = null;
                    slot.SetAlive(false);
                }

                _scheduler.Remove(this);
                _targetMap.Dispose();
                if (_hasNameMap)
                {
                    _availableNameHeadMap.Dispose();
                    if (_allowMultiSpawn)
                    {
                        _allNameHeadMap.Dispose();
                    }

                    _hasNameMap = false;
                }

                _storage.ReturnStorage();
                _pendingReleaseCount = 0;
                _overCapacitySince = -1f;
                _unnamedAvailableHead = -1;
                _unnamedAllHead = -1;
                _unusedHead = -1;
                _unusedTail = -1;
                _unusedCount = 0;
                _isShuttingDown = false;
            }

            #endregion

            #region 内部契约 [INTERNAL CONTRACTS]

            internal override int GetAllObjectInfos(ObjectInfo[] results)
            {
                if (results == null)
                {
                    LogUtility.Error("Results is invalid.");
                    return 0;
                }

                int write = 0;
                int capacity = results.Length;
                int slotCount = _storage.SlotCount;
                for (int i = 0; i < slotCount; i++)
                {
                    ref ObjectSlot slot = ref _storage.GetSlotRef(i);
                    if (!slot.IsAlive())
                    {
                        continue;
                    }

                    if (write < capacity)
                    {
                        results[write] = new ObjectInfo(slot.Obj.Name, slot.Obj.Locked,
                            slot.Obj.CustomCanReleaseFlag,
                            slot.Obj.LastUseTime, slot.SpawnCount);
                    }

                    write++;
                }

                return write;
            }

            #endregion

            #region 私有方法 — 取用核心 [PRIVATE SPAWN CORE]

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void SpawnSlot(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (slot.SpawnCount == 0)
                {
                    MarkSlotUnavailable(idx);
                }

                slot.SpawnCount++;
                if (TrackLastUseTime)
                {
                    float now = Time.realtimeSinceStartup;
                    slot.LastUseTime = now;
                    slot.Obj.LastUseTime = now;
                }

                slot.Obj.OnSpawn();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void DespawnSlot(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (TrackLastUseTime)
                {
                    float now = Time.realtimeSinceStartup;
                    slot.LastUseTime = now;
                    slot.Obj.LastUseTime = now;
                }

                slot.Obj.OnDespawn();
                slot.SpawnCount--;
                if (slot.SpawnCount < 0)
                {
                    LogUtility.Error("Object '{0}' spawn count < 0.", slot.Obj.Name);
                    slot.SpawnCount = 0;
                }

                if (slot.SpawnCount == 0)
                {
                    MarkSlotAvailable(idx);
                }

                if (Count > _capacity && slot.SpawnCount == 0)
                {
                    MarkRelease(Count - _capacity);
                }

                RefreshMaintenance();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void MarkRelease(int count)
            {
                if (count > 0)
                {
                    _pendingReleaseCount = Math.Max(_pendingReleaseCount, count);
                }
            }

            private T SpawnAny(string name)
            {
                int head = FindAllByName(name);
                if (head < 0)
                {
                    return null;
                }

                SpawnSlot(head);
                return _storage.GetSlotRef(head).Obj;
            }

            #endregion

            #region 私有方法 — 槽位释放 [PRIVATE SLOT RELEASE]

            private void ReleaseSlot(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (!slot.IsAlive() || slot.SpawnCount > 0)
                {
                    return;
                }

                T obj = slot.Obj;
                MarkSlotUnavailable(idx);
                if (_allowMultiSpawn)
                {
                    RemoveFromAllNameChain(idx);
                }

                _targetMap.Remove(obj.Target);

                obj.Release(false);
                RecycleObject(obj);

                slot.Obj = null;
                slot.SetAlive(false);
                slot.SpawnCount = 0;
                slot.PrevAvailable = -1;
                slot.NextAvailable = -1;
                slot.PrevUnused = -1;
                slot.NextUnused = -1;
                slot.PrevAll = -1;
                slot.NextAll = -1;
                _storage.FreeSlot(idx);
            }

            private bool EnsureRegisterCapacity()
            {
                if (_capacity == int.MaxValue || Count < _capacity)
                {
                    return true;
                }

                int released = ReleaseUnused(1, false, float.MinValue);
                if (released > 0)
                {
                    _pendingReleaseCount = Math.Max(0, _pendingReleaseCount - released);
                }

                return Count < _capacity;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void RecycleObject(T obj)
            {
                // 仅回收经 MemoryPool.Acquire 取出的对象；外部 new 构造的对象无属主句柄，交由 GC。
                if (obj != null && obj.OwnerHandle.IsValid && _objectMemoryPoolHandle.IsValid)
                {
                    _objectMemoryPoolHandle.Release(obj);
                }
            }

            private int ReleaseUnused(int maxReleaseCount, bool requireExpired, float expireThreshold)
            {
                int released = 0;
                int visited = 0;
                int limit = _unusedCount;
                int current = _unusedHead;

                while (current >= 0 && released < maxReleaseCount && visited < limit)
                {
                    visited++;
                    ref ObjectSlot slot = ref _storage.GetSlotRef(current);
                    int next = slot.NextUnused;

                    if (requireExpired && slot.LastUseTime > expireThreshold)
                    {
                        current = next;
                        continue;
                    }

                    if (CanReleaseSlot(ref slot))
                    {
                        ReleaseSlot(current);
                        released++;
                    }

                    current = next;
                }

                return released;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool CanReleaseSlot(ref ObjectSlot slot)
            {
                return slot.IsAlive()
                       && slot.SpawnCount == 0
                       && !slot.Obj.Locked
                       && slot.Obj.CustomCanReleaseFlag;
            }

            #endregion

            #region 私有方法 — 链表维护 [PRIVATE CHAIN MAINTENANCE]

            private void MarkSlotAvailable(int idx)
            {
                AddToUnusedListTail(idx);
                AddToAvailableNameChain(idx);
            }

            private void MarkSlotUnavailable(int idx)
            {
                RemoveFromAvailableNameChain(idx);
                RemoveFromUnusedList(idx);
            }

            private int FindAvailableByName(string name)
            {
                if (name.Length == 0)
                {
                    return _unnamedAvailableHead;
                }

                if (!_hasNameMap)
                {
                    return -1;
                }

                if (!_availableNameHeadMap.TryGetValue(name, out int head))
                {
                    return -1;
                }

                return head;
            }

            private int FindAllByName(string name)
            {
                if (name.Length == 0)
                {
                    return _unnamedAllHead;
                }

                if (!_hasNameMap)
                {
                    return -1;
                }

                if (!_allNameHeadMap.TryGetValue(name, out int head))
                {
                    return -1;
                }

                return head;
            }

            private void AddToAvailableNameChain(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (!slot.IsAlive() || slot.SpawnCount != 0)
                {
                    return;
                }

                if (slot.PrevAvailable >= 0 || slot.NextAvailable >= 0)
                {
                    return;
                }

                string objectName = slot.Obj.Name ?? string.Empty;
                if (objectName.Length == 0)
                {
                    if (_unnamedAvailableHead == idx)
                    {
                        return;
                    }

                    slot.NextAvailable = _unnamedAvailableHead;
                    slot.PrevAvailable = -1;
                    if (_unnamedAvailableHead >= 0)
                    {
                        _storage.GetSlotRef(_unnamedAvailableHead).PrevAvailable = idx;
                    }

                    _unnamedAvailableHead = idx;
                    return;
                }

                EnsureNameMap();
                if (_availableNameHeadMap.TryGetValue(objectName, out int head))
                {
                    if (head == idx)
                    {
                        return;
                    }

                    _storage.GetSlotRef(head).PrevAvailable = idx;
                    slot.NextAvailable = head;
                }
                else
                {
                    slot.NextAvailable = -1;
                }

                slot.PrevAvailable = -1;
                _availableNameHeadMap.AddOrUpdate(objectName, idx);
            }

            private void RemoveFromAvailableNameChain(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (!slot.IsAlive())
                {
                    return;
                }

                string objectName = slot.Obj.Name ?? string.Empty;
                int prev = slot.PrevAvailable;
                int next = slot.NextAvailable;
                bool isUnnamed = objectName.Length == 0;
                bool isHead = isUnnamed ? _unnamedAvailableHead == idx : false;
                if (!isUnnamed && _hasNameMap && _availableNameHeadMap.TryGetValue(objectName, out int namedHead))
                {
                    isHead = namedHead == idx;
                }

                if (!isHead && prev < 0 && next < 0)
                {
                    return;
                }

                if (prev >= 0)
                {
                    _storage.GetSlotRef(prev).NextAvailable = next;
                }
                else if (isHead)
                {
                    if (isUnnamed)
                    {
                        _unnamedAvailableHead = next;
                    }
                    else if (next >= 0)
                    {
                        _availableNameHeadMap.AddOrUpdate(objectName, next);
                    }
                    else
                    {
                        _availableNameHeadMap.Remove(objectName);
                    }
                }

                if (next >= 0)
                {
                    _storage.GetSlotRef(next).PrevAvailable = prev;
                }

                slot.PrevAvailable = -1;
                slot.NextAvailable = -1;
            }

            private void AddToAllNameChain(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                string objectName = slot.Obj.Name ?? string.Empty;
                if (objectName.Length == 0)
                {
                    slot.NextAll = _unnamedAllHead;
                    slot.PrevAll = -1;
                    if (_unnamedAllHead >= 0)
                    {
                        _storage.GetSlotRef(_unnamedAllHead).PrevAll = idx;
                    }

                    _unnamedAllHead = idx;
                    return;
                }

                EnsureNameMap();
                if (_allNameHeadMap.TryGetValue(objectName, out int head))
                {
                    _storage.GetSlotRef(head).PrevAll = idx;
                    slot.NextAll = head;
                }
                else
                {
                    slot.NextAll = -1;
                }

                slot.PrevAll = -1;
                _allNameHeadMap.AddOrUpdate(objectName, idx);
            }

            private void RemoveFromAllNameChain(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                string objectName = slot.Obj.Name ?? string.Empty;
                int prev = slot.PrevAll;
                int next = slot.NextAll;
                bool isUnnamed = objectName.Length == 0;
                bool isHead = isUnnamed ? _unnamedAllHead == idx : false;
                if (!isUnnamed && _hasNameMap && _allNameHeadMap.TryGetValue(objectName, out int namedHead))
                {
                    isHead = namedHead == idx;
                }

                if (!isHead && prev < 0 && next < 0)
                {
                    return;
                }

                if (prev >= 0)
                {
                    _storage.GetSlotRef(prev).NextAll = next;
                }
                else if (isHead)
                {
                    if (isUnnamed)
                    {
                        _unnamedAllHead = next;
                    }
                    else if (next >= 0)
                    {
                        _allNameHeadMap.AddOrUpdate(objectName, next);
                    }
                    else
                    {
                        _allNameHeadMap.Remove(objectName);
                    }
                }

                if (next >= 0)
                {
                    _storage.GetSlotRef(next).PrevAll = prev;
                }

                slot.PrevAll = -1;
                slot.NextAll = -1;
            }

            private void AddToUnusedListTail(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (_unusedHead == idx || slot.PrevUnused >= 0 || slot.NextUnused >= 0)
                {
                    return;
                }

                slot.PrevUnused = _unusedTail;
                slot.NextUnused = -1;
                if (_unusedTail >= 0)
                {
                    _storage.GetSlotRef(_unusedTail).NextUnused = idx;
                }
                else
                {
                    _unusedHead = idx;
                }

                _unusedTail = idx;
                _unusedCount++;
            }

            private void RemoveFromUnusedList(int idx)
            {
                ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                if (_unusedHead != idx && slot.PrevUnused < 0 && slot.NextUnused < 0)
                {
                    return;
                }

                int prev = slot.PrevUnused;
                int next = slot.NextUnused;
                if (prev >= 0)
                {
                    _storage.GetSlotRef(prev).NextUnused = next;
                }
                else
                {
                    _unusedHead = next;
                }

                if (next >= 0)
                {
                    _storage.GetSlotRef(next).PrevUnused = prev;
                }
                else
                {
                    _unusedTail = prev;
                }

                slot.PrevUnused = -1;
                slot.NextUnused = -1;
                _unusedCount--;
            }

            private void EnsureNameMap()
            {
                if (_hasNameMap)
                {
                    return;
                }

                int cap = Math.Max(8, _storage.SlotCount);
                _availableNameHeadMap = new OpenHashMap<string>(cap);
                if (_allowMultiSpawn)
                {
                    _allNameHeadMap = new OpenHashMap<string>(cap);
                }

                _hasNameMap = true;
            }

            #endregion

            #region 私有方法 — 维护调度 [PRIVATE MAINTENANCE SCHEDULING]

            /// <summary>
            /// 重算本池下一次维护到期时间并调度：待释放项 → 下一帧再醒；
            /// 过期项 → 未用链头（最旧）的过期点；超容项 → 连续超容达到间隔的点。
            /// </summary>
            private void RefreshMaintenance()
            {
                float now = Time.realtimeSinceStartup;
                float due = float.MaxValue;

                if (_pendingReleaseCount > 0)
                {
                    due = now + MAINTENANCE_WAKE_INTERVAL;
                }

                if (_expireTime < float.MaxValue && _unusedHead >= 0)
                {
                    float expireDue = _storage.GetSlotRef(_unusedHead).LastUseTime + _expireTime;
                    if (expireDue <= now)
                    {
                        expireDue = now + MAINTENANCE_WAKE_INTERVAL;
                    }

                    if (expireDue < due)
                    {
                        due = expireDue;
                    }
                }

                bool overCapacity = _autoReleaseInterval < float.MaxValue && Count > _capacity;
                if (overCapacity)
                {
                    if (_overCapacitySince < 0f)
                    {
                        _overCapacitySince = now;
                    }

                    float intervalDue = _overCapacitySince + _autoReleaseInterval;
                    if (intervalDue < due)
                    {
                        due = intervalDue;
                    }
                }
                else
                {
                    _overCapacitySince = -1f;
                }

                if (due <= now)
                {
                    due = now + MAINTENANCE_WAKE_INTERVAL;
                }

                _scheduler.Schedule(this, due);
            }

            [Conditional("UNITY_EDITOR")]
            private void ValidateState()
            {
#if UNITY_EDITOR && ENABLE_OBJECTPOOL_VALIDATION
                int aliveCount = 0;
                int unusedCount = 0;
                int slotCount = _storage.SlotCount;
                for (int idx = 0; idx < slotCount; idx++)
                {
                    ref ObjectSlot slot = ref _storage.GetSlotRef(idx);
                    if (!slot.IsAlive())
                    {
                        continue;
                    }

                    aliveCount++;
                    object target = slot.Obj.Target;
                    if (!_targetMap.TryGetValue(target, out int mappedIdx) || mappedIdx != idx)
                    {
                        LogUtility.Error("Object pool '{0}' target index map is inconsistent.", FullName);
                        continue;
                    }

                    if (slot.SpawnCount == 0)
                    {
                        unusedCount++;
                    }
                }

                if (aliveCount != _targetMap.Count)
                {
                    LogUtility.Error("Object pool '{0}' alive count is inconsistent.", FullName);
                }

                int walkUnusedCount = 0;
                int current = _unusedHead;
                int prevUnused = -1;
                while (current >= 0)
                {
                    ref ObjectSlot slot = ref _storage.GetSlotRef(current);
                    if (!slot.IsAlive() || slot.SpawnCount != 0)
                    {
                        LogUtility.Error("Object pool '{0}' unused chain contains invalid slot.", FullName);
                    }

                    if (slot.PrevUnused != prevUnused)
                    {
                        LogUtility.Error("Object pool '{0}' unused chain linkage is inconsistent.", FullName);
                    }

                    walkUnusedCount++;
                    prevUnused = current;
                    current = slot.NextUnused;
                }

                if (walkUnusedCount != unusedCount || walkUnusedCount != _unusedCount)
                {
                    LogUtility.Error("Object pool '{0}' unused chain count is inconsistent.", FullName);
                }
#endif
            }

            #endregion
        }

        #endregion
    }
}
