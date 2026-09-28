namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 已注册回调列表中的代码行，记录回调注册点的源文件、行号与高亮状态。
    /// </summary>
    internal class CodeLineInfo : IRegisteredCallbackLine
    {
        /// <inheritdoc/>
        public LineType Type => LineType.CodeLine;

        /// <inheritdoc/>
        public string Text { get; }

        /// <inheritdoc/>
        public CallbackEventHandler CallbackHandler { get; }

        /// <summary>
        /// 获取回调注册点所在的源文件路径。
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// 获取回调注册点所在的行号。
        /// </summary>
        public int LineNumber { get; }

        /// <summary>
        /// 获取该代码行的哈希码，用于高亮匹配。
        /// </summary>
        public int LineHashCode { get; }

        /// <summary>
        /// 获取或设置该行是否处于高亮状态。
        /// </summary>
        public bool Highlighted { get; set; }

        /// <summary>
        /// 创建代码行信息。
        /// </summary>
        /// <param name="text">显示文本（回调名）。</param>
        /// <param name="handler">注册该回调的元素。</param>
        /// <param name="fileName">源文件路径。</param>
        /// <param name="lineNumber">源文件行号。</param>
        /// <param name="lineHashCode">用于高亮匹配的行哈希码。</param>
        public CodeLineInfo(string text, CallbackEventHandler handler, string fileName, int lineNumber, int lineHashCode)
        {
            Text = text;
            CallbackHandler = handler;
            FileName = fileName;
            LineNumber = lineNumber;
            LineHashCode = lineHashCode;
        }
    }
}
