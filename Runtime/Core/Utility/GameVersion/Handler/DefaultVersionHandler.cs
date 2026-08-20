using System;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认版本号处理器。
    /// </summary>
    [Serializable]
    public sealed class DefaultVersionHandler : VersionHandler
    {
        protected override void OnInit()
        {
        }

        protected override void Shutdown()
        {
        }

        public override string GameVersion => "Ver." + Application.version;
        
        public override string InternalGameVersion => string.Empty;

        public override string ResourceVersion => "ResVer." + GameApp.Resource.GetPackageVersion();

        public override string InternalResourceVersion => "InternalResVer." + GameApp.Resource.InternalResourceVersion.ToString();
    }
}