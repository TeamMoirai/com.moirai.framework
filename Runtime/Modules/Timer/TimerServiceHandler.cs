using System;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器处理器抽象基类（策略模式抽象策略）。
    /// <para>默认实现为 <see cref="DefaultTimerHandler"/>（四级时间轮算法）。</para>
    /// <para>无全量扫描 · 精准承载技能 CD / 心跳 / 延时任务</para>
    /// 可在 <see cref="TimerServiceSettings"/> 中替换为自定义实现。
    /// </summary>
    [Serializable]
    public abstract class TimerServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 每帧轮询——推进时间轮并触发到期计时器。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        internal abstract void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 添加计时器（Action 式）。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">延迟时间（秒）。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否使用不受时间缩放影响的时间。</param>
        /// <returns>计时器句柄。</returns>
        internal abstract ulong AddTimer(Action callback, float time, bool isLoop = false, bool isUnscaled = false);

        /// <summary>
        /// 添加计时器（泛型式）。
        /// </summary>
        /// <typeparam name="T">回调参数类型。</typeparam>
        /// <param name="callback">计时器回调。</param>
        /// <param name="arg">回调参数。</param>
        /// <param name="time">延迟时间（秒）。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否使用不受时间缩放影响的时间。</param>
        /// <returns>计时器句柄。</returns>
        internal abstract ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class;

        /// <summary>
        /// 暂停计时器。
        /// </summary>
        /// <param name="timerId">计时器句柄。</param>
        internal abstract void Stop(ulong timerId);

        /// <summary>
        /// 恢复计时器。
        /// </summary>
        /// <param name="timerId">计时器句柄。</param>
        internal abstract void Resume(ulong timerId);

        internal abstract bool IsRunning(ulong timerHandle);
        internal abstract float GetLeftTime(ulong timerHandle);

        /// <summary>
        /// 重启计时器。
        /// </summary>
        /// <param name="timerId">计时器句柄。</param>
        internal abstract void Restart(ulong timerId);

        /// <summary>
        /// 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器句柄。</param>
        internal abstract void RemoveTimer(ulong timerId);

        /// <summary>
        /// 获取计时器统计信息。
        /// </summary>
        /// <param name="activeCount">活跃计时器数。</param>
        /// <param name="poolCapacity">池容量。</param>
        /// <param name="peakActiveCount">峰值活跃数。</param>
        /// <param name="freeCount">空闲槽位数。</param>
        internal abstract void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

        /// <summary>
        /// 获取所有计时器调试信息。
        /// </summary>
        /// <param name="results">结果数组。</param>
        /// <returns>填充的计时器数量。</returns>
        internal abstract int GetAllTimers(TimerDebugInfo[] results);

#if UNITY_EDITOR
        /// <summary>
        /// 获取长寿命一次性计时器。
        /// </summary>
        /// <param name="results">结果数组。</param>
        /// <returns>存活超过 300 秒的一次性计时器数量。</returns>
        /// <remarks>用于检测“僵尸”计时器（可能因逻辑错误未释放），帮助开发者定位泄漏。</remarks>
        internal abstract int GetStaleOneShotTimers(TimerDebugInfo[] results);
#endif
    }
}
