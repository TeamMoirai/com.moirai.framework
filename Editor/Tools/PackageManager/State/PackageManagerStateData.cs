using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 包管理器落盘的状态整体。
    /// </summary>
    [Serializable]
    internal sealed class PackageManagerStateData
    {
        public PackageManagerSettingsData settings;
        public List<InstalledPluginData> installed;
        public List<UpmOwnershipData> upmOwnership;
    }
}
