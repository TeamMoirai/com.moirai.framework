namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 组件数据模式迁移钩子（可选接口，由含 <see cref="SaveFieldAttribute"/> 字段的组件类自行实现）。
    /// <para>恢复管线读出的存档模式版本（KVT 块内 <c>$schemas</c> 记录）与捕获器当前 <see cref="ISaveComponentCapturer.SchemaVersion"/>
    /// 不符时，标准键匹配恢复被替换为本钩子——由组件以旧键序逐条消费记录并写回字段。</para>
    /// <para>主线程调用；读取器已进入本组件类型作用域（子项数已读出为 <paramref name="recordCount"/>），
    /// 实现必须恰好消费 <paramref name="recordCount"/> 条记录（读值或 <see cref="SaveKeyValueReader.SkipRecordPayload"/>），否则外层作用域游标错位。</para>
    /// </summary>
    public interface ISaveComponentMigrator
    {
        /// <summary>
        /// 从旧模式版本的键值记录恢复组件字段。
        /// </summary>
        /// <param name="fromVersion">存档内记录的旧模式版本。</param>
        /// <param name="reader">键值读取器（位于本组件类型作用域首条记录）。</param>
        /// <param name="recordCount">作用域内记录数（须恰好消费完）。</param>
        void OnMigrateComponent(int fromVersion, ref SaveKeyValueReader reader, int recordCount);
    }
}
