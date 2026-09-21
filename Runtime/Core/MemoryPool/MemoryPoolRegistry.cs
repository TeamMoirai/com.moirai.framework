using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 内存池注册表，管理所有类型的内存池句柄。
    /// </summary>
    public static class MemoryPoolRegistry
    {
        #region 内部句柄 [INTERNAL HANDLE]

        internal sealed class MemoryPoolHandle
        {
            public delegate MemoryObject AcquireHandler();
            public delegate void ReleaseHandler(MemoryObject memory);
            public delegate void ClearHandler();
            public delegate void IntHandler(int value);
            public delegate void CapacityHandler(int softCapacity, int hardCapacity);
            public delegate bool TickHandler(int value);
            public delegate void GetInfoHandler(ref MemoryPoolInfo info);

            public readonly int PoolId;
            public readonly AcquireHandler Acquire;
            public readonly ReleaseHandler Release;
            public readonly ClearHandler Clear;
            public readonly ClearHandler ClearNativeMetadata;
            public readonly IntHandler Add;
            public readonly CapacityHandler SetCapacity;
            public readonly GetInfoHandler GetInfo;
            public readonly TickHandler Tick;
            public readonly IntHandler Shrink;
            public readonly ClearHandler Compact;
            public readonly ClearHandler TrimNativeMetadata;
            public readonly ClearHandler ResetStats;
            public int ActiveIndex = -1;

            public MemoryPoolHandle(
                AcquireHandler acquire,
                ReleaseHandler release,
                ClearHandler clear,
                ClearHandler clearNativeMetadata,
                IntHandler add,
                CapacityHandler setCapacity,
                GetInfoHandler getInfo,
                TickHandler tick,
                IntHandler shrink,
                ClearHandler compact,
                ClearHandler trimNativeMetadata,
                ClearHandler resetStats)
            {
                PoolId = ++s_NextPoolId;
                Acquire = acquire;
                Release = release;
                Clear = clear;
                ClearNativeMetadata = clearNativeMetadata;
                Add = add;
                SetCapacity = setCapacity;
                GetInfo = getInfo;
                Tick = tick;
                Shrink = shrink;
                Compact = compact;
                TrimNativeMetadata = trimNativeMetadata;
                ResetStats = resetStats;
            }
        }

        #endregion

        #region 字段 [FIELDS]

        private static IntPtr[] s_HandleKeys = new IntPtr[64];
        private static MemoryPoolHandle[] s_HandleValues = new MemoryPoolHandle[64];
        private static int s_HandleCount;

        private static MemoryPoolHandle[] s_ActivePools = new MemoryPoolHandle[16];
        private static Action[] s_NativeReleasers = Array.Empty<Action>();
        private static int s_NativeReleaserCount;
        private static int s_ActiveCount;
        private static int s_NextPoolId;
        private static EMemoryPoolPhase s_Phase = EMemoryPoolPhase.Gameplay;
        private static int s_MainThreadId;
        private static int s_CallbackDepth;
        private static MemoryPoolInfo[] s_StatsBuffer = Array.Empty<MemoryPoolInfo>();

        #endregion

        #region 事件 [EVENTS]

        /// <summary>
        /// 每帧 TickAll 完成后触发的统计快照事件。仅在订阅时填充数据，零订阅时无开销。
        /// </summary>
        public static event Action<MemoryPoolInfo[]> OnPoolStatsUpdated;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取内存池数量。
        /// </summary>
        public static int Count => s_HandleCount;

        /// <summary>
        /// 获取当前帧计数。
        /// </summary>
        internal static int CurrentFrame { get; private set; }

        /// <summary>
        /// 获取或设置内存池阶段。
        /// </summary>
        public static EMemoryPoolPhase Phase
        {
            get => s_Phase;
            set
            {
                AssertMainThread();
                s_Phase = value;
            }
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        static MemoryPoolRegistry()
        {
            AppDomain.CurrentDomain.DomainUnload += ReleaseNativeOnDomainUnload;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeMainThreadOnLoad()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private static void ReleaseNativeOnDomainUnload(object sender, EventArgs e)
        {
            ForceReleaseAllNativeMetadata();
        }

        #endregion

        #region Native 资源管理 [NATIVE RESOURCE MANAGEMENT]

        internal static void RegisterNativeReleaser(Action releaser)
        {
            if (s_NativeReleaserCount == s_NativeReleasers.Length)
            {
                int newLength = s_NativeReleasers.Length == 0 ? 16 : s_NativeReleasers.Length << 1;
                Action[] releasers = new Action[newLength];
                Array.Copy(s_NativeReleasers, 0, releasers, 0, s_NativeReleaserCount);
                s_NativeReleasers = releasers;
            }

            s_NativeReleasers[s_NativeReleaserCount++] = releaser;
        }

        private static void ForceReleaseAllNativeMetadata()
        {
            for (int i = 0; i < s_NativeReleaserCount; i++)
            {
                s_NativeReleasers[i]?.Invoke();
            }

            ClearActiveScheduleState();
        }

        #endregion

        #region 主线程断言 [MAIN THREAD ASSERT]

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (s_MainThreadId == 0)
            {
                s_MainThreadId = currentThreadId;
            }

            if (s_MainThreadId != currentThreadId)
            {
                throw new InvalidOperationException("MemoryPool must be used from the Unity main thread.");
            }
        }

        #endregion

        #region 阶段预算 [PHASE BUDGETS]

        internal static int GetGrowthBudget()
        {
            switch (s_Phase)
            {
                case EMemoryPoolPhase.Boot:
                case EMemoryPoolPhase.Loading:
                    return 32;
                case EMemoryPoolPhase.Background:
                    return 8;
                case EMemoryPoolPhase.LowMemory:
                    return 0;
                default:
                    return 2;
            }
        }

        internal static int GetEvictBudget()
        {
            switch (s_Phase)
            {
                case EMemoryPoolPhase.LowMemory:
                    return 32;
                case EMemoryPoolPhase.Background:
                    return 16;
                case EMemoryPoolPhase.Boot:
                case EMemoryPoolPhase.Loading:
                    return 4;
                default:
                    return 2;
            }
        }

        #endregion

        #region 注册与查找 [REGISTRATION & LOOKUP]

        internal static void Register(Type type, MemoryPoolHandle handle)
        {
            AddOrUpdateHandle(type.TypeHandle.Value, handle);
            ReserveActiveCapacity(s_HandleCount);
        }

        internal static void ScheduleTick(MemoryPoolHandle handle)
        {
            if (handle.ActiveIndex >= 0)
            {
                return;
            }

            handle.ActiveIndex = s_ActiveCount;
            s_ActivePools[s_ActiveCount++] = handle;
        }

        internal static void UnscheduleTick(MemoryPoolHandle handle)
        {
            int index = handle.ActiveIndex;
            if (index < 0)
            {
                return;
            }

            int lastIndex = --s_ActiveCount;
            MemoryPoolHandle last = s_ActivePools[lastIndex];
            s_ActivePools[lastIndex] = null;
            handle.ActiveIndex = -1;

            if (index != lastIndex)
            {
                s_ActivePools[index] = last;
                last.ActiveIndex = index;
            }
        }

        #endregion

        #region 公共 API [PUBLIC API]

        /// <summary>
        /// 获取动态内存类型的缓存句柄。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        /// <returns>缓存句柄。</returns>
        public static Moirai.Atropos.MemoryPoolHandle GetHandle(Type type)
        {
            AssertMainThread();
            return new Moirai.Atropos.MemoryPoolHandle(GetOrCreateHandle(type));
        }

        /// <summary>
        /// 从内存池获取内存对象。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        /// <returns>内存对象。</returns>
        public static MemoryObject Acquire(Type type)
        {
            AssertMainThread();
            return GetOrCreateHandle(type).Acquire();
        }

        /// <summary>
        /// 将内存对象归还内存池。
        /// </summary>
        /// <param name="memory">内存对象。</param>
        public static void Release(MemoryObject memory)
        {
            AssertMainThread();
            if (memory == null)
            {
                return;
            }

            MemoryPoolHandle handle = memory.OwnerHandle.Inner;
            if (handle == null)
            {
                throw new InvalidOperationException("Memory object has no owner pool.");
            }

            handle.Release(memory);
        }

        /// <summary>
        /// 获取所有内存池信息到指定缓冲区。
        /// </summary>
        /// <param name="infos">信息缓冲区。</param>
        /// <returns>内存池数量。</returns>
        public static int GetAllInfos(MemoryPoolInfo[] infos)
        {
            AssertMainThread();
            if (infos == null)
            {
                throw new ArgumentNullException(nameof(infos));
            }

            int count = s_HandleCount;
            if (infos.Length < count)
            {
                throw new ArgumentException("Target buffer is too small.", nameof(infos));
            }

            int i = 0;
            for (int slot = 0; slot < s_HandleValues.Length; slot++)
            {
                MemoryPoolHandle handle = s_HandleValues[slot];
                if (handle == null)
                {
                    continue;
                }

                handle.GetInfo(ref infos[i]);
                i++;
            }

            return count;
        }

        /// <summary>
        /// 清除所有内存池。
        /// </summary>
        public static void ClearAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
            {
                CollectException(ref exceptions, handles[i]?.Clear);
            }

            Rethrow(exceptions);
        }

        /// <summary>
        /// 压缩所有内存池。
        /// </summary>
        public static void CompactAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
            {
                CollectException(ref exceptions, handles[i]?.Compact);
            }

            Rethrow(exceptions);
        }

        /// <summary>
        /// 修剪所有内存池的 Native 元数据。
        /// </summary>
        public static void TrimAllNativeMetadata()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
            {
                CollectException(ref exceptions, handles[i]?.TrimNativeMetadata);
            }

            Rethrow(exceptions);
        }

        /// <summary>
        /// 重置所有内存池统计信息。
        /// </summary>
        public static void ResetAllStats()
        {
            AssertMainThread();
            ThrowIfInCallback();
            for (int i = 0; i < s_HandleValues.Length; i++)
            {
                s_HandleValues[i]?.ResetStats();
            }
        }

        /// <summary>
        /// 清除所有 Native 元数据。
        /// </summary>
        public static void ClearAllNativeMetadata()
        {
            AssertMainThread();
            ThrowIfInCallback();
            List<Exception> exceptions = null;
            MemoryPoolHandle[] handles = s_HandleValues;
            for (int i = 0; i < handles.Length; i++)
            {
                CollectException(ref exceptions, handles[i]?.ClearNativeMetadata);
            }

            Rethrow(exceptions);
        }

        /// <summary>
        /// 向指定类型内存池追加对象。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        /// <param name="count">追加数量。</param>
        public static void Add(Type type, int count)
        {
            AssertMainThread();
            GetOrCreateHandle(type).Add(count);
        }

        /// <summary>
        /// 设置指定类型内存池容量。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        /// <param name="softCapacity">软容量上限。</param>
        /// <param name="hardCapacity">硬容量上限。</param>
        public static void SetCapacity(Type type, int softCapacity, int hardCapacity)
        {
            AssertMainThread();
            GetOrCreateHandle(type).SetCapacity(softCapacity, hardCapacity);
        }

        /// <summary>
        /// 设置所有内存池容量。
        /// </summary>
        /// <param name="softCapacity">软容量上限。</param>
        /// <param name="hardCapacity">硬容量上限。</param>
        public static void SetCapacityAll(int softCapacity, int hardCapacity)
        {
            AssertMainThread();
            ThrowIfInCallback();
            for (int i = 0; i < s_HandleValues.Length; i++)
            {
                s_HandleValues[i]?.SetCapacity(softCapacity, hardCapacity);
            }
        }

        /// <summary>
        /// 清除指定类型内存池。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        public static void ClearType(Type type)
        {
            AssertMainThread();
            GetOrCreateHandle(type).Clear();
        }

        /// <summary>
        /// 压缩指定类型内存池。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        public static void CompactType(Type type)
        {
            AssertMainThread();
            GetOrCreateHandle(type).Compact();
        }

        /// <summary>
        /// 修剪指定类型内存池的 Native 元数据。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        public static void TrimNativeMetadata(Type type)
        {
            AssertMainThread();
            GetOrCreateHandle(type).TrimNativeMetadata();
        }

        /// <summary>
        /// 从指定类型内存池移除对象。
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        /// <param name="count">移除数量。</param>
        public static void RemoveFromType(Type type, int count)
        {
            AssertMainThread();
            if (count <= 0)
            {
                return;
            }

            MemoryPoolHandle handle = GetOrCreateHandle(type);
            MemoryPoolInfo info = default;
            handle.GetInfo(ref info);
            handle.Shrink(info.UnusedCount - count);
        }

        /// <summary>
        /// 每帧驱动所有活跃内存池的 Tick。
        /// </summary>
        /// <param name="frameCount">当前帧计数。</param>
        public static void TickAll(int frameCount)
        {
            AssertMainThread();
            ThrowIfInCallback();
            CurrentFrame = frameCount;
            List<Exception> exceptions = null;
            int i = 0;
            while (i < s_ActiveCount)
            {
                MemoryPoolHandle handle = s_ActivePools[i];
                try
                {
                    if (!handle.Tick(frameCount))
                    {
                        UnscheduleTick(handle);
                    }
                }
                catch (Exception exception)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(exception);
                }

                // Tick 内的淘汰回调可以把本池就地摘出活跃数组（OnEvict → 注销/停止调度），
                // 数组此时已左移：槽位被后面的池顶上，必须原地重跑该槽位而不是前进。
                if (i < s_ActiveCount && ReferenceEquals(s_ActivePools[i], handle))
                {
                    i++;
                }
            }

            FirePoolStatsUpdated();
            Rethrow(exceptions);
        }

        /// <summary>
        /// 触发内存池统计快照事件，向已订阅的监听器广播所有内存池的当前运行状态。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 该方法在 <see cref="TickAll"/> 每帧驱动流程的末尾被调用，用于将内部各内存池的
        /// 运行指标（如使用中数量、未命中率、空闲缓存水位等）以快照形式同步给外部系统。
        /// </para>
        /// <para>
        /// <b>性能与 GC 行为：</b><br />
        /// 1. 若没有任何外部订阅者（<see cref="OnPoolStatsUpdated"/> 为 null），该方法
        ///    会立即返回，仅执行一次指针判空检查（开销小于 1ns），对主线程几乎无影响。<br />
        /// 2. 当存在订阅者时，内部使用静态缓存数组 <see cref="s_StatsBuffer"/> 进行复用。
        ///    仅在内存池数量（<see cref="s_HandleCount"/>）增长导致缓存容量不足时，
        ///    才会触发 <c>new MemoryPoolInfo[count]</c> 进行扩容。<br />
        /// 3. 在游戏运行稳定期（池类型不再动态增加），该方法可实现 <b>零 GC 分配</b>
        ///    （Zero Garbage Collection Allocation），确保不会因遥测数据的采集而引发性能毛刺。
        /// </para>
        /// <para>
        /// <b>线程安全与约束：</b><br />
        /// 本方法必须在 Unity 主线程中执行（由 <see cref="MemoryPoolRegistry.AssertMainThread"/>
        /// 隐式保证）。订阅者回调中严禁执行耗时操作（如同步 I/O 或大量堆栈日志输出），
        /// 亦不应在回调内尝试对内存池进行获取（Acquire）或归还（Release），以免引发不可预知的
        /// 重入问题。
        /// </para>
        /// </remarks>
        /// <seealso cref="OnPoolStatsUpdated"/>
        /// <seealso cref="TickAll"/>
        private static void FirePoolStatsUpdated()
        {
            if (OnPoolStatsUpdated == null) return;

            int count = s_HandleCount;
            if (s_StatsBuffer.Length < count)
            {
                s_StatsBuffer = new MemoryPoolInfo[count];
            }

            int i = 0;
            for (int slot = 0; slot < s_HandleValues.Length; slot++)
            {
                MemoryPoolHandle handle = s_HandleValues[slot];
                if (handle == null)
                {
                    continue;
                }

                handle.GetInfo(ref s_StatsBuffer[i]);
                i++;
            }

            OnPoolStatsUpdated.Invoke(s_StatsBuffer);
        }

        #endregion

        #region 内部方法 [INTERNAL METHODS]

        private static void ReserveActiveCapacity(int required)
        {
            if (s_ActivePools.Length >= required)
            {
                return;
            }

            int newLength = s_ActivePools.Length == 0 ? 16 : s_ActivePools.Length;
            while (newLength < required)
            {
                newLength <<= 1;
            }

            MemoryPoolHandle[] activePools = new MemoryPoolHandle[newLength];
            Array.Copy(s_ActivePools, 0, activePools, 0, s_ActiveCount);
            s_ActivePools = activePools;
        }

        private static bool TryGetHandle(IntPtr key, out MemoryPoolHandle handle)
        {
            int index = FindHandleSlot(key, out bool found);
            if (found)
            {
                handle = s_HandleValues[index];
                return true;
            }

            handle = null;
            return false;
        }

        private static void AddOrUpdateHandle(IntPtr key, MemoryPoolHandle handle)
        {
            if ((s_HandleCount + 1) * 4 >= s_HandleKeys.Length * 3)
            {
                GrowHandleCache();
            }

            int index = FindHandleSlot(key, out bool found);
            if (!found)
            {
                s_HandleCount++;
            }

            s_HandleKeys[index] = key;
            s_HandleValues[index] = handle;
        }

        private static int FindHandleSlot(IntPtr key, out bool found)
        {
            int mask = s_HandleKeys.Length - 1;
            int index = Mix((ulong)key.ToInt64()) & mask;
            while (true)
            {
                IntPtr existing = s_HandleKeys[index];
                if (existing == IntPtr.Zero)
                {
                    found = false;
                    return index;
                }

                if (existing == key)
                {
                    found = true;
                    return index;
                }

                index = (index + 1) & mask;
            }
        }

        private static void GrowHandleCache()
        {
            IntPtr[] oldKeys = s_HandleKeys;
            MemoryPoolHandle[] oldValues = s_HandleValues;
            s_HandleKeys = new IntPtr[oldKeys.Length << 1];
            s_HandleValues = new MemoryPoolHandle[oldValues.Length << 1];
            int oldCount = s_HandleCount;
            s_HandleCount = 0;
            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != IntPtr.Zero)
                {
                    AddOrUpdateHandle(oldKeys[i], oldValues[i]);
                }
            }

            s_HandleCount = oldCount;
        }

        private static int Mix(ulong value)
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            value *= 0xc4ceb9fe1a85ec53UL;
            value ^= value >> 33;
            return (int)value;
        }

        private static MemoryPoolHandle GetOrCreateHandle(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            RuntimeTypeHandle typeHandle = type.TypeHandle;
            if (TryGetHandle(typeHandle.Value, out MemoryPoolHandle handle))
            {
                return handle;
            }

            ValidateMemoryObjectType(type);

#if ENABLE_IL2CPP
            // IL2CPP：MakeGenericType 对未 AOT 预编译的闭泛型会失败——引导至编译期安全路径。
            throw new InvalidOperationException(
                $"MemoryPool: Type '{type.FullName}' could not be materialized under IL2CPP via the dynamic Type path. " +
                $"Call MemoryPool<{type.Name}>.EnsureRegistered() during startup, or use the generic API MemoryPool<T>.Acquire().");
#else
            RuntimeHelpers.RunClassConstructor(
                typeof(MemoryPool<>).MakeGenericType(type).TypeHandle);

            if (TryGetHandle(typeHandle.Value, out handle))
            {
                return handle;
            }

            throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' could not be materialized.");
#endif
        }

        private static void ValidateMemoryObjectType(Type type)
        {
            if (!type.IsClass)
            {
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must be a class.");
            }

            if (type.IsAbstract)
            {
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must not be abstract.");
            }

            if (type.ContainsGenericParameters)
            {
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must not be an open generic type.");
            }

            if (!typeof(MemoryObject).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must inherit MemoryObject.");
            }

            if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) == null)
            {
                throw new InvalidOperationException($"MemoryPool: Type '{type.FullName}' must have a public parameterless constructor.");
            }
        }

        private static void ClearActiveScheduleState()
        {
            for (int i = 0; i < s_ActiveCount; i++)
            {
                s_ActivePools[i].ActiveIndex = -1;
            }

            Array.Clear(s_ActivePools, 0, s_ActiveCount);
            s_ActiveCount = 0;
        }

        internal static void BeginCallback()
        {
            s_CallbackDepth++;
        }

        internal static void EndCallback()
        {
            s_CallbackDepth--;
        }

        /// <summary>
        /// 池回调（构造 / Clear / OnEvict）期间禁止全局维护入口重入：
        /// 这类调用会跨过当前持有页元数据引用的池，把底下的非托管数组换掉。
        /// </summary>
        private static void ThrowIfInCallback()
        {
            if (s_CallbackDepth != 0)
            {
                throw new InvalidOperationException("Global memory pool maintenance is not allowed during a pool callback.");
            }
        }

        private static void CollectException(ref List<Exception> exceptions, MemoryPoolHandle.ClearHandler action)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(exception);
            }
        }

        private static void Rethrow(List<Exception> exceptions)
        {
            if (exceptions == null)
            {
                return;
            }

            Exception exception = exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        #endregion
    }
}
