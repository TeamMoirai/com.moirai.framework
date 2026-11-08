using System;

namespace Moirai.Atropos.PackageManager
{
    public abstract partial class PackageManagerWindowBase
    {
        /// <summary>
        /// 一个包管理器窗口的身份、落点、源缺省与文案配置。
        /// </summary>
        /// <remarks>
        /// 一份 Profile 对应一套独立的包源：状态账本、检出目录与宿主包版本量取目标互不共享，
        /// 因此同一个工程里可以并存多个包管理器（如框架插件与玩法模块各一套）。<br />
        /// <see cref="StateFileName"/> 与 <see cref="WorkspaceFolderName"/> 只收单段名字，不得含路径分隔符与上跳段。<br />
        /// <see cref="HostPackageType"/> 决定 <c>minFrameworkVersion</c> / <c>maxFrameworkVersion</c> 拿哪个包版本来判：
        /// 空引用表示本轮不判这条区间。
        /// </remarks>
        public sealed class PackageManagerProfile
        {
            /// <summary>
            /// 建一份包源配置；三个身份项之外的可选项按框架插件源的现状取值。
            /// </summary>
            /// <param name="windowTitle">窗口标题。</param>
            /// <param name="stateFileName">状态账本文件名，落在工程 <c>ProjectSettings/</c> 下。</param>
            /// <param name="workspaceFolderName">检出（clone）落点目录名，落在工程 <c>Library/</c> 下。</param>
            public PackageManagerProfile(string windowTitle, string stateFileName, string workspaceFolderName)
            {
                WindowTitle = RequirePlainText(windowTitle, nameof(windowTitle));
                StateFileName = RequirePlainFileName(stateFileName, nameof(stateFileName));
                WorkspaceFolderName = RequirePlainFileName(workspaceFolderName, nameof(workspaceFolderName));
            }

            /// <summary>窗口标题。</summary>
            public string WindowTitle { get; }

            /// <summary>状态账本文件名，落在 <c>ProjectSettings/</c> 下。</summary>
            public string StateFileName { get; }

            /// <summary>检出（clone）落点目录名，落在 <c>Library/</c> 下。</summary>
            /// <remarks>实际安装的包不在这里：每个包装在 <c>Packages/&lt;本目录名&gt;.&lt;id&gt;/</c>。</remarks>
            public string WorkspaceFolderName { get; }

            /// <summary>远端地址缺省值。</summary>
            public string DefaultRepoUrl { get; set; } = string.Empty;

            /// <summary>分支名缺省值。</summary>
            public string DefaultBranch { get; set; } = "plugin";

            /// <summary>git 可执行文件缺省值；空表示按 <c>git</c> 调用。</summary>
            public string DefaultGitPath { get; set; } = "git";

            /// <summary>宿主包版本的量取目标；空引用表示不判宿主版本区间。</summary>
            public Type HostPackageType { get; set; }

            /// <summary>单个可装包在界面上的称法（如「插件」「模块」）。</summary>
            public string EntryNoun { get; set; } = "插件";

            /// <summary>分支上一批包的称法（如「包」「模块」），用于计数与结果句。</summary>
            public string BranchEntryNoun { get; set; } = "包";

            /// <summary>设置面里源设置节点与工具栏设置面的称法。</summary>
            public string SourceNodeName { get; set; } = "分支包源";

            /// <summary>清单未写 <c>function</c> 时的分组名。</summary>
            public string UngroupedNodeName { get; set; } = "未分组";

            /// <summary>宿主包在界面上的称法（如「框架」「本体」），用于版本区间与冲突文案。</summary>
            public string HostLabel { get; set; } = "框架";

            /// <summary>写进 Console 的日志前缀。</summary>
            public string LogPrefix { get; set; } = "[PackageManager]";

            /// <summary>
            /// 建一份该包源的缺省设置；账本缺失或设置项为空时由账本层取用。
            /// </summary>
            internal PackageManagerSettingsData CreateDefaultSettings()
            {
                return new PackageManagerSettingsData
                {
                    repoUrl = DefaultRepoUrl,
                    branch = DefaultBranch,
                    gitPath = DefaultGitPath
                };
            }

            private static string RequirePlainText(string value, string parameterName)
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("窗口标题不能为空", parameterName);

                return value;
            }

            private static string RequirePlainFileName(string value, string parameterName)
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("名字不能为空", parameterName);

                if (value == "." || value == "..")
                    throw new ArgumentException($"'{value}' 不是可用的名字", parameterName);

                if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0)
                    throw new ArgumentException($"'{value}' 不得含路径分隔符", parameterName);

                return value;
            }
        }
    }
}
