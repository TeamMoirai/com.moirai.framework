namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioClip 缓存策略：决定一次路径播放用完的租约是留池还是当场释放。
    /// </summary>
    public enum AudioCachePolicy : byte
    {
        /// <summary>默认：回落到 <see cref="AudioServiceSettings"/> 配置。</summary>
        Default = 0,

        /// <summary>不缓存：引用归零即释放租约。</summary>
        None = 1,

        /// <summary>用后缓存：TTL 过期或容量驱逐。</summary>
        Ttl = 2,

        /// <summary>常驻：不参与 LRU/TTL 驱逐，直到 Unload/ClearCache(force)。</summary>
        Pin = 3,
    }
}
