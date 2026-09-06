using System;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用对象池抽象基类——统一池元数据与释放契约，维护经共享 <see cref="PoolMaintenanceScheduler"/> 调度。
    /// </summary>
    public abstract class ObjectPoolBase : IPoolMaintenanceItem
    {
        #region 字段 [FIELDS]

        private readonly string _name;
        private string _fullName;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="ObjectPoolBase"/> 的新实例。
        /// </summary>
        /// <param name="name">池名称。</param>
        protected ObjectPoolBase(string name)
        {
            _name = name ?? string.Empty;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取池名称。
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// 获取池全名（类型全名[.池名]，惰性缓存）。
        /// </summary>
        public string FullName
        {
            get
            {
                if (_fullName == null)
                {
                    _fullName = string.IsNullOrEmpty(_name)
                        ? ObjectType.FullName ?? ObjectType.Name
                        : StringUtility.Concat(ObjectType.FullName ?? ObjectType.Name, ".", _name);
                }

                return _fullName;
            }
        }

        /// <summary>
        /// 获取对象类型。
        /// </summary>
        public abstract Type ObjectType { get; }

        /// <summary>
        /// 获取池内对象总数。
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// 获取是否允许引用计数复用。
        /// </summary>
        public abstract bool AllowMultiSpawn { get; }

        /// <summary>
        /// 获取或设置超容自动释放间隔（秒）。
        /// </summary>
        public abstract float AutoReleaseInterval { get; set; }

        /// <summary>
        /// 获取或设置池容量。
        /// </summary>
        public abstract int Capacity { get; set; }

        /// <summary>
        /// 获取或设置空闲过期时间（秒）。
        /// </summary>
        public abstract float ExpireTime { get; set; }

        /// <summary>
        /// 获取或设置池优先级。
        /// </summary>
        public abstract int Priority { get; set; }

        #endregion

        #region 维护调度 [MAINTENANCE SCHEDULING]

        /// <summary>
        /// 维护堆索引——由 <see cref="PoolMaintenanceScheduler"/> 独占维护。
        /// </summary>
        public int MaintenanceHeapIndex { get; set; } = -1;

        /// <summary>
        /// 执行一次维护操作（由调度器到期唤醒或服务低内存遍历调用）。
        /// </summary>
        /// <param name="now">当前实时时钟。</param>
        /// <param name="lowMemory">是否为低内存强制维护。</param>
        public abstract void ExecuteMaintenance(float now, bool lowMemory);

        #endregion

        #region 释放 [RELEASE]

        /// <summary>
        /// 释放全部可释放对象。
        /// </summary>
        public abstract void Release();

        /// <summary>
        /// 释放指定数量的空闲对象。
        /// </summary>
        /// <param name="toReleaseCount">释放数量。</param>
        public abstract void Release(int toReleaseCount);

        /// <summary>
        /// 释放全部未使用且可释放的对象。
        /// </summary>
        public abstract void ReleaseAllUnused();

        #endregion

        #region 内部契约 [INTERNAL CONTRACTS]

        /// <summary>
        /// 获取全部对象调试信息。
        /// </summary>
        /// <param name="results">结果数组。</param>
        /// <returns>填充的对象总数（可能超出数组容量）。</returns>
        internal abstract int GetAllObjectInfos(ObjectInfo[] results);

        /// <summary>
        /// 关闭池（释放全部对象并归还存储）。
        /// </summary>
        internal abstract void Shutdown();

        #endregion
    }
}
