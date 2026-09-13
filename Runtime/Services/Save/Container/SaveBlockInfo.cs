namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块元信息（<see cref="SaveService.GetBlockInfos"/> 返回值）：键、模式版本、后端、载荷大小与逐块错误分型。
    /// <para>只读值对象；不含块数据本体。容器 v2 逐块校验下坏块也会列入清单——<see cref="Error"/> 非
    /// <see cref="SaveError.None"/> 即坏块（载荷不可信）；坏块的 <see cref="DataVersion"/>/<see cref="Backend"/>/<see cref="SizeBytes"/>
    /// 仅在 <see cref="HasMetadata"/> 为 <c>true</c>（块框架完好的 CRC 坏块）时可信，结构性坏块为零值且 <see cref="Key"/> 可能为 <c>null</c>。</para>
    /// </summary>
    public readonly struct SaveBlockInfo
    {
        /// <summary>
        /// 数据块键（结构性坏块可能为 <c>null</c>——块边界不可读）。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 数据块模式版本（声明于 <c>SaveDataAttribute</c>，供迁移管线判定升级路径）。
        /// </summary>
        public int DataVersion { get; }

        /// <summary>
        /// 序列化后端标识。
        /// </summary>
        public ESaveBackend Backend { get; }

        /// <summary>
        /// 块载荷字节数。
        /// </summary>
        public int SizeBytes { get; }

        /// <summary>
        /// 逐块错误码（健康块为 <see cref="SaveError.None"/>；坏块为 <see cref="SaveError.Corrupted"/>）。
        /// </summary>
        public SaveError Error { get; }

        /// <summary>
        /// 块框架是否完整可读（健康块与 CRC 坏块为 <c>true</c>；结构性坏块为 <c>false</c>，此时元数据字段为零值）。
        /// </summary>
        public bool HasMetadata { get; }

        /// <summary>
        /// 创建健康块元信息（<see cref="Error"/> 为 <see cref="SaveError.None"/>）。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        public SaveBlockInfo(string key, int dataVersion, ESaveBackend backend, int sizeBytes)
            : this(key, dataVersion, backend, sizeBytes, SaveError.None, true)
        {
        }

        /// <summary>
        /// 创建块元信息（含逐块错误分型，供坏块列报）。
        /// </summary>
        /// <param name="key">数据块键（结构性坏块为 <c>null</c>）。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        /// <param name="error">逐块错误码。</param>
        /// <param name="hasMetadata">块框架是否完整可读。</param>
        public SaveBlockInfo(string key, int dataVersion, ESaveBackend backend, int sizeBytes, SaveError error, bool hasMetadata)
        {
            Key = key;
            DataVersion = dataVersion;
            Backend = backend;
            SizeBytes = sizeBytes;
            Error = error;
            HasMetadata = hasMetadata;
        }
    }
}
