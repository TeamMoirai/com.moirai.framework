using System;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器设置项。
    /// </summary>
    /// <remarks>缺省值不写在这里：每套包源由自己的 <c>PackageManagerWindowBase.PackageManagerProfile</c> 给出，读取时补齐。<br />
    /// 只收配置项：像"上次刷新"这种配置没变也会变的读数住 <see cref="PackageManagerCacheData"/>，不进这里。</remarks>
    [Serializable]
    internal sealed class PackageManagerSettingsData
    {
        public string repoUrl;
        public string branch;
        public string gitPath;
    }
}
