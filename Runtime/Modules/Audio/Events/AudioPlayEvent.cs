using Moirai.Atropos.Events;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 通过事件而非直接调用音频模块来播放音频。
    /// </summary>
    /// <remarks>以避免音频模块未初始化或被释放造成的错误</remarks>
    public class AudioPlayEvent : EventBase<AudioPlayEvent>, IAudioModuleEvent
    {
        /// <summary>
        /// 播放返回的音频句柄（模块自维护）
        /// </summary>
        public ulong AudioHandle { get; set; }

        /// <summary>
        /// 播放配置
        /// </summary>
        public AudioPlayOptions Options { get; private set; }
                
        // ---------------- 直接使用引用的 Clip ----------------
        /// <summary>
        /// 要播放的音频
        /// </summary>
        public AudioClip Clip { get; private set; }
        
        // ---------------- 从资源系统加载 Clip ----------------
        /// <summary>
        /// 音频文件路径
        /// </summary>
        public string AudioPath { get; private set; }
        /// <summary>
        /// 是否异步加载
        /// </summary>
        public bool LoadAssetAsync { get; private set; }
        /// <summary>
        /// 是否缓存已加载资源（适用于多次重复加载的资源）
        /// </summary>
        public bool CacheAssetHandle { get; private set; }

        protected override void Init()
        {
            base.Init();

            AudioHandle = 0;
            Clip = null;
        }

        private static AudioPlayEvent GetPooled(AudioClip clip, AudioPlayOptions options)
        {
            var evt = GetPooled();
            evt.Clip = clip;
            evt.Options = options;
            return evt;
        }

        /// <summary>
        /// 触发音频播放事件，返回音频句柄
        /// </summary>
        /// <param name="clip">音频剪辑</param>
        /// <param name="options">音频播放选项设置</param>
        /// <returns>音频句柄</returns>
        public static ulong Trigger(AudioClip clip, AudioPlayOptions options)
        {
            using var evt = GetPooled(clip, options);
            EventManager.SendEvent(evt, DispatchMode.Immediate);
            return evt.AudioHandle;
        }

        // ---------------- 从资源路径加载 ----------------
        
        private static AudioPlayEvent GetPooled(string path, AudioPlayOptions options, bool bAsync, bool bInPool)
        {
            var evt = GetPooled();
            evt.AudioPath = path;
            evt.Options = options;
            evt.LoadAssetAsync = bAsync;
            evt.CacheAssetHandle = bInPool;
            return evt;
        }

        /// <summary>
        /// 触发音频播放事件，返回音频句柄
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="options">音频播放选项设置。</param>
        /// <param name="bAsync">是否异步加载。</param>
        /// <param name="bInPool">是否缓存已加载资源（适用于多次重复加载的资源）。</param>
        /// <returns>音频句柄</returns>
        public static ulong Trigger(string path, AudioPlayOptions options, bool bAsync, bool bInPool = false)
        {
            using var evt = GetPooled(path, options, bAsync, bInPool);
            EventManager.SendEvent(evt, DispatchMode.Immediate);
            return evt.AudioHandle;
        }
    }
}
