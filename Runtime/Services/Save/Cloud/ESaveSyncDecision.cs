namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云存档冲突裁决结果（<see cref="SaveSyncConflictResolver"/> 返回值）。
    /// </summary>
    public enum ESaveSyncDecision
    {
        /// <summary>
        /// 采用本地镜像（远端较旧时回传补传）。
        /// </summary>
        UseLocal = 0,

        /// <summary>
        /// 采用远端（刷新本地镜像）。
        /// </summary>
        UseRemote = 1,
    }
}
