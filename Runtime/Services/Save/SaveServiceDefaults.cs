namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档公共常量中立宿主：保留块键与目录名约定（契约、门面与家族基座三方共用的单一来源）。
    /// </summary>
    /// <remarks>
    /// 自 <see cref="SaveServiceHandler"/> 迁出——契约接口（<see cref="ISaveService"/>）的默认参数此前反向引用实现基座， <br />
    /// 依赖箭头倒置；常量落中立宿主后契约自立，基座经转发别名保持源码兼容。 <br />
    /// 数值即落盘约定（块键写入容器条目、目录名拼 persistentDataPath 路径），一经发行不得变更。
    /// </remarks>
    public static class SaveServiceDefaults
    {
        /// <summary>存档数据根目录名（persistentDataPath 下）。</summary>
        public const string DATA_FOLDER_NAME = "Data";

        /// <summary>未显式指定文件夹时的默认存档文件夹名。</summary>
        public const string DEFAULT_FOLDER_NAME = "Save";

        /// <summary>便捷单对象 API 读写的保留数据块键。</summary>
        public const string MAIN_BLOCK_KEY = "__main__";

        /// <summary>槽位元数据保留块键（<c>__</c> 前缀豁免名单成员）。</summary>
        internal const string META_BLOCK_KEY = "__meta";

        /// <summary>保留块键前缀（用户块禁用；豁免名单见 <see cref="META_BLOCK_KEY"/>）。</summary>
        internal const string RESERVED_BLOCK_KEY_PREFIX = "__";

        /// <summary>数据块键最大长度。</summary>
        internal const int MAX_BLOCK_KEY_LENGTH = 64;
    }
}
