using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块声明特性：块键、模式版本与序列化后端。
    /// <para>标注于 <see cref="SaveDataBlock"/> 子类（手动编写的存档数据脚本）；
    /// 框架经静态泛型描述符缓存标注（每类型仅反射一次，AOT 安全）。</para>
    /// <para>版本升级契约：存档内记录的 <see cref="Version"/> 低于声明值时，框架调用类内
    /// <see cref="SaveDataBlock.OnMigrate"/> 级联迁移；高于声明值时 fail-fast 拒绝（未来格式保护）。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SaveDataAttribute : Attribute
    {
        /// <summary>
        /// 块键（须满足块键规则：非空白、长度受限、禁止保留前缀 <c>__</c>）。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 当前数据模式版本（从 1 起；字段结构变化时递增并同步扩展 <c>OnMigrate</c> 级联）。
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// 序列化后端（缺省 JSON，无需任何类型标注）；
        /// 二进制后端要求类型带各自 AOT 标注（<c>MessagePackObject</c>/<c>MemoryPackable</c>/<c>ProtoContract</c>）。
        /// </summary>
        public ESaveBackend Backend { get; set; }

        /// <summary>
        /// 创建存档数据块声明。
        /// </summary>
        /// <param name="key">块键。</param>
        /// <param name="version">当前数据模式版本。</param>
        public SaveDataAttribute(string key, int version)
        {
            Key = key;
            Version = version;
            Backend = ESaveBackend.Json;
        }
    }
}
