using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 游戏对象池处理器。使用最小堆调度维护，PoolCatalog 数据驱动配置。
    /// <para>由 <see cref="ObjectPoolServiceSettings"/> 序列化配置，可替换为自定义对象池后端。</para>
    /// </summary>
    [Serializable]
    public abstract class ObjectPoolServiceHandler : FrameworkHandler
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化。
        /// </summary>
        protected override void OnInit()
        {
        }

        /// <summary>
        /// 处理器关闭。
        /// </summary>
        protected override void OnShutdown()
        {
        }

        /// <summary>
        /// 每帧 Tick，处理到期的维护操作。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        #endregion

        #region 对象池操作 [POOL OPERATIONS]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        public abstract GameObject Spawn(string location, Transform parent = null);

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        public abstract T Spawn<T>(string location, Transform parent = null) where T : Component;

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        public abstract bool TrySpawn(string location, Transform parent, out GameObject instance);

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        public abstract UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        public abstract UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        /// <summary>
        /// 异步预热。
        /// </summary>
        public abstract UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default);

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        public abstract void Despawn(GameObject instance);

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        public abstract void Despawn(ObjectPoolHandle handle);

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        public abstract void Flush(string location);

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        public abstract void FlushGroup(string group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        public abstract void FlushAll();

        /// <summary>
        /// 加载池配置。
        /// </summary>
        public abstract void LoadCatalog(PoolConfigScriptableObject config);

        #endregion

        #region 调试接口 [DEBUG INTERFACE]

        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        public abstract ObjectPoolSummarySnapshot GetDebugSummary();

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public abstract int GetDebugSnapshots(ObjectPoolSnapshot[] snapshots);

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public abstract void FillDebugInstances(ObjectPoolSnapshot snapshot);

        #endregion

        #region 内部方法 — 维护调度 [INTERNAL MAINTENANCE SCHEDULING]

        internal abstract void ScheduleMaintenance(int poolIndex, float dueTime, ref int heapIndex);

        internal abstract void RemoveMaintenance(ref int heapIndex);

        #endregion
    }
}
