using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频处理器抽象基类（策略模式抽象策略）。定义 <see cref="AudioService"/> 外观调用的音频后端契约。
    /// <para>默认实现为 <see cref="UnityAudioHandler"/>（基于 Unity AudioSource/AudioMixer），可替换为自定义音频后端。</para>
    /// <para>场景3D音效挂到场景物件、技能3D音效挂到技能特效上，并在 <see cref="AudioSource"/> 的Output上设置对应分类的 <see cref="AudioMixerGroup"/>。</para>
    /// <para>跨后端语义约定（Unity / 中间件保持一致）：</para>
    /// <para>1. 暂停的音轨会拦截新播放（<see cref="Play"/> 直接返回 0）；</para>
    /// <para>2. <see cref="MasterVolume"/> getter 始终返回未静音的设置值（静音只影响实际输出）；</para>
    /// <para>3. 主音量与音轨音量都是线性 <c>0..1</c>，且夹取只发生在契约入口一次——
    /// 曾经 Unity 侧允许 0..10 而中间件落总线时偷偷 Clamp01，同一份设置换后端上限就从 10 变 1；</para>
    /// <para>4. Master/音轨 Fade 经共享 <see cref="AudioFadeScheduler"/> 驱动，带缓动且可中途停止；</para>
    /// <para>5. 句柄生命周期与用户 ID 映射由共享 <see cref="AudioHandleRegistry{TVoice}"/> 保证；</para>
    /// <para>6. 后端整体失效时（<see cref="IsBackendInert"/>）音量面读作 0、写作无效——两后端同一个口径，
    /// 不允许出现"报着一个音量却完全听不见"的第三种状态。</para>
    /// <para>Unity 专属成员（中间件后端返回 null/空操作）见各成员 remarks；中间件不支持 InitialDelay / PlaybackDuration / Solo。</para>
    /// </summary>
    [Serializable]
    internal abstract class AudioServiceHandler : FrameworkHandler
    {
        #region 处理器属性 [HANDLER PROPERTIES]

        /// <summary>
        /// 音频混响器。
        /// </summary>
        /// <remarks>Unity 专属；中间件后端返回 null。</remarks>
        public abstract AudioMixer AudioMixer { get; }

        /// <summary>实例化根节点。</summary>
        public abstract Transform InstanceRoot { get; set; }

        /// <summary>
        /// 已缓存音频资源的只读视图（后端原生句柄/租约的 object 包装）。
        /// </summary>
        /// <remarks>
        /// 条目的增删与租约释放由服务内部配对管理（<see cref="PutInAudioPool"/> / <see cref="RemoveClipFromPool"/> /
        /// <see cref="CleanAudioPool"/>，以及 Clip 缓存自身的驱逐与卸载），因此这里只给只读形态：
        /// 外部若直接从字典里摘走一项，租约就脱离了引用计数，等于一条没人会释放的后端引用。
        /// <para>中间件后端仅作键值占位，不持有真实资源句柄。</para>
        /// </remarks>
        public abstract IReadOnlyDictionary<string, object> AssetHandlePool { get; }

        #endregion 处理器属性 [HANDLER PROPERTIES]

        #region 音轨状态 [TRACK STATUS]

        /// <summary>
        /// 后端整体失效（音频引擎不可用）时的统一口径：<b>音量面读作 0、写作无效</b>，
        /// 且不入库、不落总线；播放与批量控制同样静默 no-op。
        /// <para>两个后端都有这个状态，只是原来各叫各的：Unity 侧是
        /// <c>AudioSettings.unityAudioDisabled</c>（反射读一次，<c>#if UNITY_EDITOR</c> 内，
        /// 所以玩家构建里恒为 false——它表示"开发者在编辑器菜单里关了音频"，不是设备故障）；
        /// 中间件侧是"桥接 <c>Initialize</c> 返回 false"，按上线门槛 G5 整体禁用、运行期不自愈。</para>
        /// <para>刻意与"<c>_bridge</c> 还没建起来"区分开：未初始化是启动过程中间态，那段时间的
        /// getter 必须照实报设置值，否则设置面板在初始化前打开会把滑杆显示成 0、用户一动就把 0 写回并持久化。
        /// 只有"初始化明确失败"才算 inert。</para>
        /// <para>为什么要把这个直觉命名：新后端只要漏判 getter，就留下第三种状态——
        /// "报着一个音量，却一点声音都没有"，而那恰恰是最无法归因的线上症状。</para>
        /// <para><c>internal</c> 而非 <c>protected</c>：契约是 public 的，后端只允许框架内替换。</para>
        /// </summary>
        internal virtual bool IsBackendInert => false;

        /// <summary>
        /// 所有音轨。
        /// </summary>
        /// <remarks>Unity 专属；中间件后端返回空数组。</remarks>
        public abstract AudioCategory[] AudioCategories { get; }

        /// <summary>
        /// 主音轨（总音量）音量。
        /// </summary>
        /// <remarks>线性 <c>0..1</c>（1 = 满刻度）；越界值在 setter 处夹取，getter 报回的就是实际生效值。
        /// 该值域对 Unity 与中间件后端一致。</remarks>
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
        /// 获取指定音轨的音量（线性 <c>0..1</c>）。
        /// </summary>
        public abstract float GetTrackVolume(EAudioTrack track);

        /// <summary>
        /// 设置指定音轨的音量，线性 <c>0..1</c>；越界在入口夹取，不静默改变语义。
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
        /// 重启音频服务。
        /// </summary>
        public abstract void Restart();

        /// <summary>
        /// Agent 进入 End 时回调——默认空实现；Unity 后端用于自动释放句柄。
        /// </summary>
        /// <param name="agent">结束播放的代理。</param>
        internal virtual void OnAgentPlaybackEnded(AudioAgent agent)
        {
        }

        /// <summary>
        /// 音轨上是否有声部处于活跃（加载/播放/淡入淡出/暂停）。自动 Ducking 用；默认无概念返回 false。
        /// </summary>
        internal virtual bool HasActiveAudioOn(EAudioTrack track) => false;

        /// <summary>
        /// 应用前后台切换（<c>OnApplicationPause</c>）。<paramref name="paused"/> 为 true 表示进入后台。
        /// </summary>
        /// <remarks>
        /// 刻意不接 <c>OnApplicationFocus</c>：桌面端切窗口不应静音，移动端的挂起信号只有 Pause 可靠。
        /// <para>默认空实现：Unity 后端冻结 <see cref="AudioListener"/>；中间件后端由其自身挂起策略处理。</para>
        /// </remarks>
        public virtual void OnApplicationPaused(bool paused)
        {
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 中间件接入面 [AUTHORING APIS]

        /// <summary>
        /// 加载声音库（FMOD Studio bank / Wwise SoundBank）。
        /// </summary>
        /// <remarks>仅中间件后端且桥接具备该能力时有效；Unity 后端无概念，返回 <c>false</c>。</remarks>
        public virtual bool LoadBank(string bankPath) => false;

        /// <summary>
        /// 卸载声音库。
        /// </summary>
        /// <remarks>语义同 <see cref="LoadBank"/>。</remarks>
        public virtual bool UnloadBank(string bankPath) => false;

        /// <summary>
        /// 设置实时参数（FMOD event parameter / Wwise RTPC）。
        /// </summary>
        /// <remarks>
        /// 仅中间件后端且桥接具备该能力时生效；Unity 后端无概念（空操作）。
        /// <paramref name="handle"/> 为 0 时作用于工程/全局参数，否则作用于该句柄对应的实例。
        /// </remarks>
        public virtual void SetRtpc(string name, float value, ulong handle = 0UL)
        {
        }

        #endregion 中间件接入面 [AUTHORING APIS]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public abstract ulong Play(AudioClip clip, in AudioPlayOptions options);

        /// <summary>
        /// 16 字节热请求 + 冷参数播放（推荐热路径 API）。冷参数所有权转移给服务。
        /// </summary>
        public abstract ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold);

        /// <summary>
        /// 播放音频（传统巨型签名重载——虚拟转发到 <see cref="AudioPlayOptions"/> 版本，仅为兼容保留）。
        /// </summary>
        /// <remarks>默认值与各工厂方法/契约对齐：<c>doNotAutoRecycleIfNotDonePlaying</c> 为 true。</remarks>
        [Obsolete("使用 Play(AudioClip, in AudioPlayOptions) 或 Play(AudioClip, in AudioPlayRequest, AudioPlayColdParams)", false)]
        public virtual ulong Play(AudioClip clip, EAudioTrack track, Vector3 location,
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
            float initialDelay = 0f
            )
        {
            return Play(clip, BuildOptions(track, location, loop, volume, id, fade, fadeInitialVolume,
                fadeDuration, fadeTweenEase, persistent, recycleAudioSource, audioGroup, pitch, panStereo,
                spatialBlend, soloSingleTrack, soloAllTracks, autoUnSoloOnEnd, bypassEffects, bypassListenerEffects,
                bypassReverbZones, priority, reverbZoneMix, dopplerLevel, spread, rolloffMode, minDistance,
                maxDistance, doNotAutoRecycleIfNotDonePlaying, playbackTime, playbackDuration, attachToTransform,
                useSpreadCurve, spreadCurve, useCustomRolloffCurve, customRolloffCurve, useSpatialBlendCurve,
                spatialBlendCurve, useReverbZoneMixCurve, reverbZoneMixCurve, initialDelay));
        }

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        /// <remarks>默认异步加载；同步加载（<paramref name="bAsync"/>=false）会阻塞主线程，仅限启动期/预加载场景使用。</remarks>
        public abstract ulong Play(string path, in AudioPlayOptions options, bool bAsync = true, bool bInPool = false);

        /// <summary>
        /// 播放音频（传统巨型签名重载——虚拟转发到 <see cref="AudioPlayOptions"/> 版本，仅为兼容保留）。
        /// </summary>
        /// <remarks>默认值与各工厂方法/契约对齐：<c>doNotAutoRecycleIfNotDonePlaying</c> 为 true，<paramref name="bAsync"/> 为 true。</remarks>
        [Obsolete("使用 Play(AudioClip, in AudioPlayOptions) 或 Play(AudioClip, in AudioPlayRequest, AudioPlayColdParams)", false)]
        public virtual ulong Play(string path, EAudioTrack track, Vector3 location, bool bAsync = true, bool bInPool = false,
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
            return Play(path, BuildOptions(track, location, loop, volume, id, fade, fadeInitialVolume,
                fadeDuration, fadeTweenEase, persistent, recycleAudioSource, audioGroup, pitch, panStereo,
                spatialBlend, soloSingleTrack, soloAllTracks, autoUnSoloOnEnd, bypassEffects, bypassListenerEffects,
                bypassReverbZones, priority, reverbZoneMix, dopplerLevel, spread, rolloffMode, minDistance,
                maxDistance, doNotAutoRecycleIfNotDonePlaying, playbackTime, playbackDuration, attachToTransform,
                useSpreadCurve, spreadCurve, useCustomRolloffCurve, customRolloffCurve, useSpatialBlendCurve,
                spatialBlendCurve, useReverbZoneMixCurve, reverbZoneMixCurve, initialDelay), bAsync, bInPool);
        }

        /// <summary>
        /// 由传统巨型签名参数构建 <see cref="AudioPlayOptions"/>（内部——巨型重载的唯一翻译点）。
        /// </summary>
        internal static AudioPlayOptions BuildOptions(EAudioTrack track, Vector3 location,
            bool loop, float volume, int id, bool fade, float fadeInitialVolume, float fadeDuration,
            TweenEase fadeTweenEase, bool persistent, AudioSource recycleAudioSource, AudioMixerGroup audioGroup,
            float pitch, float panStereo, float spatialBlend, bool soloSingleTrack, bool soloAllTracks,
            bool autoUnSoloOnEnd, bool bypassEffects, bool bypassListenerEffects, bool bypassReverbZones,
            int priority, float reverbZoneMix, float dopplerLevel, int spread, AudioRolloffMode rolloffMode,
            float minDistance, float maxDistance, bool doNotAutoRecycleIfNotDonePlaying, float playbackTime,
            float playbackDuration, Transform attachToTransform, bool useSpreadCurve, AnimationCurve spreadCurve,
            bool useCustomRolloffCurve, AnimationCurve customRolloffCurve, bool useSpatialBlendCurve,
            AnimationCurve spatialBlendCurve, bool useReverbZoneMixCurve, AnimationCurve reverbZoneMixCurve,
            float initialDelay)
        {
            return new AudioPlayOptions
            {
                AudioTrack = track,
                AudioGroup = audioGroup,
                Loop = loop,
                Volume = volume,
                Pitch = pitch,
                ID = id,
                FadeInOnPlay = fade,
                FadeInInitialVolume = fadeInitialVolume,
                FadeInDuration = fadeDuration,
                FadeInTweenEase = fadeTweenEase,
                Persistent = persistent,
                RecycleAudioSource = recycleAudioSource,
                InitialDelay = initialDelay,
                PlaybackTime = playbackTime,
                PlaybackDuration = playbackDuration,
                PanStereo = panStereo,
                SpatialBlend = spatialBlend,
                AttachToTransform = attachToTransform,
                SoloSingleTrack = soloSingleTrack,
                SoloAllTracks = soloAllTracks,
                AutoUnSoloOnEnd = autoUnSoloOnEnd,
                BypassEffects = bypassEffects,
                BypassListenerEffects = bypassListenerEffects,
                BypassReverbZones = bypassReverbZones,
                Priority = priority,
                ReverbZoneMix = reverbZoneMix,
                DopplerLevel = dopplerLevel,
                Location = location,
                Spread = spread,
                RolloffMode = rolloffMode,
                MinDistance = minDistance,
                MaxDistance = maxDistance,
                DoNotAutoRecycleIfNotDonePlaying = doNotAutoRecycleIfNotDonePlaying,
                UseCustomRolloffCurve = useCustomRolloffCurve,
                CustomRolloffCurve = customRolloffCurve,
                UseSpatialBlendCurve = useSpatialBlendCurve,
                SpatialBlendCurve = spatialBlendCurve,
                UseReverbZoneMixCurve = useReverbZoneMixCurve,
                ReverbZoneMixCurve = reverbZoneMixCurve,
                UseSpreadCurve = useSpreadCurve,
                SpreadCurve = spreadCurve
            };
        }

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
        /// <remarks>Unity 专属；中间件后端为空操作。</remarks>
        public abstract void ForEachAgentByID(int id, Action<AudioAgent> action);

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        /// <remarks>Unity 专属；中间件后端为空操作。</remarks>
        public abstract void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action);

        /// <summary>
        /// 对每个匹配 ID 的 AudioHandle 执行操作（零分配）。
        /// </summary>
        public abstract void ForEachHandleByID(int id, Action<ulong> action);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量
        /// </summary>
        /// <remarks>中间件后端按 clip 名映射的事件路径统计。</remarks>
        public abstract int CurrentlyPlayingCount(AudioClip clip);

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。
        /// </summary>
        /// <remarks>Unity 专属；中间件后端返回 null。</remarks>
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
        /// 音轨暂停标记（按 <see cref="EAudioTrack"/> 下标索引），契约第 1 条"暂停的音轨拦截新播放"的唯一真相源。
        /// <para>两后端此前各自声明一份同名数组、各写一遍"取数组 + 判空 + 判界"，六个现场三份逻辑——
        /// 这类同名异处的状态正是跨后端语义分歧的产地（音量值域那条就是这么长出来的）。</para>
        /// <para>但**分配与释放仍留在各后端自己的时机**：Unity 在 <c>Initialize</c> 建、<c>OnShutdown</c> 置 null，
        /// 中间件经 <c>EnsureTrackArrays</c> 懒建。"未分配即视为未暂停"（含 Unity 初始化前调 <c>PauseTrack</c>
        /// 会被忘掉这条）两边都有调用侧依赖，收进基类顺手改成懒分配就是行为改动，不该混在重构里。</para>
        /// </summary>
        [NonSerialized] internal bool[] _pausedTracks;

        /// <summary>
        /// 置位/复位音轨暂停标记。数组尚未分配或下标越界一律忽略——与两后端原来的写法同语义。
        /// </summary>
        internal void SetTrackPaused(EAudioTrack track, bool paused)
        {
            var flags = _pausedTracks;
            if (flags == null) return;

            int index = (int)track;
            if (index >= 0 && index < flags.Length) flags[index] = paused;
        }

        /// <summary>
        /// 音轨是否被标记为暂停。数组未分配或下标越界按未暂停处理。
        /// </summary>
        internal bool IsTrackPaused(EAudioTrack track)
        {
            var flags = _pausedTracks;
            int index = (int)track;
            return flags != null && index >= 0 && index < flags.Length && flags[index];
        }

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        public abstract void PauseTrack(EAudioTrack track);

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        public abstract void UnpauseTrack(EAudioTrack track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        public abstract bool IsPaused(EAudioTrack track);

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        public abstract void StopTrack(EAudioTrack track, float fadeoutDuration = 0f);

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

        /// <summary>
        /// 停止匹配用户 ID 的全部句柄（零 lambda 分配）。用于分层替换，不影响其它 ID。
        /// </summary>
        public abstract void StopByID(int id, float fadeoutDuration = 0f);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <summary>
        /// 音量过渡调度器（声部句柄与 Master/音轨总线伪句柄共用）。
        /// <para>放在基类不是图省事：两个后端的总线过渡族此前逐字相同，只把"落到哪儿"经
        /// <see cref="IAudioFadeTarget.ApplyFade"/> 分派出去。同一段编排写两遍，就是下一处分歧的产地。</para>
        /// <para><c>internal</c> 而非 <c>protected</c>：调度器是内部类型，而本契约是 public——
        /// 既然后端只允许框架内替换，就不该为"外部也能派生"这条不存在的需求把内部件抬成 public。</para>
        /// </summary>
        [NonSerialized] internal readonly AudioFadeScheduler _fades = new AudioFadeScheduler();

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量。
        /// </summary>
        /// <remarks>时长为 0 等价于直接赋值；总线伪句柄与声部句柄共用同一张调度表。
        /// 后端 inert 时不排过渡——排了也不会响，却让 <see cref="SoundIsFadingOut"/> 报真，
        /// 那是"报着一个音量却完全听不见"的同一种假象。</remarks>
        public virtual void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default)
        {
            if (IsBackendInert) return;
            if (duration <= 0f) { MasterVolume = finalVolume; return; }

            _fades.Stop(AudioFadeScheduler.MASTER_FADE_HANDLE);
            _fades.Add(new AudioFadeState
            {
                Handle = AudioFadeScheduler.MASTER_FADE_HANDLE,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
                Ease = tweenEase,
            });
        }

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）。
        /// </summary>
        /// <remarks>只撤过渡，不还原已写出去的音量——停在哪儿就是哪儿。</remarks>
        public virtual void StopFadeMasterTrack() => _fades.Stop(AudioFadeScheduler.MASTER_FADE_HANDLE);

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量。
        /// </summary>
        public virtual void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default)
        {
            if (IsBackendInert) return;
            if (duration <= 0f) { SetTrackVolume(track, finalVolume); return; }

            ulong fadeHandle = AudioFadeScheduler.TrackFadeHandle((int)track);
            _fades.Stop(fadeHandle);
            _fades.Add(new AudioFadeState
            {
                Handle = fadeHandle,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
                Ease = tweenEase,
            });
        }

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）。
        /// </summary>
        public virtual void StopFadeTrack(EAudioTrack track)
            => _fades.Stop(AudioFadeScheduler.TrackFadeHandle((int)track));

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。
        /// </summary>
        /// <remarks>使用手动过渡系统，完全零 GC；应用 <paramref name="tweenEase"/> 曲线。</remarks>
        public abstract void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase);

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）。
        /// </summary>
        public virtual void StopFadeAudio(ulong handle) => _fades.Stop(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中。
        /// </summary>
        public virtual bool SoundIsFadingOut(ulong handle) => _fades.IsFading(handle);

        /// <summary>
        /// 对匹配用户 ID 的全部句柄执行淡入/音量过渡（零 lambda 分配）。
        /// </summary>
        public abstract void PlayFadeByID(int id, float duration, float finalVolume, TweenEase ease);

        /// <summary>
        /// 停止匹配用户 ID 的全部句柄上的过渡（零 lambda 分配）。
        /// </summary>
        public abstract void StopFadeByID(int id);

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

        /// <summary>预加载地址（策略默认 Pin 常驻）。</summary>
        /// <returns>已加载完成返回 true；加载中或失败返回 false。</returns>
        public abstract bool Preload(string address, EAudioCachePolicy policy = EAudioCachePolicy.Pin);

        /// <summary>异步预加载地址。</summary>
        public abstract void PreloadAsync(string address, EAudioCachePolicy policy, Action<bool> completed = null);

        /// <summary>卸载地址缓存。force=true 时忽略引用计数。</summary>
        public abstract bool UnloadClipCache(string address, bool force = false);

        /// <summary>清空 Clip 缓存。force=true 时连 Pin 一并清。</summary>
        public abstract void ClearClipCache(bool force = false);

        #endregion 资源池 [ASSET POOL]
    }
}