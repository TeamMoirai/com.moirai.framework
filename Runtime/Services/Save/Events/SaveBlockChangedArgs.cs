namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块变动事件参数（<see cref="SaveService.BlockSaved"/> / <see cref="SaveService.BlockDeleted"/>）。
    /// </summary>
    public readonly struct SaveBlockChangedArgs
    {
        /// <summary>
        /// 存档文件名。
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// 存档文件夹名称。
        /// </summary>
        public string FolderName { get; }

        /// <summary>
        /// 数据块键。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 序列化后端标识。
        /// </summary>
        public ESaveBackend Backend { get; }

        /// <summary>
        /// 块载荷字节数。
        /// </summary>
        public int SizeBytes { get; }

        /// <summary>
        /// 创建块变动参数。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        public SaveBlockChangedArgs(string fileName, string folderName, string key, ESaveBackend backend, int sizeBytes)
        {
            FileName = fileName;
            FolderName = folderName;
            Key = key;
            Backend = backend;
            SizeBytes = sizeBytes;
        }
    }
}
