using System;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频服务配置抽象基类（纯数据，无行为无生命周期）。
    /// <para>以 <see cref="UnityEngine.SerializeReference"/> 存于 <see cref="AudioServiceSettings"/> 资产；
    /// 经 <see cref="CreateHandler"/> 工厂创建绑定的后端处理器实例，处理器不再被序列化。</para>
    /// </summary>
    [Serializable]
    public abstract class AudioServiceHandlerConfig
    {
        /// <summary>
        /// 创建配置绑定的音频后端处理器实例。
        /// </summary>
        /// <returns>新的音频处理器实例。</returns>
        public abstract AudioServiceHandler CreateHandler();
    }

    /// <summary>
    /// 音频处理器抽象基类（策略模式抽象策略）。定义 <see cref="AudioService"/> 外观调用的音频后端契约。
    /// <para>配置数据由 <see cref="AudioServiceHandlerConfig"/> 系列纯数据类承载——处理器实例本身不再被序列化，由 <see cref="AudioServiceHandlerConfig.CreateHandler"/> 工厂在运行期创建。</para>
    /// <para>默认实现为 <see cref="UnityAudioHandler"/>（基于 Unity AudioSource/AudioMixer），可替换为自定义音频后端。</para>
    /// <para>场景3D音效挂到场景物件、技能3D音效挂到技能特效上，并在 <see cref="AudioSource"/> 的Output上设置对应分类的 <see cref="AudioMixerGroup"/>。</para>
    /// </summary>
    public abstract class AudioServiceHandler : FrameworkHandler
    {
        #region 处理器属性 [HANDLER PROPERTIES]

        /// <summary>
        /// 音频混响器。
        /// </summary>
        public abstract AudioMixer AudioMixer { get; }

        /// <summary>实例化根节点。</summary>
        public abstract Transform InstanceRoot { get; set; }

        /// <summary>
        /// 资源句柄池，用于缓存资源系统的已加载音频资源（后端原生句柄的 object 包装）。
        /// </summary>
        public abstract Dictionary<string, object> AssetHandlePool { get; }

        #endregion 处理器属性 [HANDLER PROPERTIES]

        #region 音轨状态 [TRACK STATUS]

        /// <summary>
        /// 所有音轨。
        /// </summary>
        public abstract AudioCategory[] AudioCategories { get; }

        /// <summary>
        /// 主音轨（总音量）音量。
        /// </summary>
        /// <remarks>0-1</remarks>
        public abstract float MasterVolume { get; set; }

        /// <summary>
        /// 主音轨（总音量）静音。
        /// </summary>
        public abstract bool MasterMute { get; set; }

        /// <summary>
        /// 写入主音轨（总音量）配置。
        /// </summary>
        public abstract void SetMasterSettings();

        /// <summary>
        /// 加载主音轨（总音量）配置。
        /// </summary>
        public abstract void LoadMasterSettings();

        /// <summary>
        /// 移除主音轨（总音量）设置。
        /// </summary>
        public abstract void RemoveMasterSetting();

        /// <summary>
        /// 获取指定音轨的音量。
        /// </summary>
        public abstract float GetTrackVolume(EAudioTrack track);

        /// <summary>
        /// 设置指定音轨的音量。
        /// </summary>
        public abstract void SetTrackVolume(EAudioTrack track, float volume);

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        public abstract bool GetTrackMute(EAudioTrack track);

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        public abstract void SetTrackMute(EAudioTrack track, bool mute);

        #endregion 音轨状态 [TRACK STATUS]

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 容器 Tick 驱动——轮询音轨与手动过渡。
        /// </summary>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 初始化音频服务。
        /// </summary>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <exception cref="GameException"></exception>
        public abstract void Initialize(Transform instanceRoot = null, AudioMixer audioMixer = null, AudioGroupConfig[] audioGroupConfigs = null);

        /// <summary>
        /// 重启音频服务。
        /// </summary>
        public abstract void Restart();

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public abstract ulong Play(AudioClip clip, AudioPlayOptions options);

        /// <summary>
        /// 播放音频。
        /// </summary>
        public abstract ulong Play(AudioClip clip, EAudioTrack track, Vector3 location,
            bool loop = false,
            float volume = 1, int id = 0, bool fade = false, float fadeInitialVolume = 0, float fadeDuration = 1,
            TweenEase fadeTweenEase = default, bool persistent = false, AudioSource recycleAudioSource = null,
            AudioMixerGroup audioGroup = null, float pitch = 1, float panStereo = 0, float spatialBlend = 0,
            bool soloSingleTrack = false, bool soloAllTracks = false, bool autoUnSoloOnEnd = false,
            bool bypassEffects = false,
            bool bypassListenerEffects = false, bool bypassReverbZones = false, int priority = 128,
            float reverbZoneMix = 1,
            float dopplerLevel = 1, int spread = 0, AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic,
            float minDistance = 1, float maxDistance = 500, bool doNotAutoRecycleIfNotDonePlaying = false,
            float playbackTime = 0, float playbackDuration = 0, Transform attachToTransform = null,
            bool useSpreadCurve = false,
            AnimationCurve spreadCurve = null, bool useCustomRolloffCurve = false,
            AnimationCurve customRolloffCurve = null,
            bool useSpatialBlendCurve = false, AnimationCurve spatialBlendCurve = null,
            bool useReverbZoneMixCurve = false, AnimationCurve reverbZoneMixCurve = null,
            float initialDelay = 0f
            );

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public abstract ulong Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false);

        /// <summary>
        /// 播放音频。
        /// </summary>
        public abstract ulong Play(string path, EAudioTrack track, Vector3 location, bool bAsync = false, bool bInPool = false,
            bool loop = false, float volume = 1.0f, int id = 0,
            bool fade = false, float fadeInitialVolume = 0f, float fadeDuration = 1f, TweenEase fadeTweenEase = default,
            bool persistent = false,
            AudioSource recycleAudioSource = null, AudioMixerGroup audioGroup = null,
            float pitch = 1f, float panStereo = 0f, float spatialBlend = 0.0f,
            bool soloSingleTrack = false, bool soloAllTracks = false, bool autoUnSoloOnEnd = false,
            bool bypassEffects = false, bool bypassListenerEffects = false, bool bypassReverbZones = false,
            int priority = 128, float reverbZoneMix = 1f,
            float dopplerLevel = 1f, int spread = 0, AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic,
            float minDistance = 1f, float maxDistance = 500f,
            bool doNotAutoRecycleIfNotDonePlaying = false, float playbackTime = 0f, float playbackDuration = 0f,
            Transform attachToTransform = null,
            bool useSpreadCurve = false, AnimationCurve spreadCurve = null, bool useCustomRolloffCurve = false,
            AnimationCurve customRolloffCurve = null,
            bool useSpatialBlendCurve = false, AnimationCurve spatialBlendCurve = null,
            bool useReverbZoneMixCurve = false, AnimationCurve reverbZoneMixCurve = null,
            float initialDelay = 0f);

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停指定句柄的音频
        /// </summary>
        public abstract void Pause(ulong handle);

        /// <summary>
        /// 恢复播放指定句柄的音频
        /// </summary>
        public abstract void Unpause(ulong handle);

        /// <summary>
        /// 停止指定句柄的音频
        /// </summary>
        public abstract void Stop(ulong handle, float fadeoutDuration = 0f);

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <summary>
        /// 对每个匹配 ID 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public abstract void ForEachAgentByID(int id, Action<AudioAgent> action);

        /// <summary>
        /// 返回播放过指定 ID 的音频代理（零分配：使用共享缓冲区，调用方需在下次调用前消费结果）。
        /// </summary>
        public abstract IReadOnlyList<AudioAgent> FindAgentsByID(int id);

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public abstract void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action);

        /// <summary>
        /// 返回播放过指定 clip 的音频代理（零分配：使用共享缓冲区，调用方需在下次调用前消费结果）。
        /// </summary>
        public abstract IReadOnlyList<AudioAgent> FindAgentsByClip(AudioClip clip);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量
        /// </summary>
        public abstract int CurrentlyPlayingCount(AudioClip clip);

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。
        /// </summary>
        public abstract AudioAgent GetAgentByHandle(ulong handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        public abstract bool IsPlaying(ulong handle);

        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        public abstract bool IsStopped(ulong handle);

        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        public abstract void ReleaseHandle(ulong handle);

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        public abstract void Pause(EAudioTrack track);

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        public abstract void Unpause(EAudioTrack track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        public abstract bool IsPaused(EAudioTrack track);

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        public abstract void Stop(EAudioTrack track, float fadeoutDuration = 0f);

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public abstract void PauseAll();

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public abstract void UnpauseAll();

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        public abstract void StopAll(float fadeoutDuration = 0f);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        public abstract void StopAllButPersistent(float fadeoutDuration = 0f);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        public abstract void StopAllLooping(float fadeoutDuration = 0f);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量
        /// </summary>
        public abstract void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default);

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）
        /// </summary>
        public abstract void StopFadeMasterTrack();

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量
        /// </summary>
        public abstract void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default);

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）
        /// </summary>
        public abstract void StopFadeTrack(EAudioTrack track);

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。
        /// </summary>
        /// <remarks>使用手动过渡系统，完全零 GC。</remarks>
        public abstract void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase);

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）
        /// </summary>
        public abstract void StopFadeAudio(ulong handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中
        /// </summary>
        public abstract bool SoundIsFadingOut(ulong handle);

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        public abstract void PutInAudioPool(List<string> list);

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        public abstract void RemoveClipFromPool(List<string> list);

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        public abstract void CleanAudioPool();

        #endregion 资源池 [ASSET POOL]
    }
}
