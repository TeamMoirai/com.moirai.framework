namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块基类（手动编写的存档数据脚本）：声明版本迁移钩子。
    /// <para>子类以 <see cref="SaveDataAttribute"/> 声明块键与当前版本；
    /// 加载时存档内版本低于声明值即按 <paramref name="fromVersion"/> 起点执行 <see cref="OnMigrate"/> 级联升级（switch fallthrough 惯例），
    /// 内存对象就地修正，下一次写入自然持久化新版本。</para>
    /// <para>迁移在工作线程执行（读档管线内）；禁止触达 Unity 主线程 API。</para>
    /// </summary>
    public abstract class SaveDataBlock
    {
        /// <summary>
        /// 从 <paramref name="fromVersion"/> 升级到当前声明版本的迁移钩子（就地修改字段；推荐 switch 级联写法）。
        /// <para>声明版本与存档版本一致时不调用；无需迁移的子类可不覆写。</para>
        /// </summary>
        /// <param name="fromVersion">存档内记录的旧模式版本。</param>
        protected internal virtual void OnMigrate(int fromVersion)
        {
        }
    }
}
