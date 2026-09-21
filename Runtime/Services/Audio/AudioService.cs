using System;
using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Timer;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音效管理外观（Facade），为游戏提供统一的音效播放接口。
    /// <para>统一的静态音频访问入口，通过替换 <see cref="Handler"/> 即可在不同音频后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="AudioServiceSettings"/> 解析；settings 未配置则回退 <see cref="CreateDefaultHandler"/>。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>场景3D音效挂到场景物件、技能3D音效挂到技能特效上，并在 <see cref="AudioSource"/> 的Output上设置对应分类的 <see cref="AudioMixerGroup"/>。</para>
    /// </summary>
    [HandlerHost(typeof(AudioServiceHandler))]
    [ServiceDependency(typeof(DebuggerService), typeof(ResourceService))]
    public partial class AudioService : ServiceBase, IServiceTickable
    {
        /// <summary>前后台切换订阅句柄（注销只能靠它，lambda 事后摘不掉）。</summary>
        private static GameApp.Subscription s_PauseSubscription;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认音频处理器（settings 未配置时的代码兜底）。
        /// </summary>
        /// <returns>默认音频处理器实例。</returns>
        internal static AudioServiceHandler CreateDefaultHandler() => new UnityAudioHandler();

        /// <summary>
        /// 从 <see cref="AudioServiceSettings"/> 解析音频处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——懒加载主路径（settings 已配置时 <see cref="CreateDefaultHandler"/> 被短路）首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static AudioServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<AudioService>();
            return AudioServiceSettings.AudioServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化音频服务。由容器在构建期调用。
        /// <para>确保 <c>AudioService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
        /// 并向游戏内调试器注册调试面板（依赖组合根先注册 <see cref="DebuggerService"/>——外观未就绪时静默跳过）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;

            // 预热 AudioSource 宿主栈池（配置在 AudioServiceSettings）
            if (AudioServiceSettings.WarmupAudioHostPool && Handler?.InstanceRoot != null)
            {
                AudioAgentHostPool.Warmup(Handler.InstanceRoot, AudioServiceSettings.AudioHostWarmupCount);
            }

            // 加载音频设置，必须等一帧设置才能生效
            TimerService.WaitFrame(1, LoadSettings);

            DebuggerService.RegisterDebuggerWindow("Profiler/Audio", new AudioServiceDebuggerWindow());

            // 移动端只有 OnApplicationPause 可靠（Focus 在桌面切窗时也会触发，不应据此静音）
            s_PauseSubscription ??= GameApp.AddOnApplicationPauseListener(HandleApplicationPause);
        }

        /// <summary>
        /// 前后台切换：转发给当前后端。订阅在 <see cref="OnInit"/> 建立、<see cref="OnShutdown"/> 注销，
        /// 生命周期与 <see cref="AudioListener"/> 的挂起状态一致。
        /// </summary>
        private static void HandleApplicationPause(bool paused)
        {
            try
            {
                s_Handler?.OnApplicationPaused(paused);
            }
            catch (Exception e)
            {
                AudioFault.Report($"{nameof(HandleApplicationPause)}", e);
            }
        }

        /// <summary>
        /// 关闭音频服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            DebuggerService.UnregisterDebuggerWindow("Profiler/Audio");

            s_PauseSubscription?.Dispose();
            s_PauseSubscription = null;

            AudioMixService.Shutdown();

            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();

            // 诊断状态跟着服务一起归零，避免重启后首帧异常被上一轮的退避窗口吞掉
            AudioFault.Reset();
            AudioWarnOnce.Reset();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器轮询音轨与手动过渡。
        /// </summary>
        /// <remarks>
        /// 隔离必须在音频内部做：容器的 tick 保护在开发构建下是「记录后重新抛出并打断整轮 tick」，
        /// 一条音的异常于是会连带冻住同帧的输入/UI/存档。发布构建下容器只会隔离本服务，
        /// 这里的退避上报保证两种构建行为一致，且不会因为持续抛异常而刷屏。
        /// </remarks>
        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            var handler = s_Handler;
            if (handler == null) return;

            try
            {
                handler.Tick(elapseSeconds, realElapseSeconds);
            }
            catch (Exception e)
            {
                AudioFault.Report($"{nameof(AudioService)}.{nameof(Tick)}", e);
            }
        }

        #endregion

        #region 属性 [PROPERTIES]
        
        /// <summary>
        /// 音频混响器。
        /// </summary>
        public static AudioMixer AudioMixer => s_Handler?.AudioMixer;

        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public static Transform InstanceRoot
        {
            get => s_Handler?.InstanceRoot;
            set
            {
                if (s_Handler == null) return;
                s_Handler.InstanceRoot = value;
            }
        }

        /// <summary>
        /// 资源句柄池（只读视图），用于缓存资源系统的已加载音频资源（后端原生句柄的 object 包装）。
        /// <para>池条目的增删与租约释放由服务内部配对管理（<see cref="PutInAudioPool"/>/<see cref="RemoveClipFromPool"/>/<see cref="CleanAudioPool"/>），外部请勿直接改写。</para>
        /// </summary>
        public static IReadOnlyDictionary<string, object> AssetHandlePool => s_Handler?.AssetHandlePool;

        #endregion

        #region 音轨状态 [TRACK STATUS]

        /// <summary>
        /// 所有音轨。
        /// </summary>
        public static AudioCategory[] AudioCategories => s_Handler?.AudioCategories;

        /// <summary>
        /// 主音轨（总音量）音量。
        /// </summary>
        /// <remarks>0-1</remarks>
        public static float MasterVolume
        {
            get => s_Handler?.MasterVolume ?? 0f;
            set
            {
                if (s_Handler == null) return;
                s_Handler.MasterVolume = value;
            }
        }

        /// <summary>
        /// 主音轨（总音量）静音。
        /// </summary>
        public static bool MasterMute
        {
            get => s_Handler?.MasterMute ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.MasterMute = value;
            }
        }

        /// <summary>
        /// 获取指定音轨的音量。
        /// </summary>
        public static float GetTrackVolume(EAudioTrack track) => s_Handler?.GetTrackVolume(track) ?? 0f;

        /// <summary>
        /// 设置指定音轨的音量。
        /// </summary>
        public static void SetTrackVolume(EAudioTrack track, float volume) => s_Handler?.SetTrackVolume(track, volume);

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        public static bool GetTrackMute(EAudioTrack track) => s_Handler?.GetTrackMute(track) ?? false;

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        public static void SetTrackMute(EAudioTrack track, bool mute) => s_Handler?.SetTrackMute(track, mute);

        /// <summary>
        /// 写入配置。
        /// </summary>
        /// <remarks>如果需要保存，直接调用 <see cref="SettingUtility.Save"/></remarks>
        public static void SetSettings()
        {
            var handler = s_Handler;
            if (handler == null) return;

            handler.SetMasterSettings();

            var categories = handler.AudioCategories;
            if (categories == null) return;

            foreach (var category in categories)
            {
                category?.SetSettings();
            }
        }

        /// <summary>
        /// 加载配置。
        /// </summary>
        public static void LoadSettings()
        {
            var handler = s_Handler;
            if (handler == null) return;

            handler.LoadMasterSettings();

            var categories = handler.AudioCategories;
            if (categories == null) return;

            foreach (var category in categories)
            {
                category?.LoadSettings();
            }
        }

        /// <summary>
        /// 移除设置。
        /// </summary>
        public static void RemoveSetting()
        {
            var handler = s_Handler;
            if (handler == null) return;

            handler.RemoveMasterSetting();

            var categories = handler.AudioCategories;
            if (categories == null) return;

            foreach (var category in categories)
            {
                category?.RemoveSetting();
            }
        }

        #endregion 音轨状态 [TRACK STATUS]

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 请求混音快照切换（优先级保护，低优先级不可打断高优先级）。
        /// </summary>
        public static bool RequestMixSnapshot(EMixSnapshot snapshot, float blendSeconds = -1f, bool force = false) =>
            AudioMixService.Request(snapshot, blendSeconds, force);

        /// <summary>
        /// 回到默认混音快照。
        /// </summary>
        public static void ResetMixSnapshot(float blendSeconds = -1f) =>
            AudioMixService.ResetToDefault(blendSeconds);

        /// <summary>
        /// 当前混音快照状态。
        /// </summary>
        public static EMixSnapshot CurrentMixSnapshot => AudioMixService.Current;

        /// <summary>
        /// 重启音频服务。
        /// </summary>
        public static void Restart() => s_Handler?.Restart();

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public static ulong Play(AudioClip clip, in AudioPlayOptions options) =>
            s_Handler?.Play(clip, options) ?? 0UL;

        /// <summary>
        /// 16 字节热请求 + 冷参数播放（推荐热路径 API）。cold 可为 null。
        /// </summary>
        public static ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold = null) =>
            s_Handler?.Play(clip, request, cold) ?? 0UL;

        /// <summary>
        /// 播放音频（传统巨型签名重载，仅为兼容保留）。
        /// </summary>
        /// <remarks>内部转发到 <see cref="AudioPlayOptions"/> 路径；新代码请用参数对象。</remarks>
        [Obsolete("使用 Play(AudioClip, in AudioPlayOptions) 或 Play(AudioClip, in AudioPlayRequest, AudioPlayColdParams)", false)]
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
            float minDistance = 1, float maxDistance = 500, bool doNotAutoRecycleIfNotDonePlaying = true,
            float playbackTime = 0, float playbackDuration = 0, Transform attachToTransform = null,
            bool useSpreadCurve = false,
            AnimationCurve spreadCurve = null, bool useCustomRolloffCurve = false,
            AnimationCurve customRolloffCurve = null,
            bool useSpatialBlendCurve = false, AnimationCurve spatialBlendCurve = null,
            bool useReverbZoneMixCurve = false, AnimationCurve reverbZoneMixCurve = null,
            float initialDelay = 0f)
        {
            if (s_Handler == null) return 0UL;

            return s_Handler.Play(clip, AudioServiceHandler.BuildOptions(track, location, loop, volume, id, fade,
                fadeInitialVolume, fadeDuration, fadeTweenEase, persistent, recycleAudioSource, audioGroup, pitch,
                panStereo, spatialBlend, soloSingleTrack, soloAllTracks, autoUnSoloOnEnd, bypassEffects,
                bypassListenerEffects, bypassReverbZones, priority, reverbZoneMix, dopplerLevel, spread, rolloffMode,
                minDistance, maxDistance, doNotAutoRecycleIfNotDonePlaying, playbackTime, playbackDuration,
                attachToTransform, useSpreadCurve, spreadCurve, useCustomRolloffCurve, customRolloffCurve,
                useSpatialBlendCurve, spatialBlendCurve, useReverbZoneMixCurve, reverbZoneMixCurve, initialDelay));
        }

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        /// <remarks>默认异步加载；同步加载（<paramref name="bAsync"/>=false）会阻塞主线程，仅限启动期/预加载场景使用。</remarks>
        public static ulong Play(string path, in AudioPlayOptions options, bool bAsync = true, bool bInPool = false) =>
            s_Handler?.Play(path, options, bAsync, bInPool) ?? 0UL;

        /// <summary>
        /// 播放音频（传统巨型签名重载，仅为兼容保留）。
        /// </summary>
        /// <remarks>内部转发到 <see cref="AudioPlayOptions"/> 路径；默认值与各工厂方法/契约对齐（DoNotAutoRecycle 为 true）。
        /// 默认异步加载；同步加载（<paramref name="bAsync"/>=false）会阻塞主线程，仅限启动期/预加载场景使用。</remarks>
        [Obsolete("使用 Play(AudioClip, in AudioPlayOptions) 或 Play(AudioClip, in AudioPlayRequest, AudioPlayColdParams)", false)]
        public static ulong Play(string path, EAudioTrack track, Vector3 location, bool bAsync = true, bool bInPool = false,
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
            bool doNotAutoRecycleIfNotDonePlaying = true, float playbackTime = 0f, float playbackDuration = 0f,
            Transform attachToTransform = null,
            bool useSpreadCurve = false, AnimationCurve spreadCurve = null, bool useCustomRolloffCurve = false,
            AnimationCurve customRolloffCurve = null,
            bool useSpatialBlendCurve = false, AnimationCurve spatialBlendCurve = null,
            bool useReverbZoneMixCurve = false, AnimationCurve reverbZoneMixCurve = null,
            float initialDelay = 0f)
        {
            if (s_Handler == null) return 0UL;

            return s_Handler.Play(path, AudioServiceHandler.BuildOptions(track, location, loop, volume, id, fade,
                fadeInitialVolume, fadeDuration, fadeTweenEase, persistent, recycleAudioSource, audioGroup, pitch,
                panStereo, spatialBlend, soloSingleTrack, soloAllTracks, autoUnSoloOnEnd, bypassEffects,
                bypassListenerEffects, bypassReverbZones, priority, reverbZoneMix, dopplerLevel, spread, rolloffMode,
                minDistance, maxDistance, doNotAutoRecycleIfNotDonePlaying, playbackTime, playbackDuration,
                attachToTransform, useSpreadCurve, spreadCurve, useCustomRolloffCurve, customRolloffCurve,
                useSpatialBlendCurve, spatialBlendCurve, useReverbZoneMixCurve, reverbZoneMixCurve, initialDelay),
                bAsync, bInPool);
        }

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停指定句柄的音频
        /// </summary>
        public static void Pause(ulong handle) => s_Handler?.Pause(handle);

        /// <summary>
        /// 恢复播放指定句柄的音频
        /// </summary>
        public static void Unpause(ulong handle) => s_Handler?.Unpause(handle);

        /// <summary>
        /// 停止指定句柄的音频
        /// </summary>
        public static void Stop(ulong handle, float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => s_Handler?.Stop(handle, fadeoutDuration);

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <summary>
        /// 对每个匹配 ID 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public static void ForEachAgentByID(int id, Action<AudioAgent> action) => s_Handler?.ForEachAgentByID(id, action);

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public static void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action) => s_Handler?.ForEachAgentByClip(clip, action);

        /// <summary>
        /// 对每个匹配 ID 的 AudioHandle 执行操作（零分配）。
        /// </summary>
        public static void ForEachHandleByID(int id, Action<ulong> action) => s_Handler?.ForEachHandleByID(id, action);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量
        /// </summary>
        public static int CurrentlyPlayingCount(AudioClip clip) => s_Handler?.CurrentlyPlayingCount(clip) ?? 0;

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。
        /// </summary>
        public static AudioAgent GetAgentByHandle(ulong handle) => s_Handler?.GetAgentByHandle(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        public static bool IsPlaying(ulong handle) => s_Handler?.IsPlaying(handle) ?? false;

        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        public static bool IsStopped(ulong handle) => s_Handler?.IsStopped(handle) ?? true;

        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        public static void ReleaseHandle(ulong handle) => s_Handler?.ReleaseHandle(handle);

        /// <summary>
        /// 对指定 ID 的音频进行音量过渡（零 lambda 分配）。
        /// </summary>
        public static void PlayFade(int id, float duration, float finalVolume, TweenEase ease = default) =>
            s_Handler?.PlayFadeByID(id, duration, finalVolume, ease);

        /// <summary>
        /// 停止指定 ID 音频的音量过渡（零 lambda 分配）。
        /// </summary>
        public static void StopFade(int id) => s_Handler?.StopFadeByID(id);

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        public static void PauseTrack(EAudioTrack track) => s_Handler?.PauseTrack(track);

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        public static void UnpauseTrack(EAudioTrack track) => s_Handler?.UnpauseTrack(track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        public static bool IsPaused(EAudioTrack track) => s_Handler?.IsPaused(track) ?? false;

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        public static void StopTrack(EAudioTrack track, float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => s_Handler?.StopTrack(track, fadeoutDuration);

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public static void PauseAll() => s_Handler?.PauseAll();

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public static void UnpauseAll() => s_Handler?.UnpauseAll();

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        public static void StopAll(float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => s_Handler?.StopAll(fadeoutDuration);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        public static void StopAllButPersistent(float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => s_Handler?.StopAllButPersistent(fadeoutDuration);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        public static void StopAllLooping(float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => s_Handler?.StopAllLooping(fadeoutDuration);

        /// <summary>
        /// 停止匹配用户 ID 的全部音频（零 lambda）。用于分层 BGM 同 ID 替换，不影响其它 ID。
        /// </summary>
        public static void StopByID(int id, float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => s_Handler?.StopByID(id, fadeoutDuration);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量
        /// </summary>
        public static void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default) =>
            s_Handler?.FadeMasterTrack(duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）
        /// </summary>
        public static void StopFadeMasterTrack() => s_Handler?.StopFadeMasterTrack();

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量
        /// </summary>
        public static void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default) =>
            s_Handler?.FadeTrack(track, duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）
        /// </summary>
        public static void StopFadeTrack(EAudioTrack track) => s_Handler?.StopFadeTrack(track);

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。
        /// </summary>
        /// <remarks>使用手动过渡系统，完全零 GC。</remarks>
        public static void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase) =>
            s_Handler?.FadeAudio(handle, duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）
        /// </summary>
        public static void StopFadeAudio(ulong handle) => s_Handler?.StopFadeAudio(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中
        /// </summary>
        public static bool SoundIsFadingOut(ulong handle) => s_Handler?.SoundIsFadingOut(handle) ?? false;

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        public static void PutInAudioPool(List<string> list) => s_Handler?.PutInAudioPool(list);

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        public static void RemoveClipFromPool(List<string> list) => s_Handler?.RemoveClipFromPool(list);

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        public static void CleanAudioPool() => s_Handler?.CleanAudioPool();

        /// <summary>预加载地址（默认 Pin 常驻；Lease 保留 + 缓存）。</summary>
        public static bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Pin) =>
            s_Handler?.Preload(address, policy) ?? false;

        /// <summary>异步预加载地址。</summary>
        public static void PreloadAsync(string address, AudioCachePolicy policy = AudioCachePolicy.Pin,
            Action<bool> completed = null) =>
            s_Handler?.PreloadAsync(address, policy, completed);

        /// <summary>卸载地址缓存（force=true 忽略引用计数）。</summary>
        public static bool UnloadClipCache(string address, bool force = false) =>
            s_Handler?.UnloadClipCache(address, force) ?? false;

        /// <summary>清空 Clip 缓存（force=true 连 Pin 一并清）。</summary>
        public static void ClearClipCache(bool force = false) => s_Handler?.ClearClipCache(force);

        #endregion 资源池 [ASSET POOL]

        #region 中间件接入面 [AUTHORING APIS]

        /// <summary>
        /// 加载声音库（FMOD/Wwise）。
        /// </summary>
        /// <returns>后端或桥接不支持、以及加载失败都返回 <c>false</c>。</returns>
        public static bool LoadBank(string bankPath) => s_Handler?.LoadBank(bankPath) ?? false;

        /// <summary>卸载声音库（语义同 <see cref="LoadBank"/>）。</summary>
        public static bool UnloadBank(string bankPath) => s_Handler?.UnloadBank(bankPath) ?? false;

        /// <summary>
        /// 设置实时参数（FMOD event parameter / Wwise RTPC），如 <c>SetRtpc("PlayerHealth", 0.2f)</c>。
        /// </summary>
        /// <remarks>Unity 后端无概念，为空操作。</remarks>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值（线性，量纲由音效师在工程里定义）。</param>
        /// <param name="handle">播放句柄；<c>0</c> 表示工程/全局参数。</param>
        public static void SetRtpc(string name, float value, ulong handle = 0UL) => s_Handler?.SetRtpc(name, value, handle);

        #endregion 中间件接入面 [AUTHORING APIS]
    }
}
