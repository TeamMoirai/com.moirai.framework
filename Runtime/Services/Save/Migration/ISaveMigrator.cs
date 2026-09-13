using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档迁移器契约：声明一个文件级数据版本跃迁（<see cref="FromVersion"/> → <see cref="ToVersion"/>）的纯数据变换。
    /// <para>实现类由 SaveHost SourceGenerator 扫描并经模块初始化器自注册 <see cref="SaveMigrationManager"/>（AOT 安全），
    /// 也可手动 <see cref="SaveMigrationManager.Register(ISaveMigrator)"/>；实现必须为可实例化的非抽象类且提供无参构造。</para>
    /// <para>版本号约定：int 递增（0 = 版本化前的基线存档）；语义化三段式映射建议——主版本.次版本.修订 依次乘 10000/100 偏移相加
    /// （如 1.2.3 → 10203），映射规则由项目文档固化后勿再变更。<see cref="ToVersion"/> 必须大于 <see cref="FromVersion"/>（仅允许升级方向）。</para>
    /// <para>执行约束：迁移在加载管线内同步执行（读档串行门持有期，可能在主线程）——<see cref="Migrate"/> 必须同步完成，
    /// 禁止内部切线程/异步等待（返回未完成的任务将 fail-fast）；禁止触达 Unity 主线程 API（可能在后台线程执行）。</para>
    /// </summary>
    public interface ISaveMigrator
    {
        /// <summary>
        /// 迁移起始版本（存档内记录的数据版本等于该值时本迁移器参与链）。
        /// </summary>
        int FromVersion { get; }

        /// <summary>
        /// 迁移目标版本（必须大于 <see cref="FromVersion"/>）。
        /// </summary>
        int ToVersion { get; }

        /// <summary>
        /// 同一边（<see cref="FromVersion"/>/<see cref="ToVersion"/> 相同）内多个迁移器的执行次序（值小者先执行；缺省 0）。
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 执行迁移（经 <paramref name="context"/> 操作块集合；必须同步完成）。
        /// </summary>
        /// <param name="context">迁移上下文（纯数据操作面：块改名/变换/删除与字段改名/改型）。</param>
        /// <returns>迁移完成的异步任务（实现须同步完成，不得返回未完成任务）。</returns>
        UniTask Migrate(SaveMigrationContext context);
    }
}
