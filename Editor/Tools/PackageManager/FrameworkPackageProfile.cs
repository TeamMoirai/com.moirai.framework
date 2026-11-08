namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 框架插件源这一套包源配置。
    /// </summary>
    /// <remarks>
    /// 单独成档而不挂在窗口类上：账本缓存与 Provider 下拉也要按同一份身份读同一份状态文件，
    /// 那些调用方不该为了取一个常量而触碰 Odin 窗口类型。
    /// </remarks>
    internal static class FrameworkPackageProfile
    {
        /// <summary>框架插件源的身份、落点、源缺省与文案。</summary>
        internal static readonly PackageManagerWindowBase.PackageManagerProfile Instance = new PackageManagerWindowBase.PackageManagerProfile(
                "Framework Package Manager", "MoiraiFrameworkPackage.json", "com.moirai.framework.plugin")
            {
                DefaultRepoUrl = "https://github.com/TeamMoirai/com.moirai.framework.git",
                DefaultBranch = "plugin",
                DefaultGitPath = "git",
                HostPackageType = typeof(FrameworkPackageProfile),
                EntryNoun = "插件",
                BranchEntryNoun = "包",
                SourceNodeName = "分支包源",
                UngroupedNodeName = "未分组",
                HostLabel = "框架",
                LogPrefix = "[FrameworkPackage]"
            };
    }
}
