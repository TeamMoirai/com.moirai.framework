namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 块保存/删除事件。
    /// </summary>
    public readonly struct SaveBlockChangedEvent : IGameEvent
    {
        /// <summary>
        /// 块变动类别。
        /// </summary>
        public enum ESaveBlockChangeKind
        {
            /// <summary>块已保存。</summary>
            Saved = 0,

            /// <summary>块已删除。</summary>
            Deleted,
        }

        /// <summary>变动类别。</summary>
        public readonly ESaveBlockChangeKind Kind;

        /// <summary>存档文件名。</summary>
        public string FileName { get; }

        /// <summary>存档文件夹名称。</summary>
        public string FolderName { get; }

        /// <summary>数据块键。</summary>
        public string Key { get; }

        /// <summary>序列化后端标识。</summary>
        public ushort Backend { get; }

        /// <summary>块载荷字节数。</summary>
        public int SizeBytes { get; }
        
        /// <summary>
        /// 创建块变动事件。
        /// </summary>
        /// <param name="kind">变动类别。</param>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        public SaveBlockChangedEvent(ESaveBlockChangeKind kind, string fileName, string folderName, string key, ushort backend, int sizeBytes)
        {
            Kind = kind;
            FileName = fileName;
            FolderName = folderName;
            Key = key;
            Backend = backend;
            SizeBytes = sizeBytes;
        }
    }
}