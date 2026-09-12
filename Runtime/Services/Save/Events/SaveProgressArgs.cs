namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档进度事件参数（<see cref="SaveService.SaveProgress"/> / <see cref="SaveService.LoadProgress"/>）。
    /// <para>组件存取管线按固定批次回报（每 <c>ProgressBatchSize</c> 个组件一批 + 最终一批必报）；
    /// <see cref="Total"/> 为已注册组件快照数，<see cref="Completed"/> 为已处理数。</para>
    /// </summary>
    public readonly struct SaveProgressArgs
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
        /// 已处理组件数。
        /// </summary>
        public int Completed { get; }

        /// <summary>
        /// 组件总数（已注册快照）。
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// 创建进度参数。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="completed">已处理组件数。</param>
        /// <param name="total">组件总数。</param>
        public SaveProgressArgs(string fileName, string folderName, int completed, int total)
        {
            FileName = fileName;
            FolderName = folderName;
            Completed = completed;
            Total = total;
        }
    }
}
