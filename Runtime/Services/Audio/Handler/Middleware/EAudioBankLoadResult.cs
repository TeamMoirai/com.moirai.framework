namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 声音库加载结果（内部）：Loaded / AlreadyLoaded / Failed 三态。
    /// </summary>
    /// <remarks>刻意不用 <c>bool</c>：幂等命中与真失败需要可分，只有 <see cref="Failed"/> 才应告警（已加载的 master/Init 库是正常路径）。</remarks>
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
