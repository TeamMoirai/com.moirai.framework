namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音轨（音效分类），可分别关闭/开启对应分类音效。
    /// </summary>
    /// <remarks>命名与AudioMixer中分类名保持一致。</remarks>
    /// <example>
    /// Master Bus (主总线)
    /// ├── Music Bus (音乐)
    /// ├── Voice Bus (人声)
    /// ├── UI Bus (界面音效)
    /// ├── SFX Bus (音效)  ← 用于所有“一次性”的游戏交互音效
    /// │   ├── Player SFX (玩家音效)
    /// │   ├── Enemy SFX (敌人音效)
    /// │   └── Weapon SFX (武器音效)
    /// └── Ambience Bus (环境音) ← 用于持续播放的背景环境声
    ///     ├── Outdoor Ambience (室外环境)
    ///     └── Indoor Ambience (室内环境)
    /// </example>
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

        /// <summary>
        /// 环境音
        /// </summary>
        Ambience
    }
}