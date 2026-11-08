using System;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 一个已装插件的记账项。
    /// </summary>
    [Serializable]
    internal sealed class InstalledPluginData
    {
        public string id;
        public string version;
        public string commit;
        public string function;
        public string[] tags;
    }
}
