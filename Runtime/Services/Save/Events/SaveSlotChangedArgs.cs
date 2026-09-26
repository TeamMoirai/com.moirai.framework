namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档槽位变动类别。
    /// </summary>
    public enum ESaveSlotChangeKind
    {
        /// <summary>
        /// 槽位已写入（创建或更新的合并语义——读-改-写管线不区分两者）。
        /// </summary>
        Saved = 0,

        /// <summary>
        /// 槽位已删除（<see cref="SaveSlotChangedArgs.FileName"/> 为 <c>null</c> 时表示目录级批量删除）。
        /// </summary>
        Deleted,

        /// <summary>
        /// 槽位已重命名（预留——重命名 API 后续版本提供）。
        /// </summary>
        Renamed,

        /// <summary>
        /// 槽位单档备份已创建（<c>.bak</c>）。
        /// </summary>
        BackupCreated,

        /// <summary>
        /// 槽位已从单档备份恢复。
        /// </summary>
        BackupRestored,
    }

    /// <summary>
    /// 存档槽位变动事件参数（<see cref="SaveService.SlotChanged"/>）。
    /// </summary>
    public readonly struct SaveSlotChangedArgs
    {
        /// <summary>
        /// 变动类别。
        /// </summary>
        public ESaveSlotChangeKind Kind { get; }

        /// <summary>
        /// 存档文件名（目录级批量删除为 <c>null</c>）。
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// 存档文件夹名称。
        /// </summary>
        public string FolderName { get; }

        /// <summary>
        /// 创建槽位变动参数。
        /// </summary>
        /// <param name="kind">变动类别。</param>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        public SaveSlotChangedArgs(ESaveSlotChangeKind kind, string fileName, string folderName)
        {
            Kind = kind;
            FileName = fileName;
            FolderName = folderName;
        }
    }
}
