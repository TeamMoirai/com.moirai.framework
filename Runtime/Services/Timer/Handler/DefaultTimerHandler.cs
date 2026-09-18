using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器处理器默认实现——两套独立引擎的复合外观（Composite）。
    /// <para>时间轮（<see cref="WheelTimerEngine"/>，按秒）与帧计时（<see cref="FrameTimerEngine"/>，按帧）
    /// 各占一条泳道，自持独立的槽位池与句柄命名空间，互不知晓、互不糅合。</para>
    /// <para>本类只负责：创建时按语义落到对应引擎；句柄操作按 <see cref="TimerHandleLayout.LaneOf"/> 内嵌泳道号路由；
    /// 阶段推进与统计调试跨引擎扇出 / 聚合。对外 API 保持不变（<c>Delay</c> / <c>WaitFrame</c> / <c>Cancel</c> /
    /// <c>Pause</c> / <c>Resume</c>）。</para>
    /// <para>可在 <see cref="TimerServiceSettings"/> 中替换为自定义实现。</para>
    /// </summary>
    [Serializable]
    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class DefaultTimerHandler : TimerServiceHandler
    {
        private const int MIN_INITIAL_CAPACITY = 256;

        [Min(MIN_INITIAL_CAPACITY)]
        [SerializeField] private int m_InitialCapacity = 1024;

        private readonly WheelTimerEngine _wheel = new WheelTimerEngine();
        private readonly FrameTimerEngine _frame = new FrameTimerEngine();

        // 泳道路由表：下标即 TimerLaneKinds.*，OnInit 构造，供句柄按位路由。
        private ITimerEngine[] _lanes;

        protected override void OnInit()
        {
            _lanes = new ITimerEngine[TimerLaneKinds.Count];
            _lanes[TimerLaneKinds.Wheel] = _wheel;
            _lanes[TimerLaneKinds.Frame] = _frame;

            int capacity = Mathf.Max(MIN_INITIAL_CAPACITY, m_InitialCapacity);
            _wheel.Init(capacity);
            _frame.Init(capacity);
        }

        protected override void OnShutdown()
        {
            _wheel.Shutdown();
            _frame.Shutdown();
        }

        #region 阶段驱动 [PHASE TICK]

        internal override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            _wheel.Tick();
            _frame.Tick();
        }

        internal override void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            _wheel.FixedTick();
            _frame.FixedTick();
        }

        internal override void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            _wheel.LateTick();
            _frame.LateTick();
        }

        #endregion

        #region 调度注册 [SCHEDULE]

        internal override ulong Delay(float delaySeconds, Action onComplete, bool isLooped = false,
            bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update)
        {
            return _wheel.Delay(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase);
        }

        internal override ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg, bool isLooped = false,
            bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) where T : class
        {
            return _wheel.Delay(delaySeconds, onComplete, arg, isLooped, ignoreTimeScale, phase);
        }

        internal override ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update)
        {
            return _wheel.Delay(delaySeconds, onComplete, onUpdate, isLooped, ignoreTimeScale, phase);
        }

        internal override ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete, bool isLooped = false,
            bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update)
        {
            return _wheel.DelayUnsafe(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase);
        }

        internal override ulong WaitFrame(int frames, Action onComplete, bool isLooped = false,
            TimerPhase phase = TimerPhase.Update)
        {
            return _frame.WaitFrame(frames, onComplete, isLooped, phase);
        }

        internal override ulong WaitFrame(int frames, Action<int> onUpdate, bool isLooped = false,
            TimerPhase phase = TimerPhase.Update)
        {
            return _frame.WaitFrame(frames, onUpdate, isLooped, phase);
        }

        internal override ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete, bool isLooped = false,
            TimerPhase phase = TimerPhase.Update)
        {
            return _frame.WaitFrameUnsafe(frames, onComplete, isLooped, phase);
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

        internal override void Pause(ulong timerHandle)
        {
            Lane(timerHandle)?.Pause(timerHandle);
        }

        internal override void Resume(ulong timerHandle)
        {
            Lane(timerHandle)?.Resume(timerHandle);
        }

        internal override void Restart(ulong timerHandle)
        {
            Lane(timerHandle)?.Restart(timerHandle);
        }

        internal override void Cancel(ulong timerHandle)
        {
            Lane(timerHandle)?.Cancel(timerHandle);
        }

        internal override void PauseAll()
        {
            _wheel.PauseAll();
            _frame.PauseAll();
        }

        internal override void ResumeAll()
        {
            _wheel.ResumeAll();
            _frame.ResumeAll();
        }

        internal override void CancelAll()
        {
            _wheel.CancelAll();
            _frame.CancelAll();
        }

        internal override bool IsRunning(ulong timerHandle)
        {
            return Lane(timerHandle)?.IsRunning(timerHandle) ?? false;
        }

        internal override bool IsDone(ulong timerHandle)
        {
            ITimerEngine lane = Lane(timerHandle);
            return lane == null || lane.IsDone(timerHandle);
        }

        internal override float GetLeftTime(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetLeftTime(timerHandle) ?? 0f;
        }

        internal override float GetElapsed(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetElapsed(timerHandle) ?? 0f;
        }

        internal override float GetDuration(ulong timerHandle)
        {
            return Lane(timerHandle)?.GetDuration(timerHandle) ?? 0f;
        }

        #endregion

        #region 统计 / 调试 [STATS & DEBUG]

        internal override void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount,
            out int freeCount)
        {
            _wheel.GetStatistics(out int wheelActive, out int wheelCapacity, out int wheelPeak, out int wheelFree);
            _frame.GetStatistics(out int frameActive, out int frameCapacity, out int framePeak, out int frameFree);
            activeCount = wheelActive + frameActive;
            poolCapacity = wheelCapacity + frameCapacity;
            peakActiveCount = wheelPeak + framePeak;
            freeCount = wheelFree + frameFree;
        }

        internal override int GetAllTimers(TimerDebugInfo[] results)
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
        internal override int GetStaleOneShotTimers(TimerDebugInfo[] results)
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
