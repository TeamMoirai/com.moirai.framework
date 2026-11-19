using System;
using UnityEngine;
using Moirai.Atropos.Asset;

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

        public override string ResourceVersion => "ResVer." + AssetService.GetPackageVersion();

        public override string InternalResourceVersion => "InternalResVer." + AssetService.InternalResourceVersion.ToString();
    }
}