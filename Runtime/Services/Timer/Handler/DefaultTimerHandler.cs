using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器处理器默认实现：时间轮（按秒）与帧计时（按帧）两条泳道的复合外观（Composite）。
    /// </summary>
    /// <remarks>
    /// 两条泳道各自持有独立的槽位池与句柄命名空间；创建时按语义落到对应引擎，
    /// 句柄操作按 <see cref="TimerHandleLayout.LaneOf"/> 内嵌泳道号路由，阶段推进与统计调试跨引擎扇出 / 聚合。 <br />
    /// 可在 <see cref="TimerServiceSettings"/> 中替换为自定义实现。
    /// </remarks>
    [Serializable]
    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [HotPath]
    [ServiceDependency(typeof(IDebuggerService))]
    [ProviderDisplay(title: "默认计时器", description: "时间轮（按秒）+ 帧计时（按帧）双泳道，槽位容量可配")]
    internal sealed class DefaultTimerHandler : ServiceBase, IServiceTickable, IServiceFixedTickable, IServiceLateTickable, ITimerService
    {
        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;
        private const int MIN_INITIAL_CAPACITY = 256;
        private const int DEFAULT_WHEEL_CAPACITY = 1024;
        private const int DEFAULT_FRAME_CAPACITY = 256;

        [Min(MIN_INITIAL_CAPACITY)]
        [Tooltip("时间轮（Delay）引擎初始预热的槽位容量。")]
        [SerializeField] private int m_WheelInitialCapacity = DEFAULT_WHEEL_CAPACITY;

        [Min(MIN_INITIAL_CAPACITY)]
        [Tooltip("帧计时（WaitFrame）引擎初始预热的槽位容量；通常远小于时间轮。")]
        [SerializeField] private int m_FrameInitialCapacity = DEFAULT_FRAME_CAPACITY;

        private readonly WheelTimerEngine _wheel = new WheelTimerEngine();
        private readonly FrameTimerEngine _frame = new FrameTimerEngine();

        // 泳道路由表：下标即 TimerLaneKinds.*，OnInit 构造，供句柄按位路由。
        private ITimerEngine[] _lanes;

        // 跨引擎真实并发峰值：活跃总数仅在创建时上升，故每次创建后采样的最大值即真实峰值。
        private int _peakActiveCount;

        /// <summary>
        /// 处理器初始化。由容器在拓扑位驱动；容器编排（调试面板注册）以 Context 就位为闸随行。
        /// </summary>
        protected override void OnInit()
        {
            if (Context != null)
            {
                TimerService.OnHandlerInitialized(Context.GetRequiredService<IDebuggerService>());
            }

            _lanes = new ITimerEngine[TimerLaneKinds.Count];
            _lanes[TimerLaneKinds.Wheel] = _wheel;
            _lanes[TimerLaneKinds.Frame] = _frame;
            _peakActiveCount = 0;

            _wheel.Init(Mathf.Max(MIN_INITIAL_CAPACITY, m_WheelInitialCapacity));
            _frame.Init(Mathf.Max(MIN_INITIAL_CAPACITY, m_FrameInitialCapacity));
        }

        protected override void OnShutdown()
        {
            // 槽位失效只在真实容器拆除时发生（State 门）：handler 内部复用 OnShutdown 做引导重置时不清槽
            if (State >= EServiceState.ShuttingDown)
            {
                TimerService.InvalidateHandler(this);
            }
            // 编排收尾以就绪态为闸：世界销毁兜底会对未 OnInit 的服务也驱动 OnShutdown，此时依赖可能已先被拆走
            if (IsReady && Context != null)
            {
                TimerService.OnHandlerShuttingDown(Context.GetRequiredService<IDebuggerService>());
            }

            _wheel.Shutdown();
            _frame.Shutdown();
            // 摘掉路由表：Lane() 见 null 即整体降级，句柄操作不再进入已关停的引擎。
            _lanes = null;
        }

        #region 阶段驱动 [PHASE TICK]

        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (_lanes == null)
            {
                return;
            }

            _wheel.Tick();
            _frame.Tick();
        }

        public void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            if (_lanes == null)
            {
                return;
            }

            _wheel.FixedTick();
            _frame.FixedTick();
        }

        public void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            if (_lanes == null)
            {
                return;
            }

            _wheel.LateTick();
            _frame.LateTick();
        }

        #endregion

        #region 调度注册 [SCHEDULE]

        public ulong Delay(float delaySeconds, Action onComplete, bool isLooped = false,
            bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update)
        {
            ulong handle = _wheel.Delay(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase);
            SampleActivePeak();
            return handle;
        }

        public ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg, bool isLooped = false,
            bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) where T : class
        {
            ulong handle = _wheel.Delay(delaySeconds, onComplete, arg, isLooped, ignoreTimeScale, phase);
            SampleActivePeak();
            return handle;
        }

        public ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update)
        {
            ulong handle = _wheel.Delay(delaySeconds, onComplete, onUpdate, isLooped, ignoreTimeScale, phase);
            SampleActivePeak();
            return handle;
        }

        public ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete, bool isLooped = false,
            bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update)
        {
            ulong handle = _wheel.DelayUnsafe(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase);
            SampleActivePeak();
            return handle;
        }

        public ulong WaitFrame(int frames, Action onComplete, bool isLooped = false,
            TimerPhase phase = TimerPhase.Update)
        {
            ulong handle = _frame.WaitFrame(frames, onComplete, isLooped, phase);
            SampleActivePeak();
            return handle;
        }

        public ulong WaitFrame(int frames, Action<int> onUpdate, bool isLooped = false,
            TimerPhase phase = TimerPhase.Update)
        {
            ulong handle = _frame.WaitFrame(frames, onUpdate, isLooped, phase);
            SampleActivePeak();
            return handle;
        }

        public ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete, bool isLooped = false,
            TimerPhase phase = TimerPhase.Update)
        {
            ulong handle = _frame.WaitFrameUnsafe(frames, onComplete, isLooped, phase);
            SampleActivePeak();
            return handle;
        }

        // 活跃总数只在创建后上升、释放时下降，故每次创建采样即可捕获真实并发峰值（避免两引擎峰值相加的高估）。
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SampleActivePeak()
        {
            int total = _wheel.ActiveCount + _frame.ActiveCount;
            if (total > _peakActiveCount)
            {
                _peakActiveCount = total;
            }
        }

        #endregion

        #region 句柄控制 [HANDLE CONTROL]

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ITimerEngine Lane(ulong timerHandle)
        {
            ITimerEngine[] lanes = _lanes;
            if (timerHandle == 0UL || lanes == null)
            {
                return null;
            }

            byte lane = TimerHandleLayout.LaneOf(timerHandle);
            return lane < lanes.Length ? lanes[lane] : null;
        }

        public void Pause(ulong timerHandle)
        {
            Lane(timerHandle)?.Pause(timerHandle);
        }

        public void Resume(ulong timerHandle)
        {
            Lane(timerHandle)?.Resume(timerHandle);
        }

        public void Restart(ulong timerHandle)
        {
            Lane(timerHandle)?.Restart(timerHandle);
        }

        public void Cancel(ulong timerHandle)
        {
            Lane(timerHandle)?.Cancel(timerHandle);
        }

        public void PauseAll()
        {
            if (_lanes == null)
            {
                return;
            }

            _wheel.PauseAll();
            _frame.PauseAll();
        }

        public void ResumeAll()
        {
            if (_lanes == null)
            {
                return;
            }

            _wheel.ResumeAll();
            _frame.ResumeAll();
        }

        public void CancelAll()
        {
            if (_lanes == null)
            {
                return;
            }

            _wheel.CancelAll();
            _frame.CancelAll();
        }

        public UniTask WaitAsync(ulong timerHandle, CancellationToken cancellationToken = default)
        {
            ITimerEngine lane = Lane(timerHandle);
            return lane != null ? lane.WaitAsync(timerHandle, cancellationToken) : UniTask.CompletedTask;
        }

        public bool IsRunning(ulong timerHandle)
        {
            return Lane(timerHandle)?.IsRunning(timerHandle) ?? false;
        }

        public bool IsDone(ulong timerHandle)
        {
            ITimerEngine lane = Lane(timerHandle);
            return lane == null || lane.IsDone(timerHandle);
        }

        public float GetLeftTime(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetLeftTime(timerHandle) ?? 0f;
        }

        /// <inheritdoc />
        public int GetLeftFrames(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetLeftFrames(timerHandle) ?? 0;
        }

        public float GetElapsed(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetElapsed(timerHandle) ?? 0f;
        }

        public float GetDuration(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetDuration(timerHandle) ?? 0f;
        }

        #endregion

        #region 统计 / 调试 [STATS & DEBUG]

        public void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount,
            out int freeCount)
        {
            _wheel.GetStatistics(out int wheelActive, out int wheelCapacity, out _, out int wheelFree);
            _frame.GetStatistics(out int frameActive, out int frameCapacity, out _, out int frameFree);
            activeCount = wheelActive + frameActive;
            poolCapacity = wheelCapacity + frameCapacity;
            peakActiveCount = _peakActiveCount;
            freeCount = wheelFree + frameFree;
        }

        public int GetAllTimers(TimerDebugInfo[] results)
        {
            if (results == null || results.Length == 0)
            {
                return 0;
            }

            int count = _wheel.GetAllTimers(results, 0, results.Length);
            count += _frame.GetAllTimers(results, count, results.Length - count);
            return count;
        }

#if UNITY_EDITOR
        public int GetStaleOneShotTimers(TimerDebugInfo[] results)
        {
            if (results == null || results.Length == 0)
            {
                return 0;
            }

            int count = _wheel.GetStaleOneShotTimers(results, 0, results.Length);
            count += _frame.GetStaleOneShotTimers(results, count, results.Length - count);
            return count;
        }
#endif

        #endregion
    }
}
