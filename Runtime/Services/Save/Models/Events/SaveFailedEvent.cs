namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存取失败事件。
    /// </summary>
    /// <remarks>写路径失败同时以 <see cref="GameException"/> fail-fast 上抛（事件不替代异常）；读路径缺档（<see cref="SaveError.FileNotFound"/>）属正常业务流， <br />
    /// 不产生失败事件。</remarks>
    public readonly struct SaveFailedEvent : IGameEvent
    {
        /// <summary>
        /// 失败事件的操作类别（<see cref="SaveFailedEvent.Operation"/>）。
        /// </summary>
        public enum ESaveFailureOperation
        {
            /// <summary>保存（写路径）。</summary>
            Save = 0,

            /// <summary>加载（读路径）。</summary>
            Load,
        }

        /// <summary>
        /// 存档失败阶段（<see cref="SaveFailedEvent.Stage"/>）：失败发生在管线的哪一段。
        /// </summary>
        public enum ESaveFailureStage
        {
            /// <summary>写：块序列化。</summary>
            Serialize = 0,

            /// <summary>写：容器压缩。</summary>
            Compress,

            /// <summary>写：载荷变换（加密）。</summary>
            Transform,

            /// <summary>写：存储层原子提交。</summary>
            StorageWrite,

            /// <summary>读：存储层读取（IO）。</summary>
            StorageRead,

            /// <summary>读：文件头与整档 CRC 校验。</summary>
            HeaderValidation,

            /// <summary>读：载荷还原（解密/HMAC）。</summary>
            Restore,

            /// <summary>读：容器解压。</summary>
            Decompress,

            /// <summary>读：容器解析（含逐块 CRC 与块后端解析）。</summary>
            ContainerParse,

            /// <summary>读：块反序列化。</summary>
            Deserialize,

            /// <summary>读：版本迁移。</summary>
            Migrate,
        }        
        
        /// <summary>操作类别。</summary>
        public readonly ESaveFailureOperation Operation;

        /// <summary>存档文件名。</summary>
        public string FileName { get; }

        /// <summary>存档文件夹名称。</summary>
        public string FolderName { get; }

        /// <summary>数据块键（整档级失败为 <c>null</c>）。</summary>
        public string Key { get; }

        /// <summary>失败阶段。</summary>
        public readonly ESaveFailureStage Stage;

        /// <summary>错误码。</summary>
        public readonly SaveError Error;

        /// <summary>
        /// 创建存取失败事件。
        /// </summary>
        /// <param name="operation">操作类别。</param>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键（整档级失败为 <c>null</c>）。</param>
        /// <param name="stage">失败阶段。</param>
        /// <param name="error">错误码。</param>
        public SaveFailedEvent(ESaveFailureOperation operation, string fileName, string folderName, string key, ESaveFailureStage stage, SaveError error)
        {
            Operation = operation;
            FileName = fileName;
            FolderName = folderName;
            Key = key;
            Stage = stage;
            Error = error;
        }
    }
}
