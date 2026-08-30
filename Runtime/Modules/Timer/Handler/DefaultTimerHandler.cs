using System;
using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 默认计时器后端配置（四级时间轮）。
    /// </summary>
    [Serializable]
    public sealed class DefaultTimerHandlerConfig : TimerServiceHandlerConfig
    {
        [Min(MIN_INITIAL_CAPACITY)]
        [SerializeField] private int m_InitialCapacity = 1024;
        private const int MIN_INITIAL_CAPACITY = 256;

        /// <summary>
        /// 初始槽位容量（最小 256，向上取整到页对齐）。
        /// </summary>
        public int InitialCapacity
        {
            get => m_InitialCapacity;
            set => m_InitialCapacity = value;
        }

        /// <inheritdoc />
        public override TimerServiceHandler CreateHandler()
        {
            return new DefaultTimerHandler(this);
        }
    }

    /// <summary>
    /// 计时器处理器。四级时间轮算法的默认实现。<br/>
    /// 无全量扫描 · 精准承载技能 CD / 心跳 / 延时任务<br/>
    /// 由 <see cref="DefaultTimerHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化——运行时字段无需 [NonSerialized] 标注）。
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class DefaultTimerHandler : TimerServiceHandler
    {
        /// <summary>后端配置（组合持有，不参与序列化）。</summary>
        private readonly DefaultTimerHandlerConfig m_Config;

        /// <summary>
        /// 以指定配置创建处理器。
        /// </summary>
        /// <param name="config">默认计时器配置。</param>
        public DefaultTimerHandler(DefaultTimerHandlerConfig config)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 以默认配置创建处理器（供测试与默认值场景使用）。
        /// </summary>
        public DefaultTimerHandler() : this(new DefaultTimerHandlerConfig())
        {
        }

        private const int PAGE_SHIFT = 8;
        private const int PAGE_SIZE = 1 << PAGE_SHIFT;
        private const int PAGE_MASK = PAGE_SIZE - 1;
        private const int MAX_PAGE_COUNT = 4096;
        private const int INVALID_INDEX = -1;
        private const int WHEEL_SHIFT = 8;
        private const int WHEEL_SIZE = 1 << WHEEL_SHIFT;
        private const int WHEEL_MASK = WHEEL_SIZE - 1;
        private const int WHEEL_LEVEL_COUNT = 4;
        private const int WHEEL_MAX_LEVEL = WHEEL_LEVEL_COUNT - 1;
        private const int WHEEL_BUCKET_COUNT = WHEEL_SIZE * WHEEL_LEVEL_COUNT;
        private const int MAX_WHEEL_TICKS_PER_FRAME = 64;
        private const double TICKS_PER_SECOND = 1000d;
        private const double MINIMUM_DELAY_SECONDS = 0.000001d;
#if UNITY_EDITOR
        private const double STALE_ONE_SHOT_SECONDS = 300d;
#endif

        private const byte HANDLER_NONE = 0;
        private const byte HANDLER_NO_ARGS = 1;
        private const byte HANDLER_GENERIC = 2;

        private const byte STATE_ACTIVE = 1 << 0;
        private const byte STATE_RUNNING = 1 << 1;
        private const byte STATE_LOOP = 1 << 2;
        private const byte STATE_UNSCALED = 1 << 3;
        private const byte STATE_RELEASE_PENDING = 1 << 4;

        internal delegate void TimerGenericInvoker(object handler, object arg);
        internal static class TimerGenericInvokerCache<T> where T : class
        {
            public static readonly TimerGenericInvoker Invoke = InvokeGeneric;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void InvokeGeneric(object handler, object arg)
            {
                ((Action<T>)handler).Invoke((T)arg);
            }
        }

        private sealed class TimerPage
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
            public readonly Action[] NoArgsHandlers = new Action[PAGE_SIZE];
            public readonly TimerGenericInvoker[] GenericInvokers = new TimerGenericInvoker[PAGE_SIZE];
            public readonly object[] GenericHandlers = new object[PAGE_SIZE];
            public readonly object[] GenericArgs = new object[PAGE_SIZE];

            public TimerPage()
            {
                for (int i = 0; i < PAGE_SIZE; i++)
                {
                    QueueIndices[i] = INVALID_INDEX;
                    QueueNextIndices[i] = INVALID_INDEX;
                    QueuePrevIndices[i] = INVALID_INDEX;
                    ActiveIndices[i] = INVALID_INDEX;
                }
            }
        }

        private sealed class IntPage
        {
            public readonly int[] Values = new int[PAGE_SIZE];
        }

        private TimerPage[] _pages;
        private IntPage[] _freeSlotPages;
        private IntPage[] _activeSlotPages;
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

        protected override void OnInit()
        {
            _pages = new TimerPage[MAX_PAGE_COUNT];
            _freeSlotPages = new IntPage[MAX_PAGE_COUNT];
            _activeSlotPages = new IntPage[MAX_PAGE_COUNT];
            _scaledWheelHeads = CreateWheelHeads();
            _scaledWheelTails = CreateWheelHeads();
            _unscaledWheelHeads = CreateWheelHeads();
            _unscaledWheelTails = CreateWheelHeads();
            _pageCount = 0;
            _slotCapacity = 0;
            _freeCount = 0;
            _activeCount = 0;
            _peakActiveCount = 0;
            _scaledQueueCount = 0;
            _unscaledQueueCount = 0;
            _scaledCurrentTick = TimeToTickFloor(Time.timeAsDouble);
            _unscaledCurrentTick = TimeToTickFloor(Time.unscaledTimeAsDouble);
            _executingSlotIndex = INVALID_INDEX;

            Prewarm(NormalizeCapacity(m_Config.InitialCapacity));
        }

        protected override void OnShutdown()
        {
            ClearAll();

            // 释放引用
            _pages = null;
            _freeSlotPages = null;
            _activeSlotPages = null;
            _scaledWheelHeads = null;
            _scaledWheelTails = null;
            _unscaledWheelHeads = null;
            _unscaledWheelTails = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        internal override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            AdvanceQueue(false, Time.timeAsDouble);
            AdvanceQueue(true, Time.unscaledTimeAsDouble);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override ulong AddTimer(Action callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            if (callback == null)
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("callback is null.");
#endif
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("no available timer slot.");
#endif
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(time), isLoop, isUnscaled);
            SetHandlerType(slotIndex, HANDLER_NO_ARGS);
            SetNoArgsHandler(slotIndex, callback);
            AddActive(slotIndex);
            AddToQueue(slotIndex, isUnscaled);
            return GetHandle(slotIndex);
        }

        /// <summary>
        /// 热路径调用方必须使用缓存委托或静态方法组，禁止传入捕获 lambda 或闭包，
        /// 因为闭包对象会产生分配，破坏 0GC 约束。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class
        {
            if (callback == null)
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("callback is null.");
#endif
                return 0UL;
            }

            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
