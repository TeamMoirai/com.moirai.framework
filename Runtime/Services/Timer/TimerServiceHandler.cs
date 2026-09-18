using System;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器处理器抽象基类（策略模式抽象策略）。
    /// <para>默认实现为 <see cref="DefaultTimerHandler"/>（四级时间轮 + 帧计时 + 阶段触发）。</para>
    /// <para>对外 API：<c>Delay</c> / <c>DelayUnsafe</c> / <c>WaitFrame</c> / <c>WaitFrameUnsafe</c> /
    /// <c>Cancel</c> / <c>Pause</c> / <c>Resume</c> / <c>IsDone</c>。</para>
    /// <para>可在 <see cref="TimerServiceSettings"/> 中替换为自定义实现。</para>
    /// </summary>
    [Serializable]
    public abstract class TimerServiceHandler : FrameworkHandler
    {
        /// <summary>Update 阶段轮询——推进时间轮、处理 Update 阶段帧计时与进度回调。</summary>
        internal abstract void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>FixedUpdate 阶段轮询。</summary>
        internal abstract void FixedTick(float elapseSeconds, float realElapseSeconds);

        /// <summary>LateUpdate 阶段轮询。</summary>
        internal abstract void LateTick(float elapseSeconds, float realElapseSeconds);

        /// <summary>延迟 <paramref name="delaySeconds"/> 秒后回调。</summary>
        internal abstract ulong Delay(float delaySeconds, Action onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>延迟后带参回调（泛型参数注入）。</summary>
        internal abstract ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) where T : class;

        /// <summary>延迟并在到期前每帧上报进度（0..1）。<paramref name="onComplete"/> 可为 null。</summary>
        internal abstract ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>延迟（零分配函数指针绑定）。</summary>
        internal abstract ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>等待 <paramref name="frames"/> 帧后回调。</summary>
        internal abstract ulong WaitFrame(int frames, Action onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>等待 N 帧，每帧回调累计帧数（从 1 起）。</summary>
        internal abstract ulong WaitFrame(int frames, Action<int> onUpdate,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>帧等待（零分配函数指针绑定）。</summary>
        internal abstract ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>暂停（保留剩余时间，可 <c>Resume</c>）。</summary>
        internal abstract void Pause(ulong timerHandle);

        /// <summary>暂停全部计时器。</summary>
        internal abstract void PauseAll();

        /// <summary>恢复。</summary>
        internal abstract void Resume(ulong timerHandle);

        /// <summary>恢复全部计时器。</summary>
        internal abstract void ResumeAll();

        /// <summary>是否在运行（有效且未暂停）。</summary>
        internal abstract bool IsRunning(ulong timerHandle);

        /// <summary>剩余秒数（帧计时器返回 0）。</summary>
        internal abstract float GetLeftTime(ulong timerHandle);

        /// <summary>是否已结束（完成 / 取消 / 无效）。</summary>
        internal abstract bool IsDone(ulong timerHandle);

        /// <summary>已流逝量：时间计时器为秒，帧计时器为帧。</summary>
        internal abstract float GetElapsed(ulong timerHandle);

        /// <summary>总时长：时间计时器为秒，帧计时器为帧。</summary>
        internal abstract float GetDuration(ulong timerHandle);

        /// <summary>按原时长重启。</summary>
        internal abstract void Restart(ulong timerHandle);

        /// <summary>取消（释放槽位，句柄随之失效）。</summary>
        internal abstract void Cancel(ulong timerHandle);

        /// <summary>取消全部计时器。</summary>
        internal abstract void CancelAll();

        /// <summary>获取计时器统计信息。</summary>
        internal abstract void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

        /// <summary>获取所有计时器调试信息。</summary>
        internal abstract int GetAllTimers(TimerDebugInfo[] results);

#if UNITY_EDITOR
        /// <summary>获取存活超过 300 秒的一次性计时器（僵尸检测）。</summary>
        internal abstract int GetStaleOneShotTimers(TimerDebugInfo[] results);
#endif
    }
}
