namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一次 git 进程调用的读数。
    /// </summary>
    /// <remarks>
    /// 超时与启动失败都折算成 <see cref="Ok"/> 为假的一条读数，绝不向上抛——包源层要在窗口上出红条而不是异常栈。<br />
    /// <see cref="ExitCode"/> 在没跑成进程时是 -1，与 git 自己的非零退出码区分开。
    /// </remarks>
    internal sealed class GitRunResult
    {
        internal GitRunResult(bool ok, int exitCode, string standardOutput, string standardError)
        {
            Ok = ok;
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        /// <summary>退出码为零且没超时。</summary>
        internal bool Ok { get; }

        /// <summary>进程退出码，没跑成进程时为 -1。</summary>
        internal int ExitCode { get; }

        /// <summary>标准输出全文。</summary>
        internal string StandardOutput { get; }

        /// <summary>标准错误全文，超时与启动失败时写的是原因。</summary>
        internal string StandardError { get; }

        /// <summary>
        /// 取一行可直接展示的失败原因；成功时为空。
        /// </summary>
        internal string FailureReason()
        {
            if (Ok) return null;

            string detail = StandardError.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (!string.IsNullOrEmpty(detail)) return detail;
            return ExitCode < 0 ? "git 进程没跑起来" : $"git 退出码 {ExitCode}";
        }
    }
}
