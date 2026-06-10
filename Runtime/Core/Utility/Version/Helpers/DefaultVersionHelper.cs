using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认版本号辅助器。
    /// </summary>
    public class DefaultVersionHelper : VersionUtility.IVersionHelper
    {
        public string GameVersion => "Ver." + Application.version;
        
        public string InternalGameVersion => string.Empty;

        public string ResourceVersion => "ResVer." + GameModule.Resource.GetPackageVersion();
        
        public string InternalResourceVersion => "InternalResVer." + GameModule.Resource.InternalResourceVersion.ToString();
    }
}