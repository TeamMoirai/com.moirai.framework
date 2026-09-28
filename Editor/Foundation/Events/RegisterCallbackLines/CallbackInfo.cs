namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 已注册回调列表中的回调行，表示某元素注册的一种事件类型。
    /// </summary>
    internal class CallbackInfo : IRegisteredCallbackLine
    {
        /// <inheritdoc/>
        public LineType Type => LineType.Callback;

        /// <inheritdoc/>
        public string Text { get; }

        /// <inheritdoc/>
        public CallbackEventHandler CallbackHandler { get; }

        /// <summary>
        /// 使用显示文本与关联元素创建回调行。
        /// </summary>
        /// <param name="text">显示文本（事件类型名称）。</param>
        /// <param name="handler">注册该回调的元素。</param>
        public CallbackInfo(string text, CallbackEventHandler handler)
        {
            Text = text;
            CallbackHandler = handler;
        }
    }
}
