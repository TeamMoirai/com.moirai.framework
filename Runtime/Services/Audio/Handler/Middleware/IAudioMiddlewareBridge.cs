using UnityEngine;

namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 音频中间件统一桥接接口（FMOD / Wwise 共用契约）。
    /// <para>预编译宏约定：<c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c> 切换真 SDK 桥；未定义时用 Stub。</para>
    /// </summary>
    public interface IAudioMiddlewareBridge
    {
        /// <summary>初始化音频系统。</summary>
        bool Initialize(Transform instanceRoot);

        /// <summary>关闭音频系统。</summary>
        void Shutdown();

        /// <summary>每帧更新。</summary>
        void Update(float unscaledDeltaTime);

        /// <summary>播放事件，返回原生实例 ID（0 失败）。</summary>
        ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D);

        /// <summary>停止实例。</summary>
        void StopInstance(ulong instanceId, bool immediate);

        /// <summary>暂停/恢复。</summary>
        void SetPaused(ulong instanceId, bool paused);

        /// <summary>设置实例线性音量。</summary>
        void SetInstanceVolume(ulong instanceId, float volume);

        /// <summary>设置总线线性音量（busPath 如 bus:/Master）。</summary>
        void SetBusVolume(string busPath, float volume);

        /// <summary>获取总线线性音量。</summary>
        float GetBusVolume(string busPath);

        /// <summary>实例是否在播。</summary>
        bool IsPlaying(ulong instanceId);

        /// <summary>从 AudioClip 推导事件路径。</summary>
        string GetEventPathFromClip(AudioClip clip);
    }
}
