using System;
using System.Collections.Generic;
using Moirai.Atropos.Schedulers;
using UnityEngine;
using UnityEngine.Pool;
#if R3_INSTALLED
using Moirai.Atropos.R3;
#endif

namespace Moirai.Atropos.Pool
{
    /// <summary>
    /// 用于池化 gameObject 的包装器
    /// </summary>
    public class PooledGameObject : IDisposable
#if R3_INSTALLED
        , IDisposableUnregister
#endif
    {
        private static readonly _ObjectPool<PooledGameObject> Pool =
            new _ObjectPool<PooledGameObject>(() => new PooledGameObject());
        
        public GameObject GameObject { get; protected set; }
        
        public Transform Transform { get; protected set; }

        protected bool IsDisposed { get; set; }
        
        /// <summary>
        /// 池化范围内的 Disposable 管理
        /// </summary>
        /// <returns></returns>
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        
        private List<SchedulerHandle> _schedulerHandles;
        
        /// <summary>
        /// 游戏对象池的键
        /// </summary>
        /// <value></value>
        protected PoolKey PoolKey { get; set; }
        
        public static void SetMaxSize(int size)
        {
            Pool.MaxSize = size;
        }
        
        /// <summary>
        /// 按地址获取或创建空的池化游戏对象
        /// </summary>
        /// <param name="address"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        public static PooledGameObject Get(string address, Transform parent = null)
        {
            var pooledObject = Pool.Get();
            pooledObject.PoolKey = new PoolKey(address);
            pooledObject.GameObject = GameObjectPoolManager.Get(pooledObject.PoolKey, out _, parent, createEmptyIfNotExist: true);
            pooledObject.Init();
            return pooledObject;
        }

#if R3_INSTALLED
        /// <summary>
        /// 不应使用 AddTo(GameObject gameObject)，因为在池管理器清理之前不会销毁游戏对象。
        /// </summary>
        /// <param name="disposable"></param>
        /// <remarks>
        /// 实现 <see cref="IDisposableUnregister"/> 来管理池化范围内的 <see cref="IDisposable"/>。
        /// </remarks>
        void IDisposableUnregister.Register(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }
#endif

        /// <summary>
        /// 手动添加 <see cref="SchedulerHandle"/> 以减少分配
        /// </summary>
        /// <param name="handle"></param>
        protected void Add(SchedulerHandle handle)
        {
            _schedulerHandles ??= ListPool<SchedulerHandle>.Get();
            _schedulerHandles.Add(handle);
        }
        
        protected virtual void Init()
        {
            LocalInit();
        }
        
        private void LocalInit()
        {
            IsDisposed = false;
            Transform = GameObject.transform;
            InitDisposables();
        }
        
        public virtual void Dispose()
        {
            if (IsDisposed) return;
            ReleaseDisposables();
            if (GameObjectPoolManager.HasInstance) GameObjectPoolManager.Release(GameObject, PoolKey);
            IsDisposed = true;
            Pool.Release(this);
        }
        
        protected void InitDisposables()
        {
            _disposables.Clear();
        }
        
        protected void ReleaseDisposables()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
            _disposables.Clear();
            if (_schedulerHandles == null) return;
            foreach (var schedulerHandle in _schedulerHandles)
                schedulerHandle.Cancel();
            ListPool<SchedulerHandle>.Release(_schedulerHandles);
            _schedulerHandles = null;
        }

        protected unsafe void Destroy(float t = 0f)
        {
            if (t >= 0f)
                Add(Scheduler.DelayUnsafe(t, new SchedulerUnsafeBinding(this, &Dispose_Imp)));
            else
                Dispose();
        }
        
        private static void Dispose_Imp(object @object)
        {
            ((IDisposable)@object).Dispose();
        }
    }
}