namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 阻塞式加载门禁：仅启动/预加载窗口允许路径 Play 走同步加载。
    /// </summary>
    /// <remarks>
    /// 首帧 Tick 后关门，之后的 <c>bAsync:false</c> 被强制改异步并 warn-once，避免运行时把主线程卡在资源 IO 上。
    /// 显式同步预载（<c>Preload</c>）不受此门限制。仅 Unity 后端参与；中间件按事件路径即时下发、无同步资源加载。
    /// </remarks>
    internal static class AudioBlockingLoadGate
    {
        private static bool s_Open = true;

        /// <summary>是否仍处于启动窗口（允许阻塞加载）。仅主线程读写。</summary>
        public static bool IsOpen => s_Open;

        /// <summary>关闭启动窗口（Handler 首次 Tick 调用；Restart 时重新打开）。</summary>
        public static void Close() => s_Open = false;

        /// <summary>重新打开（服务 Restart / 测试 SetUp）。</summary>
        public static void Open() => s_Open = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload() => s_Open = true;
#endif
    }
}
