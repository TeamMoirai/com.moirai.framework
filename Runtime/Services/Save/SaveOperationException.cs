using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档管线内部操作异常：携带结构化 <see cref="SaveError"/> 错误码，
    /// 供加密处理器向文件管线传递「完整性失败/解密失败」等分型结果。
    /// <para>内部流转专用；外部调用方应通过 <see cref="SaveResult{T}"/> 的错误码判别，而非捕获本异常。</para>
    /// </summary>
    [Serializable]
    internal sealed class SaveOperationException : Exception
    {
        /// <summary>
        /// 结构化错误码。
        /// </summary>
        public SaveError Error { get; }

        /// <summary>
        /// 创建存档操作异常。
        /// </summary>
        /// <param name="error">结构化错误码。</param>
        /// <param name="message">异常消息。</param>
        public SaveOperationException(SaveError error, string message) : base(message)
        {
            Error = error;
        }
    }
}
