namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 重绑结果事件：一次交互式重绑到达终态（成功/取消/冲突/不可重绑）时发布。
    /// </summary>
    /// <remarks>
    /// UI 设置页订阅本事件刷新按键提示（Glyph 随新绑定即时更新），不轮询查询后端。
    /// </remarks>
    [GameEvent]
    public readonly struct InputRebindEvent : IGameEvent
    {
        /// <summary>终态类别。</summary>
        public readonly ERebindOutcome Outcome;

        /// <summary>重绑目标动作分组（空串为全局）。</summary>
        public readonly string ActionGroup;

        /// <summary>重绑目标动作名。</summary>
        public readonly string ActionName;

        /// <summary>成功时为实际落绑的控件路径；其余终态为 null。</summary>
        public readonly string BindingPath;

        /// <summary>
        /// 创建重绑结果事件。
        /// </summary>
        public InputRebindEvent(in InputRebindResult result)
        {
            Outcome = result.Outcome;
            ActionGroup = result.ActionGroup;
            ActionName = result.ActionName;
            BindingPath = result.BindingPath;
        }
    }
}
