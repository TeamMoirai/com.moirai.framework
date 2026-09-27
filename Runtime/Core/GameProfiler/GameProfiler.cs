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
        /// 免域重载复位：嵌套深度带脏值进下一 Play 会让采样层级错位（Begin/End 跨会话不配对时）。
        /// ProfileLevel 一并复位为「未设置」——等级由 Debugger 启动时重设，残留旧等级没有正当语义。
        /// </summary>
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