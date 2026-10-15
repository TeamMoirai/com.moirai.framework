using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 包管理器设置项。
    /// </summary>
    [Serializable]
    internal sealed class PackageManagerSettingsData
    {
        public string repoUrl;
        public string branch;
        public string gitPath;
        public string lastFetchAt;
    }
}
