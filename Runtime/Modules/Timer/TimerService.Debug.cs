namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器调试信息。
    /// </summary>
    public struct TimerDebugInfo
    {
        public ulong TimerHandle;
        public float LeftTime;
        public float Duration;
        public float Age;
        public byte Flags;
    }

    /// <summary>
    /// 计时器调试标志位。
    /// </summary>
    public static class TimerDebugFlags
    {
        public const byte RUNNING = 1 << 0;
        public const byte LOOP = 1 << 1;
        public const byte UNSCALED = 1 << 2;
    }

    public partial class TimerService
    {
        /// <summary>
        /// 获取计时器统计。
        /// </summary>
        /// <param name="activeCount">活跃的数量。</param>
        /// <param name="poolCapacity">池容量。</param>
        /// <param name="peakActiveCount">峰值活跃数量。</param>
        /// <param name="freeCount">空闲数量。</param>
        public static void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            if (s_Handler == null)
            {
                activeCount = 0;
                poolCapacity = 0;
                peakActiveCount = 0;
                freeCount = 0;
                return;
            }

            s_Handler.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
        }

        /// <summary>
        /// 获取所有计时器调试信息。
        /// </summary>
        /// <param name="results">结果缓冲区。</param>
        /// <returns>填充的数量。</returns>
        public static int GetAllTimers(TimerDebugInfo[] results) => s_Handler?.GetAllTimers(results) ?? 0;

#if UNITY_EDITOR
        /// <summary>
        /// 获取长寿命一次性计时器（“僵尸”计时器，存活超过 300 秒可能因逻辑错误未释放）。
        /// </summary>
        /// <param name="results">结果缓冲区。</param>
        /// <returns>符合条件的计时器数量。</returns>
        public static int GetStaleOneShotTimers(TimerDebugInfo[] results) => s_Handler?.GetStaleOneShotTimers(results) ?? 0;
#endif
    }
}