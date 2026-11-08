using System;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器的本机缓存：上次刷新时刻与目录缓存。
    /// </summary>
    /// <remarks>
    /// 这些读数在配置没变时也会随每次刷新变化，因此不写进 <c>ProjectSettings/</c> 的状态账本，
    /// 单独落在工程 <c>Library/MoiraiPackageManager/</c> 下；缓存坏了或删了只是开窗空一次，下一次刷新即重写。
    /// </remarks>
    [Serializable]
    internal sealed class PackageManagerCacheData
    {
        public string lastFetchAt;
        public PackageManagerCatalogCache catalogCache = new PackageManagerCatalogCache();
    }
}
