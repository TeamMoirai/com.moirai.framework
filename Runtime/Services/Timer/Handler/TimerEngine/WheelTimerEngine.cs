using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using static Moirai.Atropos.Timer.TimerHandlerTypes;
using static Moirai.Atropos.Timer.TimerStates;
using static Moirai.Atropos.Timer.TimerPool;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 时间轮引擎（泳道 <see cref="TimerLaneKinds.Wheel"/>）：四级时间轮，缩放 / 非缩放各一轮。
    /// <para>自持独立的分页槽位池、版本化句柄命名空间（句柄内嵌 Wheel 泳道号）、完成回调派发、进度回调列表
    /// 与 Fixed/Late 阶段的延后触发列表；完全不知晓帧计时的存在，因此不存在任何跨引擎分支。</para>
    /// </summary>
    internal sealed class WheelTimerEngine : ITimerEngine
    {
        private const double TICKS_PER_SECOND = 1000d;
        private const double MINIMUM_DELAY_SECONDS = 0.000001d;
        // tick 换算的饱和上限。(long)double 越界是未定义行为（x64 产出 long.MinValue），
        // 游标一旦落到 MinValue，每帧最多追 64 tick 的时间轮相当于永久冻结，故一律饱和而不是让它溢出。
        private const long MAX_TICK = long.MaxValue / 4;
        private const int WHEEL_SHIFT = 8;
        private const int WHEEL_SIZE = 1 << WHEEL_SHIFT;
        private const int WHEEL_MASK = WHEEL_SIZE - 1;
        private const int WHEEL_LEVEL_COUNT = 4;
        private const int WHEEL_MAX_LEVEL = WHEEL_LEVEL_COUNT - 1;
        private const int WHEEL_BUCKET_COUNT = WHEEL_SIZE * WHEEL_LEVEL_COUNT;
        private const int MAX_WHEEL_TICKS_PER_FRAME = 64;
        // 延后触发列表的槽位归属位（仅本泳道用）：以位判定替代 List.Contains 的线性去重，
        // 并让释放路径上的线性摘除只对真正在列的槽位发生。随快照排空一并清除，故循环计时器可再次入列。
        private const byte STATE_DEFERRED_FIXED = 1 << 6;
        private const byte STATE_DEFERRED_LATE = 1 << 7;
#if UNITY_EDITOR
        private const double STALE_ONE_SHOT_SECONDS = 300d;
#endif

        private sealed class WheelPage
        {
            public readonly ulong[] Handles = new ulong[PAGE_SIZE];
            public readonly uint[] Versions = new uint[PAGE_SIZE];
            public readonly byte[] States = new byte[PAGE_SIZE];
            public readonly byte[] HandlerTypes = new byte[PAGE_SIZE];
            public readonly double[] TriggerTimes = new double[PAGE_SIZE];
            public readonly double[] Durations = new double[PAGE_SIZE];
            public readonly double[] RemainingTimes = new double[PAGE_SIZE];
#if UNITY_EDITOR
            public readonly double[] CreationTimes = new double[PAGE_SIZE];
#endif
            public readonly long[] DueTicks = new long[PAGE_SIZE];
            public readonly int[] QueueIndices = new int[PAGE_SIZE];
            public readonly int[] QueueNextIndices = new int[PAGE_SIZE];
            public readonly int[] QueuePrevIndices = new int[PAGE_SIZE];
            public readonly int[] ActiveIndices = new int[PAGE_SIZE];
            // 进度列表下标（支持 O(1) swap-remove，语义同帧泳道的 FrameListPositions）。
            public readonly int[] ProgressPositions = new int[PAGE_SIZE];
            public readonly Action[] NoArgsHandlers = new Action[PAGE_SIZE];
            public readonly TimerGenericInvoker[] GenericInvokers = new TimerGenericInvoker[PAGE_SIZE];
            public readonly object[] GenericHandlers = new object[PAGE_SIZE];
            public readonly object[] GenericArgs = new object[PAGE_SIZE];
            public readonly ulong[] UnsafeFnPtrs = new ulong[PAGE_SIZE];
            public readonly object[] UnsafeInstances = new object[PAGE_SIZE];
            public readonly Action<float>[] ProgressHandlers = new Action<float>[PAGE_SIZE];
            public readonly byte[] Phases = new byte[PAGE_SIZE];
            // 按槽位挂载的完成信号（仅 await 时惰性创建；释放时唤醒并清空）。
            public readonly UniTaskCompletionSource[] WaitSignals = new UniTaskCompletionSource[PAGE_SIZE];

            public WheelPage()
            {
                for (int i = 0; i < PAGE_SIZE; i++)
                {
                    QueueIndices[i] = INVALID_INDEX;
                    QueueNextIndices[i] = INVALID_INDEX;
                    QueuePrevIndices[i] = INVALID_INDEX;
                    ActiveIndices[i] = INVALID_INDEX;
                    ProgressPositions[i] = INVALID_INDEX;
                }
            }
        }

        private WheelPage[] _pages;
        private IndexPage[] _freeSlotPages;
        private IndexPage[] _activeSlotPages;
        private int[] _scaledWheelHeads;
        private int[] _scaledWheelTails;
        private int[] _unscaledWheelHeads;
        private int[] _unscaledWheelTails;
        private int _pageCount;
        private int _slotCapacity;
        private int _freeCount;
        private int _activeCount;
        private int _peakActiveCount;
        private int _scaledQueueCount;
        private int _unscaledQueueCount;
        private long _scaledCurrentTick;
        private long _unscaledCurrentTick;
        private int _executingSlotIndex;

        // 进度回调列表 + 快照。
        private List<int> _progressSlots;
        private List<ulong> _progressScratch;
        // Fixed/Late 阶段到期的计时延后触发列表（存句柄，规避释放槽位复用误触发）。
        private List<ulong> _deferredFixedFire;
        private List<ulong> _deferredLateFire;
        private List<ulong> _fireScratch;
        // 完成信号延迟唤醒队列 + 快照（在阶段 Tick 末尾排空，避免回调在释放调用栈内同步续跑）。
        private List<UniTaskCompletionSource> _deferredSignals;
        private List<UniTaskCompletionSource> _signalScratch;

        public void Init(int capacity)
        {
            _pages = new WheelPage[MAX_PAGE_COUNT];
            _freeSlotPages = new IndexPage[MAX_PAGE_COUNT];
            _activeSlotPages = new IndexPage[MAX_PAGE_COUNT];
            _scaledWheelHeads = CreateWheelHeads();
            _scaledWheelTails = CreateWheelHeads();
            _unscaledWheelHeads = CreateWheelHeads();
            _unscaledWheelTails = CreateWheelHeads();
            _progressSlots = new List<int>(64);
            _progressScratch = new List<ulong>(64);
            _deferredFixedFire = new List<ulong>(32);
            _deferredLateFire = new List<ulong>(32);
            _fireScratch = new List<ulong>(32);
            _deferredSignals = new List<UniTaskCompletionSource>(16);
            _signalScratch = new List<UniTaskCompletionSource>(16);
            _pageCount = 0;
            _slotCapacity = 0;
            _freeCount = 0;
            _activeCount = 0;
            _peakActiveCount = 0;
            _scaledQueueCount = 0;
            _unscaledQueueCount = 0;
            _scaledCurrentTick = TimeToTickFloor(ScaledNow);
            _unscaledCurrentTick = TimeToTickFloor(UnscaledNow);
            _executingSlotIndex = INVALID_INDEX;

            Prewarm(NormalizeCapacity(capacity));
        }

        public void Shutdown()
        {
            if (_pages == null)
            {
                return; // 幂等：未 Init 或已关停时，下面的排空 / 清理都无从下手（列表已被置空）
            }

            ClearAll();
            // 关停前必须同步排空：ClearAll 释放的槽位若挂着 awaiter，其信号已入队 _deferredSignals，
            // 此处不 TrySetResult 就会随下面置空一起丢失，导致 await 方永久挂起。
            DrainSignals();
            _pages = null;
            _freeSlotPages = null;
            _activeSlotPages = null;
            _scaledWheelHeads = null;
            _scaledWheelTails = null;
            _unscaledWheelHeads = null;
            _unscaledWheelTails = null;
            _progressSlots = null;
            _progressScratch = null;
            _deferredFixedFire = null;
            _deferredLateFire = null;
            _fireScratch = null;
            _deferredSignals = null;
            _signalScratch = null;

            // 计数一并归零：GetSlotIndex 的第一道范围判定与 AcquireSlot 都只看计数，
            // 残留非零会让关停后的过期句柄穿过判定、去解引用已置空的页数组（NRE 而非按无效句柄降级）。
            _pageCount = 0;
            _slotCapacity = 0;
            _freeCount = 0;
            _activeCount = 0;
            _executingSlotIndex = INVALID_INDEX;
        }

        private void Prewarm(int capacity)
        {
            int targetCapacity = NormalizeCapacity(capacity);
            if (targetCapacity > MAX_PAGE_COUNT * PAGE_SIZE)
            {
                targetCapacity = MAX_PAGE_COUNT * PAGE_SIZE;
            }

            while (_slotCapacity < targetCapacity)
            {
                AddPage();
            }
        }

        #region 注册 [REGISTER]

        internal ulong Delay(float delaySeconds, Action onComplete, bool isLooped, bool ignoreTimeScale, TimerPhase phase)
        {
            if (onComplete == null)
            {
                WarnScheduleFailed("onComplete is null.");
                return 0UL;
            }

            if (!IsSchedulableDelay(delaySeconds))
            {
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                WarnScheduleFailed("no available timer slot.");
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(delaySeconds), isLooped, ignoreTimeScale, phase);
            SetHandlerType(slotIndex, HANDLER_NO_ARGS);
            SetNoArgsHandler(slotIndex, onComplete);
            AddActive(slotIndex);
            AddToQueue(slotIndex, ignoreTimeScale);
            return GetHandle(slotIndex);
        }

        internal ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg, bool isLooped, bool ignoreTimeScale,
            TimerPhase phase) where T : class
        {
            if (onComplete == null)
            {
                WarnScheduleFailed("onComplete is null.");
                return 0UL;
            }

            if (!IsSchedulableDelay(delaySeconds))
            {
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                WarnScheduleFailed("no available timer slot.");
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(delaySeconds), isLooped, ignoreTimeScale, phase);
            SetHandlerType(slotIndex, HANDLER_GENERIC);
            SetGenericInvoker(slotIndex, TimerGenericInvokerCache<T>.Invoke);
            SetGenericHandler(slotIndex, onComplete);
            SetGenericArg(slotIndex, arg);
            AddActive(slotIndex);
            AddToQueue(slotIndex, ignoreTimeScale);
            return GetHandle(slotIndex);
        }

        internal ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate, bool isLooped,
            bool ignoreTimeScale, TimerPhase phase)
        {
            if (onComplete == null && onUpdate == null)
            {
                WarnScheduleFailed("both onComplete and onUpdate are null.");
                return 0UL;
            }

            if (!IsSchedulableDelay(delaySeconds))
            {
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                WarnScheduleFailed("no available timer slot.");
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(delaySeconds), isLooped, ignoreTimeScale, phase);
            SetHandlerType(slotIndex, onComplete != null ? HANDLER_NO_ARGS : HANDLER_NONE);
            SetNoArgsHandler(slotIndex, onComplete);
            AddActive(slotIndex);

            if (onUpdate != null)
            {
                SetProgressHandler(slotIndex, onUpdate);
                SetState(slotIndex, STATE_PROGRESS);
                AddProgressSlot(slotIndex);
            }

            AddToQueue(slotIndex, ignoreTimeScale);
            return GetHandle(slotIndex);
        }

        internal unsafe ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete, bool isLooped,
            bool ignoreTimeScale, TimerPhase phase)
        {
            if (!onComplete.IsValid())
            {
                WarnScheduleFailed("unsafe binding is invalid.");
                return 0UL;
            }

            if (!IsSchedulableDelay(delaySeconds))
            {
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                WarnScheduleFailed("no available timer slot.");
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(delaySeconds), isLooped, ignoreTimeScale, phase);
            SetUnsafeComplete(slotIndex, onComplete);
            AddActive(slotIndex);
            AddToQueue(slotIndex, ignoreTimeScale);
            return GetHandle(slotIndex);
        }

        #endregion

        #region 阶段驱动 [PHASE TICK]

        public void Tick()
        {
            AdvanceQueue(false, ScaledNow);
            AdvanceQueue(true, UnscaledNow);
            ProcessProgressTimers();
            DrainSignals();
        }

        public void FixedTick()
        {
            DrainDeferredFires(_deferredFixedFire, STATE_DEFERRED_FIXED);
            DrainSignals();
        }

        public void LateTick()
        {
            DrainDeferredFires(_deferredLateFire, STATE_DEFERRED_LATE);
            DrainSignals();
        }

        #endregion

        #region 进度回调 [PROGRESS]

        private void ProcessProgressTimers()
        {
            if (_progressSlots.Count == 0)
            {
                return;
            }

            _progressScratch.Clear();
            for (int i = 0; i < _progressSlots.Count; i++)
            {
                _progressScratch.Add(GetHandle(_progressSlots[i]));
            }

            for (int i = 0; i < _progressScratch.Count; i++)
            {
                int slotIndex = GetSlotIndex(_progressScratch[i]);
                if (slotIndex < 0)
                {
                    continue;
                }

                byte state = GetState(slotIndex);
                if ((state & (STATE_ACTIVE | STATE_PROGRESS)) != (STATE_ACTIVE | STATE_PROGRESS))
                {
                    continue;
                }

                Action<float> progress = GetProgressHandler(slotIndex);
                if (progress == null)
                {
                    continue;
                }

                double duration = GetDuration(slotIndex);
                if (duration <= 0d)
                {
                    continue;
                }

                double left = (state & STATE_RUNNING) != 0
                    ? GetTriggerTime(slotIndex) - GetCurrentTime(IsUnscaled(slotIndex))
                    : GetRemainingTime(slotIndex);
                double elapsed = duration - left;
                if (elapsed < 0d)
                {
                    elapsed = 0d;
                }

                float ratio = (float)(elapsed / duration);
                if (ratio > 1f)
                {
                    ratio = 1f;
                }

                try
                {
                    progress.Invoke(ratio);
                }
                catch (Exception exception)
                {
                    LogUtility.Fatal(exception);
                }
            }

            _progressScratch.Clear();
        }

        private void AddProgressSlot(int slotIndex)
        {
            SetProgressPosition(slotIndex, _progressSlots.Count);
            _progressSlots.Add(slotIndex);
        }

        /// <summary>按槽位记录的列表下标做 swap-remove，O(1) 且顺序无关（进度以快照迭代）。</summary>
        private void RemoveProgressSlot(int slotIndex)
        {
            int pos = GetProgressPosition(slotIndex);
            if (pos < 0)
            {
                return;
            }

            int last = _progressSlots.Count - 1;
            if (pos != last)
            {
                int moved = _progressSlots[last];
                _progressSlots[pos] = moved;
                SetProgressPosition(moved, pos);
            }

            _progressSlots.RemoveAt(last);
            SetProgressPosition(slotIndex, INVALID_INDEX);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetProgressPosition(int slotIndex)
        {
            return GetPage(slotIndex).ProgressPositions[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetProgressPosition(int slotIndex, int value)
        {
            GetPage(slotIndex).ProgressPositions[GetOffset(slotIndex)] = value;
        }

        #endregion

        #region 到期派发 [FIRE]

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessDueTimer(int slotIndex, double currentTime)
        {
            byte state = GetState(slotIndex);
            if ((state & (STATE_ACTIVE | STATE_RUNNING)) != (STATE_ACTIVE | STATE_RUNNING))
            {
                return;
            }

            byte phase = GetPhase(slotIndex);
            if (phase == (byte)TimerPhase.FixedUpdate)
            {
                AddDeferredFire(_deferredFixedFire, slotIndex, STATE_DEFERRED_FIXED);
                return;
            }

            if (phase == (byte)TimerPhase.LateUpdate)
            {
                AddDeferredFire(_deferredLateFire, slotIndex, STATE_DEFERRED_LATE);
                return;
            }

            FireTimeTimer(slotIndex, currentTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddDeferredFire(List<ulong> list, int slotIndex, byte deferredBit)
        {
            if ((GetState(slotIndex) & deferredBit) != 0)
            {
                return; // 归属位即去重判据，无需 List.Contains 的线性扫描
            }

            SetState(slotIndex, deferredBit);
            list.Add(GetHandle(slotIndex));
        }

        private void DrainDeferredFires(List<ulong> list, byte deferredBit)
        {
            if (list.Count == 0)
            {
                return;
            }

            // 快照迭代：FireTimeTimer 内的自释放会改动 list（RemoveDeferredRef），直接索引迭代会漏触发。
            _fireScratch.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                _fireScratch.Add(list[i]);
            }

            list.Clear();

            for (int i = 0; i < _fireScratch.Count; i++)
            {
                int slotIndex = GetSlotIndex(_fireScratch[i]);
                if (slotIndex < 0)
                {
                    continue; // 已被释放或槽位被复用（版本不符）；归属位随 ReleaseSlot 一并清过
                }

                // 出列即失归属：本帧之后该计时器（尤其循环型）需能再次入列。
                ClearState(slotIndex, deferredBit);

                byte state = GetState(slotIndex);
                if ((state & (STATE_ACTIVE | STATE_RUNNING)) != (STATE_ACTIVE | STATE_RUNNING))
                {
                    continue;
                }

                // 逐槽取当前时间：缩放/非缩放计时器混列时不能共用同一时间基准。
                double currentTime = GetCurrentTime(IsUnscaled(slotIndex));
                FireTimeTimer(slotIndex, currentTime);
            }

            _fireScratch.Clear();
        }

        /// <summary>触发一个计时器的完成回调，并处理循环重排 / 释放。</summary>
        private void FireTimeTimer(int slotIndex, double currentTime)
        {
            _executingSlotIndex = slotIndex;
            InvokeCompleteBody(slotIndex);

            byte state = GetState(slotIndex);
            if ((state & STATE_RELEASE_PENDING) != 0)
            {
                FreeReleasedExecutingSlot(slotIndex);
                RemoveProgressSlot(slotIndex);
                ClearExecutingMark(slotIndex);
                return;
            }

            ClearExecutingMark(slotIndex);

            if ((state & STATE_ACTIVE) == 0 || GetQueueIndex(slotIndex) >= 0)
            {
                return;
            }

            if ((state & STATE_LOOP) != 0)
            {
                if ((state & STATE_RUNNING) != 0)
                {
                    RescheduleLoop(slotIndex, currentTime);
                }

                return;
            }

            ReleaseSlot(slotIndex);
            RemoveProgressSlot(slotIndex);
        }

        /// <summary>
        /// 回收执行标记：仅当标记仍属于本槽时才清。回调内嵌套触发时，内层不得抹掉外层的标记
        /// （与 <c>FrameTimerEngine.InvokeComplete</c> 同形）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearExecutingMark(int slotIndex)
        {
            if (_executingSlotIndex == slotIndex)
            {
                _executingSlotIndex = INVALID_INDEX;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RescheduleLoop(int slotIndex, double currentTime)
        {
            double duration = GetDuration(slotIndex);
            double triggerTime = GetTriggerTime(slotIndex) + duration;
            // 时钟不可用时宁可留着已过的有限触发时间（下帧即再触发），也不写非有限值：那会让循环计时器永久出局。
            if (triggerTime <= currentTime && IsUsableClockTime(currentTime))
            {
                triggerTime = currentTime + duration;
            }

            SetTriggerTime(slotIndex, triggerTime);
            AddToQueue(slotIndex, IsUnscaled(slotIndex));
        }

        #endregion

        #region 句柄控制 [HANDLE CONTROL]

        public void Pause(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0 || (GetState(slotIndex) & STATE_RUNNING) == 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, isUnscaled);
                double leftTime = GetTriggerTime(slotIndex) - GetCurrentTime(isUnscaled);
                SetRemainingTime(slotIndex, leftTime > MINIMUM_DELAY_SECONDS ? leftTime : MINIMUM_DELAY_SECONDS);
            }
            else
            {
                SetRemainingTime(slotIndex, IsLoop(slotIndex) ? GetDuration(slotIndex) : MINIMUM_DELAY_SECONDS);
            }

            ClearState(slotIndex, STATE_RUNNING);
        }

        public void Resume(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0 || (GetState(slotIndex) & STATE_RUNNING) != 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            double currentTime = GetCurrentTime(isUnscaled);
            if (!IsUsableClockTime(currentTime))
            {
                WarnScheduleFailed("clock time is not usable, resume skipped.");
                return; // 保持暂停原状：非有限触发时间会让该计时器永远够不到
            }

            double delay = GetRemainingTime(slotIndex);
            if (delay <= MINIMUM_DELAY_SECONDS)
            {
                delay = MINIMUM_DELAY_SECONDS;
            }

            SetTriggerTime(slotIndex, currentTime + delay);
            SetRemainingTime(slotIndex, 0d);
            SetState(slotIndex, STATE_RUNNING);
            AddToQueue(slotIndex, isUnscaled);
        }

        public void Restart(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            double currentTime = GetCurrentTime(isUnscaled);
            if (!IsUsableClockTime(currentTime))
            {
                WarnScheduleFailed("clock time is not usable, restart skipped.");
                return; // 先验后改：一旦出列却写进非有限触发时间，计时器就再也回不来
            }

            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, isUnscaled);
            }

            SetTriggerTime(slotIndex, currentTime + GetDuration(slotIndex));
            SetRemainingTime(slotIndex, 0d);
            SetState(slotIndex, STATE_RUNNING);
            AddToQueue(slotIndex, isUnscaled);
        }

        public void Cancel(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex >= 0)
            {
                ReleaseSlot(slotIndex);
            }
        }

        public void PauseAll()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                Pause(GetHandle(GetActiveSlot(i)));
            }
        }

        public void ResumeAll()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                Resume(GetHandle(GetActiveSlot(i)));
            }
        }

        public void CancelAll()
        {
            ClearAll();
            _progressSlots.Clear();
            _deferredFixedFire.Clear();
            _deferredLateFire.Clear();
        }

        public bool IsRunning(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            return slotIndex >= 0 && (GetState(slotIndex) & STATE_RUNNING) != 0;
        }

        public bool IsDone(ulong handle)
        {
            return handle == 0UL || GetSlotIndex(handle) < 0;
        }

        public float GetLeftTime(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return 0f;
            }

            double leftTime = (GetState(slotIndex) & STATE_RUNNING) == 0
                ? GetRemainingTime(slotIndex)
                : GetTriggerTime(slotIndex) - GetCurrentTime(IsUnscaled(slotIndex));
            return leftTime > 0d ? (float)leftTime : 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLeftFrames(ulong handle)
        {
            // 时间轮泳道无“帧”概念。
            return 0;
        }

        public float GetElapsed(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return 0f;
            }

            float duration = (float)GetDuration(slotIndex);
            float left = (GetState(slotIndex) & STATE_RUNNING) == 0
                ? (float)GetRemainingTime(slotIndex)
                : (float)(GetTriggerTime(slotIndex) - GetCurrentTime(IsUnscaled(slotIndex)));
            float elapsed = duration - left;
            return elapsed > 0f ? elapsed : 0f;
        }

        public float GetDuration(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            return slotIndex < 0 ? 0f : (float)GetDuration(slotIndex);
        }

        #endregion

        #region 完成回调派发 [COMPLETE INVOCATION]

        private unsafe void InvokeCompleteBody(int slotIndex)
        {
            try
            {
                switch (GetHandlerType(slotIndex))
                {
                    case HANDLER_NO_ARGS:
                        GetNoArgsHandler(slotIndex)?.Invoke();
                        break;
                    case HANDLER_GENERIC:
                        GetGenericInvoker(slotIndex)?.Invoke(GetGenericHandler(slotIndex), GetGenericArg(slotIndex));
                        break;
                    case HANDLER_UNSAFE_STATIC:
                    {
                        ulong fn = GetUnsafeFnPtr(slotIndex);
                        if (fn != 0UL)
                        {
                            ((delegate* managed<void>)fn)();
                        }

                        break;
                    }
                    case HANDLER_UNSAFE_OBJECT:
                    {
                        ulong fn = GetUnsafeFnPtr(slotIndex);
                        if (fn != 0UL)
                        {
                            ((delegate* managed<object, void>)fn)(GetUnsafeInstance(slotIndex));
                        }

                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                LogUtility.Fatal(exception);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void SetUnsafeComplete(int slotIndex, in TimerUnsafeBinding binding)
        {
            if (!binding.IsValid())
            {
                SetHandlerType(slotIndex, HANDLER_NONE);
                return;
            }

            if (binding.BindingType == TimerUnsafeBinding.TYPE_DELEGATE)
            {
                SetHandlerType(slotIndex, HANDLER_NO_ARGS);
                SetNoArgsHandler(slotIndex, binding.DelegateCallback);
            }
            else if (binding.Instance != null)
            {
                SetHandlerType(slotIndex, HANDLER_UNSAFE_OBJECT);
                SetUnsafeFnPtr(slotIndex, (ulong)(nint)binding.FunctionPointer);
                SetUnsafeInstance(slotIndex, binding.Instance);
            }
            else
            {
                SetHandlerType(slotIndex, HANDLER_UNSAFE_STATIC);
                SetUnsafeFnPtr(slotIndex, (ulong)(nint)binding.FunctionPointer);
            }
        }

        /// <summary>调度失败诊断：整条调用（含实参求值）在非编辑器构建下被编译器摘除。</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void WarnScheduleFailed(string reason)
        {
            LogUtility.Warning("[Timer] Schedule failed: {0}", reason);
        }

        #endregion

        #region 等待信号 [WAIT SIGNAL]

        public UniTask WaitAsync(ulong handle, CancellationToken cancellationToken)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return UniTask.CompletedTask; // 已结束 / 无效句柄
            }

            if (GetWaitSignal(slotIndex) != null)
            {
                // 同一句柄已有等待者：额外等待者退回轮询，避免覆盖首信号使其永不唤醒。
                return UniTask.WaitUntil(() => IsDone(handle), cancellationToken: cancellationToken);
            }

            UniTaskCompletionSource source = new UniTaskCompletionSource();
            SetWaitSignal(slotIndex, source);
            return cancellationToken.CanBeCanceled
                ? source.Task.AttachExternalCancellation(cancellationToken)
                : source.Task;
        }

        private void SignalCompletion(int slotIndex)
        {
            UniTaskCompletionSource source = GetWaitSignal(slotIndex);
            if (source == null)
            {
                return;
            }

            SetWaitSignal(slotIndex, null);
            _deferredSignals.Add(source);
        }

        private void DrainSignals()
        {
            if (_deferredSignals.Count == 0)
            {
                return;
            }

            // 快照后排空：续跑回调可能再次挂载/触发信号，落在本帧或下帧排空均可。
            _signalScratch.Clear();
            for (int i = 0; i < _deferredSignals.Count; i++)
            {
                _signalScratch.Add(_deferredSignals[i]);
            }

            _deferredSignals.Clear();

            for (int i = 0; i < _signalScratch.Count; i++)
            {
                _signalScratch[i].TrySetResult();
            }

            _signalScratch.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private UniTaskCompletionSource GetWaitSignal(int slotIndex)
        {
            return GetPage(slotIndex).WaitSignals[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetWaitSignal(int slotIndex, UniTaskCompletionSource value)
        {
            GetPage(slotIndex).WaitSignals[GetOffset(slotIndex)] = value;
        }

        #endregion

        #region 统计 / 调试 [STATS & DEBUG]

        public int ActiveCount => _activeCount;

        public void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            activeCount = _activeCount;
            poolCapacity = _slotCapacity;
            peakActiveCount = _peakActiveCount;
            freeCount = _freeCount;
        }

        public int GetAllTimers(TimerDebugInfo[] results, int offset, int limit)
        {
            if (results == null || limit <= 0)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = ScaledNow;
            double unscaledTime = UnscaledNow;
#if UNITY_EDITOR
            double realtime = UnityEngine.Time.realtimeSinceStartupAsDouble;
#else
            const double realtime = 0d;
#endif
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                FillDebugInfo(GetActiveSlot(i), ref results[offset + count], scaledTime, unscaledTime, realtime);
                count++;
            }

            return count;
        }

#if UNITY_EDITOR
        public int GetStaleOneShotTimers(TimerDebugInfo[] results, int offset, int limit)
        {
            if (results == null || limit <= 0)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = ScaledNow;
            double unscaledTime = UnscaledNow;
            double realtime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                int slotIndex = GetActiveSlot(i);
                if (IsLoop(slotIndex) || realtime - GetCreationTime(slotIndex) <= STALE_ONE_SHOT_SECONDS)
                {
                    continue;
                }

                FillDebugInfo(slotIndex, ref results[offset + count], scaledTime, unscaledTime, realtime);
                count++;
            }

            return count;
        }
#endif

        private void FillDebugInfo(int slotIndex, ref TimerDebugInfo info, double scaledTime, double unscaledTime,
            double realtime)
        {
            byte state = GetState(slotIndex);
            bool running = (state & STATE_RUNNING) != 0;
            bool unscaled = (state & STATE_UNSCALED) != 0;
            double leftTime = running ? GetTriggerTime(slotIndex) - (unscaled ? unscaledTime : scaledTime) : GetRemainingTime(slotIndex);
            if (leftTime < 0d)
            {
                leftTime = 0d;
            }

            byte flags = 0;
            if (running)
            {
                flags |= TimerDebugFlags.RUNNING;
            }

            if ((state & STATE_LOOP) != 0)
            {
                flags |= TimerDebugFlags.LOOP;
            }

            if (unscaled)
            {
                flags |= TimerDebugFlags.UNSCALED;
            }

            info.TimerHandle = GetHandle(slotIndex);
            info.LeftTime = (float)leftTime;
            info.Duration = (float)GetDuration(slotIndex);
#if UNITY_EDITOR
            info.Age = (float)(realtime - GetCreationTime(slotIndex));
#else
            info.Age = 0f;
#endif
            info.Flags = flags;
        }

        #endregion

        #region 时间轮 [TIMING WHEEL]

        private void AdvanceQueue(bool isUnscaled, double currentTime)
        {
            if (!IsUsableClockTime(currentTime))
            {
                return; // 时钟被污染：本帧整体不推进。饱和换算会把游标压到 0 或推到 MAX_TICK，之后每帧最多追 64 tick，等于把已运行的秒数再等一遍
            }

            long currentTick = TimeToTickFloor(currentTime);
            if (currentTick >= MAX_TICK)
            {
                return; // 有限但大到触顶的读数同样按异常帧处理：游标推到 MAX_TICK 等于时间轮报废
            }

            long cursorTick = GetCurrentWheelTick(isUnscaled);
            int tickBudget = MAX_WHEEL_TICKS_PER_FRAME;
            while (cursorTick <= currentTick && tickBudget > 0 && GetQueueCount(isUnscaled) > 0)
            {
                SetCurrentWheelTick(isUnscaled, cursorTick);
                AdvanceWheelTick(isUnscaled, cursorTick, currentTime);
                cursorTick++;
                tickBudget--;
            }

            if (GetQueueCount(isUnscaled) == 0)
            {
                SetCurrentWheelTick(isUnscaled, currentTick + 1L);
                return;
            }

            // 时钟回退（换时钟后端 / 回放）时游标跟着落回：已入列计时器的绝对 DueTicks 本就无从修复，
            // 但这里绝不允许停住——游标悬在高处而队列永不空，会把整轮连同新排的计时器一起永久卡死。
            SetCurrentWheelTick(isUnscaled, cursorTick <= currentTick ? cursorTick : currentTick + 1L);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToQueue(int slotIndex, bool isUnscaled)
        {
            long dueTick = TimeToTickCeiling(GetTriggerTime(slotIndex));
            long currentTick = GetCurrentWheelTick(isUnscaled);
            if (dueTick < currentTick)
            {
                dueTick = currentTick;
            }

            int bucketIndex = GetWheelBucketIndex(dueTick, currentTick);
            SetDueTick(slotIndex, dueTick);
            AddToBucket(slotIndex, bucketIndex, isUnscaled);
            SetQueueCount(isUnscaled, GetQueueCount(isUnscaled) + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveFromQueue(int slotIndex, bool isUnscaled)
        {
            int bucketIndex = GetQueueIndex(slotIndex);
            if (bucketIndex < 0)
            {
                return;
            }

            RemoveFromBucket(slotIndex, bucketIndex, isUnscaled);
            SetQueueCount(isUnscaled, GetQueueCount(isUnscaled) - 1);
        }

        private void AdvanceWheelTick(bool isUnscaled, long tick, double currentTime)
        {
            for (int level = WHEEL_MAX_LEVEL; level > 0; level--)
            {
                CascadeWheelLevel(isUnscaled, tick, level);
            }

            ProcessBucket(isUnscaled, tick & WHEEL_MASK, currentTime, TimeToTickFloor(currentTime));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CascadeWheelLevel(bool isUnscaled, long tick, int level)
        {
            long mask = (1L << (level * WHEEL_SHIFT)) - 1L;
            if ((tick & mask) != 0L)
            {
                return;
            }

            int bucketIndex = (level << WHEEL_SHIFT) + (int)((tick >> (level * WHEEL_SHIFT)) & WHEEL_MASK);
            int slotIndex = RemoveBucketHead(bucketIndex, isUnscaled);
            while (slotIndex >= 0)
            {
                int nextSlotIndex = GetQueueNextIndex(slotIndex);
                DetachRemovedBucketSlot(slotIndex);
                int targetBucketIndex = GetWheelBucketIndex(GetDueTick(slotIndex), tick);
                AddToBucket(slotIndex, targetBucketIndex, isUnscaled);
                slotIndex = nextSlotIndex;
            }
        }

        private void ProcessBucket(bool isUnscaled, long bucketIndex, double currentTime, long currentTick)
        {
            int slotIndex = GetWheelHead((int)bucketIndex, isUnscaled);
            while (slotIndex >= 0)
            {
                RemoveFromQueue(slotIndex, isUnscaled);

                byte state = GetState(slotIndex);
                if ((state & (STATE_ACTIVE | STATE_RUNNING)) == (STATE_ACTIVE | STATE_RUNNING))
                {
                    if (GetDueTick(slotIndex) <= currentTick)
                    {
                        ProcessDueTimer(slotIndex, currentTime);
                    }
                    else
                    {
                        AddToQueue(slotIndex, isUnscaled);
                    }
                }

                slotIndex = GetWheelHead((int)bucketIndex, isUnscaled);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToBucket(int slotIndex, int bucketIndex, bool isUnscaled)
        {
            int tailSlotIndex = GetWheelTail(bucketIndex, isUnscaled);
            SetQueueIndex(slotIndex, bucketIndex);
            SetQueuePrevIndex(slotIndex, tailSlotIndex);
            SetQueueNextIndex(slotIndex, INVALID_INDEX);
            if (tailSlotIndex >= 0)
            {
                SetQueueNextIndex(tailSlotIndex, slotIndex);
            }
            else
            {
                SetWheelHead(bucketIndex, slotIndex, isUnscaled);
            }

            SetWheelTail(bucketIndex, slotIndex, isUnscaled);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveFromBucket(int slotIndex, int bucketIndex, bool isUnscaled)
        {
            int prevSlotIndex = GetQueuePrevIndex(slotIndex);
            int nextSlotIndex = GetQueueNextIndex(slotIndex);
            if (prevSlotIndex >= 0)
            {
                SetQueueNextIndex(prevSlotIndex, nextSlotIndex);
            }
            else
            {
                SetWheelHead(bucketIndex, nextSlotIndex, isUnscaled);
            }

            if (nextSlotIndex >= 0)
            {
                SetQueuePrevIndex(nextSlotIndex, prevSlotIndex);
            }
            else
            {
                SetWheelTail(bucketIndex, prevSlotIndex, isUnscaled);
            }

            DetachRemovedBucketSlot(slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int RemoveBucketHead(int bucketIndex, bool isUnscaled)
        {
            int headSlotIndex = GetWheelHead(bucketIndex, isUnscaled);
            SetWheelHead(bucketIndex, INVALID_INDEX, isUnscaled);
            SetWheelTail(bucketIndex, INVALID_INDEX, isUnscaled);
            return headSlotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DetachRemovedBucketSlot(int slotIndex)
        {
            SetQueueIndex(slotIndex, INVALID_INDEX);
            SetQueueNextIndex(slotIndex, INVALID_INDEX);
            SetQueuePrevIndex(slotIndex, INVALID_INDEX);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetWheelBucketIndex(long dueTick, long currentTick)
        {
            long delta = dueTick - currentTick;
            if (delta < WHEEL_SIZE)
            {
                return (int)(dueTick & WHEEL_MASK);
            }

            if (delta < (1L << (WHEEL_SHIFT * 2)))
            {
                return WHEEL_SIZE + (int)((dueTick >> WHEEL_SHIFT) & WHEEL_MASK);
            }

            if (delta < (1L << (WHEEL_SHIFT * 3)))
            {
                return (WHEEL_SIZE << 1) + (int)((dueTick >> (WHEEL_SHIFT * 2)) & WHEEL_MASK);
            }

            return (WHEEL_MAX_LEVEL << WHEEL_SHIFT) + (int)((dueTick >> (WHEEL_SHIFT * WHEEL_MAX_LEVEL)) & WHEEL_MASK);
        }

        #endregion

        #region 槽位池 [SLOT POOL]

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AcquireSlot()
        {
            if (_pages == null)
            {
                return INVALID_INDEX; // 已关停：不能再 AddPage 扩页，按「无可用槽位」降级为 0 句柄
            }

            if (_freeCount <= 0)
            {
                AddPage();
                if (_freeCount <= 0)
                {
                    return INVALID_INDEX;
                }
            }

            return TimerPool.GetPagedInt(_freeSlotPages, --_freeCount);
        }

        private void AddPage()
        {
            if (_pageCount >= MAX_PAGE_COUNT)
            {
                return;
            }

            EnsureIndexPage(_freeSlotPages, _pageCount);
            EnsureIndexPage(_activeSlotPages, _pageCount);

            WheelPage page = new WheelPage();
            _pages[_pageCount] = page;

            int baseSlotIndex = _pageCount << PAGE_SHIFT;
            for (int i = 0; i < PAGE_SIZE; i++)
            {
                SetPagedInt(_freeSlotPages, _freeCount++, baseSlotIndex + i);
            }

            _pageCount++;
            _slotCapacity += PAGE_SIZE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetActiveSlot(int index)
        {
            return TimerPool.GetPagedInt(_activeSlotPages, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetActiveSlot(int index, int value)
        {
            TimerPool.SetPagedInt(_activeSlotPages, index, value);
        }

        private void InitializeSlot(int slotIndex, double duration, bool isLoop, bool isUnscaled, TimerPhase phase)
        {
            int pageIndex = slotIndex >> PAGE_SHIFT;
            int offset = slotIndex & PAGE_MASK;
            WheelPage page = _pages[pageIndex];
            uint version = page.Versions[offset] + 1U;
            page.Versions[offset] = version == 0U ? 1U : version;
            page.Handles[offset] = TimerHandleLayout.Compose(version, TimerLaneKinds.Wheel, slotIndex);
            page.States[offset] = ComposeState(isLoop, isUnscaled);
            page.TriggerTimes[offset] = GetCurrentTime(isUnscaled) + duration;
            page.Durations[offset] = duration;
            page.RemainingTimes[offset] = 0d;
#if UNITY_EDITOR
            page.CreationTimes[offset] = UnityEngine.Time.realtimeSinceStartupAsDouble;
#endif
            page.DueTicks[offset] = 0L;
            page.QueueIndices[offset] = INVALID_INDEX;
            page.QueueNextIndices[offset] = INVALID_INDEX;
            page.QueuePrevIndices[offset] = INVALID_INDEX;
            page.ActiveIndices[offset] = INVALID_INDEX;
            page.ProgressPositions[offset] = INVALID_INDEX;
            page.HandlerTypes[offset] = HANDLER_NONE;
            page.NoArgsHandlers[offset] = null;
            page.GenericInvokers[offset] = null;
            page.GenericHandlers[offset] = null;
            page.GenericArgs[offset] = null;
            page.UnsafeFnPtrs[offset] = 0UL;
            page.UnsafeInstances[offset] = null;
            page.ProgressHandlers[offset] = null;
            page.Phases[offset] = (byte)phase;
            page.WaitSignals[offset] = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComposeState(bool isLoop, bool isUnscaled)
        {
            byte state = (byte)(STATE_ACTIVE | STATE_RUNNING);
            if (isLoop)
            {
                state |= STATE_LOOP;
            }

            if (isUnscaled)
            {
                state |= STATE_UNSCALED;
            }

            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSlotIndex(ulong handle)
        {
            int slotIndex = TimerHandleLayout.SlotOf(handle);
            if ((uint)slotIndex >= (uint)_slotCapacity || GetHandle(slotIndex) != handle ||
                (GetState(slotIndex) & STATE_ACTIVE) == 0)
            {
                return INVALID_INDEX;
            }

            return slotIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddActive(int slotIndex)
        {
            int activeIndex = _activeCount++;
            SetActiveSlot(activeIndex, slotIndex);
            SetActiveIndex(slotIndex, activeIndex);
            if (_activeCount > _peakActiveCount)
            {
                _peakActiveCount = _activeCount;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveActive(int slotIndex)
        {
            int activeIndex = GetActiveIndex(slotIndex);
            if ((uint)activeIndex >= (uint)_activeCount)
            {
                SetActiveIndex(slotIndex, INVALID_INDEX);
                return;
            }

            int lastIndex = --_activeCount;
            int lastSlotIndex = GetActiveSlot(lastIndex);
            SetActiveSlot(activeIndex, lastSlotIndex);
            SetActiveIndex(lastSlotIndex, activeIndex);
            SetActiveSlot(lastIndex, 0);
            SetActiveIndex(slotIndex, INVALID_INDEX);
        }

        private void ReleaseSlot(int slotIndex)
        {
            byte state = GetState(slotIndex);
            if ((state & STATE_ACTIVE) == 0)
            {
                return;
            }

            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, (state & STATE_UNSCALED) != 0);
            }

            RemoveActive(slotIndex);
            ClearHandler(slotIndex);
            SetRemainingTime(slotIndex, 0d);
            SetTriggerTime(slotIndex, 0d);
            SetDuration(slotIndex, 0d);
#if UNITY_EDITOR
            SetCreationTime(slotIndex, 0d);
#endif
            SetDueTick(slotIndex, 0L);

            // 列表归属清理必须在 executing 早返回之前完成——否则自取消的槽位会从列表丢失。
            // 句柄须在 SetHandle(0UL) 销毁前取出：延迟触发列表以句柄为键。
            RemoveProgressSlot(slotIndex);
            RemoveDeferredRef(slotIndex, GetHandle(slotIndex));

            // 释放即唤醒等待者（完成 / 取消 / CancelAll 统一经此）；延后到 Tick 末尾排空，避免同步续跑重入。
            SignalCompletion(slotIndex);

            SetHandle(slotIndex, 0UL);

            if (slotIndex == _executingSlotIndex)
            {
                SetStateRaw(slotIndex, STATE_RELEASE_PENDING);
                return;
            }

            SetStateRaw(slotIndex, 0);
            SetPagedInt(_freeSlotPages, _freeCount++, slotIndex);
        }

        /// <summary>从两条延后触发列表摘除本槽位；不在列的槽位（绝大多数）一次位判即返回。</summary>
        private void RemoveDeferredRef(int slotIndex, ulong handle)
        {
            byte deferred = (byte)(GetState(slotIndex) & (STATE_DEFERRED_FIXED | STATE_DEFERRED_LATE));
            if (deferred == 0)
            {
                return;
            }

            if ((deferred & STATE_DEFERRED_FIXED) != 0)
            {
                RemoveFromHandleList(_deferredFixedFire, handle);
            }

            if ((deferred & STATE_DEFERRED_LATE) != 0)
            {
                RemoveFromHandleList(_deferredLateFire, handle);
            }

            ClearState(slotIndex, STATE_DEFERRED_FIXED | STATE_DEFERRED_LATE);
        }

        private static void RemoveFromHandleList(List<ulong> list, ulong value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != value)
                {
                    continue;
                }

                int last = list.Count - 1;
                list[i] = list[last];
                list.RemoveAt(last);
                return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearHandler(int slotIndex)
        {
            SetHandlerType(slotIndex, HANDLER_NONE);
            SetNoArgsHandler(slotIndex, null);
            SetGenericInvoker(slotIndex, null);
            SetGenericHandler(slotIndex, null);
            SetGenericArg(slotIndex, null);
            SetUnsafeFnPtr(slotIndex, 0UL);
            SetUnsafeInstance(slotIndex, null);
            SetProgressHandler(slotIndex, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FreeReleasedExecutingSlot(int slotIndex)
        {
            SetStateRaw(slotIndex, 0);
            SetPagedInt(_freeSlotPages, _freeCount++, slotIndex);
        }

        private void ClearAll()
        {
            while (_activeCount > 0)
            {
                _executingSlotIndex = INVALID_INDEX;
                ReleaseSlot(GetActiveSlot(_activeCount - 1));
            }

            ClearWheelHeads(_scaledWheelHeads);
            ClearWheelHeads(_scaledWheelTails);
            ClearWheelHeads(_unscaledWheelHeads);
            ClearWheelHeads(_unscaledWheelTails);
            _scaledQueueCount = 0;
            _unscaledQueueCount = 0;
            _executingSlotIndex = INVALID_INDEX;
        }

        #endregion

        #region 存取器 [ACCESSORS]

        private static double ScaledNow => GameTime.Handler.ScaledNow;

        private static double UnscaledNow => GameTime.Handler.UnscaledNow;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GetCurrentTime(bool isUnscaled)
        {
            return isUnscaled ? UnscaledNow : ScaledNow;
        }

        private static int[] CreateWheelHeads()
        {
            int[] heads = new int[WHEEL_BUCKET_COUNT];
            ClearWheelHeads(heads);
            return heads;
        }

        private static void ClearWheelHeads(int[] heads)
        {
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i] = INVALID_INDEX;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long TimeToTickFloor(double time)
        {
            return ToTick(time * TICKS_PER_SECOND, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long TimeToTickCeiling(double time)
        {
            return ToTick(time * TICKS_PER_SECOND, true);
        }

        /// <summary>
        /// 把 tick 数饱和到 [0, MAX_TICK]：直接强转溢出成的负数会把轮游标打到
        /// long.MinValue，此后每帧最多追 64 tick 等于时间轮永久冻结。
        /// </summary>
        private static long ToTick(double ticks, bool ceiling)
        {
            if (!(ticks > 0d)) // 一并挡住 NaN 与负数
            {
                return 0L;
            }

            if (ticks >= (double)MAX_TICK) // 正无穷走这里
            {
                return MAX_TICK;
            }

            return ceiling ? (long)Math.Ceiling(ticks) : (long)ticks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NormalizeDelay(float delay)
        {
            return delay > MINIMUM_DELAY_SECONDS ? delay : MINIMUM_DELAY_SECONDS;
        }

        /// <summary>
        /// 校验延时可排期：非有限值会让触发时间与轮游标溢出，必须在占用槽位之前拒绝。
        /// </summary>
        private static bool IsSchedulableDelay(float delaySeconds)
        {
            if (float.IsNaN(delaySeconds) || float.IsInfinity(delaySeconds))
            {
                WarnScheduleFailed("delaySeconds is not a finite number.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验时钟读数可用：NaN / ±∞ / 负值都会让触发时间与轮游标一起溢出。异常帧既不推进轮，
        /// 也不刷新槽位的触发时间；注册路径上的非有限延时由 <see cref="IsSchedulableDelay"/> 拦在占用槽位之前。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsUsableClockTime(double time)
        {
            return time >= 0d && time < double.PositiveInfinity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQueueCount(bool isUnscaled)
        {
            return isUnscaled ? _unscaledQueueCount : _scaledQueueCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetQueueCount(bool isUnscaled, int value)
        {
            if (isUnscaled)
            {
                _unscaledQueueCount = value;
            }
            else
            {
                _scaledQueueCount = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetCurrentWheelTick(bool isUnscaled)
        {
            return isUnscaled ? _unscaledCurrentTick : _scaledCurrentTick;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCurrentWheelTick(bool isUnscaled, long value)
        {
            if (isUnscaled)
            {
                _unscaledCurrentTick = value;
            }
            else
            {
                _scaledCurrentTick = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetWheelHead(int bucketIndex, bool isUnscaled)
        {
            return isUnscaled ? _unscaledWheelHeads[bucketIndex] : _scaledWheelHeads[bucketIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetWheelHead(int bucketIndex, int value, bool isUnscaled)
        {
            if (isUnscaled)
            {
                _unscaledWheelHeads[bucketIndex] = value;
            }
            else
            {
                _scaledWheelHeads[bucketIndex] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetWheelTail(int bucketIndex, bool isUnscaled)
        {
            return isUnscaled ? _unscaledWheelTails[bucketIndex] : _scaledWheelTails[bucketIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetWheelTail(int bucketIndex, int value, bool isUnscaled)
        {
            if (isUnscaled)
            {
                _unscaledWheelTails[bucketIndex] = value;
            }
            else
            {
                _scaledWheelTails[bucketIndex] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsUnscaled(int slotIndex)
        {
            return (GetState(slotIndex) & STATE_UNSCALED) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLoop(int slotIndex)
        {
            return (GetState(slotIndex) & STATE_LOOP) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private WheelPage GetPage(int slotIndex)
        {
            return _pages[slotIndex >> PAGE_SHIFT];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetOffset(int slotIndex)
        {
            return slotIndex & PAGE_MASK;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong GetHandle(int slotIndex)
        {
            return GetPage(slotIndex).Handles[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetHandle(int slotIndex, ulong value)
        {
            GetPage(slotIndex).Handles[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetState(int slotIndex)
        {
            return GetPage(slotIndex).States[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetState(int slotIndex, byte mask)
        {
            WheelPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] | mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearState(int slotIndex, byte mask)
        {
            WheelPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] & ~mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetStateRaw(int slotIndex, byte value)
        {
            GetPage(slotIndex).States[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetHandlerType(int slotIndex)
        {
            return GetPage(slotIndex).HandlerTypes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetHandlerType(int slotIndex, byte value)
        {
            GetPage(slotIndex).HandlerTypes[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetTriggerTime(int slotIndex)
        {
            return GetPage(slotIndex).TriggerTimes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTriggerTime(int slotIndex, double value)
        {
            GetPage(slotIndex).TriggerTimes[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetDuration(int slotIndex)
        {
            return GetPage(slotIndex).Durations[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDuration(int slotIndex, double value)
        {
            GetPage(slotIndex).Durations[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetRemainingTime(int slotIndex)
        {
            return GetPage(slotIndex).RemainingTimes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetRemainingTime(int slotIndex, double value)
        {
            GetPage(slotIndex).RemainingTimes[GetOffset(slotIndex)] = value;
        }

#if UNITY_EDITOR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetCreationTime(int slotIndex)
        {
            return GetPage(slotIndex).CreationTimes[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCreationTime(int slotIndex, double value)
        {
            GetPage(slotIndex).CreationTimes[GetOffset(slotIndex)] = value;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetDueTick(int slotIndex)
        {
            return GetPage(slotIndex).DueTicks[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDueTick(int slotIndex, long value)
        {
            GetPage(slotIndex).DueTicks[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQueueIndex(int slotIndex)
        {
            return GetPage(slotIndex).QueueIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetQueueIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).QueueIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQueueNextIndex(int slotIndex)
        {
            return GetPage(slotIndex).QueueNextIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetQueueNextIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).QueueNextIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQueuePrevIndex(int slotIndex)
        {
            return GetPage(slotIndex).QueuePrevIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetQueuePrevIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).QueuePrevIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetActiveIndex(int slotIndex)
        {
            return GetPage(slotIndex).ActiveIndices[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetActiveIndex(int slotIndex, int value)
        {
            GetPage(slotIndex).ActiveIndices[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Action GetNoArgsHandler(int slotIndex)
        {
            return GetPage(slotIndex).NoArgsHandlers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetNoArgsHandler(int slotIndex, Action value)
        {
            GetPage(slotIndex).NoArgsHandlers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimerGenericInvoker GetGenericInvoker(int slotIndex)
        {
            return GetPage(slotIndex).GenericInvokers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGenericInvoker(int slotIndex, TimerGenericInvoker value)
        {
            GetPage(slotIndex).GenericInvokers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGenericHandler(int slotIndex)
        {
            return GetPage(slotIndex).GenericHandlers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGenericHandler(int slotIndex, object value)
        {
            GetPage(slotIndex).GenericHandlers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGenericArg(int slotIndex)
        {
            return GetPage(slotIndex).GenericArgs[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGenericArg(int slotIndex, object value)
        {
            GetPage(slotIndex).GenericArgs[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong GetUnsafeFnPtr(int slotIndex)
        {
            return GetPage(slotIndex).UnsafeFnPtrs[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUnsafeFnPtr(int slotIndex, ulong value)
        {
            GetPage(slotIndex).UnsafeFnPtrs[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetUnsafeInstance(int slotIndex)
        {
            return GetPage(slotIndex).UnsafeInstances[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUnsafeInstance(int slotIndex, object value)
        {
            GetPage(slotIndex).UnsafeInstances[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Action<float> GetProgressHandler(int slotIndex)
        {
            return GetPage(slotIndex).ProgressHandlers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetProgressHandler(int slotIndex, Action<float> value)
        {
            GetPage(slotIndex).ProgressHandlers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetPhase(int slotIndex)
        {
            return GetPage(slotIndex).Phases[GetOffset(slotIndex)];
        }

        #endregion
    }
}
