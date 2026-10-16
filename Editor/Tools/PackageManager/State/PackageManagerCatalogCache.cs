using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 最近一次扫分支的结果缓存：开窗先拿它铺列表，随后后台重扫覆盖。
    /// </summary>
    /// <remarks>
    /// 连当时的远端与分支一起记：读的时候对不上（换过源）即作废，不拿旧源的结果充数。<br />
    /// 只是缓存——它坏了或删了最多是开窗空一次，下一次扫分支就会重写。
    /// </remarks>
    [Serializable]
    internal sealed class PackageManagerCatalogCache
    {
        public string repoUrl;
        public string branch;
        public string headCommit;
        public List<PluginManifest> manifests = new List<PluginManifest>();
    }
}
