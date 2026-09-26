namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 声音库加载结果（内部）。
    /// <para>刻意做成三态而不是 <c>bool</c>：幂等命中与真失败在 <c>bool</c> 下不可分，
    /// 而「已由插件自行加载的 master/Init 库」是正常路径——把它当失败报出来，
    /// 告警就成了音效师忽略掉的噪音，真正的路径写错反而被埋在里面。</para>
    /// </summary>
    internal enum EAudioBankLoadResult : byte
    {
        /// <summary>本次调用完成加载。</summary>
        Loaded,

        /// <summary>该库已在内存里（重复请求，或由 SDK/插件启动时自行加载）——不是错误。</summary>
        AlreadyLoaded,

        /// <summary>加载失败：路径写错、文件未随包，或引擎未就绪。</summary>
        Failed,
    }
}
