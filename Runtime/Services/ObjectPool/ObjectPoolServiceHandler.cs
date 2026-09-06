using System;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用对象池处理器抽象基类（策略模式抽象策略）。
    /// <para>默认实现为 <see cref="DefaultObjectPoolHandler"/>（分页槽位存储 + 按名链 + 最小堆维护调度）。</para>
    /// <para>可在 <see cref="ObjectPoolServiceSettings"/> 中替换为自定义实现。</para>
    /// </summary>
    [Serializable]
    public abstract class ObjectPoolServiceHandler : FrameworkHandler
    {
        #region 轮询 [TICK]

        /// <summary>
        /// 每帧轮询——处理到期的池维护操作。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        #endregion

        #region 池管理 [POOL MANAGEMENT]

        /// <summary>
        /// 获取池数量。
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// 是否存在指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>是否存在。</returns>
        public abstract bool HasObjectPool<T>(string name = "") where T : ObjectBase;

        /// <summary>
        /// 获取指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>池实例；不存在返回 null。</returns>
        public abstract IObjectPool<T> GetObjectPool<T>(string name = "") where T : ObjectBase;

        /// <summary>
        /// 获取或创建指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="options">创建选项（已存在时忽略）。</param>
        /// <returns>池实例。</returns>
        public abstract IObjectPool<T> GetOrCreatePool<T>(ObjectPoolCreateOptions options = default) where T : ObjectBase;

        /// <summary>
        /// 销毁指定类型的池（释放其全部对象）。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>是否销毁成功。</returns>
        public abstract bool DestroyObjectPool<T>(string name = "") where T : ObjectBase;

        #endregion

        #region 释放 [RELEASE]

        /// <summary>
        /// 释放所有池的全部可释放对象。
        /// </summary>
        public abstract void Release();

        /// <summary>
        /// 释放所有池的全部未使用且可释放的对象。
        /// </summary>
        public abstract void ReleaseAllUnused();

        #endregion

        #region 调试 [DEBUG]

        /// <summary>
        /// 获取全部池（按优先级可选排序）填充到结果数组。
        /// </summary>
        /// <param name="sort">是否按优先级降序排序。</param>
        /// <param name="results">结果数组。</param>
        /// <returns>池总数（可能超出数组容量）。</returns>
        public abstract int GetAllObjectPools(bool sort, ObjectPoolBase[] results);

        #endregion
    }
}
