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
        public PackageManagerSettingsData settings = new PackageManagerSettingsData();
        public List<InstalledPluginData> installed = new List<InstalledPluginData>();
        public List<UpmOwnershipData> upmOwnership = new List<UpmOwnershipData>();
        public PackageManagerCatalogCache catalogCache = new PackageManagerCatalogCache();
    }
}
