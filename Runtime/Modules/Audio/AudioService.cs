using System;
using System.Collections.Generic;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音效管理外观（Facade），为游戏提供统一的音效播放接口。
    /// <para>统一的静态音频访问入口，通过替换 <see cref="Handler"/> 即可在不同音频后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="AudioServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>场景3D音效挂到场景物件、技能3D音效挂到技能特效上，并在 <see cref="AudioSource"/> 的Output上设置对应分类的 <see cref="AudioMixerGroup"/>。</para>
    /// </summary>
    [HandlerHost(typeof(AudioServiceHandler))]
    [ServiceDependency(typeof(ResourceService))]
    public partial class AudioService : ServiceBase, IServiceTickable
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="AudioServiceSettings"/> 创建默认音频处理器。
        /// </summary>
        /// <returns>默认音频处理器实例。</returns>
        private static AudioServiceHandler CreateDefaultHandler()
        {
            return AudioServiceSettings.AudioServiceHandler;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化音频服务。由容器在构建期调用。
        /// <para>确保 <c>AudioService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
        /// 并向处理器注入资源服务引用。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭音频服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器轮询音轨与手动过渡。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            Handler.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        /// <summary>
        /// 音频混响器。
        /// </summary>
        public static AudioMixer AudioMixer => Handler.AudioMixer;

        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public static Transform InstanceRoot
        {
            get => Handler.InstanceRoot;
            set { Handler.InstanceRoot = value; }
        }

        /// <summary>
        /// 资源句柄池，用于缓存资源系统的已加载音频资源（后端原生句柄的 object 包装）。
        /// </summary>
        public static Dictionary<string, object> AssetHandlePool => Handler.AssetHandlePool;

        #endregion

        #region 音轨状态 [TRACK STATUS]

        /// <summary>
        /// 所有音轨。
        /// </summary>
        public static AudioCategory[] AudioCategories => Handler.AudioCategories;

        /// <summary>
        /// 主音轨（总音量）音量。
        /// </summary>
        /// <remarks>0-1</remarks>
        public static float MasterVolume
        {
            get => Handler.MasterVolume;
            set { Handler.MasterVolume = value; }
        }

        /// <summary>
        /// 主音轨（总音量）静音。
        /// </summary>
        public static bool MasterMute
        {
            get => Handler.MasterMute;
            set { Handler.MasterMute = value; }
        }

        /// <summary>
        /// 写入主音轨（总音量）配置。
        /// </summary>
        public static void SetMasterSettings() => Handler.SetMasterSettings();

        /// <summary>
        /// 加载主音轨（总音量）配置。
        /// </summary>
        public static void LoadMasterSettings() => Handler.LoadMasterSettings();

        /// <summary>
        /// 移除主音轨（总音量）设置。
        /// </summary>
        public static void RemoveMasterSetting() => Handler.RemoveMasterSetting();

        /// <summary>
        /// 获取指定音轨的音量。
        /// </summary>
        public static float GetTrackVolume(EAudioTrack track) => Handler.GetTrackVolume(track);

        /// <summary>
        /// 设置指定音轨的音量。
        /// </summary>
        public static void SetTrackVolume(EAudioTrack track, float volume) => Handler.SetTrackVolume(track, volume);

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        public static bool GetTrackMute(EAudioTrack track) => Handler.GetTrackMute(track);

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        public static void SetTrackMute(EAudioTrack track, bool mute) => Handler.SetTrackMute(track, mute);

        #endregion 音轨状态 [TRACK STATUS]

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 初始化音频服务。
        /// </summary>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <exception cref="GameException"></exception>
        public static void Initialize(Transform instanceRoot = null, AudioMixer audioMixer = null, AudioGroupConfig[] audioGroupConfigs = null) =>
            Handler.Initialize(instanceRoot, audioMixer, audioGroupConfigs);

        /// <summary>
        /// 重启音频服务。
        /// </summary>
        public static void Restart() => Handler.Restart();

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public static ulong Play(AudioClip clip, AudioPlayOptions options) =>
            Handler.Play(clip, options);

        /// <summary>
        /// 播放音频。
        /// </summary>
        public static ulong Play(AudioClip clip, EAudioTrack track, Vector3 location,
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
            ) =>
            Handler.Play(clip, track, location,
                loop, volume, id, fade, fadeInitialVolume, fadeDuration, fadeTweenEase, persistent,
                recycleAudioSource, audioGroup, pitch, panStereo, spatialBlend, soloSingleTrack, soloAllTracks,
                autoUnSoloOnEnd, bypassEffects, bypassListenerEffects, bypassReverbZones, priority, reverbZoneMix,
                dopplerLevel, spread, rolloffMode, minDistance, maxDistance, doNotAutoRecycleIfNotDonePlaying,
                playbackTime, playbackDuration, attachToTransform, useSpreadCurve, spreadCurve,
                useCustomRolloffCurve, customRolloffCurve, useSpatialBlendCurve, spatialBlendCurve,
                useReverbZoneMixCurve, reverbZoneMixCurve, initialDelay);

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public static ulong Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false) =>
            Handler.Play(path, options, bAsync, bInPool);

        /// <summary>
        /// 播放音频。
        /// </summary>
        public static ulong Play(string path, EAudioTrack track, Vector3 location, bool bAsync = false, bool bInPool = false,
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
            float initialDelay = 0f) =>
            Handler.Play(path, track, location, bAsync, bInPool,
                loop, volume, id, fade, fadeInitialVolume, fadeDuration, fadeTweenEase, persistent,
                recycleAudioSource, audioGroup, pitch, panStereo, spatialBlend, soloSingleTrack, soloAllTracks,
                autoUnSoloOnEnd, bypassEffects, bypassListenerEffects, bypassReverbZones, priority, reverbZoneMix,
                dopplerLevel, spread, rolloffMode, minDistance, maxDistance, doNotAutoRecycleIfNotDonePlaying,
                playbackTime, playbackDuration, attachToTransform, useSpreadCurve, spreadCurve,
                useCustomRolloffCurve, customRolloffCurve, useSpatialBlendCurve, spatialBlendCurve,
                useReverbZoneMixCurve, reverbZoneMixCurve, initialDelay);

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停指定句柄的音频
        /// </summary>
        public static void Pause(ulong handle) => Handler.Pause(handle);

        /// <summary>
        /// 恢复播放指定句柄的音频
        /// </summary>
        public static void Unpause(ulong handle) => Handler.Unpause(handle);

        /// <summary>
        /// 停止指定句柄的音频
        /// </summary>
        public static void Stop(ulong handle, float fadeoutDuration = 0f) => Handler.Stop(handle, fadeoutDuration);

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <summary>
        /// 对每个匹配 ID 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public static void ForEachAgentByID(int id, Action<AudioAgent> action) => Handler.ForEachAgentByID(id, action);

        /// <summary>
        /// 返回播放过指定 ID 的音频代理（零分配：使用共享缓冲区，调用方需在下次调用前消费结果）。
        /// </summary>
        public static IReadOnlyList<AudioAgent> FindAgentsByID(int id) => Handler.FindAgentsByID(id);

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public static void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action) => Handler.ForEachAgentByClip(clip, action);

        /// <summary>
        /// 返回播放过指定 clip 的音频代理（零分配：使用共享缓冲区，调用方需在下次调用前消费结果）。
        /// </summary>
        public static IReadOnlyList<AudioAgent> FindAgentsByClip(AudioClip clip) => Handler.FindAgentsByClip(clip);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量
        /// </summary>
        public static int CurrentlyPlayingCount(AudioClip clip) => Handler.CurrentlyPlayingCount(clip);

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。
        /// </summary>
        public static AudioAgent GetAgentByHandle(ulong handle) => Handler.GetAgentByHandle(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        public static bool IsPlaying(ulong handle) => Handler.IsPlaying(handle);

        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        public static bool IsStopped(ulong handle) => Handler.IsStopped(handle);

        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        public static void ReleaseHandle(ulong handle) => Handler.ReleaseHandle(handle);

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        public static void Pause(EAudioTrack track) => Handler.Pause(track);

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        public static void Unpause(EAudioTrack track) => Handler.Unpause(track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        public static bool IsPaused(EAudioTrack track) => Handler.IsPaused(track);

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        public static void Stop(EAudioTrack track, float fadeoutDuration = 0f) => Handler.Stop(track, fadeoutDuration);

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public static void PauseAll() => Handler.PauseAll();

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public static void UnpauseAll() => Handler.UnpauseAll();

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        public static void StopAll(float fadeoutDuration = 0f) => Handler.StopAll(fadeoutDuration);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        public static void StopAllButPersistent(float fadeoutDuration = 0f) => Handler.StopAllButPersistent(fadeoutDuration);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        public static void StopAllLooping(float fadeoutDuration = 0f) => Handler.StopAllLooping(fadeoutDuration);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量
        /// </summary>
        public static void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default) =>
            Handler.FadeMasterTrack(duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）
        /// </summary>
        public static void StopFadeMasterTrack() => Handler.StopFadeMasterTrack();

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量
        /// </summary>
        public static void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default) =>
            Handler.FadeTrack(track, duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）
        /// </summary>
        public static void StopFadeTrack(EAudioTrack track) => Handler.StopFadeTrack(track);

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。
        /// </summary>
        /// <remarks>使用手动过渡系统，完全零 GC。</remarks>
        public static void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase) =>
            Handler.FadeAudio(handle, duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）
        /// </summary>
        public static void StopFadeAudio(ulong handle) => Handler.StopFadeAudio(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中
        /// </summary>
        public static bool SoundIsFadingOut(ulong handle) => Handler.SoundIsFadingOut(handle);

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        public static void PutInAudioPool(List<string> list) => Handler.PutInAudioPool(list);

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        public static void RemoveClipFromPool(List<string> list) => Handler.RemoveClipFromPool(list);

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        public static void CleanAudioPool() => Handler.CleanAudioPool();

        #endregion 资源池 [ASSET POOL]
    }
}
