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
    /// 帧计时引擎（泳道 <see cref="TimerLaneKinds.Frame"/>）：按帧递减、不占时间轮。
    /// <para>自持独立的分页槽位池、版本化句柄命名空间（句柄内嵌 Frame 泳道号）、完成回调派发与三阶段活跃列表；
    /// 完全不知晓时间轮的存在，因此不存在任何 <c>STATE_FRAME</c> 式跨引擎分支。</para>
    /// </summary>
    internal sealed class FrameTimerEngine : ITimerEngine
    {
#if UNITY_EDITOR
        private const double STALE_ONE_SHOT_SECONDS = 300d;
#endif

        private sealed class FramePage
        {
            public readonly ulong[] Handles = new ulong[PAGE_SIZE];
            public readonly uint[] Versions = new uint[PAGE_SIZE];
            public readonly byte[] States = new byte[PAGE_SIZE];
            public readonly byte[] HandlerTypes = new byte[PAGE_SIZE];
            public readonly double[] Durations = new double[PAGE_SIZE];
            public readonly byte[] Phases = new byte[PAGE_SIZE];
            public readonly Action[] NoArgsHandlers = new Action[PAGE_SIZE];
            public readonly TimerGenericInvoker[] GenericInvokers = new TimerGenericInvoker[PAGE_SIZE];
            public readonly object[] GenericHandlers = new object[PAGE_SIZE];
            public readonly object[] GenericArgs = new object[PAGE_SIZE];
            public readonly ulong[] UnsafeFnPtrs = new ulong[PAGE_SIZE];
            public readonly object[] UnsafeInstances = new object[PAGE_SIZE];
            public readonly Action<int>[] FrameProgressHandlers = new Action<int>[PAGE_SIZE];
            public readonly int[] FrameRemainings = new int[PAGE_SIZE];
            public readonly int[] FrameTargets = new int[PAGE_SIZE];
            public readonly int[] FrameListPositions = new int[PAGE_SIZE];
            public readonly int[] ActiveIndices = new int[PAGE_SIZE];
            // 按槽位挂载的完成信号（仅 await 时惰性创建；释放时唤醒并清空）。
            public readonly UniTaskCompletionSource[] WaitSignals = new UniTaskCompletionSource[PAGE_SIZE];
#if UNITY_EDITOR
            public readonly double[] CreationTimes = new double[PAGE_SIZE];
#endif

            public FramePage()
            {
                for (int i = 0; i < PAGE_SIZE; i++)
                {
                    ActiveIndices[i] = INVALID_INDEX;
                    FrameListPositions[i] = INVALID_INDEX;
                }
            }
        }

        private FramePage[] _pages;
        private IndexPage[] _freeSlotPages;
        private IndexPage[] _activeSlotPages;
        private int _pageCount;
        private int _slotCapacity;
        private int _freeCount;
        private int _activeCount;
        private int _peakActiveCount;
        private int _executingSlotIndex;

        // 按阶段各一条活跃列表（含每槽位列表下标，支持 O(1) 安全移除）。
        private List<int> _updateSlots;
        private List<int> _fixedSlots;
        private List<int> _lateSlots;
        private List<ulong> _scratchUpdate;
        private List<ulong> _scratchFixed;
        private List<ulong> _scratchLate;
        // 完成信号延迟唤醒队列 + 快照（在阶段 Tick 末尾排空，避免回调在释放调用栈内同步续跑）。
        private List<UniTaskCompletionSource> _deferredSignals;
        private List<UniTaskCompletionSource> _signalScratch;

        public void Init(int capacity)
        {
            _pages = new FramePage[MAX_PAGE_COUNT];
            _freeSlotPages = new IndexPage[MAX_PAGE_COUNT];
            _activeSlotPages = new IndexPage[MAX_PAGE_COUNT];
            _updateSlots = new List<int>(64);
            _fixedSlots = new List<int>(64);
            _lateSlots = new List<int>(64);
            _scratchUpdate = new List<ulong>(64);
            _scratchFixed = new List<ulong>(64);
            _scratchLate = new List<ulong>(64);
            _deferredSignals = new List<UniTaskCompletionSource>(16);
            _signalScratch = new List<UniTaskCompletionSource>(16);
            _pageCount = 0;
            _slotCapacity = 0;
            _freeCount = 0;
            _activeCount = 0;
            _peakActiveCount = 0;
            _executingSlotIndex = INVALID_INDEX;

            // 帧泳道按其自身（通常更小的）容量预热；超出部分仍按需 AddPage 扩容。
            Prewarm(NormalizeCapacity(capacity));
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

        public void Shutdown()
        {
            ClearAll();
            _pages = null;
            _freeSlotPages = null;
            _activeSlotPages = null;
            _updateSlots = null;
            _fixedSlots = null;
            _lateSlots = null;
            _scratchUpdate = null;
            _scratchFixed = null;
            _scratchLate = null;
            _deferredSignals = null;
            _signalScratch = null;
        }

        #region 注册 [REGISTER]

        internal ulong WaitFrame(int frames, Action onComplete, bool isLooped, TimerPhase phase)
        {
            if (onComplete == null || frames <= 0)
            {
                return 0UL;
            }

            return RegisterFrameTimer(onComplete, default, frames, isLooped, phase, null);
        }

        internal ulong WaitFrame(int frames, Action<int> onUpdate, bool isLooped, TimerPhase phase)
        {
            if (onUpdate == null || frames <= 0)
            {
                return 0UL;
            }

            return RegisterFrameTimer(null, default, frames, isLooped, phase, onUpdate);
        }

        internal unsafe ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete, bool isLooped, TimerPhase phase)
        {
            if (!onComplete.IsValid() || frames <= 0)
            {
                return 0UL;
            }

            return RegisterFrameTimer(null, onComplete, frames, isLooped, phase, null);
        }

        private unsafe ulong RegisterFrameTimer(Action callback, in TimerUnsafeBinding binding, int frames,
            bool isLooped, TimerPhase phase, Action<int> onFrameUpdate)
        {
            int slotIndex = AcquireSlot();
            if (slotIndex < 0)
            {
                return 0UL;
            }

            // 帧计时不占时间轮：duration 记为帧数（供 GetDuration/GetElapsed 使用）。
            InitializeSlot(slotIndex, frames, isLooped, phase);
            SetFrameTarget(slotIndex, frames);
            SetFrameRemaining(slotIndex, frames);

            if (callback != null)
            {
                SetHandlerType(slotIndex, HANDLER_NO_ARGS);
                SetNoArgsHandler(slotIndex, callback);
            }
            else
            {
                SetUnsafeComplete(slotIndex, binding);
            }

            if (onFrameUpdate != null)
            {
                SetFrameProgressHandler(slotIndex, onFrameUpdate);
                SetState(slotIndex, STATE_PROGRESS);
            }

            AddActive(slotIndex);
            AddToFrameList(slotIndex, phase);
            return GetHandle(slotIndex);
        }

        #endregion

        #region 阶段驱动 [PHASE TICK]

        public void Tick()
        {
            ProcessFrameTimers(_updateSlots, _scratchUpdate);
            DrainSignals();
        }

        public void FixedTick()
        {
            ProcessFrameTimers(_fixedSlots, _scratchFixed);
            DrainSignals();
        }

        public void LateTick()
        {
            ProcessFrameTimers(_lateSlots, _scratchLate);
            DrainSignals();
        }

        /// <summary>
        /// 逐帧推进指定阶段的帧计时器。
        /// <para>采用快照迭代（存句柄而非索引）：回调内取消/自释放，甚至释放槽位被同帧新建计时器复用，
        /// 都因 <see cref="GetSlotIndex"/> 的版本校验而天然安全——杜绝迭代期索引错乱与重复扣帧。</para>
        /// </summary>
        private void ProcessFrameTimers(List<int> live, List<ulong> scratch)
        {
            if (live.Count == 0)
            {
                return;
            }

            scratch.Clear();
            for (int i = 0; i < live.Count; i++)
            {
                scratch.Add(GetHandle(live[i]));
            }

            for (int k = 0; k < scratch.Count; k++)
            {
                int slotIndex = GetSlotIndex(scratch[k]);
                if (slotIndex < 0)
                {
                    continue; // 本轮已被释放/取消，或槽位已被复用（版本不符）
                }

                byte state = GetState(slotIndex);
                if ((state & STATE_RUNNING) == 0)
                {
                    continue; // 已暂停
                }

                int target = GetFrameTarget(slotIndex);
                int remaining = GetFrameRemaining(slotIndex) - 1;
                if (remaining < 0)
                {
                    remaining = 0;
                }

                SetFrameRemaining(slotIndex, remaining);
                InvokeFrameProgress(slotIndex, target - remaining);

                if (remaining > 0)
                {
                    continue;
                }

                InvokeComplete(slotIndex);

                state = GetState(slotIndex);
                if ((state & STATE_RELEASE_PENDING) != 0)
                {
                    FreeReleasedExecutingSlot(slotIndex); // ReleaseSlot 已从 live 移除，无需再处理
                    continue;
                }

                if ((state & STATE_ACTIVE) == 0)
                {
                    continue; // 回调内取消，已移除
                }

                if ((state & STATE_LOOP) != 0)
                {
                    SetFrameRemaining(slotIndex, target);
                    continue;
                }

                ReleaseSlot(slotIndex);
            }

            scratch.Clear();
        }

        #endregion

        #region 句柄控制 [HANDLE CONTROL]

        public void Pause(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return;
            }

            ClearState(slotIndex, STATE_RUNNING);
        }

        public void Resume(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return;
            }

            SetState(slotIndex, STATE_RUNNING);
        }

        public void Restart(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return;
            }

            SetFrameRemaining(slotIndex, GetFrameTarget(slotIndex));
            SetState(slotIndex, STATE_RUNNING);
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
                ClearState(GetActiveSlot(i), STATE_RUNNING);
            }
        }

        public void ResumeAll()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                SetState(GetActiveSlot(i), STATE_RUNNING);
            }
        }

        public void CancelAll()
        {
            ClearAll();
            _updateSlots.Clear();
            _fixedSlots.Clear();
            _lateSlots.Clear();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetLeftTime(ulong handle)
        {
            // 帧计时无“剩余秒数”语义。
            return 0f;
        }

        public int GetLeftFrames(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return 0;
            }

            int remaining = GetFrameRemaining(slotIndex);
            return remaining > 0 ? remaining : 0;
        }

        public float GetElapsed(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            if (slotIndex < 0)
            {
                return 0f;
            }

            int elapsedFrames = GetFrameTarget(slotIndex) - GetFrameRemaining(slotIndex);
            return elapsedFrames > 0 ? elapsedFrames : 0f;
        }

        public float GetDuration(ulong handle)
        {
            int slotIndex = GetSlotIndex(handle);
            return slotIndex < 0 ? 0f : (float)GetDuration(slotIndex);
        }

        #endregion

        #region 帧列表 [FRAME LIST]

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<int> GetFramePhaseList(TimerPhase phase)
        {
            switch (phase)
            {
                case TimerPhase.FixedUpdate:
                    return _fixedSlots;
                case TimerPhase.LateUpdate:
                    return _lateSlots;
                default:
                    return _updateSlots;
            }
        }

        private void AddToFrameList(int slotIndex, TimerPhase phase)
        {
            List<int> live = GetFramePhaseList(phase);
            SetFrameListPos(slotIndex, live.Count);
            live.Add(slotIndex);
        }

        private void RemoveFromFrameList(int slotIndex)
        {
            int pos = GetFrameListPos(slotIndex);
            if (pos < 0)
            {
                return;
            }

            List<int> live = GetFramePhaseList((TimerPhase)GetPhase(slotIndex));
            int last = live.Count - 1;
            if (pos != last)
            {
                int moved = live[last];
                live[pos] = moved;
                SetFrameListPos(moved, pos);
            }

            live.RemoveAt(last);
            SetFrameListPos(slotIndex, INVALID_INDEX);
        }

        private void InvokeFrameProgress(int slotIndex, int frameCount)
        {
            if ((GetState(slotIndex) & STATE_PROGRESS) == 0)
            {
                return;
            }

            Action<int> progress = GetFrameProgressHandler(slotIndex);
            if (progress == null)
            {
                return;
            }

            try
            {
                progress.Invoke(frameCount);
            }
            catch (Exception exception)
            {
                LogUtility.Fatal(exception);
            }
        }

        #endregion

        #region 完成回调派发 [COMPLETE INVOCATION]

        /// <summary>调用完成回调（异常隔离 + 执行槽标记）。</summary>
        private unsafe void InvokeComplete(int slotIndex)
        {
            _executingSlotIndex = slotIndex;
            InvokeCompleteBody(slotIndex);
            if (_executingSlotIndex == slotIndex)
            {
                _executingSlotIndex = INVALID_INDEX;
            }
        }

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
            double realtime = 0d;
