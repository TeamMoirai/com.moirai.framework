using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务契约（策略模式契约接口）：计时器后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 默认实现为 <see cref="DefaultTimerHandler"/>（四级时间轮 + 帧计时 + 阶段触发），可在 <see cref="TimerServiceSettings"/> 中替换。 <br />
    /// 实现约束：实现类须继承 <see cref="Moirai.Atropos.FrameworkHandler"/>（生命周期与序列化基座——<c>Internal_Init</c>/<c>Internal_Shutdown</c> 由宿主生成代码强制调用）； <br />
    /// 跨后端语义约定：句柄 0 一律无效；<c>GetLeftFrames</c> 对时间计时器返回 0、<c>GetLeftTime</c> 对帧计时器返回 0； <br />
    /// <c>WaitAsync</c> 的轮询实现语义：<c>UniTask.WaitUntil</c> 挂 <see cref="IsDone"/>，完成/取消在引擎本阶段 Tick 末尾统一唤醒，Shutdown 时同步排空。 <br />
    /// <see cref="GetStatistics"/>/<see cref="GetAllTimers"/>（与编辑器 <see cref="GetStaleOneShotTimers"/>）是调试面，服务壳的调试窗口经宿主转发调用。
    /// </remarks>
    public interface ITimerService
    {
        #region 阶段驱动 [PHASE TICK]

        /// <summary>
        /// Update 阶段轮询——推进时间轮、处理 Update 阶段帧计时与进度回调。
        /// </summary>
        void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// FixedUpdate 阶段轮询。
        /// </summary>
        void FixedTick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// LateUpdate 阶段轮询。
        /// </summary>
        void LateTick(float elapseSeconds, float realElapseSeconds);

        #endregion 阶段驱动 [PHASE TICK]

        #region 调度注册 [SCHEDULE]

        /// <summary>
        /// 延迟 <paramref name="delaySeconds"/> 秒后回调。
        /// </summary>
        /// <param name="onComplete">完成回调（热路径请用缓存方法组，禁止捕获闭包）。</param>
        /// <returns>计时器句柄；无效时为 0。</returns>
        ulong Delay(float delaySeconds, Action onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>
        /// 延迟后带参回调（泛型参数注入）。
        /// </summary>
        ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) where T : class;

        /// <summary>
        /// 延迟并在到期前每帧上报进度（0..1）。<paramref name="onComplete"/> 可为 null。
        /// </summary>
        ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>
        /// 延迟（零分配函数指针绑定）。
        /// </summary>
        ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>
        /// 等待 <paramref name="frames"/> 帧后回调。
        /// </summary>
        ulong WaitFrame(int frames, Action onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>
        /// 等待 N 帧，每帧回调累计帧数（从 1 起）。
        /// </summary>
        ulong WaitFrame(int frames, Action<int> onUpdate,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update);

        /// <summary>
        /// 帧等待（零分配函数指针绑定）。
        /// </summary>
        ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update);

        #endregion 调度注册 [SCHEDULE]

        #region 句柄控制 [HANDLE CONTROL]

        /// <summary>
        /// 暂停（保留剩余时间，可 <see cref="Resume"/>）。
        /// </summary>
        void Pause(ulong timerHandle);

        /// <summary>
        /// 暂停全部计时器。
        /// </summary>
        void PauseAll();

        /// <summary>
        /// 恢复。
        /// </summary>
        void Resume(ulong timerHandle);

        /// <summary>
        /// 恢复全部计时器。
        /// </summary>
        void ResumeAll();

        /// <summary>
        /// 按原时长重启。
        /// </summary>
        void Restart(ulong timerHandle);

        /// <summary>
        /// 取消（释放槽位，句柄随之失效）。
        /// </summary>
        void Cancel(ulong timerHandle);

        /// <summary>
        /// 取消全部计时器。
        /// </summary>
        void CancelAll();

        #endregion 句柄控制 [HANDLE CONTROL]

        #region 状态查询 [QUERY]

        /// <summary>
        /// 是否在运行（有效且未暂停）。
        /// </summary>
        bool IsRunning(ulong timerHandle);

        /// <summary>
        /// 剩余秒数（帧计时器返回 0）。
        /// </summary>
        float GetLeftTime(ulong timerHandle);

        /// <summary>
        /// 剩余帧数（时间计时器返回 0）。默认 0，供仅支持秒的自定义后端免实现。
        /// </summary>
        int GetLeftFrames(ulong timerHandle) => 0;

        /// <summary>
        /// 是否已结束（完成 / 取消 / 无效）。
        /// </summary>
        bool IsDone(ulong timerHandle);

        /// <summary>
        /// 已流逝量：时间计时器为秒，帧计时器为帧。
        /// </summary>
        float GetElapsed(ulong timerHandle);

        /// <summary>
        /// 总时长：时间计时器为秒，帧计时器为帧。
        /// </summary>
        float GetDuration(ulong timerHandle);

        /// <summary>
        /// 等待计时器完成。
        /// </summary>
        /// <remarks>
        /// 默认实现每帧轮询 <see cref="IsDone"/>；<see cref="DefaultTimerHandler"/> 覆写为按槽位完成信号驱动。 <br />
        /// 完成 / 取消在引擎本阶段 Tick 末尾统一唤醒；Shutdown 时同步排空，避免 await 方永久挂起。
        /// </remarks>
        UniTask WaitAsync(ulong timerHandle, CancellationToken cancellationToken = default)
        {
            if (timerHandle == 0UL || IsDone(timerHandle))
            {
                return UniTask.CompletedTask;
            }

            // 状态经元组传入 + 静态谓词：轮询在整个 await 期间每帧求值，不留闭包分配。
            return UniTask.WaitUntil((engine: this, handle: timerHandle), static s => s.engine.IsDone(s.handle),
                cancellationToken: cancellationToken);
        }

        #endregion 状态查询 [QUERY]

        #region 统计 / 调试 [STATS & DEBUG]

        /// <summary>
        /// 获取计时器统计信息。
        /// </summary>
        void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

        /// <summary>
        /// 获取所有计时器调试信息。
        /// </summary>
        int GetAllTimers(TimerDebugInfo[] results);

#if UNITY_EDITOR
        /// <summary>
        /// 获取存活超过 300 秒的一次性计时器（僵尸检测）。
        /// </summary>
        int GetStaleOneShotTimers(TimerDebugInfo[] results);
#endif

        #endregion 统计 / 调试 [STATS & DEBUG]
    }
}
