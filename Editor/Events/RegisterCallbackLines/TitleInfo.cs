namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 已注册回调列表中的分组标题行，按发送事件的元素分组。
    /// </summary>
    internal class TitleInfo : IRegisteredCallbackLine
    {
        /// <inheritdoc/>
        public LineType Type => LineType.Title;

        /// <inheritdoc/>
        public string Text { get; }

        /// <inheritdoc/>
        public CallbackEventHandler CallbackHandler { get; }

        /// <summary>
        /// 使用显示文本与关联元素创建标题行。
        /// </summary>
        /// <param name="text">显示文本（元素显示名称）。</param>
        /// <param name="handler">发送事件的元素。</param>
        public TitleInfo(string text, CallbackEventHandler handler)
        {
            Text = text;
            CallbackHandler = handler;
        }
    }
}
