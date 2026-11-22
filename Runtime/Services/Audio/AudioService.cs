using System;
using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Asset;
using Moirai.Atropos.Timer;
using UnityEngine;
using UnityEngine.Audio;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音效管理外观（Facade）：统一静态访问入口 + 后端实例公共缓存 + 常用功能高层封装。
    /// </summary>
    /// <remarks>
    /// 容器是唯一事实源：settings 选出的后端 handler（<see cref="UnityAudioHandler"/> / 中间件家族）作为 <see cref="IAudioService"/> 契约服务注册进 <see cref="GameServices"/>， <br />
    /// 本类缓存该实例（<c>Handler</c> 懒链注册结果回填，槽位 ≡ 容器实例）并把后端基础 API 组合成播放/音轨/混音等常用功能； <br />
    /// 服务互调与测试注入经 <c>GameServices.GetRequiredService&lt;IAudioService&gt;()</c> 解析到真实现。 <br />
    /// 未显式设置处理器时懒链优先从 <see cref="AudioServiceSettings"/> 解析，未配置则回退 <see cref="CreateDefaultHandler"/>； <br />
    /// Handler 属性由 <c>ProviderHandlerHostGenerator</c> 源生成器生成（线程安全懒加载 + 绑定即注册）。
    /// </remarks>
    [ProviderHandlerHost(typeof(IAudioService), typeof(UnityAudioHandler))]
    public static partial class AudioService
    {
        #region 生命周期钩子 [LIFECYCLE HOOKS]

        /// <summary>
        /// 从 <see cref="AudioServiceSettings"/> 解析音频处理器（懒链 settings 来源；注册与缓存回填由生成代码完成）。
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static IAudioService GetHandlerFromSettings() => AudioServiceSettings.AudioServiceHandler;

        /// <summary>前后台切换订阅句柄（注销只能靠它，lambda 事后摘不掉）。</summary>
        private static GameApp.Subscription s_PauseSubscription;

#if UNITY_EDITOR
        /// <summary>
        /// 重置静态状态：兼容关闭 Domain Reload 的编辑器工作流（进入 Play 时清空上一会话残留）。
        /// </summary>
        /// <remarks>兜底未经后端 OnShutdown 的异常退出：<see cref="GameApp.Subscription.Dispose"/> 幂等，先注销再置空。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_PauseSubscription?.Dispose();
            s_PauseSubscription = null;
        }
