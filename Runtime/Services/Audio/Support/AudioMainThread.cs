namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频侧主线程不变量（内部）：句柄注册表与 Clip 缓存的 LRU/All 双链、引用计数都是无锁裸结构，播放入口必须在主线程调用。
    /// </summary>
    /// <remarks>开发期直接断言，发布版整体裁剪；跨线程兜底由调用处显式判断 <see cref="IsMainThread"/> 后派发或丢弃。</remarks>
    internal static class AudioMainThread
    {
        /// <summary>当前是否为主线程（发布版同样可用，供调用处做兜底分支）。</summary>
        public static bool IsMainThread => MainThreadDispatcher.IsMainThread;

        /// <summary>
        /// 断言处于主线程。仅编辑器/开发构建生效，发布构建调用点被整体裁剪。
        /// </summary>
        /// <remarks>断言通过的主线程快路径零分配：消息插值只在失败分支构造，避免播放入口每帧产生 GC 抖动。</remarks>
        /// <param name="where">调用点标识，出现在断言消息中。</param>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void AssertMainThread(string where)
        {
            if (!IsMainThread)
            {
                UnityEngine.Assertions.Assert.IsTrue(false,
                    $"[Audio] {where} 必须在主线程调用：句柄表与 Clip 缓存的 LRU/引用计数无跨线程保护。" +
                    "后台线程/回调里请用 MainThreadDispatcher.Post 包一层。");
            }
        }
    }
}
