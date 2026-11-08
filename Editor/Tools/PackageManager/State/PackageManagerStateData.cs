using System;
using System.Collections.Generic;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器落盘的状态整体：设置项、已装记录与 UPM 依赖归属。
    /// </summary>
    /// <remarks>设置项为空表示这份账本还没写过设置，读侧按各自包源的缺省值补齐。<br />
    /// 刷新产物（上次刷新与目录缓存）不在这里：它们配置没变也会变，住 <see cref="PackageManagerCacheData"/> 的本机缓存文件。</remarks>
    [Serializable]
    internal sealed class PackageManagerStateData
    {
        public PackageManagerSettingsData settings;
        public List<InstalledPluginData> installed = new List<InstalledPluginData>();
        public List<UpmOwnershipData> upmOwnership = new List<UpmOwnershipData>();
    }
}
