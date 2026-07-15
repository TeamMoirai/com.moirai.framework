using System;

namespace Moirai.Atropos.Timer
{
    public delegate void TimerHandler(object[] args);

    public struct TimerDebugInfo
    {
        public ulong timerHandle;
        public float leftTime;
        public float duration;
        public float age;
        public byte flags;
    }

    public static class TimerDebugFlags
    {
        public const byte RUNNING = 1 << 0;
        public const byte LOOP = 1 << 1;
        public const byte UNSCALED = 1 << 2;
    }

    public interface ITimerModule
    {
        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <param name="args">传参。(避免闭包)</param>
        /// <returns>计时器Id。</returns>
        ulong AddTimer(TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false, params object[] args);

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <returns>计时器Id。</returns>
        ulong AddTimer(Action callback, float time, bool isLoop = false, bool isUnscaled = false);

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="arg">传参。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <returns>计时器Id。</returns>
        ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class;

        /// <summary>
        /// 暂停计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        void Stop(ulong timerId);

        /// <summary>
        /// 恢复计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        void Resume(ulong timerId);

        /// <summary>
        /// 计时器是否在运行中。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        /// <returns>是否在运行中。</returns>
        void Restart(ulong timerId);

        /// <summary>
        /// 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        void RemoveTimer(ulong timerId);

        /// <summary>
        /// 预热。
        /// </summary>
        /// <param name="capacity">容量。</param>
        void Prewarm(int capacity);

        /// <summary>
        /// 获取计时器统计。
        /// </summary>
        /// <param name="activeCount">活跃的数量。</param>
        /// <param name="poolCapacity">池容量。</param>
        /// <param name="peakActiveCount">峰值活跃数量。</param>
        /// <param name="freeCount">空闲数量。</param>
        void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

        /// <summary>
        /// 移除所有计时器。
        /// </summary>
        int GetAllTimers(TimerDebugInfo[] results);
    }
}
