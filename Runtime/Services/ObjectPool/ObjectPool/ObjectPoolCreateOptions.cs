namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用池创建选项（缺省项以服务默认值填充）。
    /// </summary>
    public readonly struct ObjectPoolCreateOptions
    {
        #region 字段 [FIELDS]

        /// <summary>
        /// 池名称（同类型多池区分键）。
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 是否允许同一对象被多次取用（引用计数模式）。
        /// </summary>
        public readonly bool AllowMultiSpawn;

        /// <summary>
        /// 超容自动释放间隔（秒）；null 表示不启用。
        /// </summary>
        public readonly float? AutoReleaseInterval;

        /// <summary>
        /// 池容量；null 表示不限。
        /// </summary>
        public readonly int? Capacity;

        /// <summary>
        /// 空闲过期时间（秒）；null 表示不过期。
        /// </summary>
        public readonly float? ExpireTime;

        /// <summary>
        /// 池优先级（调试排序用）。
        /// </summary>
        public readonly int Priority;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="ObjectPoolCreateOptions"/> 的新实例。
        /// </summary>
        /// <param name="name">池名称。</param>
        /// <param name="allowMultiSpawn">是否允许引用计数复用。</param>
        /// <param name="autoReleaseInterval">超容自动释放间隔（秒）。</param>
        /// <param name="capacity">池容量。</param>
        /// <param name="expireTime">空闲过期时间（秒）。</param>
        /// <param name="priority">池优先级。</param>
        public ObjectPoolCreateOptions(
            string name = "",
            bool allowMultiSpawn = false,
            float? autoReleaseInterval = null,
            int? capacity = null,
            float? expireTime = null,
            int priority = 0)
        {
            Name = name ?? string.Empty;
            AllowMultiSpawn = allowMultiSpawn;
            AutoReleaseInterval = autoReleaseInterval;
            Capacity = capacity;
            ExpireTime = expireTime;
            Priority = priority;
        }

        #endregion
    }
}
