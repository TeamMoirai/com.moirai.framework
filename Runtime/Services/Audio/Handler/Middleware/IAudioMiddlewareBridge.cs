using UnityEngine;

namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 音频中间件统一桥接接口（FMOD / Wwise 共用契约）。
    /// </summary>
    /// <remarks>预编译宏约定：<c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c> 切换真 SDK 桥，未定义时用 Stub。</remarks>
    internal interface IAudioMiddlewareBridge
    {
        /// <summary>
        /// 初始化音频系统。
        /// </summary>
        bool Initialize(Transform instanceRoot);

        /// <summary>
        /// 关闭音频系统。
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 每帧更新。
        /// </summary>
        void Update(float unscaledDeltaTime);

        /// <summary>
        /// 播放事件，返回原生实例 ID（0 失败）。
        /// </summary>
        /// <remarks><paramref name="loop"/> 为尽力生效：FMOD 桥支持运行时设置循环模式；Wwise 桥忽略该参数（循环由 Wwise 工程侧事件配置）。</remarks>
        ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D);

        /// <summary>
        /// 停止实例（一律立即，后端不自带淡出分支）。
        /// </summary>
        /// <remarks>
        /// 带尾音的停止在 <c>MiddlewareAudioHandler</c> 侧排程：先把音量 Fade 到 0 并记入待停时刻表，
        /// 到点才调本方法。实现侧因此不得再插第二段淡出——那会与框架的淡出叠成双份时长；
        /// 也不得把「停」与「回收发射体」拆成两步异步收尾，待停表已经保证调用发生时尾音走完了。
        /// </remarks>
        void StopInstance(ulong instanceId);

        /// <summary>
        /// 暂停/恢复。
        /// </summary>
        void SetPaused(ulong instanceId, bool paused);

        /// <summary>
        /// 设置实例线性音量。
        /// </summary>
        void SetInstanceVolume(ulong instanceId, float volume);

        /// <summary>
        /// 设置总线线性音量（busPath 如 bus:/Master）。
        /// </summary>
        void SetBusVolume(string busPath, float volume);

        /// <summary>
        /// 获取总线线性音量。
        /// </summary>
        float GetBusVolume(string busPath);

        /// <summary>
        /// 实例是否在播。
        /// </summary>
        bool IsPlaying(ulong instanceId);

        /// <summary>
        /// 从 AudioClip 推导事件路径。
        /// </summary>
        string GetEventPathFromClip(AudioClip clip);
    }
}
