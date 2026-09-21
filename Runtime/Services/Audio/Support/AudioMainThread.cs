namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频侧主线程不变量（内部）。
    /// <para>句柄注册表、Clip 缓存的 LRU/All 双链与引用计数都是无锁裸结构：从工作线程调用播放入口
    /// 不会立刻崩，而是留下偶发错音、漏播、条目失联这类在线上几乎无法归因的症状。</para>
    /// <para>所以开发期直接断言（与 <c>GameServices.EnsureMainThread</c> / <c>MemoryPoolRegistry.AssertMainThread</c>
    /// 同一约定），发布版整体裁剪；跨线程的实际兜底由调用处显式判断 <see cref="IsMainThread"/> 后派发或丢弃。</para>
    /// </summary>
    internal static class AudioMainThread
    {
        /// <summary>当前是否为主线程（发布版同样可用，供调用处做兜底分支）。</summary>
        public static bool IsMainThread => MainThreadDispatcher.IsMainThread;

        /// <summary>
        /// 断言处于主线程。仅编辑器/开发构建生效，发布构建调用点被整体裁剪。
        /// </summary>
        /// <param name="where">调用点标识，出现在断言消息中。</param>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void AssertMainThread(string where)
        {
            UnityEngine.Assertions.Assert.IsTrue(IsMainThread,
                $"[Audio] {where} 必须在主线程调用：句柄表与 Clip 缓存的 LRU/引用计数无跨线程保护。" +
                "后台线程/回调里请用 MainThreadDispatcher.Post 包一层。");
        }
    }
}
