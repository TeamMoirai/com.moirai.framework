namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频后端自报的播放能力快照：一项能力一枚开关，取值全部来自被判定方自己。
    /// </summary>
    /// <remarks>
    /// 缺省为「不支持」——未知后端不得自称支持，宁可多问一次也不静默改变语义。 <br />
    /// 与 <see cref="AudioServiceHandler"/> 上的能力成员同构，但不吃后端实例，判据因此可脱离装配单测。 <br />
    /// <see cref="BackendName"/> 只进报告文本，不参与判据。
    /// </remarks>
    internal struct AudioBackendCapabilities
    {
        /// <summary>是否消费 <see cref="AudioPlayOptions.InitialDelay"/>（延迟起播）。</summary>
        internal bool SupportsDelayedStart;

        /// <summary>是否消费 <see cref="AudioPlayOptions.PlaybackTime"/> 与 <see cref="AudioPlayOptions.PlaybackDuration"/>（按请求裁剪播放区间）。</summary>
        internal bool SupportsPlaybackTrim;

        /// <summary>是否消费 <see cref="AudioPlayOptions.SoloMode"/>（开播时静音其他音频）。</summary>
        internal bool SupportsSoloControl;

        /// <summary>后端类型名，供报告指出当前选定的是哪一支后端。</summary>
        internal string BackendName;

        /// <summary>
        /// 从后端实例取能力快照。
        /// </summary>
        /// <param name="handler">设置资产上序列化的后端；为 <c>null</c> 时按全不支持返回。</param>
        /// <returns>能力快照。</returns>
        internal static AudioBackendCapabilities From(AudioServiceHandler handler)
        {
            if (handler == null)
            {
                return default;
            }

            return new AudioBackendCapabilities
            {
                SupportsDelayedStart = handler.SupportsDelayedStart,
                SupportsPlaybackTrim = handler.SupportsPlaybackTrim,
                SupportsSoloControl = handler.SupportsSoloControl,
                BackendName = handler.GetType().Name,
            };
        }
    }
}
