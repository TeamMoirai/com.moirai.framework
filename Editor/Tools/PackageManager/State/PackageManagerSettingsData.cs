using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 包管理器设置项。
    /// </summary>
    [Serializable]
    internal sealed class PackageManagerSettingsData
    {
        public string repoUrl = "https://github.com/TeamMoirai/com.moirai.framework.git";
        public string branch = "plugin";
        public string gitPath = "git";
        public string lastFetchAt;
    }
}
