using System.Runtime.InteropServices;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用池对象调试信息快照。
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ObjectInfo
    {
        #region 字段 [FIELDS]

        /// <summary>
        /// 对象名称。
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 是否锁定。
        /// </summary>
        public readonly bool Locked;

        /// <summary>
        /// 自定义可释放标记。
        /// </summary>
        public readonly bool CustomCanReleaseFlag;

        /// <summary>
        /// 最近使用时间（实时时钟）。
        /// </summary>
        public readonly float LastUseTime;

        /// <summary>
        /// 取用计数（引用计数模式可大于 1）。
        /// </summary>
        public readonly int SpawnCount;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取对象是否处于使用中。
        /// </summary>
        public bool IsInUse => SpawnCount > 0;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="ObjectInfo"/> 的新实例。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="locked">是否锁定。</param>
        /// <param name="customCanReleaseFlag">自定义可释放标记。</param>
        /// <param name="lastUseTime">最近使用时间。</param>
        /// <param name="spawnCount">取用计数。</param>
        public ObjectInfo(string name, bool locked, bool customCanReleaseFlag,
            float lastUseTime, int spawnCount)
        {
            Name = name;
            Locked = locked;
            CustomCanReleaseFlag = customCanReleaseFlag;
            LastUseTime = lastUseTime;
            SpawnCount = spawnCount;
        }

        #endregion
    }
}
