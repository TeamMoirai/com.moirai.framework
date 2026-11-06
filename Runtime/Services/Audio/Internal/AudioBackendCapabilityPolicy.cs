using System.Collections.Generic;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 播放选项与后端能力的对齐判据：挑出「配置用到了当前后端不消费的能力」这类静默改变语义的组合。
    /// </summary>
    /// <remarks>
    /// <b>只报不改</b>：把字段归零或换后端都是取用方的决定，判据不代为改写。 <br />
    /// 纯判据——输入是能力快照、选项值与资产标识串，不吃 <c>AssetDatabase</c> 与检视器属性，编辑期与构建期共用同一份规则。 <br />
    /// 「用到」的口径与运行期消费点一致：非默认值才算用（能力缺失但字段停在默认值时语义没有分叉，不报）。 <br />
    /// 命中多条时逐条追加，不合并——每条要各自指认字段与改法。
    /// </remarks>
    internal static class AudioBackendCapabilityPolicy
    {
        /// <summary>
        /// 按能力快照逐项比对选项值，把冲突追加到 <paramref name="problems"/>。
        /// </summary>
        /// <param name="capabilities">当前选定后端自报的能力快照。</param>
        /// <param name="options">要判定的播放选项。</param>
        /// <param name="assetPath">配置来源标识（资产路径），原样落到每条结论上。</param>
        /// <param name="problems">结论收集容器；<c>null</c> 时什么都不做。</param>
        internal static void CollectIssues(in AudioBackendCapabilities capabilities, in AudioPlayOptions options,
            string assetPath, List<AudioCapabilityIssue> problems)
        {
            if (problems == null)
            {
                return;
            }

            if (!capabilities.SupportsDelayedStart && options.InitialDelay != 0f)
            {
                Add(problems, assetPath, nameof(AudioPlayOptions.InitialDelay),
                    $"InitialDelay = {options.InitialDelay:0.###}s，后端 {capabilities.BackendName} 不消费它：这一声会立刻响，" +
                    "配好的起播延迟不会生效。改法：把该字段归 0，或换成消费它的音频后端（AudioServiceSettings → 音频处理器）；" +
                    "后端确实支持时，在 AudioServiceHandler 上覆写 SupportsDelayedStart。");
            }

            if (!capabilities.SupportsPlaybackTrim && options.PlaybackTime > 0f)
            {
                Add(problems, assetPath, nameof(AudioPlayOptions.PlaybackTime),
                    $"PlaybackTime = {options.PlaybackTime:0.###}s，后端 {capabilities.BackendName} 不消费它：" +
                    "配好的起播位置不会生效，音频从自身开头播起。改法：把该字段归 0，" +
                    "或换成消费它的音频后端（AudioServiceSettings → 音频处理器）；后端确实支持时，" +
                    "在 AudioServiceHandler 上覆写 SupportsPlaybackTrim。");
            }

            if (!capabilities.SupportsPlaybackTrim && options.PlaybackDuration > 0f)
            {
                Add(problems, assetPath, nameof(AudioPlayOptions.PlaybackDuration),
                    $"PlaybackDuration = {options.PlaybackDuration:0.###}s，后端 {capabilities.BackendName} 不消费它：" +
                    "提前结束与随之而来的淡出都不会发生，音频会按自身长度播完。改法：把该字段归 0，" +
                    "或换成消费它的音频后端（AudioServiceSettings → 音频处理器）；后端确实支持时，" +
                    "在 AudioServiceHandler 上覆写 SupportsPlaybackTrim。");
            }

            if (!capabilities.SupportsSoloControl && options.SoloMode != EAudioSoloMode.None)
            {
                Add(problems, assetPath, nameof(AudioPlayOptions.SoloMode),
                    $"SoloMode = {options.SoloMode}，后端 {capabilities.BackendName} 不消费它：开播时不会静音其他音轨/其他音频，" +
                    "AutoUnSoloOnEnd 同属独奏分支也不会兑现。改法：把该字段改回 None，" +
                    "或换成消费它的音频后端（AudioServiceSettings → 音频处理器）；后端确实支持时，" +
                    "在 AudioServiceHandler 上覆写 SupportsSoloControl。");
            }
        }

        private static void Add(List<AudioCapabilityIssue> problems, string assetPath, string field, string detail)
        {
            problems.Add(new AudioCapabilityIssue
            {
                AssetPath = assetPath ?? string.Empty,
                Field = field,
                Detail = detail,
            });
        }
    }
}