#if UNITY_EDITOR
            realtime = UnityEngine.Time.realtimeSinceStartupAsDouble;
#endif
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                FillDebugInfo(GetActiveSlot(i), ref results[offset + count], realtime);
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
            double realtime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < _activeCount && count < limit; i++)
            {
                int slotIndex = GetActiveSlot(i);
                if (IsLoop(slotIndex) || realtime - GetCreationTime(slotIndex) <= STALE_ONE_SHOT_SECONDS)
                {
                    continue;
                }

                FillDebugInfo(slotIndex, ref results[offset + count], realtime);
                count++;
            }

            return count;
        }
#endif

        private void FillDebugInfo(int slotIndex, ref TimerDebugInfo info, double realtime)
        {
            byte state = GetState(slotIndex);
            byte flags = 0;
            if ((state & STATE_RUNNING) != 0)
            {
                flags |= TimerDebugFlags.RUNNING;
            }

            if ((state & STATE_LOOP) != 0)
            {
                flags |= TimerDebugFlags.LOOP;
            }

            info.TimerHandle = GetHandle(slotIndex);
            info.LeftTime = 0f;
            info.Duration = (float)GetDuration(slotIndex);
#if UNITY_EDITOR
            info.Age = (float)(realtime - GetCreationTime(slotIndex));
#else
            info.Age = 0f;
#endif
            info.Flags = flags;
        }

        #endregion

        #region 槽位池 [SLOT POOL]

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

            FramePage page = new FramePage();
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

        private void InitializeSlot(int slotIndex, int frames, bool isLoop, TimerPhase phase)
        {
            int pageIndex = slotIndex >> PAGE_SHIFT;
            int offset = slotIndex & PAGE_MASK;
            FramePage page = _pages[pageIndex];
            uint version = page.Versions[offset] + 1U;
            page.Versions[offset] = version == 0U ? 1U : version;
            page.Handles[offset] = TimerHandleLayout.Compose(version, TimerLaneKinds.Frame, slotIndex);
            page.States[offset] = ComposeState(isLoop);
            page.Durations[offset] = frames;
#if UNITY_EDITOR
            page.CreationTimes[offset] = UnityEngine.Time.realtimeSinceStartupAsDouble;
#endif
            page.HandlerTypes[offset] = HANDLER_NONE;
            page.NoArgsHandlers[offset] = null;
            page.GenericInvokers[offset] = null;
            page.GenericHandlers[offset] = null;
            page.GenericArgs[offset] = null;
            page.UnsafeFnPtrs[offset] = 0UL;
            page.UnsafeInstances[offset] = null;
            page.FrameProgressHandlers[offset] = null;
            page.Phases[offset] = (byte)phase;
            page.FrameRemainings[offset] = 0;
            page.FrameTargets[offset] = 0;
            page.FrameListPositions[offset] = INVALID_INDEX;
            page.ActiveIndices[offset] = INVALID_INDEX;
            page.WaitSignals[offset] = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComposeState(bool isLoop)
        {
            byte state = (byte)(STATE_ACTIVE | STATE_RUNNING);
            if (isLoop)
            {
                state |= STATE_LOOP;
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

            RemoveActive(slotIndex);
            RemoveFromFrameList(slotIndex);
            ClearHandler(slotIndex);
            SetDuration(slotIndex, 0d);
#if UNITY_EDITOR
            SetCreationTime(slotIndex, 0d);
#endif

            // 句柄须在 SetHandle(0UL) 销毁前用于快照；executing 早返回前必须完成列表归属清理。
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
            SetFrameProgressHandler(slotIndex, null);
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

            _executingSlotIndex = INVALID_INDEX;
        }

        #endregion

        #region 存取器 [ACCESSORS]

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FramePage GetPage(int slotIndex)
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
            FramePage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] | mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearState(int slotIndex, byte mask)
        {
            FramePage page = GetPage(slotIndex);
            int offset = GetOffset(slotIndex);
            page.States[offset] = (byte)(page.States[offset] & ~mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetStateRaw(int slotIndex, byte value)
        {
            GetPage(slotIndex).States[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLoop(int slotIndex)
        {
            return (GetState(slotIndex) & STATE_LOOP) != 0;
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
        private byte GetPhase(int slotIndex)
        {
            return GetPage(slotIndex).Phases[GetOffset(slotIndex)];
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
        private Action<int> GetFrameProgressHandler(int slotIndex)
        {
            return GetPage(slotIndex).FrameProgressHandlers[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFrameProgressHandler(int slotIndex, Action<int> value)
        {
            GetPage(slotIndex).FrameProgressHandlers[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFrameRemaining(int slotIndex)
        {
            return GetPage(slotIndex).FrameRemainings[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFrameRemaining(int slotIndex, int value)
        {
            GetPage(slotIndex).FrameRemainings[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFrameTarget(int slotIndex)
        {
            return GetPage(slotIndex).FrameTargets[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFrameTarget(int slotIndex, int value)
        {
            GetPage(slotIndex).FrameTargets[GetOffset(slotIndex)] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFrameListPos(int slotIndex)
        {
            return GetPage(slotIndex).FrameListPositions[GetOffset(slotIndex)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFrameListPos(int slotIndex, int value)
        {
            GetPage(slotIndex).FrameListPositions[GetOffset(slotIndex)] = value;
        }

        #endregion
    }
}
