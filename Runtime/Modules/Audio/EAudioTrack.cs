namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音轨（音效分类），可分别关闭/开启对应分类音效。
    /// </summary>
    /// <remarks>命名与AudioMixer中分类名保持一致。</remarks>
    public enum EAudioTrack
    {
        /// <summary>
        /// 常规音效。
        /// </summary>
        Sfx,

        /// <summary>
        /// UI声效。
        /// </summary>
        UI,

        /// <summary>
        /// 背景音乐音效。
        /// </summary>
        Music,

        /// <summary>
        /// 人声音效。
        /// </summary>
        Voice,
    }
}