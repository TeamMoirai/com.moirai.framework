namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 已注册回调列表的行类型。
    /// </summary>
    enum LineType
    {
        /// <summary>分组标题行。</summary>
        Title,
        /// <summary>回调（事件类型）行。</summary>
        Callback,
        /// <summary>回调注册处的源代码行。</summary>
        CodeLine
    }

    /// <summary>
    /// 已注册回调列表中一行的数据抽象，按 <see cref="LineType"/> 区分标题、回调与代码行。
    /// </summary>
    interface IRegisteredCallbackLine
    {
        /// <summary>
        /// 获取该行的类型。
        /// </summary>
        LineType Type { get; }

        /// <summary>
        /// 获取该行的显示文本。
        /// </summary>
        string Text { get; }

        /// <summary>
        /// 获取该行关联的回调处理器（VisualElement），用于事件高亮定位。
        /// </summary>
        CallbackEventHandler CallbackHandler { get; }
    }
}
