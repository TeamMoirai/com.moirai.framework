using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 按 key 去重的配置类告警（内部），同一 key 只落一次且限定在 Error/Warning 级。
    /// </summary>
    /// <remarks>缺 Mixer、缺快照、事件名对不上这类问题每帧重报会刷屏并被忽略；key 数量有上限，超出后不再新增。</remarks>
    internal static class AudioWarnOnce
    {
        private const int MaxKeys = 128;

        private static readonly HashSet<string> s_Seen = new HashSet<string>(StringComparer.Ordinal);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 域重载/重启后重新允许告警，避免关掉 EnterPlayModeOptions 时二次进入播放看不到问题。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnRestore() => s_Seen.Clear();
#endif

        /// <summary>
        /// 清空已记录状态（服务关停时调用）。
        /// </summary>
        public static void Reset() => s_Seen.Clear();

        /// <summary>
        /// 首次出现的 Warning；同一 key 后续调用被吞掉。
        /// </summary>
        /// <returns>本次实际打印了日志返回 true。</returns>
        public static bool Warning(string key, string format, params object[] args)
        {
            if (!ShouldLog(key)) return false;
            LogUtility.Warning(Compose(format, args));
            return true;
        }

        /// <summary>
        /// 首次出现的 Error；同一 key 后续调用被吞掉。
        /// </summary>
        public static bool Error(string key, string format, params object[] args)
        {
            if (!ShouldLog(key)) return false;
            LogUtility.Error(Compose(format, args));
            return true;
        }

        /// <summary>
        /// 先把占位符拼成成品串再交给日志层（<c>LogUtility</c> 无 <c>params object[]</c> 重载，直传数组会丢占位信息）。
        /// </summary>
        private static string Compose(string format, object[] args)
            => args == null || args.Length == 0 ? format : string.Format(format, args);

        private static bool ShouldLog(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (s_Seen.Contains(key)) return false;
            if (s_Seen.Count >= MaxKeys) return false;

            s_Seen.Add(key);
            return true;
        }
    }
}