#if UNITY_EDITOR
                WarnAddTimerFailed("no available timer slot.");
#endif
                return 0UL;
            }

            InitializeSlot(slotIndex, NormalizeDelay(time), isLoop, isUnscaled);
            SetHandlerType(slotIndex, HANDLER_GENERIC);
            SetGenericInvoker(slotIndex, TimerGenericInvokerCache<T>.Invoke);
            SetGenericHandler(slotIndex, callback);
            SetGenericArg(slotIndex, arg);
            AddActive(slotIndex);
            AddToQueue(slotIndex, isUnscaled);
            return GetHandle(slotIndex);
        }

#if UNITY_EDITOR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WarnAddTimerFailed(string reason)
        {
            LogUtility.Warning("[Timer] AddTimer failed: {0}", reason);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void Stop(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void Resume(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0 || (GetState(slotIndex) & STATE_RUNNING) != 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            double delay = GetRemainingTime(slotIndex);
            if (delay <= MINIMUM_DELAY_SECONDS)
            {
                delay = MINIMUM_DELAY_SECONDS;
            }

            SetTriggerTime(slotIndex, GetCurrentTime(isUnscaled) + delay);
            SetRemainingTime(slotIndex, 0d);
            SetState(slotIndex, STATE_RUNNING);
            AddToQueue(slotIndex, isUnscaled);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool IsRunning(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            return slotIndex >= 0 && (GetState(slotIndex) & STATE_RUNNING) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override float GetLeftTime(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
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
        internal override void Restart(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex < 0)
            {
                return;
            }

            bool isUnscaled = IsUnscaled(slotIndex);
            if (GetQueueIndex(slotIndex) >= 0)
            {
                RemoveFromQueue(slotIndex, isUnscaled);
            }

            SetTriggerTime(slotIndex, GetCurrentTime(isUnscaled) + GetDuration(slotIndex));
            SetRemainingTime(slotIndex, 0d);
            SetState(slotIndex, STATE_RUNNING);
            AddToQueue(slotIndex, isUnscaled);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void RemoveTimer(ulong timerHandle)
        {
            int slotIndex = GetSlotIndex(timerHandle);
            if (slotIndex >= 0)
            {
                ReleaseSlot(slotIndex);
            }
        }

        internal override void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            activeCount = _activeCount;
            poolCapacity = _slotCapacity;
            peakActiveCount = _peakActiveCount;
            freeCount = _freeCount;
        }

        internal override int GetAllTimers(TimerDebugInfo[] results)
        {
            if (results == null || results.Length == 0)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = Time.timeAsDouble;
            double unscaledTime = Time.unscaledTimeAsDouble;
#if UNITY_EDITOR
            double realtime = Time.realtimeSinceStartupAsDouble;
#else
            const double realtime = 0d;
#endif
            int limit = results.Length;
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                FillDebugInfo(GetActiveSlot(i), ref results[count], scaledTime, unscaledTime, realtime);
                count++;
            }

            return count;
        }

#if UNITY_EDITOR
        internal override int GetStaleOneShotTimers(TimerDebugInfo[] results)
        {
            if (results == null || results.Length == 0)
            {
                return 0;
            }

            int count = 0;
            double scaledTime = Time.timeAsDouble;
            double unscaledTime = Time.unscaledTimeAsDouble;
            double realtime = Time.realtimeSinceStartupAsDouble;
            int limit = results.Length;
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                int slotIndex = GetActiveSlot(i);
                if (IsLoop(slotIndex) || realtime - GetCreationTime(slotIndex) <= STALE_ONE_SHOT_SECONDS)
                {
                    continue;
                }

                FillDebugInfo(slotIndex, ref results[count], scaledTime, unscaledTime, realtime);
                count++;
            }

            return count;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AcquireSlot()
        {
            if (_freeCount <= 0)
            {
                AddPage();
                if (_freeCount <= 0)
                {
                    return INVALID_INDEX;
                }
            }

            return GetFreeSlot(--_freeCount);
        }

        private void AddPage()
        {
            if (_pageCount >= MAX_PAGE_COUNT)
            {
                return;
            }

            EnsureIndexPage(_freeSlotPages, _pageCount);
            EnsureIndexPage(_activeSlotPages, _pageCount);

            TimerPage page = new TimerPage();
            _pages[_pageCount] = page;

            int baseSlotIndex = _pageCount << PAGE_SHIFT;
            for (int i = 0; i < PAGE_SIZE; i++)
            {
                SetFreeSlot(_freeCount++, baseSlotIndex + i);
            }

            _pageCount++;
            _slotCapacity += PAGE_SIZE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureIndexPage(IntPage[] pages, int pageIndex)
        {
            if (pages[pageIndex] == null)
            {
                pages[pageIndex] = new IntPage();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFreeSlot(int index)
        {
            return GetPagedInt(_freeSlotPages, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFreeSlot(int index, int value)
        {
            SetPagedInt(_freeSlotPages, index, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetActiveSlot(int index)
        {
            return GetPagedInt(_activeSlotPages, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetActiveSlot(int index, int value)
        {
            SetPagedInt(_activeSlotPages, index, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPagedInt(IntPage[] pages, int index)
        {
            return pages[index >> PAGE_SHIFT].Values[index & PAGE_MASK];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetPagedInt(IntPage[] pages, int index, int value)
        {
            pages[index >> PAGE_SHIFT].Values[index & PAGE_MASK] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeSlot(int slotIndex, double duration, bool isLoop, bool isUnscaled)
        {
            int pageIndex = slotIndex >> PAGE_SHIFT;
            int offset = slotIndex & PAGE_MASK;
            TimerPage page = _pages[pageIndex];
            uint version = page.Versions[offset] + 1U;
            page.Versions[offset] = version == 0U ? 1U : version;
            page.Handles[offset] = ComposeHandle(slotIndex, page.Versions[offset]);
            page.States[offset] = ComposeState(isLoop, isUnscaled);
            page.TriggerTimes[offset] = GetCurrentTime(isUnscaled) + duration;
            page.Durations[offset] = duration;
            page.RemainingTimes[offset] = 0d;
#if UNITY_EDITOR
            page.CreationTimes[offset] = Time.realtimeSinceStartupAsDouble;
#endif
            page.DueTicks[offset] = 0L;
            page.QueueIndices[offset] = INVALID_INDEX;
            page.QueueNextIndices[offset] = INVALID_INDEX;
            page.QueuePrevIndices[offset] = INVALID_INDEX;
            page.ActiveIndices[offset] = INVALID_INDEX;
            page.HandlerTypes[offset] = HANDLER_NONE;
            page.NoArgsHandlers[offset] = null;
            page.GenericInvokers[offset] = null;
            page.GenericHandlers[offset] = null;
            page.GenericArgs[offset] = null;
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
        private static ulong ComposeHandle(int slotIndex, uint version)
        {
            return ((ulong)version << 32) | (uint)(slotIndex + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSlotIndex(ulong timerHandle)
        {
            int slotIndex = (int)((timerHandle & 0xffffffffUL) - 1UL);
            if ((uint)slotIndex >= (uint)_slotCapacity || GetHandle(slotIndex) != timerHandle || (GetState(slotIndex) & STATE_ACTIVE) == 0)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            SetHandle(slotIndex, 0UL);

            if (slotIndex == _executingSlotIndex)
            {
                SetStateRaw(slotIndex, STATE_RELEASE_PENDING);
                return;
            }

            SetStateRaw(slotIndex, 0);
            SetFreeSlot(_freeCount++, slotIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearHandler(int slotIndex)
        {
            SetHandlerType(slotIndex, HANDLER_NONE);
            SetNoArgsHandler(slotIndex, null);
            SetGenericInvoker(slotIndex, null);
            SetGenericHandler(slotIndex, null);
            SetGenericArg(slotIndex, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FreeReleasedExecutingSlot(int slotIndex)
        {
            SetStateRaw(slotIndex, 0);
            SetFreeSlot(_freeCount++, slotIndex);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessDueTimer(int slotIndex, double currentTime)
        {
            byte state = GetState(slotIndex);
            if ((state & (STATE_ACTIVE | STATE_RUNNING)) != (STATE_ACTIVE | STATE_RUNNING))
            {
                return;
            }

            _executingSlotIndex = slotIndex;

            try
            {
                byte handlerType = GetHandlerType(slotIndex);
                if (handlerType == HANDLER_NO_ARGS)
                {
                    GetNoArgsHandler(slotIndex).Invoke();
                }
                else if (handlerType == HANDLER_GENERIC)
                {
                    GetGenericInvoker(slotIndex).Invoke(GetGenericHandler(slotIndex), GetGenericArg(slotIndex));
                }
            }
            catch (Exception exception)
            {
                LogUtility.Fatal(exception);
            }
            finally
            {
                _executingSlotIndex = INVALID_INDEX;
            }

            state = GetState(slotIndex);
            if ((state & STATE_RELEASE_PENDING) != 0)
            {
                FreeReleasedExecutingSlot(slotIndex);
                return;
            }

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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RescheduleLoop(int slotIndex, double currentTime)
        {
            double duration = GetDuration(slotIndex);
            double triggerTime = GetTriggerTime(slotIndex) + duration;
            if (triggerTime <= currentTime)
            {
                triggerTime = currentTime + duration;
            }

            SetTriggerTime(slotIndex, triggerTime);
            AddToQueue(slotIndex, IsUnscaled(slotIndex));
        }

        private void AdvanceQueue(bool isUnscaled, double currentTime)
        {
            long currentTick = TimeToTickFloor(currentTime);
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
        private static double GetCurrentTime(bool isUnscaled)
        {
            return isUnscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
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
            return (long)(time * TICKS_PER_SECOND);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long TimeToTickCeiling(double time)
        {
            return (long)Math.Ceiling(time * TICKS_PER_SECOND);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NormalizeDelay(float delay)
        {
            return delay > MINIMUM_DELAY_SECONDS ? delay : MINIMUM_DELAY_SECONDS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NormalizeCapacity(int capacity)
        {
            int normalizedCapacity = capacity > PAGE_SIZE ? capacity : PAGE_SIZE;
            int remainder = normalizedCapacity & PAGE_MASK;
            if (remainder != 0)
            {
                normalizedCapacity += PAGE_SIZE - remainder;
            }

            return normalizedCapacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimerPage GetPage(int slotIndex)
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
            TimerPage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] | mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearState(int slotIndex, byte mask)
        {
            TimerPage page = GetPage(slotIndex);
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
        private void FillDebugInfo(int slotIndex, ref TimerDebugInfo info, double scaledTime, double unscaledTime, double realtime)
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
    }
}
