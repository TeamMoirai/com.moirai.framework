using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 内存池静态门面。
    /// </summary>
    public static partial class MemoryPool
    {
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 最小空闲保留数量。
        /// </summary>
        public const int MinimumFreeReserveLimit = 4;

        /// <summary>
        /// 池空闲多少帧后开始衰减目标空闲水位。实际每 tick 驱逐数量由 Phase 预算决定（Gameplay=2）。默认 1800 帧（@60fps ≈ 30秒）。
        /// </summary>
        public static int ShortDecayStartFrames = 1800;

        /// <summary>
        /// 池空闲多少帧后加速衰减目标空闲水位。实际每 tick 驱逐数量由 Phase 预算决定。默认 7200 帧（@60fps ≈ 2分钟）。
        /// </summary>
        public static int LongDecayStartFrames = 7200;

        /// <summary>
        /// 池空闲多少帧后停止调度 Tick（省 CPU）。默认 18000 帧（@60fps ≈ 5分钟）。
        /// </summary>
        public static int UnscheduleIdleFrames = 18000;

        /// <summary>
        /// 池空闲多少帧后允许目标空闲缓存降为 0。默认 7200 帧（@60fps ≈ 2分钟）。
        /// </summary>
        public static int ZeroFreeReserveStartFrames = 7200;

        /// <summary>
        /// 池空闲多少帧后，若已完全空闲则自动释放 Native 元数据。默认 18000 帧（@60fps ≈ 5分钟）。
        /// </summary>
        public static int AutoTrimNativeMetadataFrames = 18000;

        /// <summary>
        /// 默认空闲缓存软上限。
        /// </summary>
        public static int DefaultSoftFreeReserveLimit = 128;

        /// <summary>
        /// 默认空闲缓存硬上限。
        /// </summary>
        public static int DefaultHardFreeReserveLimit = 512;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取内存池的数量。
        /// </summary>
        public static int Count => MemoryPoolRegistry.Count;

        #endregion

        #region 获取 [ACQUIRE]

        /// <summary>
        /// 从内存池获取内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <returns>内存对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Acquire<T>() where T : MemoryObject, new()
        {
            return MemoryPool<T>.Acquire();
        }

        /// <summary>
        /// 获取动态内存类型的缓存句柄。运行时热路径应提前缓存该句柄，避免反复使用 Type 查找。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <returns>缓存句柄。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryPoolHandle GetHandle(Type memoryType)
        {
            return MemoryPoolRegistry.GetHandle(memoryType);
        }

        /// <summary>
        /// 从内存池获取内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <returns>内存对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryObject Acquire(Type memoryType)
        {
            return MemoryPoolRegistry.Acquire(memoryType);
        }

        #endregion

        #region 归还 [RELEASE]

        /// <summary>
        /// 将内存对象归还内存池。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <param name="memory">内存对象。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release<T>(T memory) where T : MemoryObject, new()
        {
            MemoryPool<T>.Release(memory);
        }

        /// <summary>
        /// 将内存对象归还内存池。
        /// </summary>
        /// <param name="memory">内存对象。</param>
        public static void Release(MemoryObject memory)
        {
            MemoryPoolRegistry.Release(memory);
        }

        #endregion

        #region 追加与移除 [ADD & REMOVE]

        /// <summary>
        /// 向内存池中追加指定数量的内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <param name="count">追加数量。</param>
        public static void Add<T>(int count) where T : MemoryObject, new()
        {
            MemoryPool<T>.Add(count);
        }

        /// <summary>
        /// 向内存池中追加指定数量的内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <param name="count">追加数量。</param>
        public static void Add(Type memoryType, int count)
        {
            MemoryPoolRegistry.Add(memoryType, count);
        }

        /// <summary>
        /// 从内存池中移除指定数量的内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <param name="count">移除数量。</param>
        public static void Remove<T>(int count) where T : MemoryObject, new()
        {
            int target = MemoryPool<T>.UnusedCount - count;
            MemoryPool<T>.Shrink(target);
        }

        /// <summary>
        /// 从内存池中移除指定数量的内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <param name="count">移除数量。</param>
        public static void Remove(Type memoryType, int count)
        {
            MemoryPoolRegistry.RemoveFromType(memoryType, count);
        }

        /// <summary>
        /// 从内存池中移除所有的内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        public static void RemoveAll<T>() where T : MemoryObject, new()
        {
            MemoryPool<T>.ClearAll();
        }

        /// <summary>
        /// 从内存池中移除所有的内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        public static void RemoveAll(Type memoryType)
        {
            MemoryPoolRegistry.ClearType(memoryType);
        }

        #endregion

        #region 容量管理 [CAPACITY MANAGEMENT]

        /// <summary>
        /// 设置指定类型内存池容量。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <param name="softCapacity">软容量上限。</param>
        /// <param name="hardCapacity">硬容量上限。</param>
        public static void SetCapacity<T>(int softCapacity, int hardCapacity) where T : MemoryObject, new()
        {
            MemoryPool<T>.SetCapacity(softCapacity, hardCapacity);
        }

        /// <summary>
        /// 设置指定类型内存池容量。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <param name="softCapacity">软容量上限。</param>
        /// <param name="hardCapacity">硬容量上限。</param>
        public static void SetCapacity(Type memoryType, int softCapacity, int hardCapacity)
        {
            MemoryPoolRegistry.SetCapacity(memoryType, softCapacity, hardCapacity);
        }

        /// <summary>
        /// 设置所有内存池的默认容量。
        /// </summary>
        /// <param name="softCapacity">软容量上限。</param>
        /// <param name="hardCapacity">硬容量上限。</param>
        public static void SetDefaultCapacity(int softCapacity, int hardCapacity)
        {
            softCapacity = Math.Max(softCapacity, MinimumFreeReserveLimit);
            hardCapacity = Math.Max(hardCapacity, softCapacity);
            DefaultSoftFreeReserveLimit = softCapacity;
            DefaultHardFreeReserveLimit = hardCapacity;
            MemoryPoolRegistry.SetCapacityAll(softCapacity, hardCapacity);
        }

        /// <summary>
        /// 压缩指定类型内存池。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        public static void Compact<T>() where T : MemoryObject, new()
        {
            MemoryPool<T>.Compact();
        }

        /// <summary>
        /// 压缩指定类型内存池。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        public static void Compact(Type memoryType)
        {
            MemoryPoolRegistry.CompactType(memoryType);
        }

        /// <summary>
        /// 压缩所有内存池。
        /// </summary>
        public static void CompactAll()
        {
            MemoryPoolRegistry.CompactAll();
        }

        #endregion

        #region Native 元数据 [NATIVE METADATA]

        /// <summary>
        /// 修剪指定类型内存池的 Native 元数据。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        public static void TrimNativeMetadata<T>() where T : MemoryObject, new()
        {
            MemoryPool<T>.TrimNativeMetadata();
        }

        /// <summary>
        /// 修剪指定类型内存池的 Native 元数据。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        public static void TrimNativeMetadata(Type memoryType)
        {
            MemoryPoolRegistry.TrimNativeMetadata(memoryType);
        }

        /// <summary>
        /// 修剪所有内存池的 Native 元数据。
        /// </summary>
        public static void TrimAllNativeMetadata()
        {
            MemoryPoolRegistry.TrimAllNativeMetadata();
        }

        #endregion

        #region 统计 [STATISTICS]

        /// <summary>
        /// 重置所有内存池统计信息。
        /// </summary>
        public static void ResetAllStats()
        {
            MemoryPoolRegistry.ResetAllStats();
        }

        #endregion

        #region 信息 [INFO]

        /// <summary>
        /// 获取所有内存池信息到指定缓冲区。
        /// </summary>
        /// <param name="infos">信息缓冲区。</param>
        /// <returns>内存池数量。</returns>
        public static int GetAllMemoryPoolInfos(MemoryPoolInfo[] infos)
        {
            return MemoryPoolRegistry.GetAllInfos(infos);
        }

        #endregion

        #region 清除 [CLEAR]

        /// <summary>
        /// 清除所有内存池。
        /// </summary>
        public static void ClearAll()
        {
            MemoryPoolRegistry.ClearAll();
        }

        #endregion
    }
}
