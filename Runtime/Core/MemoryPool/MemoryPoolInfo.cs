using System;
using System.Runtime.InteropServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 内存池信息。
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct MemoryPoolInfo
    {
        #region 字段 [FIELDS]

        private Type _type;
        private int _unusedCount;
        private int _usingCount;
        private int _acquireCount;
        private int _releaseCount;
        private int _createCount;
        private int _missCount;
        private int _targetFreeReserve;
        private int _maxCapacity;
        private int _idleFrames;
        private int _pageCapacity;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取内存池类型。
        /// </summary>
        public Type Type => _type;

        /// <summary>
        /// 获取未使用内存对象数量。
        /// </summary>
        public int UnusedCount => _unusedCount;

        /// <summary>
        /// 获取正在使用内存对象数量。
        /// </summary>
        public int UsingCount => _usingCount;

        /// <summary>
        /// 获取内存对象获取次数。
        /// </summary>
        public int AcquireCount => _acquireCount;

        /// <summary>
        /// 获取内存对象归还次数。
        /// </summary>
        public int ReleaseCount => _releaseCount;

        /// <summary>
        /// 获取内存对象创建次数。
        /// </summary>
        public int CreateCount => _createCount;

        /// <summary>
        /// 获取池未命中次数（Acquire 时池中无可用对象，需紧急创建）。
        /// </summary>
        public int MissCount => _missCount;

        /// <summary>
        /// 获取池未命中率（MissCount / AcquireCount，0~1）。AcquireCount 为 0 时返回 0。
        /// </summary>
        public float MissRate => _acquireCount > 0 ? (float)_missCount / _acquireCount : 0f;

        /// <summary>
        /// 获取目标空闲缓存数量。
        /// </summary>
        public int TargetFreeReserve => _targetFreeReserve;

        /// <summary>
        /// 获取硬容量上限。
        /// </summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>
        /// 获取空闲帧数。
        /// </summary>
        public int IdleFrames => _idleFrames;

        /// <summary>
        /// 获取页容量。
        /// </summary>
        public int PageCapacity => _pageCapacity;

        #endregion

        /// <summary>
        /// 设置内存池信息。
        /// </summary>
        internal void Set(Type type, int unusedCount, int usingCount,
            int acquireCount, int releaseCount, int createCount,
            int missCount,
            int targetFreeReserve, int maxCapacity,
            int idleFrames, int pageCapacity)
        {
            _type = type;
            _unusedCount = unusedCount;
            _usingCount = usingCount;
            _acquireCount = acquireCount;
            _releaseCount = releaseCount;
            _createCount = createCount;
            _missCount = missCount;
            _targetFreeReserve = targetFreeReserve;
            _maxCapacity = maxCapacity;
            _idleFrames = idleFrames;
            _pageCapacity = pageCapacity;
        }
    }
}