#endif

        /// <summary>
        /// 建立前后台切换订阅（后端 OnInit 经 Manager 钩子调用）。
        /// </summary>
        /// <remarks>移动端只有 OnApplicationPause 可靠（Focus 在桌面切窗时也会触发，不应据此静音）。</remarks>
        internal static void AttachPauseSubscription()
            => s_PauseSubscription ??= GameApp.AddOnApplicationPauseListener(HandleApplicationPause);

        /// <summary>
        /// 摘除前后台切换订阅（后端 OnShutdown 经 Manager 钩子调用）。
        /// </summary>
        internal static void DetachPauseSubscription()
        {
            s_PauseSubscription?.Dispose();
            s_PauseSubscription = null;
        }

        /// <summary>
        /// 前后台切换回调：把挂起状态转发给当前后端。
        /// </summary>
        /// <remarks>
        /// 订阅在后端 OnInit 建立、OnShutdown 摘除，生命周期跟随 <c>AudioListener</c> 的挂起状态。
        /// </remarks>
        private static void HandleApplicationPause(bool paused)
        {
            try
            {
                Internal_PeekHandler()?.OnApplicationPaused(paused);
            }
            catch (Exception e)
            {
                AudioFault.Report($"{nameof(HandleApplicationPause)}", e);
            }
        }

        #endregion

        #region 属性 [PROPERTIES]
        
        /// <summary>音频混响器。</summary>
        public static AudioMixer AudioMixer => Internal_PeekHandler()?.AudioMixer;

        /// <summary>实例化根节点。</summary>
        public static Transform InstanceRoot
        {
            get => Internal_PeekHandler()?.InstanceRoot;
            set
            {
                if (s_Handler == null) return;
                s_Handler.InstanceRoot = value;
            }
        }

        /// <summary>资源句柄池（只读视图），包装后端原生句柄/租约。</summary>
        /// <remarks>条目增删与租约释放由服务内部配对管理（<see cref="PutInAudioPool"/>/<see cref="RemoveClipFromPool"/>/<see cref="CleanAudioPool"/>）， <br />
        /// 外部请勿直接改写。</remarks>
        public static IReadOnlyDictionary<string, object> AssetHandlePool => Internal_PeekHandler()?.AssetHandlePool;

        /// <summary>Clip 缓存本体（仅同程序集：调试面板取诊断计数用）。中间件后端返回 <c>null</c>。</summary>
        internal static AudioClipCache ClipCacheForDiagnostics => (s_Handler as UnityAudioHandler)?.ClipCache;

        #endregion

        #region 音轨状态 [TRACK STATUS]

        /// <summary>所有音轨。</summary>
        public static AudioCategory[] AudioCategories => Internal_PeekHandler()?.AudioCategories;

        /// <summary>主音轨（总音量）音量。</summary>
        /// <remarks>0-1</remarks>
        public static float MasterVolume
        {
            get => Internal_PeekHandler()?.MasterVolume ?? 0f;
            set
            {
                if (s_Handler == null) return;
                s_Handler.MasterVolume = value;
            }
        }

        /// <summary>主音轨（总音量）静音。</summary>
        public static bool MasterMute
        {
            get => Internal_PeekHandler()?.MasterMute ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.MasterMute = value;
            }
        }

        /// <summary>
        /// 获取指定音轨的音量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetTrackVolume(EAudioTrack track) => Internal_PeekHandler()?.GetTrackVolume(track) ?? 0f;

        /// <summary>
        /// 设置指定音轨的音量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetTrackVolume(EAudioTrack track, float volume) => Internal_PeekHandler()?.SetTrackVolume(track, volume);

        /// <summary>
        /// 获取指定音轨的音高倍率（运行期，默认 1）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetTrackPitch(EAudioTrack track) => Internal_PeekHandler()?.GetTrackPitch(track) ?? AudioGroupConfig.DEFAULT_PITCH;

        /// <summary>
        /// 设置指定音轨的音高倍率，正在播放的声部会按新倍率重算音高。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetTrackPitch(EAudioTrack track, float pitch) => Internal_PeekHandler()?.SetTrackPitch(track, pitch);

        /// <summary>
        /// 把所有音轨的音高倍率重置为 1。
        /// </summary>
        public static void ResetAllTrackPitches()
        {
            var handler = s_Handler;
            if (handler == null) return;

            var tracks = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));
            for (int i = 0; i < tracks.Length; i++)
            {
                handler.SetTrackPitch(tracks[i], AudioGroupConfig.DEFAULT_PITCH);
            }
        }

        /// <summary>
        /// 设置指定句柄声部的基础音高（不含音轨倍率），并按当前音轨倍率重算实际音高。
        /// </summary>
        /// <remarks>Unity 后端走 Agent；中间件后端按声部表直推原生实例（桥接 <see cref="Middleware.IAudioMiddlewareBridge.SetInstancePitch"/>）。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetPitch(ulong handle, float pitch) => Internal_PeekHandler()?.SetVoicePitch(handle, pitch);

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetTrackMute(EAudioTrack track) => Internal_PeekHandler()?.GetTrackMute(track) ?? false;

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetTrackMute(EAudioTrack track, bool mute) => Internal_PeekHandler()?.SetTrackMute(track, mute);

        /// <summary>
        /// 写入配置。
        /// </summary>
        /// <remarks>如果需要保存，直接调用 <see cref="GameSettings.Save"/></remarks>
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

        /// <summary>当前混音快照状态。</summary>
        public static EMixSnapshot CurrentMixSnapshot => AudioMixService.Current;

        /// <summary>
        /// 重启音频服务。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Restart() => Internal_PeekHandler()?.Restart();

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        /// <remarks>每次播放从池取一份冷参并写全 AudioSource 空间整形；热路径连发改用 <see cref="AudioPlayRequest"/> 重载（cold 传 null 即零堆对象）。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Play(AudioClip clip, in AudioPlayOptions options) =>
            Internal_PeekHandler()?.Play(clip, options) ?? 0UL;

        /// <summary>
        /// 16 字节热请求 + 冷参数播放（推荐热路径 API）。
        /// </summary>
        /// <remarks>
        /// <paramref name="cold"/> 为 null 时零堆对象，且不写 AudioSource 空间整形，语义见 <see cref="AudioPlayColdParams"/>。<br />
        /// 非 null 时所有权转移给服务：调用方不得再使用或自行归还，同一实例也不可在上一次播放结束前重复投递。<br />
        /// 每次播放都要带空间整形与淡入/时间参数时改走 <see cref="AudioPlayOptions"/> 门面。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold = null) =>
            Internal_PeekHandler()?.Play(clip, request, cold) ?? 0UL;

        /// <summary>
        /// 在指定位置播放音频（常用 3D SFX 入口）；其余参数从 <see cref="AudioPlayOptions.Create(EAudioTrack)"/> 起覆盖。
        /// </summary>
        public static ulong Play(AudioClip clip, EAudioTrack track, Vector3 location, float volume = 1, bool loop = false)
        {
            AudioPlayOptions options = AudioPlayOptions.Create(track);
            options.Location = location;
            options.Volume = volume;
            options.Loop = loop;
            return Internal_PeekHandler()?.Play(clip, options) ?? 0UL;
        }

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        /// <remarks>默认异步加载；同步加载（<paramref name="bAsync"/>=false）会阻塞主线程，仅限启动期/预加载场景使用。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Play(string path, in AudioPlayOptions options, bool bAsync = true) =>
            Internal_PeekHandler()?.Play(path, options, bAsync) ?? 0UL;
        
        /// <summary>
        /// 在指定位置播放音频（常用 3D SFX 入口）；其余参数从 <see cref="AudioPlayOptions.Create(EAudioTrack)"/> 起覆盖。
        /// </summary>
        /// <remarks>默认异步加载；同步加载（<paramref name="bAsync"/>=false）会阻塞主线程，仅限启动期/预加载场景使用。</remarks>
        public static ulong Play(string path, EAudioTrack track, Vector3 location, float volume = 1, bool loop = false, bool bAsync = true)
        {
            AudioPlayOptions options = AudioPlayOptions.Create(track);
            options.Location = location;
            options.Volume = volume;
            options.Loop = loop;
            return Internal_PeekHandler()?.Play(path, options, bAsync) ?? 0UL;
        }

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停指定句柄的音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Pause(ulong handle) => Internal_PeekHandler()?.Pause(handle);

        /// <summary>
        /// 恢复播放指定句柄的音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unpause(ulong handle) => Internal_PeekHandler()?.Unpause(handle);

        /// <summary>
        /// 停止指定句柄的音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(ulong handle, float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => Internal_PeekHandler()?.Stop(handle, fadeoutDuration);

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <summary>
        /// 对每个匹配 ID 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEachAgentByID(int id, Action<AudioAgent> action) => Internal_PeekHandler()?.ForEachAgentByID(id, action);

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action) => Internal_PeekHandler()?.ForEachAgentByClip(clip, action);

        /// <summary>
        /// 对每个匹配 ID 的 AudioHandle 执行操作（零分配）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEachHandleByID(int id, Action<ulong> action) => Internal_PeekHandler()?.ForEachHandleByID(id, action);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CurrentlyPlayingCount(AudioClip clip) => Internal_PeekHandler()?.CurrentlyPlayingCount(clip) ?? 0;

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AudioAgent GetAgentByHandle(ulong handle) => Internal_PeekHandler()?.GetAgentByHandle(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPlaying(ulong handle) => Internal_PeekHandler()?.IsPlaying(handle) ?? false;

        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsStopped(ulong handle) => Internal_PeekHandler()?.IsStopped(handle) ?? true;

        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReleaseHandle(ulong handle) => Internal_PeekHandler()?.ReleaseHandle(handle);

        /// <summary>
        /// 对指定 ID 的音频进行音量过渡（零 lambda 分配）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayFade(int id, float duration, float finalVolume, TweenEase ease = default) =>
            Internal_PeekHandler()?.PlayFadeByID(id, duration, finalVolume, ease);

        /// <summary>
        /// 停止指定 ID 音频的音量过渡（零 lambda 分配）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopFade(int id) => Internal_PeekHandler()?.StopFadeByID(id);

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PauseTrack(EAudioTrack track) => Internal_PeekHandler()?.PauseTrack(track);

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnpauseTrack(EAudioTrack track) => Internal_PeekHandler()?.UnpauseTrack(track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPaused(EAudioTrack track) => Internal_PeekHandler()?.IsPaused(track) ?? false;

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopTrack(EAudioTrack track, float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => Internal_PeekHandler()?.StopTrack(track, fadeoutDuration);

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PauseAll() => Internal_PeekHandler()?.PauseAll();

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnpauseAll() => Internal_PeekHandler()?.UnpauseAll();

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopAll(float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => Internal_PeekHandler()?.StopAll(fadeoutDuration);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopAllButPersistent(float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => Internal_PeekHandler()?.StopAllButPersistent(fadeoutDuration);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopAllLooping(float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => Internal_PeekHandler()?.StopAllLooping(fadeoutDuration);

        /// <summary>
        /// 停止匹配用户 ID 的全部音频（零 lambda）。用于分层 BGM 同 ID 替换，不影响其它 ID。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopByID(int id, float fadeoutDuration = AudioAgent.FADEOUT_DEFAULT_DURATION) => Internal_PeekHandler()?.StopByID(id, fadeoutDuration);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default) =>
            Internal_PeekHandler()?.FadeMasterTrack(duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopFadeMasterTrack() => Internal_PeekHandler()?.StopFadeMasterTrack();

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default) =>
            Internal_PeekHandler()?.FadeTrack(track, duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopFadeTrack(EAudioTrack track) => Internal_PeekHandler()?.StopFadeTrack(track);

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。
        /// </summary>
        /// <remarks>使用手动过渡系统，完全零 GC。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase) =>
            Internal_PeekHandler()?.FadeAudio(handle, duration, initialVolume, finalVolume, tweenEase);

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopFadeAudio(ulong handle) => Internal_PeekHandler()?.StopFadeAudio(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SoundIsFadingOut(ulong handle) => Internal_PeekHandler()?.SoundIsFadingOut(handle) ?? false;

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PutInAudioPool(List<string> list) => Internal_PeekHandler()?.PutInAudioPool(list);

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveClipFromPool(List<string> list) => Internal_PeekHandler()?.RemoveClipFromPool(list);

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CleanAudioPool() => Internal_PeekHandler()?.CleanAudioPool();

        /// <summary>
        /// 预加载地址（默认 Pin 常驻；Lease 保留 + 缓存）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Preload(string address, EAudioCachePolicy policy = EAudioCachePolicy.Pin) =>
            Internal_PeekHandler()?.Preload(address, policy) ?? false;

        /// <summary>
        /// 异步预加载地址。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PreloadAsync(string address, EAudioCachePolicy policy = EAudioCachePolicy.Pin,
            Action<bool> completed = null) =>
            Internal_PeekHandler()?.PreloadAsync(address, policy, completed);

        /// <summary>
        /// 卸载地址缓存（force=true 忽略引用计数）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UnloadClipCache(string address, bool force = false) =>
            Internal_PeekHandler()?.UnloadClipCache(address, force) ?? false;

        /// <summary>
        /// 清空 Clip 缓存（force=true 连 Pin 一并清）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearClipCache(bool force = false) => Internal_PeekHandler()?.ClearClipCache(force);

        #endregion 资源池 [ASSET POOL]

        #region 中间件接入面 [AUTHORING APIS]

        /// <summary>
        /// 加载声音库（FMOD/Wwise）。
        /// </summary>
        /// <returns>后端或桥接不支持、以及加载失败都返回 <c>false</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LoadBank(string bankPath) => Internal_PeekHandler()?.LoadBank(bankPath) ?? false;

        /// <summary>
        /// 卸载声音库（语义同 <see cref="LoadBank"/>）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UnloadBank(string bankPath) => Internal_PeekHandler()?.UnloadBank(bankPath) ?? false;

        /// <summary>
        /// 设置实时参数（FMOD event parameter / Wwise RTPC），如 <c>SetRtpc("PlayerHealth", 0.2f)</c>。
        /// </summary>
        /// <remarks>Unity 后端无概念，为空操作。</remarks>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值（线性，量纲由音效师在工程里定义）。</param>
        /// <param name="handle">播放句柄；<c>0</c> 表示工程/全局参数。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetRtpc(string name, float value, ulong handle = 0UL) => Internal_PeekHandler()?.SetRtpc(name, value, handle);

        #endregion 中间件接入面 [AUTHORING APIS]
    }
}
