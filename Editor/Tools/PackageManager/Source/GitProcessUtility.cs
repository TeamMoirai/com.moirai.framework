using System;
using System.Diagnostics;
using System.Text;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 进程外 git 的调用面。
    /// </summary>
    /// <remarks>
    /// 不用本包 <c>ShellHelper</c>：它经 cmd.exe 且不回传标准输出。<br />
    /// 两路输出都异步收，避免管道写满把 git 卡到超时；要按字节切分的批量读走 <see cref="RunWithInput"/> 整段取回标准输出。<br />
    /// 终止用无参 <c>Kill</c>：带树开关的重载在最低引擎版本上不保证有。
    /// </remarks>
    internal static class GitProcessUtility
    {
        /// <summary>没跑成进程时的退出码。</summary>
        internal const int NOT_RUN_EXIT_CODE = -1;

        /// <summary>
        /// 跑一次 git 并收齐两路输出。
        /// </summary>
        internal static GitRunResult Run(string gitPath, string workingDirectory, string arguments, int timeoutMilliseconds)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();

            try
            {
                using var process = new Process { StartInfo = StartInfo(gitPath, workingDirectory, arguments, false) };
                process.OutputDataReceived += (_, args) => Append(output, args.Data);
                process.ErrorDataReceived += (_, args) => Append(error, args.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    TryKill(process);
                    return new GitRunResult(false, NOT_RUN_EXIT_CODE, output.ToString(),
                        $"git 超时（{timeoutMilliseconds} ms）：{arguments}");
                }

                // 零退出码之后回调仍可能在飞，再等一次把管道余下的行收完。
                process.WaitForExit();
                return new GitRunResult(process.ExitCode == 0, process.ExitCode, output.ToString(), error.ToString());
            }
            catch (Exception exception)
            {
                return new GitRunResult(false, NOT_RUN_EXIT_CODE, output.ToString(),
                    $"git 调用失败：{exception.Message}（{arguments}）");
            }
        }

        /// <summary>
        /// 跑一次 git，把一批名字写进标准输入，标准输出整段取回。
        /// </summary>
        /// <remarks>按行收口会把行尾重排、也数不准字节数，故这里整段读——<c>cat-file --batch</c> 的正文要按声明长度切分。<br />
        /// 标准错误仍异步收齐：它写满管道时 git 会卡在写回调上。</remarks>
        internal static GitRunResult RunWithInput(string gitPath, string workingDirectory, string arguments,
            int timeoutMilliseconds, string standardInput)
        {
            var error = new StringBuilder();

            try
            {
                using var process = new Process { StartInfo = StartInfo(gitPath, workingDirectory, arguments, true) };
                process.ErrorDataReceived += (_, args) => Append(error, args.Data);
                process.Start();
                process.BeginErrorReadLine();

                // 先起读再写输入：输入只有几个对象号，但输出可能远大于管道缓冲。
                var output = process.StandardOutput.ReadToEndAsync();
                if (!string.IsNullOrEmpty(standardInput)) process.StandardInput.Write(standardInput);
                process.StandardInput.Close();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    TryKill(process);
                    return new GitRunResult(false, NOT_RUN_EXIT_CODE, output.GetAwaiter().GetResult(),
                        $"git 超时（{timeoutMilliseconds} ms）：{arguments}");
                }

                process.WaitForExit();
                return new GitRunResult(process.ExitCode == 0, process.ExitCode,
                    output.GetAwaiter().GetResult(), error.ToString());
            }
            catch (Exception exception)
            {
                return new GitRunResult(false, NOT_RUN_EXIT_CODE, string.Empty,
                    $"git 调用失败：{exception.Message}（{arguments}）");
            }
        }

        /// <summary>构造进程外 git 的启动配置：两路输出一律重定向并按 UTF-8 解码，按需重定向标准输入。</summary>
        private static ProcessStartInfo StartInfo(string gitPath, string workingDirectory, string arguments,
            bool redirectStandardInput)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrEmpty(gitPath) ? "git" : gitPath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectStandardInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            if (!string.IsNullOrEmpty(workingDirectory)) startInfo.WorkingDirectory = workingDirectory;
            return startInfo;
        }

        /// <summary>给命令行参数加引号：按 Windows 参数解析规则转义引号与反斜杠。</summary>
        /// <remarks>收尾的反斜杠必须翻倍，否则它会把结束引号吃掉、参数形状就地裂变。</remarks>
        internal static string Quote(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";

            var builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');

            int backslashes = 0;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"') builder.Append('\\', backslashes * 2 + 1).Append('"');
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(c);
                }

                backslashes = 0;
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string line)
        {
            if (line == null) return;
            builder.Append(line).Append('\n');
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // 进程刚好自己退出时杀不掉不算新事实，超时那条原因已经记下。
            }
        }
    }
}
