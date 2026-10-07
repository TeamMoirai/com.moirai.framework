using System;
using UnityEngine;
using Moirai.Atropos.Resource;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认版本号处理器。
    /// </summary>
    [ProviderDisplay(title: "默认版本号", description: "游戏版本读 Application.version，资源版本读资源服务清单")]
    [Serializable]
    internal sealed class DefaultVersionHandler : VersionHandler
    {
        public override string GameVersion => "Ver." + Application.version;
        
        public override string InternalGameVersion => string.Empty;

        public override string ResourceVersion => "ResVer." + ResourceService.GetPackageVersion();

        public override string InternalResourceVersion => "InternalResVer." + ResourceService.InternalResourceVersion.ToString();
    }
}