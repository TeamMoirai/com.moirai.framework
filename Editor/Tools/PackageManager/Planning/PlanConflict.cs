namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一条规划冲突：谁、哪一类、为什么。
    /// </summary>
    /// <remarks>
    /// <see cref="PluginId"/> 是被判定住的那一方：缺依赖与版本不满足记依赖方，Unity 地板记该插件自身。<br />
    /// <see cref="Reason"/> 面向窗口文案，须同时带上升别与候选版本，缺一处就成了"知道坏了但不知道坏在哪"。
    /// </remarks>
    internal sealed class PlanConflict
    {
        internal PlanConflict(string pluginId, PlanConflictKind kind, string reason)
        {
            PluginId = pluginId;
            Kind = kind;
            Reason = reason;
        }

        /// <summary>冲突归属的插件标识。</summary>
        internal string PluginId { get; }

        /// <summary>冲突种类。</summary>
        internal PlanConflictKind Kind { get; }

        /// <summary>可直接展示的判定理由。</summary>
        internal string Reason { get; }
    }
}
