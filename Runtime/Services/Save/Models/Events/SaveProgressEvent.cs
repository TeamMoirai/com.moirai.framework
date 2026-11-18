namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存取进度事件。
    /// </summary>
    /// <remarks>组件存取管线按固定批次回报（每 <see cref="SaveService.PROGRESS_BATCH_SIZE"/> 个组件一批，最终一批必报）； <br />
    /// <see cref="Total"/> 为已注册组件快照数，<see cref="Completed"/> 为已处理数。</remarks>
    public readonly struct SaveProgressEvent : IGameEvent
    {
        /// <summary>
        /// 进度事件的操作类别（<see cref="SaveProgressEvent.Kind"/>）。
        /// </summary>
        public enum ESaveProgressKind
        {
            /// <summary>保存进度（组件捕获）。</summary>
            Save = 0,

            /// <summary>加载进度（组件恢复）。</summary>
            Load,
        }
        
        /// <summary>操作类别。</summary>
        public readonly ESaveProgressKind Kind;

        /// <summary>存档文件名。</summary>
        public string FileName { get; }

        /// <summary>存档文件夹名称。</summary>
        public string FolderName { get; }

        /// <summary>已处理组件数。</summary>
        public int Completed { get; }

        /// <summary>组件总数（已注册快照）。</summary>
        public int Total { get; }

        /// <summary>
        /// 创建存取进度事件。
        /// </summary>
        /// <param name="kind">操作类别。</param>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="completed">已处理组件数。</param>
        /// <param name="total">组件总数。</param>
        public SaveProgressEvent(ESaveProgressKind kind, string fileName, string folderName, int completed, int total)
        {
            Kind = kind;
            FileName = fileName;
            FolderName = folderName;
            Completed = completed;
            Total = total;
        }
    }
}
