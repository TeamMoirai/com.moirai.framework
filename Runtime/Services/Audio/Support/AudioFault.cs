using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频侧的异常上报与退避（内部）。
    /// <para>容器的 tick 隔离在开发构建下是「记录后重新抛出并打断整轮 tick」
    /// （<c>ServiceScope</c> 的 rethrow 开关），所以音频内部的隔离必须自己做：
    /// 一条音出问题不该让输入、UI、存档当帧停摆。</para>
    /// <para>策略是退避而不是熔断：抛一次不会把音频服务踢出轮询（那会变成"音频默默不再更新"这种
    /// 更难查的事故），只是同一位置在 <see cref="BackoffSeconds"/> 内不再重复打印，并累计被吞掉的次数。</para>
    /// </summary>
    internal static class AudioFault
    {
        /// <summary>同一位置的重复上报间隔（秒）。</summary>
        public const float BackoffSeconds = 5f;

        private static readonly Dictionary<string, int> s_Swallowed = new Dictionary<string, int>(8);
        private static readonly Dictionary<string, float> s_NextReportAt = new Dictionary<string, float>(8);

        /// <summary>
        /// 上报一处被隔离掉的异常。同一 <paramref name="where"/> 在退避窗口内只累加计数，不重复打印。
        /// </summary>
        /// <param name="where">位置标识（固定字面量，勿用运行时拼接的字符串，否则键会无界增长）。</param>
        /// <param name="exception">被吞掉的异常。</param>
        public static void Report(string where, Exception exception)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;

            if (!s_NextReportAt.TryGetValue(where, out var nextAt))
            {
                nextAt = 0f;
            }

            s_Swallowed.TryGetValue(where, out var swallowed);

            if (now < nextAt)
            {
                // 退避窗口内：只记数，等下一次真正打印时一起报出来
                s_Swallowed[where] = swallowed + 1;
                return;
            }

            s_NextReportAt[where] = now + BackoffSeconds;
            s_Swallowed[where] = 0;

            LogUtility.Error(
                "[Audio] {0} 抛出异常，已被隔离（本轮其余音频逻辑继续执行）。{1} 秒内同类异常再吞 {2} 次不再打印。异常：{3}",
                where, BackoffSeconds, swallowed, exception);
        }

        /// <summary>清空退避与计数状态（服务关停时调用）。</summary>
        public static void Reset()
        {
            s_Swallowed.Clear();
            s_NextReportAt.Clear();
        }
    }
}
