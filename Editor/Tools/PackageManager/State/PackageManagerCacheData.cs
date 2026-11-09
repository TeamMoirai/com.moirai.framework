using System;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器的本机缓存：上次刷新时刻、目录缓存与反向同步落点。
    /// </summary>
    /// <remarks>
    /// 这些读数在配置没变时也会随每次刷新变化，或是只在本机有意义的开发期设置，因此不写进 <c>ProjectSettings/</c> 的状态账本，
    /// 单独落在工程 <c>Library/MoiraiCache/</c> 下，文件名取检出目录名（<c>&lt;workspaceFolderName&gt;.cache.json</c>）；
    /// 缓存坏了或删了只是开窗空一次，下一次刷新即重写。
    /// </remarks>
    [Serializable]
    internal sealed class PackageManagerCacheData
    {
        public string lastFetchAt;
        public PackageManagerCatalogCache catalogCache = new PackageManagerCatalogCache();

        /// <summary>反向同步落点：本地 git 工作树根目录，已装插件的改动写回它下面的 <c>&lt;id&gt;/</c>。</summary>
        public string reverseSyncFolder;
    }
}
