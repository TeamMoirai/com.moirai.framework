namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块元信息（<see cref="SaveService.GetBlockInfos"/> 返回值）：键、模式版本、后端与载荷大小。
    /// <para>只读值对象；不含块数据本体。</para>
    /// </summary>
    public readonly struct SaveBlockInfo
    {
        /// <summary>
        /// 数据块键。
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
        /// 创建块元信息。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        public SaveBlockInfo(string key, int dataVersion, ESaveBackend backend, int sizeBytes)
        {
            Key = key;
            DataVersion = dataVersion;
            Backend = backend;
            SizeBytes = sizeBytes;
        }
    }
}
