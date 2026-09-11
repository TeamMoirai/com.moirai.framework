using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Pool;
using Moirai.Atropos.Schedulers;
using UnityEngine;
#if R3_INSTALLED
using Moirai.Atropos.R3;
#endif

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池化 GameObject 租约（纯 C#，非 MonoBehaviour）。
    /// <para>持有 (owner, slot, generation) 身份，是原 Handle 与包装层的合并形态；Dispose 时按代系回收。</para>
    /// <para>与 <see cref="GameObjectPoolService"/> 共用 Spawn / SpawnAsync / Despawn 动词，location 与 Prefab 引用同一套 API。</para>
    /// </summary>
    public class PooledGameObject : IDisposable
#if R3_INSTALLED
        , IDisposableUnregister
#endif
    {
        #region 常量 [CONSTANTS]

        private static readonly Internal_ObjectPool<PooledGameObject> s_Pool = new Internal_ObjectPool<PooledGameObject>(() => new PooledGameObject());

        #endregion

        #region 字段 [FIELDS]

        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private List<SchedulerHandle> _schedulerHandles;

        private RuntimeGameObjectPool _owner;
        private int _slotIndex = -1;
        private uint _generation;
        private GameObject _instance;
        private Transform _transform;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取池化游戏对象。
        /// </summary>
        public GameObject GameObject => _instance;

        /// <summary>
        /// 获取池化游戏对象的 Transform。
        /// </summary>
        public Transform Transform => _transform;

        /// <summary>
        /// 获取租约是否仍指向有效且处于 Active 状态的实例（租期代系校验）。
        /// </summary>
        public bool IsValid => _owner != null && _owner.IsAlive(_slotIndex, _generation);

        /// <summary>
        /// 获取是否已释放。
        /// </summary>
        protected bool IsDisposed { get; private set; }

        #endregion

        #region 公共方法 — Spawn [PUBLIC SPAWN]

        /// <summary>
        /// 将已生成实例包装为租约（不触发新的 Spawn）。仅 Active 实例可包装。
        /// </summary>
        /// <param name="instance">池化实例。</param>
        /// <returns>池化租约。</returns>
        public static PooledGameObject Wrap(GameObject instance)
        {
            if (!GameObjectPoolService.TryResolveInstance(instance, out RuntimeGameObjectPool pool, out int slotIndex))
            {
                return null;
            }

            if (!pool.TryBindLease(slotIndex, out uint generation, out GameObject bound, out Transform transform))
            {
                return null;
            }

            PooledGameObject pooled = s_Pool.Get();
            pooled.Bind(pool, slotIndex, generation, bound, transform);
            return pooled;
        }

        /// <summary>
        /// 按池化来源同步获取租约（地址或 Prefab）。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>池化租约。</returns>
        public static PooledGameObject Spawn(GameObjectPoolSource source, Transform parent = null) =>
            Wrap(GameObjectPoolService.Spawn(source, parent));

        /// <summary>
        /// 按池化来源异步获取租约。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>池化租约。</returns>
        public static async UniTask<PooledGameObject> SpawnAsync(GameObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) =>
            Wrap(await GameObjectPoolService.SpawnAsync(source, parent, cancellationToken));

        #endregion

        #region 公共方法 — 用户数据 [PUBLIC USER DATA]

        /// <summary>
        /// 获取随实例跨池复用保留的用户数据。
        /// </summary>
        /// <typeparam name="T">用户数据类型。</typeparam>
        /// <returns>用户数据；类型不匹配或无效租约为 null。</returns>
        public T GetUserData<T>() where T : class =>
            IsValid ? _owner.GetUserData<T>(_slotIndex) : null;

        /// <summary>
        /// 获取或创建用户数据。
        /// </summary>
        /// <typeparam name="T">用户数据类型（需可无参构造）。</typeparam>
        /// <returns>用户数据；无效租约为 null。</returns>
        public T GetOrAddUserData<T>() where T : class, new() =>
            IsValid ? _owner.GetOrAddUserData<T>(_slotIndex) : null;

        /// <summary>
        /// 覆盖用户数据。
        /// </summary>
        /// <typeparam name="T">用户数据类型。</typeparam>
        /// <param name="value">用户数据。</param>
        public void SetUserData<T>(T value) where T : class
        {
            if (IsValid)
            {
                _owner.SetUserData(_slotIndex, value);
            }
        }

        #endregion

        #region 公共方法 — 生命周期 [PUBLIC LIFECYCLE]

        /// <summary>
        /// 设置包装器池最大容量。
        /// </summary>
        /// <param name="size">最大容量。</param>
        public static void SetMaxSize(int size)
        {
            s_Pool.MaxSize = size;
        }

        /// <summary>
        /// 延迟回收。
        /// </summary>
        /// <param name="t">延迟秒数；&lt;= 0 立即回收。</param>
        protected void Destroy(float t = 0f)
        {
            if (t > 0f)
            {
                AddScheduler(Scheduler.Delay(t, Dispose));
            }
            else
            {
                Dispose();
            }
        }

        /// <summary>
        /// 回收池化对象（代系校验失败时静默忽略）。
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            OnDispose();
            ReleaseDisposables();
            if (_owner != null)
            {
                _owner.TryRelease(_slotIndex, _generation);
            }

            ClearIdentity();
            IsDisposed = true;
            ReturnToPool();
        }

#if R3_INSTALLED
        /// <summary>
        /// 池化范围内的 Disposable 管理。
        /// </summary>
        /// <param name="disposable">待注册的 Disposable。</param>
        void IDisposableUnregister.Register(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }
#endif

        #endregion

        #region 保护方法 [PROTECTED METHODS]

        /// <summary>
        /// 回收前回调。
        /// </summary>
        protected virtual void OnDispose() { }

        /// <summary>
        /// 将包装器实例归还对象池。
        /// </summary>
        protected virtual void ReturnToPool()
        {
            s_Pool.Release(this);
        }

        /// <summary>
        /// 记录调度器句柄，Dispose 时自动取消。
        /// </summary>
        /// <param name="handle">调度器句柄。</param>
        protected void AddScheduler(SchedulerHandle handle)
        {
            _schedulerHandles ??= new List<SchedulerHandle>(4);
            _schedulerHandles.Add(handle);
        }

        /// <summary>
        /// 绑定池身份并执行初始化。
        /// </summary>
        /// <param name="owner">所属池。</param>
        /// <param name="slotIndex">槽位。</param>
        /// <param name="generation">租期代系。</param>
        /// <param name="instance">实例引用。</param>
        /// <param name="transform">实例 Transform。</param>
        internal void Bind(RuntimeGameObjectPool owner, int slotIndex, uint generation, GameObject instance, Transform transform)
        {
            _owner = owner;
            _slotIndex = slotIndex;
            _generation = generation;
            _instance = instance;
            _transform = transform;
            IsDisposed = false;
            Init();
        }

        /// <summary>
        /// 初始化回调。
        /// </summary>
        protected virtual void Init()
        {
            _disposables.Clear();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void ClearIdentity()
        {
            _owner = null;
            _slotIndex = -1;
            _generation = 0;
            _instance = null;
            _transform = null;
        }

        private void ReleaseDisposables()
        {
            for (int i = 0; i < _disposables.Count; i++)
            {
                _disposables[i].Dispose();
            }

            _disposables.Clear();

            if (_schedulerHandles == null)
            {
                return;
            }

            for (int i = 0; i < _schedulerHandles.Count; i++)
            {
                _schedulerHandles[i].Cancel();
            }

            _schedulerHandles.Clear();
        }

        #endregion
    }
}
