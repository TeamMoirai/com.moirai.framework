using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云存档冲突裁决器（<see cref="ESaveSyncPolicy.Custom"/> 时的逐键裁决插拔件；框架插拔件惯例：
    /// [Serializable] 抽象基类，由 <see cref="CloudSaveStorageBackend"/> 以 [SerializeReference] + ProviderDropdown 持有）。
    /// <para>实现须为纯 .NET 逻辑（可在任意线程调用），禁止触达 Unity 主线程 API。</para>
    /// </summary>
    [Serializable]
    public abstract class SaveSyncConflictResolver
    {
        /// <summary>
        /// 裁决单键冲突（本地与远端同时存在时调用；仅一侧存在的条目由后端自动处理，不经裁决）。
        /// </summary>
        /// <param name="key">云端键（相对存档根目录，<c>/</c> 分隔）。</param>
        /// <param name="localEntry">本地镜像条目元信息。</param>
        /// <param name="remoteEntry">远端条目元信息。</param>
        /// <returns>取舍结果。</returns>
        public abstract ESaveSyncDecision Resolve(string key, SaveSyncEntryInfo localEntry, SaveSyncEntryInfo remoteEntry);
    }
}
