using System;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器设置项。
    /// </summary>
    /// <remarks>缺省值不写在这里：每套包源由自己的 <c>PackageManagerWindowBase.PackageManagerProfile</c> 给出，读取时补齐。</remarks>
    [Serializable]
    internal sealed class PackageManagerSettingsData
    {
        public string repoUrl;
        public string branch;
        public string gitPath;
        public string lastFetchAt;
    }
}
