using System;
using System.Collections.Generic;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器落盘的状态整体。
    /// </summary>
    /// <remarks>设置项为空表示这份账本还没写过设置，读侧按各自包源的缺省值补齐。</remarks>
    [Serializable]
    internal sealed class PackageManagerStateData
    {
        public PackageManagerSettingsData settings;
        public List<InstalledPluginData> installed = new List<InstalledPluginData>();
        public List<UpmOwnershipData> upmOwnership = new List<UpmOwnershipData>();
        public PackageManagerCatalogCache catalogCache = new PackageManagerCatalogCache();
    }
}
