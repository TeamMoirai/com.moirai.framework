using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 插件清单里的一条外部 UPM 包依赖。
    /// </summary>
    [Serializable]
    internal sealed class PluginUpmDependency
    {
        public string id;
        public string version;
    }
}
