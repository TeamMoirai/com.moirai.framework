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
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 内存池故障分级门控，与 <c>EventDispatchPolicy.RETHROW_DISPATCH_EXCEPTIONS</c>、
        /// <c>ServiceScope.RETHROW_TICK_EXCEPTIONS</c> 同一约定：开发期原样上抛第一时间暴露缺陷，
        /// 发布期只在边界合并上报让游戏继续跑。<c>const</c> 门控让死分支被裁掉，发布版零运行时成本。
        /// <para>改这个判据要连同上面两处一起改，房内约定不一致比统一用错更糟。</para>
        /// </summary>
        internal const bool RETHROW_POOL_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>
        /// 单轮批量维护最多收集多少条回调异常。整批对象的 <c>OnEvict()</c> 都在抛时，
        /// 无上限收集本身就是在"内存紧张、正在修剪"的那一刻攒出一次 GC 毛刺，因此超出部分只留一条汇总。
        /// </summary>
        internal const int MaxCollectedCallbackExceptions = 16;

        #endregion

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
            public delegate string ValidateHandler();

            public readonly int PoolId;
            public readonly AcquireHandler Acquire;
            public readonly ReleaseHandler Release;
            public readonly ClearHandler Clear;
            public readonly ClearHandler ClearNativeMetadata;
            public readonly IntHandler Add;
            public readonly CapacityHandler SetCapacity;
            public readonly IntHandler SetLiveLimit;
            public readonly GetInfoHandler GetInfo;
            public readonly ValidateHandler Validate;
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
                IntHandler setLiveLimit,
                GetInfoHandler getInfo,
                ValidateHandler validate,
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
                SetLiveLimit = setLiveLimit;
                GetInfo = getInfo;
                Validate = validate;
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

        /// <summary>
        /// 域卸载 / 脚本重载前的收口：确认没有任何对象在外后释放全部非托管页元数据。
        /// <para>页元数据走 <c>AllocHGlobal</c>，那是进程堆——静态字段只活在当前域里。Unity 编辑器脚本重载
        /// 并不走 <see cref="AppDomain.DomainUnload"/>，静态字段一复位那些指针就永久失联，每热重载一次就漏一份。
        /// 页存储与对象数组本体在静态构造前就已退役（不跨域存活），所以漏的只有元数据。</para>
        /// </summary>
        /// <returns>确有对象在外时返回 <see langword="false"/> 并且什么都不释放——
        /// 此时回收元数据等于让下一次归还往已释放内存里写；交给空闲修剪与下一次显式清账即可。</returns>
        internal static bool TryReleaseAllNativeMetadataForTeardown()
        {
            MemoryPoolHandle[] handles = s_HandleValues;
            MemoryPoolInfo info = default;
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] == null)
                {
                    continue;
                }

                info = default;
                handles[i].GetInfo(ref info);
                if (info.UsingCount != 0)
                {
                    return false;
                }
            }

            ForceReleaseAllNativeMetadata();
            return true;
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

        /// <summary>
        /// 当前是否执行主线程校验。
        /// <para>编辑器与开发构建恒开。正式构建默认关——但调用点不再被 <c>[Conditional]</c> 整条裁掉，
        /// 因为 QA / soak 构建需要能在跑起来之后打开它：跨线程取还不会当场报错，而是把非托管页元数据
        /// 与侵入式链表改坏，几周后以随机崩溃或数据错乱的形式回来，那时已经查不到是谁在别的线程动的手。</para>
        /// <para>关着时的成本是每个取还动作读一个静态布尔并分支一次。</para>
        /// </summary>
        private static bool s_ThreadGuardActive = true;

        /// <summary>
        /// 按编译期分级与 <see cref="MemoryPool.VerifyMainThreadInRelease"/> 刷新线程守卫，
        /// 由 <c>MemoryPoolSetting</c> 在初始化时调用。
        /// </summary>
        internal static void RefreshThreadGuard()
        {
            s_ThreadGuardActive = RETHROW_POOL_EXCEPTIONS || MemoryPool.VerifyMainThreadInRelease;
        }

        internal static void AssertMainThread()
        {
            if (!s_ThreadGuardActive)
            {
                return;
            }

            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (s_MainThreadId == 0)
            {
                // 只在没有任何运行期初始化介入时兜底认领（EditMode 测试、纯编辑器工具路径）。
                // 正式构建里 MemoryPoolSetting 在 SubsystemRegistration 就写死了主线程 id，走不到这里，
                // 否则某个后台线程抢先访问池就会把自己认成主线程，之后真正的 main thread 反而全被拒。
                s_MainThreadId = currentThreadId;
                return;
            }

            if (s_MainThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    $"MemoryPool must be used from the Unity main thread (owner thread {s_MainThreadId}, current thread {currentThreadId}).");
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
        /// 设置指定类型内存池的存活（在外）对象数量上限，0 表示不限制。
        /// <para>硬容量只约束空闲缓存、不约束总量，所以这个上限是给"业务漏还"装的可发现边界：
        /// 越界时带池身份上报，开发期直接抛出。</para>
        /// </summary>
        /// <param name="type">内存对象类型。</param>
        /// <param name="limit">存活上限，负数按 0（不限制）处理。</param>
        public static void SetLiveLimit(Type type, int limit)
        {
            AssertMainThread();
            GetOrCreateHandle(type).SetLiveLimit(limit);
        }

        /// <summary>
        /// 逐个池做结构自检（走查页链表并与计数交叉核对），把所有失配汇总成一条可读描述。
        /// <para>只读不改，且会分配字符串：给开发 / QA 构建在关键节点（关卡结束、场景卸载、加载完成）
        /// 或自动化冒烟流程里调用，不要放进每帧。自检过程中任何意外都会被收进报告，本身不外抛。</para>
        /// </summary>
        /// <returns>一切自洽返回 <see langword="null"/>；否则返回带池身份的问题清单。</returns>
        public static string ValidateAll()
        {
            AssertMainThread();
            ThrowIfInCallback();
            MemoryPoolHandle[] handles = s_HandleValues;
            string report = null;
            for (int i = 0; i < handles.Length; i++)
            {
                MemoryPoolHandle handle = handles[i];
                if (handle == null)
                {
                    continue;
                }

                string error;
                try
                {
                    error = handle.Validate();
                }
                catch (Exception exception)
                {
                    error = $"自检自身抛出 {exception.GetType().Name}: {exception.Message}";
                }

                if (string.IsNullOrEmpty(error))
                {
                    continue;
                }

                if (report != null)
                {
                    report += "\n";
                }

                report += $"{ReadPoolType(handle)}: {error}";
            }

            return report;
        }

        private static string ReadPoolType(MemoryPoolHandle handle)
        {
            try
            {
                MemoryPoolInfo info = default;
                handle.GetInfo(ref info);
                return info.Type == null ? "<unknown>" : info.Type.FullName;
            }
            catch (Exception exception)
            {
                return $"<读取身份失败 {exception.GetType().Name}>";
            }
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
                    AddCollected(ref exceptions, exception);
                }

                // Tick 内的淘汰回调可以把本池就地摘出活跃数组（OnEvict → 注销/停止调度），
                // 数组此时已左移：槽位被后面的池顶上，必须原地重跑该槽位而不是前进。
                if (i < s_ActiveCount && ReferenceEquals(s_ActivePools[i], handle))
                {
                    i++;
                }
            }

            FirePoolStatsUpdated();
            ReportMaintenanceFault(exceptions);
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

        /// <summary>
        /// 收集一条批量维护期间产生的异常，超出 <see cref="MaxCollectedCallbackExceptions"/> 后只留一条汇总。
        /// 池自己的批量路径（页退役、整批修剪）也走这里，保证"上限"只有一处定义。
        /// </summary>
        internal static void AddCollected(ref List<Exception> exceptions, Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            exceptions ??= new List<Exception>();
            if (exceptions.Count < MaxCollectedCallbackExceptions)
            {
                exceptions.Add(exception);
                return;
            }

            if (exceptions.Count == MaxCollectedCallbackExceptions)
            {
                // 只占一格说明"后面还有"，不再为省略的条数逐个分配包装对象。
                exceptions.Add(new AggregateException(
                    $"回调失败过多，单轮最多列出 {MaxCollectedCallbackExceptions} 条，其余已省略；本格是第一条被省略的原始异常。",
                    exception));
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
                AddCollected(ref exceptions, exception);
            }
        }

        private static Exception CreateException(List<Exception> exceptions)
        {
            if (exceptions == null)
            {
                return null;
            }

            return exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions);
        }

        private static void Rethrow(List<Exception> exceptions)
        {
            Exception exception = CreateException(exceptions);
            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        /// <summary>
        /// 每帧维护边界的故障收口：合并成一条带失败数量的 Fatal，然后按分级决定是否上抛。
        /// <para>这里与 <see cref="ClearAll"/> 之类的显式调用不同——TickAll 由 <c>GameApp</c> 的更新派发驱动，
        /// 没有业务能接住它，发布版外溢只会每帧刷一条栈；而一个池的坏回调已经在池内逐项隔离过了，
        /// 能逃到这里的都是框架级缺陷，必须留下带身份的记录。</para>
        /// </summary>
        private static void ReportMaintenanceFault(List<Exception> exceptions)
        {
            if (exceptions == null)
            {
                return;
            }

            int collected = exceptions.Count;
            bool capped = collected > MaxCollectedCallbackExceptions;
            Exception fault = CreateException(exceptions);
            LogUtility.Fatal(
                capped
                    ? $"[MemoryPool] Maintenance faulted during TickAll: {MaxCollectedCallbackExceptions}+ 个池失败（最多列出 {MaxCollectedCallbackExceptions} 条，其余省略）。"
                    : $"[MemoryPool] Maintenance faulted during TickAll: {collected} 个池失败。");
            if (RETHROW_POOL_EXCEPTIONS)
            {
                ExceptionDispatchInfo.Capture(fault).Throw();
            }
        }

        #endregion
    }
}
