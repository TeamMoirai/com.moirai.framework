namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云存档同步冲突策略（<see cref="CloudSaveStorageBackend"/> 读裁决与回传规则）。
    /// </summary>
    public enum ESaveSyncPolicy
    {
        /// <summary>
        /// 最新者优先：比较本地镜像与远端时间戳，较新一侧胜出（相等视为已同步，取本地镜像避免下载）。
        /// </summary>
        Latest = 0,

        /// <summary>
        /// 本地优先：本地镜像为权威（读取只读镜像；远端较旧时回传补传）。
        /// </summary>
        LocalWins = 1,

        /// <summary>
        /// 云端优先：远端为权威（读取返回远端并刷新本地镜像；远端缺失时本地镜像回传补传；离线降级本地镜像）。
        /// </summary>
        CloudWins = 2,

        /// <summary>
        /// 自定义裁决：逐键委托 <see cref="SaveSyncConflictResolver"/> 决定取舍。
        /// </summary>
        Custom = 3,
    }
}
