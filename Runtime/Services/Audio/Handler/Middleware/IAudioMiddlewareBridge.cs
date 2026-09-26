using UnityEngine;

namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 音频中间件统一桥接接口（FMOD / Wwise 共用契约）。
    /// <para>预编译宏约定：<c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c> 切换真 SDK 桥；未定义时用 Stub。</para>
    /// </summary>
    internal interface IAudioMiddlewareBridge
    {
        /// <summary>初始化音频系统。</summary>
        bool Initialize(Transform instanceRoot);

        /// <summary>关闭音频系统。</summary>
        void Shutdown();

        /// <summary>每帧更新。</summary>
        void Update(float unscaledDeltaTime);

        /// <summary>
        /// 播放事件，返回原生实例 ID（0 失败）。
        /// </summary>
        /// <remarks><paramref name="loop"/> 为尽力生效：FMOD 桥支持运行时设置循环模式；Wwise 桥忽略该参数（循环由 Wwise 工程侧事件配置）。</remarks>
        ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D);

        /// <summary>
        /// 停止实例。
        /// </summary>
        /// <remarks>
        /// <paramref name="immediate"/> 为 <c>false</c> 时要求「带尾音地停」：实现里不得紧接着做
        /// 会立刻终止播放的收尾动作（FMOD 的 <c>EventInstance.release()</c>、Wwise 的发射体回收/停用），
        /// 否则淡出被掐掉、听感与 immediate 无差别。
        /// <para>当前 <c>MiddlewareAudioHandler</c> 一律传 <c>true</c>（淡出由上层先走音量 Fade 到 0 再立即停），
        /// 所以该分支尚未被生产路径覆盖——接真 SDK 时按上线门槛 G1 单独验一次，别默认它可用。</para>
        /// </remarks>
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
