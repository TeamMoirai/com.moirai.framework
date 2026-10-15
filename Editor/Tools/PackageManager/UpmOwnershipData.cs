using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一条外部 UPM 依赖的归属记账项。
    /// </summary>
    /// <remarks>
    /// <c>preexisting</c> 为真表示该条目在写入前就存在于工程 <c>manifest.json</c>，
    /// 此时 <c>originalValue</c> 记下用户原写的值，卸载一律不回收。
    /// </remarks>
    [Serializable]
    internal sealed class UpmOwnershipData
    {
        public string packageId;
        public string requiredVersion;
        public List<string> owners;
        public bool preexisting;
        public string originalValue;
    }
}
