using System;
using UnityEngine;
using Moirai.Atropos.Resource;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认版本号处理器。
    /// </summary>
    [Serializable]
    public sealed class DefaultVersionHandler : VersionHandler
    {
        public override string GameVersion => "Ver." + Application.version;
        
        public override string InternalGameVersion => string.Empty;

        public override string ResourceVersion => "ResVer." + ResourceService.GetPackageVersion();

        public override string InternalResourceVersion => "InternalResVer." + ResourceService.InternalResourceVersion.ToString();
    }
}