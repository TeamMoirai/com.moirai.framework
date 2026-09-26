using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 泛型页式内存池，使用非托管元数据实现零 GC 压力。
    /// </summary>
    /// <typeparam name="T">内存对象类型。</typeparam>
    public static unsafe class MemoryPool<T> where T : MemoryObject, new()
    {
        #region 常量 [CONSTANTS]

        private const int PageShift = 5;
        private const int PageSize = 1 << PageShift;
        private const int PageMask = PageSize - 1;
        private const int InvalidIndex = -1;
        private const int MinKeep = MemoryPool.MINIMUM_FREE_RESERVE_LIMIT;
        private const int LookaheadFrames = 8;
        private const int MissBoost = 2;
        private const float RateEwmaAlpha = 0.25f;

        private const byte ObjectStateNone = 0;
        private const byte ObjectStateFree = 1;
        private const byte ObjectStateLeased = 2;
        private const byte ObjectStateEvicting = 4;

        private const byte SlotStateEmpty = 0;
        private const byte SlotStateFree = 1;
        private const byte SlotStateLeased = 2;
        private const byte SlotStateEvicting = 4;

        private const int PageFlagInFreeList = 1 << 0;
        private const int PageFlagInEmptyList = 1 << 1;
        private const int PageFlagTombstone = 1 << 2;

        /// <summary>
        /// 存活上限告警的限流间隔（帧）。越界往往一炸就是整段演出，逐次打日志会把 Console 与上报通道刷爆。
        /// </summary>
        private const int LiveLimitWarnIntervalFrames = 300;

        #endregion

        #region 结构体 [STRUCTS]

        /// <summary>
        /// 页在空闲/空槽双向链表中的前后指针。链表以页索引为节点，元数据与非托管页头同段存放。
        /// </summary>
        private struct PageLink
        {
            public int Previous;
            public int Next;
        }

        private struct PageHeader
        {
            public int FreeCount;
            public int LeasedCount;
            public int EmptyCount;
            public int FreeHead;
            public int EmptyHead;
            public int NextUninitializedSlot;
            public int PageGeneration;
            public PageLink FreeLink;
            public PageLink EmptyLink;
            public int Flags;
        }

        private struct SlotMeta
        {
            public int PageGeneration;
            public int SlotGeneration;
            public int Next;
            public byte State;
        }

        #endregion

        #region 静态字段 [STATIC FIELDS]

        private static readonly MemoryPoolRegistry.MemoryPoolHandle s_Handle;
        private static readonly MemoryPoolHandle s_PublicHandle;
        private static readonly int s_PoolId;

        private static PageHeader* s_PageHeaders;
        private static SlotMeta* s_SlotMetas;
        private static T[][] s_ObjectPages = Array.Empty<T[]>();
        private static int s_PageCount;
        private static int s_PageCapacity;

        private static int s_FreePageHead = InvalidIndex;
        private static int s_EmptyPageHead = InvalidIndex;

        private static int* s_ReleasedPageStack;
        private static int s_ReleasedPageCount;

        private static int s_InUse;
        private static int s_MaxInUse;
        internal static int s_FreeCount;
        private static int s_CreatedCount;
        private static int s_MissCount;
        private static int s_PendingGrowth;
        private static int s_LiveLimit = MemoryPool.DefaultLiveLimit;
        private static int s_LiveLimitBreaches;
        private static int s_LastLiveLimitWarnFrame = InvalidIndex;
        private static int s_AcquireCount;
        private static int s_ReleaseCount;
        private static int s_AcquireThisFrame;
        private static int s_ReleaseThisFrame;
        private static float s_AcquireRateEwma;
        private static float s_BurstEwma;
        private static int s_TargetFreeReserve = MinKeep;
        private static int s_SoftFreeReserveLimit = MemoryPool.DefaultSoftFreeReserveLimit;
        private static int s_HardFreeReserveLimit = MemoryPool.DefaultHardFreeReserveLimit;
        private static int s_IdleFrames;
        private static int s_LastTickFrame = InvalidIndex;
        private static bool s_InPoolCallback;
        private static bool s_PendingClearNativeMetadata;

        #endregion

        #region 静态构造 [STATIC CONSTRUCTOR]

        static MemoryPool()
        {
            MemoryPoolRegistry.AssertMainThread();
            s_Handle = new MemoryPoolRegistry.MemoryPoolHandle(
                acquire: AcquireAsMemory,
                release: ReleaseAsMemory,
                clear: ClearAll,
                clearNativeMetadata: ClearAllNativeMetadata,
                add: Add,
                setCapacity: SetCapacity,
                setLiveLimit: SetLiveLimit,
                getInfo: GetInfo,
                validate: ValidateStructure,
                tick: Tick,
                shrink: Shrink,
                compact: Compact,
                trimNativeMetadata: TrimNativeMetadata,
                resetStats: ResetStats);
            s_PublicHandle = new MemoryPoolHandle(s_Handle);
            s_PoolId = s_Handle.PoolId;
            MemoryPoolRegistry.Register(typeof(T), s_Handle);
            MemoryPoolRegistry.RegisterNativeReleaser(ForceReleaseNativeMetadata);
        }

        #endregion

        #region 公共 API [PUBLIC API]

        /// <summary>
        /// 显式物化并注册本类型的内存池（AOT/IL2CPP 安全路径）。
        /// <para>直接引用封闭泛型 <c>MemoryPool&lt;T&gt;</c> 的静态构造——编译期确定，IL2CPP 生成独立元数据；
        /// 替代 <c>MemoryPoolRegistry.GetHandle(Type)</c> 动态路径的 <c>MakeGenericType</c> 反射（对未 AOT 预编译类型会失败）。</para>
        /// <para>IL2CPP 工程中对每种内存对象类型在启动期调用一次（或经泛型路径 <c>Acquire</c> 首次调用时隐式完成）。</para>
        /// </summary>
        public static void EnsureRegistered()
        {
            // 读静态字段即触发静态构造（幂等）。
            _ = s_PoolId;
        }

        /// <summary>
        /// 获取未使用内存对象数量。
        /// </summary>
        public static int UnusedCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_FreeCount;
        }

        /// <summary>
        /// 从内存池获取内存对象。池内无空闲对象时当场构造，并按在用量抬升空闲水位。
        /// </summary>
        /// <returns>内存对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Acquire()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Acquire");
            if (s_LiveLimit > 0 && s_InUse >= s_LiveLimit)
            {
                ReportLiveLimitBreached();
            }

            if (!TryAcquireFree(out T item))
            {
                s_MissCount++;
                item = CreateLeasedObject();
            }

            s_AcquireCount++;
            s_AcquireThisFrame++;
            s_InUse++;
            if (s_InUse > s_MaxInUse)
            {
                s_MaxInUse = s_InUse;
            }

            if (s_InUse > s_TargetFreeReserve)
            {
                s_TargetFreeReserve = Clamp(s_InUse, MinKeep, s_SoftFreeReserveLimit);
            }

            MemoryPoolRegistry.ScheduleTick(s_Handle);
            return item;
        }

        /// <summary>
        /// 设置存活（在外）对象数量上限，0 表示不限制。
        /// <para>池的硬上限只约束空闲缓存，不约束总量——未命中即构造、<c>Acquire</c> 永不失败，
        /// 所以业务漏还一只就永久少一只，表现为缓慢上涨的 OOM 而不是当场报错。本上限是给"漏还"这件事
        /// 装一个可发现的边界：越界时带池身份限流上报，开发期直接抛出。</para>
        /// </summary>
        /// <param name="limit">存活上限，负数按 0（不限制）处理。</param>
        public static void SetLiveLimit(int limit)
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("SetLiveLimit");
            s_LiveLimit = Math.Max(0, limit);
            s_LiveLimitBreaches = 0;
            s_LastLiveLimitWarnFrame = InvalidIndex;
        }

        /// <summary>
        /// 上报存活上限越界。开发期抛出（漏还要第一时间被人看见，而不是等正式包 OOM）；
        /// 发布版限流打 Fatal 后照常发放——在这里拒绝取用会让已经开跑的演出当场断，
        /// 比多几只对象更糟，且拒绝发放也修不了调用方的漏还。
        /// </summary>
        private static void ReportLiveLimitBreached()
        {
            s_LiveLimitBreaches++;
            bool rethrow = MemoryPoolRegistry.RETHROW_POOL_EXCEPTIONS;
            if (!rethrow && MemoryPoolRegistry.CurrentFrame - s_LastLiveLimitWarnFrame < LiveLimitWarnIntervalFrames)
            {
                return;
            }

            s_LastLiveLimitWarnFrame = MemoryPoolRegistry.CurrentFrame;
            string detail = $"[MemoryPool<{typeof(T).Name}>] Live lease limit breached: limit={s_LiveLimit}, using={s_InUse}, breaches={s_LiveLimitBreaches}, created={s_CreatedCount}. 取还没配对，先查持有方有没有归还。";
            LogUtility.Fatal(detail);
            if (rethrow)
            {
                throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: {detail}");
            }
        }

        /// <summary>
        /// 将内存对象归还内存池。
        /// </summary>
        /// <param name="item">内存对象。</param>
        public static void Release(T item)
        {
            MemoryPoolRegistry.AssertMainThread();
            if (item == null)
            {
                return;
            }

            ThrowIfInPoolCallback("Release");
            ValidateForRelease(item, out int pageIndex, out int slotIndex);
            ReleaseLeased(item, pageIndex, slotIndex);
        }

        /// <summary>
        /// 向内存池追加指定数量的内存对象。
        /// </summary>
        /// <param name="count">追加数量。</param>
        public static void Add(int count)
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Add");
            if (count <= 0)
            {
                return;
            }

            MemoryPoolRegistry.ScheduleTick(s_Handle);
            s_TargetFreeReserve = (int)Math.Min((long)s_TargetFreeReserve + count, s_HardFreeReserveLimit);
            s_PendingGrowth = (int)Math.Min((long)s_PendingGrowth + count, s_HardFreeReserveLimit);
            LimitPendingGrowth();

            int budget = MemoryPoolRegistry.GetGrowthBudget();
            ProcessGrowth(Math.Min(count, budget));
        }

        /// <summary>
        /// 收缩内存池到指定保留数量，并撤销尚未落地的增长请求。
        /// </summary>
        /// <param name="keepCount">保留数量。</param>
        public static void Shrink(int keepCount)
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Shrink");
            keepCount = Math.Max(keepCount, 0);
            s_TargetFreeReserve = Math.Min(s_TargetFreeReserve, keepCount);
            s_PendingGrowth = 0;
            int budget = Math.Max(0, s_FreeCount - keepCount);
            ProcessEvict(budget);
        }

        /// <summary>
        /// 压缩内存池到目标空闲水位。
        /// </summary>
        public static void Compact()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Compact");
            ProcessEvict(Math.Max(0, s_FreeCount - s_TargetFreeReserve));
        }

        /// <summary>
        /// 修剪 Native 元数据。
        /// </summary>
        public static void TrimNativeMetadata()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("TrimNativeMetadata");
            if (s_InUse != 0)
            {
                return;
            }

            Exception callbackException = RetirePages();
            ReleaseNativeMetadataNow();
            MemoryPoolRegistry.UnscheduleTick(s_Handle);
            Rethrow(callbackException);
        }

        /// <summary>
        /// 设置内存池容量。
        /// </summary>
        /// <param name="softCapacity">软容量上限。</param>
        /// <param name="hardCapacity">硬容量上限。</param>
        public static void SetCapacity(int softCapacity, int hardCapacity)
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("SetCapacity");
            softCapacity = Math.Max(softCapacity, MinKeep);
            hardCapacity = Math.Max(hardCapacity, softCapacity);
            s_SoftFreeReserveLimit = softCapacity;
            s_HardFreeReserveLimit = hardCapacity;
            s_TargetFreeReserve = Math.Min(s_TargetFreeReserve, s_SoftFreeReserveLimit);
            LimitPendingGrowth();
            MemoryPoolRegistry.ScheduleTick(s_Handle);
        }

        /// <summary>
        /// 清除所有内存对象。仍有对象在外时，Native 元数据的释放推迟到最后一次归还。
        /// </summary>
        public static void ClearAll()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("ClearAll");
            Exception callbackException = RetirePages();
            if (s_InUse == 0)
            {
                ReleaseNativeMetadataNow();
            }
            else
            {
                s_PendingClearNativeMetadata = true;
            }

            Rethrow(callbackException);
        }

        /// <summary>
        /// 清除所有 Native 元数据。语义与 <see cref="ClearAll"/> 一致：退役全部页并按需释放元数据。
        /// </summary>
        public static void ClearAllNativeMetadata()
        {
            ClearAll();
        }

        /// <summary>
        /// 重置统计信息。
        /// </summary>
        public static void ResetStats()
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("ResetStats");
            s_AcquireCount = 0;
            s_ReleaseCount = 0;
            s_CreatedCount = 0;
            s_MissCount = 0;
            // 高水位按"当前在外量"重新起算：清零会让正在漏还的池看起来干净。
            s_MaxInUse = s_InUse;
            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;
            s_AcquireRateEwma = 0f;
            s_BurstEwma = 0f;
        }

        #endregion

        #region 内部 API [INTERNAL API]

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MemoryObject AcquireAsMemory()
        {
            return Acquire();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReleaseAsMemory(MemoryObject memory)
        {
            Release((T)memory);
        }

        internal static void GetInfo(ref MemoryPoolInfo info)
        {
            info.Set(
                typeof(T), s_FreeCount,
                s_InUse,
                s_AcquireCount, s_ReleaseCount,
                s_CreatedCount,
                s_MissCount,
                s_MaxInUse, s_LiveLimit,
                s_TargetFreeReserve, s_HardFreeReserveLimit,
                s_IdleFrames, Math.Max(0, s_PageCount - s_ReleasedPageCount) * PageSize);
        }

        internal static bool Tick(int frameCount)
        {
            MemoryPoolRegistry.AssertMainThread();
            ThrowIfInPoolCallback("Tick");
            if (frameCount == s_LastTickFrame)
            {
                return true;
            }

            s_LastTickFrame = frameCount;
            bool active = s_AcquireThisFrame > 0 || s_ReleaseThisFrame > 0 || s_PendingGrowth > 0;
            s_IdleFrames = active ? 0 : s_IdleFrames + 1;

            UpdateWatermarks();
            LimitPendingGrowth();
            ProcessGrowth(MemoryPoolRegistry.GetGrowthBudget());
            ProcessEvict(MemoryPoolRegistry.GetEvictBudget());

            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;

            if (TryAutoTrimNativeMetadata())
            {
                return false;
            }

            return s_IdleFrames < MemoryPool.UnscheduleIdleFrames || ShouldKeepTickingForAutoTrim() || s_FreeCount > s_TargetFreeReserve || s_PendingGrowth > 0;
        }

        internal static void ForceReleaseNativeMetadata()
        {
            ResetNativeStorage();
            s_PendingClearNativeMetadata = false;
            s_PendingGrowth = 0;
            s_TargetFreeReserve = 0;
            s_IdleFrames = 0;
            s_LastTickFrame = InvalidIndex;
        }

        #endregion

        #region 核心逻辑 [CORE LOGIC]

        /// <summary>
        /// 退役所有页：逐页打墓碑并回收其空闲槽，回调异常全部收集后一次性上抛。
        /// </summary>
        private static Exception RetirePages()
        {
            List<Exception> callbackExceptions = null;
            for (int pageIndex = 0; pageIndex < s_PageCount; pageIndex++)
            {
                CollectException(ref callbackExceptions, TombstonePage(pageIndex));
            }

            ClearPageLists();
            s_AcquireThisFrame = 0;
            s_ReleaseThisFrame = 0;
            s_PendingGrowth = 0;
            s_AcquireRateEwma = 0f;
            s_BurstEwma = 0f;
            s_TargetFreeReserve = 0;
            s_IdleFrames = 0;
            s_LastTickFrame = InvalidIndex;

            MemoryPoolRegistry.UnscheduleTick(s_Handle);
            return CreateException(callbackExceptions);
        }

        private static void ReleaseNativeMetadataNow()
        {
            s_PendingClearNativeMetadata = false;
            ResetNativeStorage();
        }

        private static void ResetNativeStorage()
        {
            ClearPageLists();
            FreeNativeMetadata();
            s_ObjectPages = Array.Empty<T[]>();
            s_PageCount = 0;
            s_ReleasedPageCount = 0;
            s_InUse = 0;
            s_FreeCount = 0;
            s_PageCapacity = 0;
        }

        private static bool TryAutoTrimNativeMetadata()
        {
            if (MemoryPool.AutoTrimNativeMetadataFrames < 0)
            {
                return false;
            }

            if (s_IdleFrames < MemoryPool.AutoTrimNativeMetadataFrames)
            {
                return false;
            }

            if (s_InUse != 0 || s_FreeCount != 0)
            {
                return false;
            }

            if (s_PageCapacity <= 0)
            {
                return false;
            }

            ReleaseNativeMetadataNow();
            return true;
        }

        private static bool ShouldKeepTickingForAutoTrim()
        {
            return MemoryPool.AutoTrimNativeMetadataFrames >= 0
                   && s_IdleFrames < MemoryPool.AutoTrimNativeMetadataFrames
                   && s_InUse == 0
                   && s_PageCapacity > 0;
        }

        private static bool TryAcquireFree(out T item)
        {
            int pageIndex = s_FreePageHead;
            if (pageIndex < 0)
            {
                item = null;
                return false;
            }

            ref PageHeader page = ref s_PageHeaders[pageIndex];
            int slotIndex = page.FreeHead;
            if (slotIndex < 0)
            {
                // FreeCount>0 的头页必然有槽，走到这里说明计数与链表已失配；
                // 先报错，别拿 -1 去索引非托管元数据。
                ThrowInvalidState("Corrupted free page.");
            }

            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            item = s_ObjectPages[pageIndex][slotIndex];
            if (item == null || slot.State != SlotStateFree || slot.PageGeneration != page.PageGeneration)
            {
                ThrowInvalidState("Corrupted free slot.");
            }

            page.FreeHead = slot.Next;
            page.FreeCount--;
            page.LeasedCount++;
            slot.Next = InvalidIndex;
            slot.State = SlotStateLeased;
            item.State = ObjectStateLeased;
            s_ObjectPages[pageIndex][slotIndex] = null;
            s_FreeCount--;
            if (page.FreeCount == 0)
            {
                UnlinkPage(pageIndex, true);
            }

            return true;
        }

        private static T CreateLeasedObject()
        {
            T item = ConstructObject();
            TakeEmptySlot(out int pageIndex, out int slotIndex);
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            s_CreatedCount++;
            page.EmptyCount--;
            page.LeasedCount++;
            InitializeMemoryObject(item, pageIndex, slotIndex, page.PageGeneration, slot.SlotGeneration, ObjectStateLeased);
            slot.PageGeneration = page.PageGeneration;
            slot.State = SlotStateLeased;
            slot.Next = InvalidIndex;
            return item;
        }

        /// <summary>
        /// 构造内存对象。构造函数在回调护栏内执行，避免对象构造期间再向池子取还或触发全局维护。
        /// </summary>
        private static T ConstructObject()
        {
            s_InPoolCallback = true;
            MemoryPoolRegistry.BeginCallback();
            try
            {
                return new T();
            }
            finally
            {
                MemoryPoolRegistry.EndCallback();
                s_InPoolCallback = false;
            }
        }

        private static void ReleaseLeased(T item, int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            bool tombstone = (page.Flags & PageFlagTombstone) != 0;
            bool keepFree = !tombstone && s_FreeCount < s_HardFreeReserveLimit;

            // Clear() 抛出时不改写任何计数与状态，对象保持在外的语义，调用方修好后可再次归还。
            InvokeClear(item);

            s_ReleaseCount++;
            s_ReleaseThisFrame++;
            MemoryPoolRegistry.ScheduleTick(s_Handle);

            if (keepFree)
            {
                item.State = ObjectStateFree;
                slot.State = SlotStateFree;
                slot.Next = page.FreeHead;
                page.FreeHead = slotIndex;
                page.LeasedCount--;
                page.FreeCount++;
                s_InUse--;
                s_FreeCount++;
                s_ObjectPages[pageIndex][slotIndex] = item;
                if (page.FreeCount == 1)
                {
                    LinkPage(pageIndex, true);
                }

                CompletePendingNativeMetadataClear();
                return;
            }

            Exception evictException = EvictLeasedObject(item, ref page, ref slot, pageIndex, slotIndex, !tombstone);
            s_InUse--;
            if (page.LeasedCount == 0 && page.FreeCount == 0)
            {
                ReleasePageStorage(pageIndex);
            }

            CompletePendingNativeMetadataClear();
            Rethrow(evictException);
        }

        private static Exception EvictLeasedObject(T item, ref PageHeader page, ref SlotMeta slot, int pageIndex, int slotIndex, bool enqueueEmpty)
        {
            item.State = ObjectStateEvicting;
            slot.State = SlotStateEvicting;
            Exception callbackException = CaptureCallbackException(item);

            ResetMemoryObject(item);
            slot.SlotGeneration++;
            slot.State = SlotStateEmpty;
            if (enqueueEmpty)
            {
                slot.Next = page.EmptyHead;
                page.EmptyHead = slotIndex;
            }
            else
            {
                slot.Next = InvalidIndex;
            }

            page.LeasedCount--;
            page.EmptyCount++;
            if (enqueueEmpty && page.EmptyCount == 1)
            {
                LinkPage(pageIndex, false);
            }

            return callbackException;
        }

        private static Exception EvictFree(int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            T item = s_ObjectPages[pageIndex][slotIndex];
            if (item == null || slot.State != SlotStateFree)
            {
                ThrowInvalidState("Corrupted evict slot.");
            }

            item.State = ObjectStateEvicting;
            slot.State = SlotStateEvicting;
            Exception callbackException = CaptureCallbackException(item);

            s_ObjectPages[pageIndex][slotIndex] = null;
            ResetMemoryObject(item);
            slot.SlotGeneration++;
            slot.State = SlotStateEmpty;
            slot.Next = page.EmptyHead;
            page.EmptyHead = slotIndex;
            page.FreeCount--;
            page.EmptyCount++;
            s_FreeCount--;
            if (page.EmptyCount == 1)
            {
                LinkPage(pageIndex, false);
            }

            return callbackException;
        }

        #endregion

        #region 增长与驱逐 [GROWTH & EVICTION]

        private static void ProcessGrowth(int budget)
        {
            LimitPendingGrowth();
            while (budget > 0 && s_PendingGrowth > 0)
            {
                T item = ConstructObject();
                TakeEmptySlot(out int pageIndex, out int slotIndex);
                ref PageHeader page = ref s_PageHeaders[pageIndex];
                ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
                s_CreatedCount++;
                page.EmptyCount--;
                page.FreeCount++;
                InitializeMemoryObject(item, pageIndex, slotIndex, page.PageGeneration, slot.SlotGeneration, ObjectStateFree);
                slot.PageGeneration = page.PageGeneration;
                slot.State = SlotStateFree;
                slot.Next = page.FreeHead;
                page.FreeHead = slotIndex;
                s_ObjectPages[pageIndex][slotIndex] = item;
                s_FreeCount++;
                s_PendingGrowth--;
                if (page.FreeCount == 1)
                {
                    LinkPage(pageIndex, true);
                }

                budget--;
            }
        }

        private static void ProcessEvict(int budget)
        {
            LimitPendingGrowth();
            if (s_PendingGrowth > 0)
            {
                return;
            }

            List<Exception> callbackExceptions = null;
            while (budget > 0 && s_FreeCount > s_TargetFreeReserve)
            {
                int pageIndex = s_FreePageHead;
                if (pageIndex < 0)
                {
                    break;
                }

                ref PageHeader page = ref s_PageHeaders[pageIndex];
                int slotIndex = page.FreeHead;
                if (slotIndex < 0)
                {
                    break;
                }

                page.FreeHead = s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)].Next;
                CollectException(ref callbackExceptions, EvictFree(pageIndex, slotIndex));
                if (page.FreeCount == 0)
                {
                    UnlinkPage(pageIndex, true);
                }

                if (page.LeasedCount == 0 && page.FreeCount == 0)
                {
                    ReleasePageStorage(pageIndex);
                }

                budget--;
            }

            Rethrow(CreateException(callbackExceptions));
        }

        #endregion

        #region 页管理 [PAGE MANAGEMENT]

        private static void TakeEmptySlot(out int pageIndex, out int slotIndex)
        {
            pageIndex = s_EmptyPageHead >= 0 ? s_EmptyPageHead : CreatePage();
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if (page.EmptyCount <= 0)
            {
                // 空槽链表头页无空槽时 NextUninitializedSlot 已越界，继续自增会踩到下一页的槽位。
                ThrowInvalidState("Corrupted empty page.");
            }

            if (page.EmptyHead >= 0)
            {
                slotIndex = page.EmptyHead;
                page.EmptyHead = s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)].Next;
            }
            else
            {
                slotIndex = page.NextUninitializedSlot++;
            }

            if (page.EmptyCount == 1)
            {
                UnlinkPage(pageIndex, false);
            }
        }

        private static int CreatePage()
        {
            int pageIndex;
            if (s_ReleasedPageCount > 0)
            {
                pageIndex = s_ReleasedPageStack[--s_ReleasedPageCount];
                s_ReleasedPageStack[s_ReleasedPageCount] = 0;
            }
            else
            {
                GrowPageStorage(s_PageCount + 1);
                pageIndex = s_PageCount++;
            }

            int pageGeneration = s_PageHeaders[pageIndex].PageGeneration;
            if (pageGeneration == 0)
            {
                pageGeneration = 1;
            }

            s_PageHeaders[pageIndex] = new PageHeader
            {
                EmptyCount = PageSize,
                FreeHead = InvalidIndex,
                EmptyHead = InvalidIndex,
                PageGeneration = pageGeneration
            };
            s_ObjectPages[pageIndex] = new T[PageSize];
            int start = pageIndex << PageShift;
            for (int i = 0; i < PageSize; i++)
            {
                s_SlotMetas[start + i].PageGeneration = pageGeneration;
                if (s_SlotMetas[start + i].SlotGeneration == 0)
                {
                    s_SlotMetas[start + i].SlotGeneration = 1;
                }

                s_SlotMetas[start + i].Next = InvalidIndex;
                s_SlotMetas[start + i].State = SlotStateEmpty;
            }

            LinkPage(pageIndex, false);
            return pageIndex;
        }

        private static void GrowPageStorage(int requiredPages)
        {
            if (s_PageCapacity >= requiredPages)
            {
                return;
            }

            int newPageCapacity = s_PageCapacity == 0 ? 4 : s_PageCapacity;
            while (newPageCapacity < requiredPages)
            {
                newPageCapacity <<= 1;
            }

            Array.Resize(ref s_ObjectPages, newPageCapacity);
            ResizeUnmanaged(ref s_PageHeaders, s_PageCapacity, newPageCapacity);
            ResizeUnmanaged(ref s_SlotMetas, s_PageCapacity * PageSize, newPageCapacity * PageSize);
            ResizeUnmanaged(ref s_ReleasedPageStack, s_PageCapacity, newPageCapacity);
            s_PageCapacity = newPageCapacity;
        }

        private static Exception TombstonePage(int pageIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if (s_ObjectPages[pageIndex] == null || (page.Flags & PageFlagTombstone) != 0)
            {
                return null;
            }

            if ((page.Flags & PageFlagInFreeList) != 0)
            {
                UnlinkPage(pageIndex, true);
            }

            if ((page.Flags & PageFlagInEmptyList) != 0)
            {
                UnlinkPage(pageIndex, false);
            }

            page.Flags |= PageFlagTombstone;

            List<Exception> callbackExceptions = null;
            int slotIndex = page.FreeHead;
            while (slotIndex >= 0)
            {
                int next = s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)].Next;
                CollectException(ref callbackExceptions, TombstoneEvictFree(pageIndex, slotIndex));
                slotIndex = next;
            }

            page.FreeHead = InvalidIndex;
            page.EmptyHead = InvalidIndex;
            page.NextUninitializedSlot = PageSize;
            page.FreeCount = 0;
            page.EmptyCount = PageSize - page.LeasedCount;

            if (page.LeasedCount == 0)
            {
                ReleasePageStorage(pageIndex);
            }

            return CreateException(callbackExceptions);
        }

        private static Exception TombstoneEvictFree(int pageIndex, int slotIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            T item = s_ObjectPages[pageIndex][slotIndex];
            item.State = ObjectStateEvicting;
            slot.State = SlotStateEvicting;
            Exception callbackException = CaptureCallbackException(item);
            ResetMemoryObject(item);

            s_ObjectPages[pageIndex][slotIndex] = null;
            slot.SlotGeneration++;
            slot.State = SlotStateEmpty;
            slot.Next = InvalidIndex;
            page.FreeCount--;
            page.EmptyCount++;
            s_FreeCount--;
            return callbackException;
        }

        private static void ReleasePageStorage(int pageIndex)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            if ((page.Flags & PageFlagInEmptyList) != 0)
            {
                UnlinkPage(pageIndex, false);
            }

            page.PageGeneration++;
            page.FreeHead = InvalidIndex;
            page.EmptyHead = InvalidIndex;
            page.NextUninitializedSlot = 0;
            page.FreeCount = 0;
            page.LeasedCount = 0;
            page.EmptyCount = PageSize;
            page.Flags = 0;
            s_ObjectPages[pageIndex] = null;
            if (s_ReleasedPageCount < s_PageCapacity)
            {
                s_ReleasedPageStack[s_ReleasedPageCount++] = pageIndex;
            }
        }

        #endregion

        #region 页链表 [PAGE LISTS]

        private static void ClearPageLists()
        {
            s_FreePageHead = InvalidIndex;
            s_EmptyPageHead = InvalidIndex;
        }

        private static void LinkPage(int pageIndex, bool free)
        {
            ref int head = ref (free ? ref s_FreePageHead : ref s_EmptyPageHead);
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref PageLink link = ref (free ? ref page.FreeLink : ref page.EmptyLink);
            link.Previous = InvalidIndex;
            link.Next = head;
            if (head >= 0)
            {
                ref PageHeader next = ref s_PageHeaders[head];
                ref PageLink nextLink = ref (free ? ref next.FreeLink : ref next.EmptyLink);
                nextLink.Previous = pageIndex;
            }

            head = pageIndex;
            page.Flags |= free ? PageFlagInFreeList : PageFlagInEmptyList;
        }

        private static void UnlinkPage(int pageIndex, bool free)
        {
            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref PageLink link = ref (free ? ref page.FreeLink : ref page.EmptyLink);
            if (link.Previous >= 0)
            {
                ref PageHeader previous = ref s_PageHeaders[link.Previous];
                ref PageLink previousLink = ref (free ? ref previous.FreeLink : ref previous.EmptyLink);
                previousLink.Next = link.Next;
            }
            else
            {
                ref int head = ref (free ? ref s_FreePageHead : ref s_EmptyPageHead);
                head = link.Next;
            }

            if (link.Next >= 0)
            {
                ref PageHeader next = ref s_PageHeaders[link.Next];
                ref PageLink nextLink = ref (free ? ref next.FreeLink : ref next.EmptyLink);
                nextLink.Previous = link.Previous;
            }

            page.Flags &= ~(free ? PageFlagInFreeList : PageFlagInEmptyList);
        }

        #endregion

        #region 水位线 [WATERMARKS]

        /// <summary>
        /// 把尚未落地的增长请求收敛到「水位目标 - 现有空闲量」，预算为 0 时直接作废，避免积压到后续帧。
        /// </summary>
        private static void LimitPendingGrowth()
        {
            if (s_PendingGrowth <= 0)
            {
                return;
            }

            if (MemoryPoolRegistry.GetGrowthBudget() <= 0)
            {
                s_PendingGrowth = 0;
                return;
            }

            int reserveLimit = Math.Min(s_TargetFreeReserve, s_HardFreeReserveLimit);
            int maxDebt = Math.Max(0, reserveLimit - s_FreeCount);
            if (s_PendingGrowth > maxDebt)
            {
                s_PendingGrowth = maxDebt;
            }
        }

        private static void UpdateWatermarks()
        {
            if (MemoryPoolRegistry.Phase == EMemoryPoolPhase.LowMemory || s_IdleFrames >= MemoryPool.ZeroFreeReserveStartFrames)
            {
                s_TargetFreeReserve = 0;
                return;
            }

            int minFreeReserve = MinKeep;
            s_AcquireRateEwma = Lerp(s_AcquireRateEwma, s_AcquireThisFrame, RateEwmaAlpha);
            int frameBurst = Math.Max(0, s_AcquireThisFrame - s_ReleaseThisFrame);
            s_BurstEwma = Lerp(s_BurstEwma, frameBurst, RateEwmaAlpha);

            if (s_IdleFrames >= MemoryPool.ShortDecayStartFrames)
            {
                s_BurstEwma *= 0.9375f;
            }

            if (s_IdleFrames >= MemoryPool.LongDecayStartFrames)
            {
                s_AcquireRateEwma *= 0.984375f;
            }

            int desiredFree = Max(
                CeilToInt(s_BurstEwma),
                CeilToInt(s_AcquireRateEwma * LookaheadFrames),
                s_PendingGrowth * MissBoost,
                minFreeReserve);
            if (s_PendingGrowth > 0 || s_IdleFrames < MemoryPool.ShortDecayStartFrames)
            {
                desiredFree = Math.Max(desiredFree, s_TargetFreeReserve);
            }

            s_TargetFreeReserve = Clamp(desiredFree, minFreeReserve, Math.Min(s_SoftFreeReserveLimit, s_HardFreeReserveLimit));
        }

        #endregion

        #region 验证 [VALIDATION]

        private static void ValidateForRelease(T item, out int pageIndex, out int slotIndex)
        {
            if (!item.OwnerHandle.IsValid)
            {
                ThrowInvalidState("Memory object has no owner pool.");
            }

            if (item.PoolId != s_PoolId)
            {
                ThrowInvalidState("Memory object belongs to another pool.");
            }

            DecodeSlotId(item.SlotId, out pageIndex, out slotIndex);
            if ((uint)pageIndex >= (uint)s_PageCount)
            {
                ThrowInvalidState("Memory object slot is out of range.");
            }

            ref PageHeader page = ref s_PageHeaders[pageIndex];
            ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
            if (item.PageGeneration != page.PageGeneration || slot.PageGeneration != page.PageGeneration)
            {
                ThrowInvalidState("Memory object page generation mismatch.");
            }

            if (item.SlotGeneration != slot.SlotGeneration)
            {
                ThrowInvalidState("Memory object slot generation mismatch.");
            }

            if (item.State != ObjectStateLeased || slot.State != SlotStateLeased)
            {
                ThrowInvalidState("Memory object is not leased.");
            }
        }

        #endregion

        #region 结构自检 [STRUCTURE VALIDATION]

        /// <summary>
        /// 结构自检：走查空闲链与空槽链，与页计数、全局计数、链表指针和标志位交叉核对。
        /// <para>页链表换来的是 O(1) 摘挂，代价是一次漏挂/漏摘就会让后面的索引落到已释放内存上——
        /// 那种失配平时不响，只在某条特定路径上以随机崩溃或数据错乱的形式回来。QA / 开发构建里在关键节点
        /// （关卡结束、场景卸载、加载完成）调一次，就能把"随机崩溃"变成"当场说出哪个页的哪条链断了"。</para>
        /// <para>本方法只读不改，且会为拼错误文案分配字符串——不要放进热路径或每帧调用。</para>
        /// </summary>
        /// <returns>一切自洽返回 <see langword="null"/>；否则返回首个失配的可读描述。</returns>
        public static string ValidateStructure()
        {
            MemoryPoolRegistry.AssertMainThread();
            int freeTotal = 0;
            int leasedTotal = 0;
            int linkedFreePages = 0;
            int expectedFreeListPages = 0;

            for (int pageIndex = 0; pageIndex < s_PageCount; pageIndex++)
            {
                ref PageHeader page = ref s_PageHeaders[pageIndex];
                bool storageReleased = s_ObjectPages[pageIndex] == null;
                bool tombstone = (page.Flags & PageFlagTombstone) != 0;
                bool inFreeList = (page.Flags & PageFlagInFreeList) != 0;
                bool inEmptyList = (page.Flags & PageFlagInEmptyList) != 0;

                if (storageReleased)
                {
                    if (page.FreeCount != 0 || page.LeasedCount != 0)
                    {
                        return $"页 {pageIndex} 存储已释放但计数未清零（Free={page.FreeCount}, Leased={page.LeasedCount}）";
                    }

                    if (inFreeList || inEmptyList)
                    {
                        return $"页 {pageIndex} 存储已释放却仍挂在链表上（Flags={page.Flags}）";
                    }

                    continue;
                }

                if (inFreeList != (page.FreeCount > 0))
                {
                    return $"页 {pageIndex} 空闲标志与计数失配（Flags={page.Flags}, Free={page.FreeCount}）";
                }

                if (inEmptyList && (page.EmptyCount <= 0 || tombstone))
                {
                    return $"页 {pageIndex} 不该在空槽链上却仍挂着（Flags={page.Flags}, Empty={page.EmptyCount}, Tombstone={tombstone}）";
                }

                if (inFreeList)
                {
                    linkedFreePages++;
                }

                if (page.FreeCount > 0)
                {
                    expectedFreeListPages++;
                }

                leasedTotal += page.LeasedCount;
                freeTotal += page.FreeCount;

                string slotsError = ValidateSlotChain(pageIndex, page.FreeHead, SlotStateFree, "空闲");
                if (slotsError != null)
                {
                    return slotsError;
                }

                if (!tombstone)
                {
                    slotsError = ValidateSlotChain(pageIndex, page.EmptyHead, SlotStateEmpty, "空槽");
                    if (slotsError != null)
                    {
                        return slotsError;
                    }
                }
            }

            if (freeTotal != s_FreeCount)
            {
                return $"全局空闲计数失真：页内合计 {freeTotal}，s_FreeCount {s_FreeCount}";
            }

            if (leasedTotal != s_InUse)
            {
                return $"全局在外计数失真：页内合计 {leasedTotal}，s_InUse {s_InUse}";
            }

            if (linkedFreePages != expectedFreeListPages)
            {
                return $"空闲页链表与实际有空闲槽的页数不符：挂链 {linkedFreePages}，应为 {expectedFreeListPages}（漏挂会让空闲对象再也取不到，多挂会索引到无空闲槽的页）";
            }

            string listError = ValidatePageList(s_FreePageHead, true);
            if (listError != null)
            {
                return listError;
            }

            return ValidatePageList(s_EmptyPageHead, false);
        }

        /// <summary>
        /// 走一页内的槽位链：索引必须落在页内、状态必须一致、长度不得超过页大小（超出即说明链上成环）。
        /// </summary>
        private static string ValidateSlotChain(int pageIndex, int slotIndex, byte expectedState, string label)
        {
            int walked = 0;
            while (slotIndex >= 0)
            {
                if ((uint)slotIndex >= PageSize)
                {
                    return $"页 {pageIndex} 的{label}链槽位越界：{slotIndex}";
                }

                if (++walked > PageSize)
                {
                    return $"页 {pageIndex} 的{label}链成环（走查超过 {PageSize} 个槽位）";
                }

                ref SlotMeta slot = ref s_SlotMetas[GetSlotMetaIndex(pageIndex, slotIndex)];
                if (slot.State != expectedState)
                {
                    return $"页 {pageIndex} 槽 {slotIndex} 在{label}链上但状态为 {slot.State}（应为 {expectedState}）";
                }

                if (expectedState == SlotStateFree && s_ObjectPages[pageIndex][slotIndex] == null)
                {
                    return $"页 {pageIndex} 槽 {slotIndex} 在空闲链上却不持有对象";
                }

                slotIndex = slot.Next;
            }

            return null;
        }

        /// <summary>
        /// 走一条页链表：前后指针必须互指，且不能成环。
        /// </summary>
        private static string ValidatePageList(int head, bool free)
        {
            string label = free ? "空闲" : "空槽";
            int walked = 0;
            int previous = InvalidIndex;
            int pageIndex = head;
            while (pageIndex >= 0)
            {
                if ((uint)pageIndex >= (uint)s_PageCount)
                {
                    return $"{label}页链表节点越界：{pageIndex}（页总数 {s_PageCount}）";
                }

                if (++walked > s_PageCount)
                {
                    return $"{label}页链表成环（走查超过 {s_PageCount} 个节点）";
                }

                ref PageHeader page = ref s_PageHeaders[pageIndex];
                ref PageLink link = ref (free ? ref page.FreeLink : ref page.EmptyLink);
                if (link.Previous != previous)
                {
                    return $"{label}页链表在页 {pageIndex} 处前指针失真：记录 {link.Previous}，实际应为 {previous}";
                }

                bool flagged = (page.Flags & (free ? PageFlagInFreeList : PageFlagInEmptyList)) != 0;
                if (!flagged)
                {
                    return $"{label}页链表包含未挂链标记的页 {pageIndex}";
                }

                previous = pageIndex;
                pageIndex = link.Next;
            }

            return null;
        }

        #endregion

        #region 回调 [CALLBACKS]

        private static void InvokeClear(T item)
        {
            s_InPoolCallback = true;
            MemoryPoolRegistry.BeginCallback();
            try
            {
                item.Clear();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: Clear() failed.", exception);
            }
            finally
            {
                MemoryPoolRegistry.EndCallback();
                s_InPoolCallback = false;
            }
        }

        private static void InvokeOnEvict(T item)
        {
            if (!(item is IPoolEvictable evictable))
            {
                return;
            }

            s_InPoolCallback = true;
            MemoryPoolRegistry.BeginCallback();
            try
            {
                evictable.OnEvict();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: OnEvict() failed.", exception);
            }
            finally
            {
                MemoryPoolRegistry.EndCallback();
                s_InPoolCallback = false;
            }
        }

        private static void ThrowIfInPoolCallback(string operation)
        {
            if (s_InPoolCallback)
            {
                ThrowInvalidState($"{operation} is not allowed during construction, Clear() or OnEvict().");
            }
        }

        #endregion

        #region 对象初始化 [OBJECT INITIALIZATION]

        private static void InitializeMemoryObject(T item, int pageIndex, int slotIndex, int pageGeneration, int slotGeneration, byte state)
        {
            item.OwnerHandle = s_PublicHandle;
            item.PoolId = s_PoolId;
            item.SlotId = EncodeSlotId(pageIndex, slotIndex);
            item.PageGeneration = pageGeneration;
            item.SlotGeneration = slotGeneration;
            item.State = state;
        }

        private static void ResetMemoryObject(T item)
        {
            item.OwnerHandle = default;
            item.PoolId = 0;
            item.SlotId = InvalidIndex;
            item.PageGeneration = 0;
            item.SlotGeneration = 0;
            item.State = ObjectStateNone;
        }

        #endregion

        #region Native 内存 [NATIVE MEMORY]

        private static U* AllocUnmanaged<U>(int count) where U : unmanaged
        {
            if (count <= 0)
            {
                return null;
            }

            int size = sizeof(U) * count;
            IntPtr memory = Marshal.AllocHGlobal(size);
            Span<byte> bytes = new Span<byte>((void*)memory, size);
            bytes.Clear();
            return (U*)memory;
        }

        private static void ResizeUnmanaged<U>(ref U* buffer, int oldCount, int newCount) where U : unmanaged
        {
            U* newBuffer = AllocUnmanaged<U>(newCount);
            if (buffer != null && oldCount > 0 && newBuffer != null)
            {
                int copyCount = Math.Min(oldCount, newCount);
                Buffer.MemoryCopy(buffer, newBuffer, sizeof(U) * newCount, sizeof(U) * copyCount);
            }

            FreeUnmanaged(buffer);
            buffer = newBuffer;
        }

        private static void FreeUnmanaged<U>(U* buffer) where U : unmanaged
        {
            if (buffer != null)
            {
                Marshal.FreeHGlobal((IntPtr)buffer);
            }
        }

        private static void FreeNativeMetadata()
        {
            FreeUnmanaged(s_PageHeaders);
            FreeUnmanaged(s_SlotMetas);
            FreeUnmanaged(s_ReleasedPageStack);
            s_PageHeaders = null;
            s_SlotMetas = null;
            s_ReleasedPageStack = null;
        }

        /// <summary>
        /// 对象全部归还后补做延迟的 Native 元数据释放。仅在空闲量已归零时进行，
        /// 否则会带着仍挂在空闲链上的对象释放页数组，留下悬空槽位。
        /// </summary>
        private static void CompletePendingNativeMetadataClear()
        {
            if (!s_PendingClearNativeMetadata || s_InUse != 0)
            {
                return;
            }

            s_PendingClearNativeMetadata = false;
            if (s_FreeCount == 0)
            {
                ReleaseNativeMetadataNow();
                MemoryPoolRegistry.UnscheduleTick(s_Handle);
            }
        }

        #endregion

        #region 辅助方法 [UTILITY METHODS]

        private static Exception CaptureCallbackException(T item)
        {
            try
            {
                InvokeOnEvict(item);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void CollectException(ref List<Exception> exceptions, Exception exception)
        {
            // 采集上限由注册表统一定义：池内批量路径与全局批量路径必须同进同退，
            // 否则"最多攒多少条"会变成两处各自演化的常数。
            MemoryPoolRegistry.AddCollected(ref exceptions, exception);
        }

        private static Exception CreateException(List<Exception> exceptions)
        {
            if (exceptions == null)
            {
                return null;
            }

            return exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions);
        }

        private static void Rethrow(Exception exception)
        {
            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EncodeSlotId(int pageIndex, int slotIndex)
        {
            return (pageIndex << PageShift) | slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecodeSlotId(int slotId, out int pageIndex, out int slotIndex)
        {
            pageIndex = slotId >> PageShift;
            slotIndex = slotId & PageMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSlotMetaIndex(int pageIndex, int slotIndex)
        {
            return (pageIndex << PageShift) + slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int Max(int a, int b, int c, int d)
        {
            return Math.Max(Math.Max(a, b), Math.Max(c, d));
        }

        private static int CeilToInt(float value)
        {
            return (int)Math.Ceiling(value);
        }

        private static float Lerp(float from, float to, float alpha)
        {
            return from + (to - from) * alpha;
        }

        private static void ThrowInvalidState(string message)
        {
            throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: {message}");
        }

        #endregion
    }
}
