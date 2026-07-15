namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频代理辅助器运行时状态枚举。
    /// </summary>
    public enum AudioAgentRuntimeState
    {
        /// <summary>
        /// 无状态
        /// </summary>
        None,

        /// <summary>
        /// 加载中
        /// </summary>
        Loading,

        /// <summary>
        /// 渐入（音量渐渐变大）
        /// </summary>
        FadingIn,
        
        /// <summary>
        /// 播放中
        /// </summary>
        Playing,

        /// <summary>
        /// 渐出（音量渐渐变小）
        /// </summary>
        FadingOut,

        /// <summary>
        /// 播放结束
        /// </summary>
        End,
        
        /// <summary>
        /// 暂停中
        /// </summary>
        Pausing
    }
}