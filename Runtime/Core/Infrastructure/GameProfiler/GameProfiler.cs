using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架Profiler分析器类。
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static class GameProfiler
    {
        private static int s_ProfileLevel = -1;
        private static int s_CurrLevel = 0;
        private static int s_SampleLevel = 0;

        /// <summary>
        /// 免域重载复位：清空采样嵌套深度并把 <c>ProfileLevel</c> 复位为「未设置」。
        /// </summary>
        /// <remarks>不清会让脏深度跨会话带入下一 Play，导致采样层级错位。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            s_ProfileLevel = -1;
            s_CurrLevel = 0;
            s_SampleLevel = 0;
        }

        /// <summary>
        /// 设置分析器等级。
        /// </summary>
        /// <param name="level">调试器等级。</param>
        public static void SetProfileLevel(int level)
        {
            s_ProfileLevel = level;
        }
        
        /// <summary>
        /// 开始使用自定义采样分析一段代码。
        /// </summary>
        /// <param name="name">用于在Profiler窗口中标识样本的字符串。</param>
        [Conditional("PROFILER_ENABLE")]
        public static void BeginSample(string name)
        {
            s_CurrLevel++;
            if (s_ProfileLevel >= 0 && s_CurrLevel > s_ProfileLevel)
            {
                return;
            }

            s_SampleLevel++;
            Profiler.BeginSample(name);
        }

        /// <summary>
        /// 结束本次自定义采样分析。
        /// </summary>
        [Conditional("PROFILER_ENABLE")]
        public static void EndSample()
        {
            if (s_CurrLevel <= s_SampleLevel)
            {
                Profiler.EndSample();
                s_SampleLevel--;
            }

            s_CurrLevel--;
        }
    }
}