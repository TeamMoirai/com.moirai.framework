namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档失败阶段（<see cref="SaveFailedArgs.Stage"/>）：失败发生在管线的哪一段。
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

    /// <summary>
    /// 存档失败事件参数（<see cref="SaveService.SaveFailed"/> / <see cref="SaveService.LoadFailed"/>）。
    /// <para>写路径失败同时以 <see cref="GameException"/> fail-fast 上抛（事件不替代异常）；读路径缺档（<see cref="SaveError.FileNotFound"/>）
    /// 属正常业务流，不产生失败事件。</para>
    /// </summary>
    public readonly struct SaveFailedArgs
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
        /// 数据块键（整档级失败为 <c>null</c>）。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 失败阶段。
        /// </summary>
        public ESaveFailureStage Stage { get; }

        /// <summary>
        /// 错误码。
        /// </summary>
        public SaveError Error { get; }

        /// <summary>
        /// 创建失败参数。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="stage">失败阶段。</param>
        /// <param name="error">错误码。</param>
        public SaveFailedArgs(string fileName, string folderName, string key, ESaveFailureStage stage, SaveError error)
        {
            FileName = fileName;
            FolderName = folderName;
            Key = key;
            Stage = stage;
            Error = error;
        }
    }
}
