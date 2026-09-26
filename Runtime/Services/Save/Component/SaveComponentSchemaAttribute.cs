using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 组件数据模式版本声明：标注于含 <see cref="SaveFieldAttribute"/> 字段的组件类上。
    /// <para>SaveHost SourceGenerator 将版本发射为捕获器 <see cref="ISaveComponentCapturer.SchemaVersion"/>；
    /// 保存时按组件类型记录进 KVT 块内 <c>$schemas</c> 作用域，恢复时版本不符走 <see cref="ISaveComponentMigrator"/> 迁移钩子
    /// （组件未实现钩子则记告警并按 KVT 键匹配容错恢复）。</para>
    /// <para>版本 int 递增（缺省 1）；字段结构变化时递增并实现迁移钩子。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SaveComponentSchemaAttribute : Attribute
    {
        /// <summary>
        /// 当前组件数据模式版本（≥ 1，小于 1 按 1 收敛）。
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// 创建组件模式版本声明。
        /// </summary>
        /// <param name="version">当前模式版本。</param>
        public SaveComponentSchemaAttribute(int version)
        {
            Version = version;
        }
    }
}
