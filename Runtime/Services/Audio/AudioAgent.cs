using Moirai.Atropos.Resource;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Schedulers;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频代理辅助器。持有单个 <see cref="AudioSource"/>，负责播放状态机、淡入淡出与资源租约生命周期。
    /// <para>热路径状态（音量/循环/跟随/优先级等）在播放时从 <see cref="AudioPlayOptions"/> 拆出缓存，避免整份巨型结构体驻留。</para>
    /// <para>句柄绑定：同一时刻仅有一个有效 <see cref="CurrentHandle"/>；换播/结束时由 Handler 自动解绑。</para>
    /// </summary>
    public class AudioAgent
    {
        private AudioServiceHandler _audioHandler;
        private ResourceServiceHandler _resourceService;
        private AudioAssetData _audioAssetData;

        private string _currentPath;
        private Transform _transform;
        private bool _inPool;

        // ===== 排队加载 — 字段复用，零分配 =====
        private string _pendingPath;
        private bool _pendingAsync;
        private bool _pendingInPool;
        private bool _hasPendingLoad;

        // ===== 异步世代 — 防止复用后串 clip =====
        private int _loadGeneration;
        private CancellationTokenSource _loadCts;

        // ===== 句柄绑定 =====
        private ulong _currentHandle;

        private float _fadeInAt;
        private float _fadeOutStartTime;
        public const float FADEOUT_DEFAULT_DURATION = 0.2f;
        private float _fadeOutDuration;

        private float _playDuration;
        private SchedulerHandle _autoUnSoloOnEnd;

        // ===== 热路径播放状态（从 AudioPlayRequest 解出，Update 高频读）=====
        private AudioPlayRequest _hot;
        private float _volume = 1f;
        private float _fadeInInitialVolume;
        private float _fadeInDuration = 1f;
        private TweenEase _fadeInTweenEase;
        private Transform _attachTarget;
        private bool _loop;
        private bool _persistent;
        private int _priority = 128;
        private AudioMixerGroup _overrideMixerGroup;
        private Vector3 _location;
        private bool _fadeInOnPlay;

        // Solo — 仅 Play 时使用
        private bool _soloSingleTrack;
        private bool _soloAllTracks;
        private bool _autoUnSoloOnEndFlag;
        private EAudioTrack _soloTrack;

        // 冷路径参数（池化实例），clip 到位后应用
        private AudioPlayColdParams _cold;

        private AudioSource _audioSource;
        private AudioSource _recycleSource;
        private AudioMixerGroup _audioMixerGroup;

        private EAudioAgentRuntimeState _audioAgentRuntimeState = EAudioAgentRuntimeState.None;

        #region 公共属性 [PUBLIC PROPERTIES]

        /// <summary>
        /// 用户定义 ID（用于事件系统按 ID 查找）。
        /// </summary>
        public int ID => _hot.Id;

        /// <summary>
        /// 当前热路径请求（16 字节）。
        /// </summary>
        public AudioPlayRequest HotRequest => _hot;

        /// <summary>
        /// 当前绑定的服务句柄；0 表示未绑定。
        /// </summary>
        public ulong CurrentHandle => _currentHandle;

        /// <summary>
        /// 资源操作句柄。
        /// </summary>
        public AudioAssetData AudioAssetData => _audioAssetData;

        /// <summary>
        /// 音频代理辅助器当前是否空闲。
        /// </summary>
        public bool IsFree => _audioAgentRuntimeState == EAudioAgentRuntimeState.None ||
                              _audioAgentRuntimeState == EAudioAgentRuntimeState.End;

        /// <summary>
        /// 音频代理辅助器播放秒数。
        /// </summary>
        public float Duration { get; private set; }

        /// <summary>
        /// 音频代理辅助器的当前声源（若指定 <see cref="AudioPlayOptions.RecycleAudioSource"/> 则优先使用）。
        /// </summary>
        public AudioSource AudioResource => _recycleSource != null ? _recycleSource : _audioSource;

        /// <summary>
        /// 当前优先级（0 最高，255 最低）。用于 Voice Stealing。
        /// </summary>
        public int Priority => _priority;

        /// <summary>
        /// 音频代理辅助器当前音频长度。
        /// </summary>
        public float Length
        {
            get
            {
                var source = AudioResource;
                if (source != null && source.clip != null)
                {
                    return source.clip.length;
                }

                return 0;
            }
        }

        /// <summary>
        /// 音频代理辅助器实例位置。
        /// </summary>
        public Vector3 Position
        {
            get => _transform != null ? _transform.position : Vector3.zero;
            set
            {
                if (_transform != null) _transform.position = value;
            }
        }

        /// <summary>
        /// 音频代理辅助器是否正在播放。
        /// </summary>
        internal bool IsPlaying => AudioResource != null && AudioResource.isPlaying;

        /// <summary>
        /// 音频代理辅助器是否正在暂停。
        /// </summary>
        internal bool IsPaused => _audioAgentRuntimeState == EAudioAgentRuntimeState.Pausing;

        /// <summary>
        /// 音频代理辅助器是否循环。
        /// </summary>
        internal bool IsLoop => _loop;

        /// <summary>
        /// 音频代理辅助器是否持久性。
        /// </summary>
        internal bool IsPersistent => _persistent;

        /// <summary>
        /// 音频代理辅助器的输出混音组。
        /// </summary>
        public AudioMixerGroup OutputAudioMixerGroup => _overrideMixerGroup == null ? _audioMixerGroup : _overrideMixerGroup;

        /// <summary>
        /// 当前异步加载世代。
        /// </summary>
        internal int LoadGeneration => _loadGeneration;

        #endregion

        #region 句柄绑定 [HANDLE BINDING]

        /// <summary>
        /// 绑定服务句柄（由 Handler 在 Play/Load 时调用）。
        /// </summary>
        internal void BindHandle(ulong handle) => _currentHandle = handle;

        /// <summary>
        /// 解绑服务句柄（由 Handler 在释放句柄时调用）。
        /// </summary>
        internal void UnbindHandle() => _currentHandle = 0UL;

        /// <summary>
        /// 中止进行中的异步加载并递增世代，使迟到的回调失效。
        /// </summary>
        private void InvalidateAsyncLoad()
        {
            _loadGeneration++;

            if (_loadCts != null)
            {
                if (!_loadCts.IsCancellationRequested)
                {
                    try
                    {
                        _loadCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // already disposed
                    }
                }

                _loadCts.Dispose();
                _loadCts = null;
            }
        }

        /// <summary>
        /// 进入 End 状态：取消异步、通知 Handler 自动释放句柄。
        /// </summary>
        private void EnterEndState()
        {
            InvalidateAsyncLoad();
            _hasPendingLoad = false;
            _pendingPath = null;
            _recycleSource = null;

            if (_cold != null)
            {
                AudioPlayColdParamsPool.Release(_cold);
                _cold = null;
            }

            _audioAgentRuntimeState = EAudioAgentRuntimeState.End;
            _audioHandler?.OnAgentPlaybackEnded(this);
        }

        #endregion 句柄绑定 [HANDLE BINDING]

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 初始化音频代理辅助器。宿主 GameObject 从 <see cref="AudioAgentHostPool"/> 获取。
        /// </summary>
        /// <param name="audioCategory">音频轨道（类别）。</param>
        /// <param name="index">音频代理辅助器编号。</param>
        public void Init(AudioCategory audioCategory, int index = 0)
        {
            // 必须绑定创建它的 Handler，不能读全局 AudioService.Handler（隔离实例/测试会串）
            _audioHandler = audioCategory.Handler;
            _resourceService = ResourceService.Handler;

            string groupName = audioCategory.AudioMixerGroup != null
                ? audioCategory.AudioMixerGroup.name
                : audioCategory.AudioTrack.ToString();

            string hostName = StringUtility.Format("{0} - {1}", groupName, index);
            _audioSource = AudioAgentHostPool.Acquire(audioCategory.InstanceRoot, hostName);
            _transform = _audioSource.transform;

            if (audioCategory.AudioMixerGroup != null)
            {
                _audioMixerGroup = audioCategory.AudioMixerGroup;
            }
            else if (audioCategory.AudioMixer != null)
            {
                string path = StringUtility.Format("Master/{0}/{0} - {1}", groupName, index);
                AudioMixerGroup[] audioMixerGroups = audioCategory.AudioMixer.FindMatchingGroups(path);
                _audioMixerGroup = audioMixerGroups.Length > 0 ? audioMixerGroups[0] : null;
            }
        }

        /// <summary>
        /// 销毁音频代理辅助器。宿主归还 <see cref="AudioAgentHostPool"/> 复用。
        /// </summary>
        public void Destroy()
        {
            InvalidateAsyncLoad();

            if (_audioSource != null)
            {
                AudioAgentHostPool.Release(_audioSource);
                _audioSource = null;
                _transform = null;
            }

            if (_audioAssetData != null)
            {
                AudioAssetData.Dealloc(_audioAssetData);
                _audioAssetData = null;
            }

            _currentHandle = 0UL;
            _recycleSource = null;
            _audioAgentRuntimeState = EAudioAgentRuntimeState.None;
        }

        /// <summary>
        /// 轮询音频代理辅助器。空闲代理由 Category 跳过。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        public void Update(float elapseSeconds)
        {
            if (_audioAgentRuntimeState == EAudioAgentRuntimeState.Playing ||
                _audioAgentRuntimeState == EAudioAgentRuntimeState.FadingIn)
            {
                if (!_loop && Duration >= _playDuration)
                {
                    Stop(FADEOUT_DEFAULT_DURATION);
                }
                else if (_audioAgentRuntimeState == EAudioAgentRuntimeState.FadingIn)
                {
                    float endTime = _fadeInAt + _fadeInDuration;
                    if (GameTime.unscaledTime <= endTime)
                    {
                        AudioResource.volume = _fadeInTweenEase.Tween(
                            GameTime.unscaledTime, _fadeInAt, endTime,
                            _fadeInInitialVolume, _volume);
                    }
                    else
                    {
                        AudioResource.volume = _volume;
                        _audioAgentRuntimeState = EAudioAgentRuntimeState.Playing;
                    }
                }

                // 跟随目标：赋值世界坐标（勿用 Translate 增量）
                if (_attachTarget != null && AudioResource != null)
                {
                    AudioResource.transform.position = _attachTarget.position;
                }

                Duration += elapseSeconds;
            }
            else if (_audioAgentRuntimeState == EAudioAgentRuntimeState.FadingOut)
            {
                float elapsed = GameTime.unscaledTime - _fadeOutStartTime;
                if (elapsed >= _fadeOutDuration)
                {
                    Stop();

                    if (_hasPendingLoad)
                    {
                        string path = _pendingPath;
                        bool bAsync = _pendingAsync;
                        bool bInPool = _pendingInPool;
                        _hasPendingLoad = false;
                        _pendingPath = null;
                        Load(path, default, bAsync, bInPool, restoreHotState: true);
                    }
                }
                else
                {
                    AudioResource.volume = _volume * (1f - elapsed / _fadeOutDuration);
                }
            }
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 公开播放入口（完整 Options 兼容层）。
        /// </summary>
        public void Play(AudioClip clip, AudioPlayOptions options)
        {
            PlayWithOptions(clip, options);
        }

        /// <summary>
        /// 应用完整热/冷参数并播放。
        /// </summary>
        internal void PlayWithOptions(AudioClip clip, in AudioPlayOptions options)
        {
            PlayWithRequest(clip, options.ToRequest(), AudioPlayColdParams.FromOptions(options));
        }

        /// <summary>
        /// 16B 热请求 + 冷参数播放（冷参数所有权转移给 Agent，由 Agent 归还池）。
        /// </summary>
        internal void PlayWithRequest(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold)
        {
            CaptureHotState(request, cold);
            if (_cold != null && !ReferenceEquals(_cold, cold))
            {
                AudioPlayColdParamsPool.Release(_cold);
            }

            _cold = cold;
            BeginPlayback(clip);
        }

        /// <summary>
        /// 从 16B 热请求 + 冷参数解出 Update 所需字段。
        /// </summary>
        private void CaptureHotState(in AudioPlayRequest request, AudioPlayColdParams cold)
        {
            _hot = request;
            _volume = request.Volume;
            _loop = request.Loop;
            _persistent = request.Persistent;
            _priority = request.Priority;
            _fadeInOnPlay = request.FadeInOnPlay;
            _soloSingleTrack = request.SoloSingleTrack;
            _soloAllTracks = request.SoloAllTracks;
            _autoUnSoloOnEndFlag = request.AutoUnSoloOnEnd;
            _soloTrack = request.Track;

            if (cold != null)
            {
                _overrideMixerGroup = cold.AudioGroup;
                _recycleSource = cold.RecycleAudioSource;
                _location = cold.Location;
                _attachTarget = cold.AttachToTransform;
                _fadeInInitialVolume = cold.FadeInInitialVolume;
                _fadeInDuration = cold.FadeInDuration;
                _fadeInTweenEase = cold.FadeInTweenEase;
            }
            else
            {
                _overrideMixerGroup = null;
                _recycleSource = null;
                _location = Vector3.zero;
                _attachTarget = null;
                _fadeInInitialVolume = 0f;
                _fadeInDuration = 1f;
                _fadeInTweenEase = default;
            }
        }

        /// <summary>
        /// 开始播放：冷路径源参数 → 热路径赋值 → Play。
        /// </summary>
        private void BeginPlayback(AudioClip clip)
        {
            if (clip == null)
            {
                EnterEndState();
                return;
            }

            var source = AudioResource;
            if (source == null)
            {
                EnterEndState();
                return;
            }

            if (_currentHandle != 0)
            {
                _audioHandler?.StopFadeAudio(_currentHandle);
            }

            if (_autoUnSoloOnEnd != default)
            {
                _autoUnSoloOnEnd.Cancel();
                _autoUnSoloOnEnd = default;
            }

            if (_cold != null)
            {
                ApplyColdSourceParams(source, _cold);
            }

            source.pitch = _hot.Pitch;
            source.priority = _hot.Priority;
            source.clip = clip;
            source.loop = _loop;
            source.transform.position = _location;
            source.outputAudioMixerGroup = OutputAudioMixerGroup;
            source.mute = false;

            float initialDelay = _cold != null ? _cold.InitialDelay : 0f;
            float playbackTime = _cold != null ? _cold.PlaybackTime : 0f;
            float playbackDuration = _cold != null ? _cold.PlaybackDuration : 0f;

            if (playbackTime > 0f && clip.length > 0f)
            {
                source.time = Mathf.Min(playbackTime, clip.length - 0.01f);
            }

            _playDuration = playbackDuration > 0f
                ? playbackDuration - playbackTime
                : clip.length - playbackTime;

            _fadeInAt = GameTime.unscaledTime;
            source.volume = _fadeInOnPlay ? _fadeInInitialVolume : _volume;

            if (initialDelay > 0f)
            {
#if UNITY_6000_0_OR_NEWER
                source.PlayDelayed(initialDelay);
#else
                source.Play((ulong)(initialDelay * 44100));
#endif
            }
            else
            {
                source.Play();
            }

            Duration = 0f;
            _audioAgentRuntimeState = _fadeInOnPlay ? EAudioAgentRuntimeState.FadingIn : EAudioAgentRuntimeState.Playing;

            // Solo
            if (_soloSingleTrack)
            {
                MuteAudiosOnTrack(_soloTrack, true);
                source.mute = false;
                if (_autoUnSoloOnEndFlag)
                {
                    EAudioTrack track = _soloTrack;
                    _autoUnSoloOnEnd = Scheduler.Delay(_playDuration, () => MuteAudiosOnTrack(track, false));
                }
            }
            else if (_soloAllTracks)
            {
                MuteAllAudios(true);
                source.mute = false;
                if (_autoUnSoloOnEndFlag)
                {
                    _autoUnSoloOnEnd = Scheduler.Delay(_playDuration, () => MuteAllAudios(false));
                }
            }
        }

        /// <summary>
        /// 应用冷路径 AudioSource 参数。
        /// </summary>
        private static void ApplyColdSourceParams(AudioSource source, AudioPlayColdParams cold)
        {
            source.spatialBlend = cold.SpatialBlend;
            source.panStereo = cold.PanStereo;
            source.bypassEffects = cold.BypassEffects;
            source.bypassListenerEffects = cold.BypassListenerEffects;
            source.bypassReverbZones = cold.BypassReverbZones;
            source.reverbZoneMix = cold.ReverbZoneMix;
            source.dopplerLevel = cold.DopplerLevel;
            source.spread = cold.Spread;
            source.rolloffMode = cold.RolloffMode;
            source.minDistance = cold.MinDistance;
            source.maxDistance = cold.MaxDistance;

            if (cold.UseSpreadCurve) source.SetCustomCurve(AudioSourceCurveType.Spread, cold.SpreadCurve);
            if (cold.UseCustomRolloffCurve) source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, cold.CustomRolloffCurve);
            if (cold.UseSpatialBlendCurve) source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, cold.SpatialBlendCurve);
            if (cold.UseReverbZoneMixCurve) source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, cold.ReverbZoneMixCurve);
        }

        /// <summary>
        /// 加载音频代理辅助器。
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="options">音频播放选项设置（restoreHotState 为 true 时忽略）。</param>
        /// <param name="bAsync">是否异步加载。</param>
        /// <param name="bInPool">是否缓存已加载资源。</param>
        /// <param name="restoreHotState">排队重载时是否跳过 options 覆盖。</param>
        public void Load(string path, AudioPlayOptions options, bool bAsync, bool bInPool = false, bool restoreHotState = false)
        {
            if (!restoreHotState)
            {
                LoadWithRequest(path, options.ToRequest(), AudioPlayColdParams.FromOptions(options), bAsync, bInPool);
                return;
            }

            LoadInternal(path, bAsync, bInPool);
        }

        /// <summary>
        /// 16B 热请求 + 冷参数路径加载。
        /// </summary>
        internal void LoadWithRequest(string path, in AudioPlayRequest request, AudioPlayColdParams cold, bool bAsync, bool bInPool)
        {
            CaptureHotState(request, cold);
            if (_cold != null && !ReferenceEquals(_cold, cold))
            {
                AudioPlayColdParamsPool.Release(_cold);
            }

            _cold = cold;
            LoadInternal(path, bAsync, bInPool);
        }

        /// <summary>
        /// 路径加载入口（Handler 调用）。
        /// </summary>
        internal void LoadWithOptions(string path, in AudioPlayOptions options, bool bAsync, bool bInPool)
        {
            Load(path, options, bAsync, bInPool);
        }

        private void LoadInternal(string path, bool bAsync, bool bInPool)
        {
            _inPool = bInPool;
            _currentPath = path;

            if (_audioAgentRuntimeState == EAudioAgentRuntimeState.None ||
                _audioAgentRuntimeState == EAudioAgentRuntimeState.End)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    if (bInPool && _audioHandler.AssetHandlePool.TryGetValue(path, out var operationHandleObj))
                    {
                        OnAssetLoadComplete(operationHandleObj);
                        return;
                    }

                    if (bAsync)
                    {
                        _audioAgentRuntimeState = EAudioAgentRuntimeState.Loading;
                        int generation = ++_loadGeneration;
                        _loadCts?.Dispose();
                        _loadCts = new CancellationTokenSource();
                        LoadLeaseAsyncInternal(path, generation, _loadCts.Token).Forget();
                    }
                    else
                    {
                        var lease = _resourceService.LoadLease<AudioClip>(path);
                        OnAssetLoadComplete(lease);
                    }
                }
            }
            else
            {
                _pendingPath = path;
                _pendingAsync = bAsync;
                _pendingInPool = bInPool;
                _hasPendingLoad = true;

                if (_audioAgentRuntimeState == EAudioAgentRuntimeState.Playing ||
                    _audioAgentRuntimeState == EAudioAgentRuntimeState.FadingIn)
                {
                    Stop(fadeoutDuration: FADEOUT_DEFAULT_DURATION);
                }
            }
        }

        /// <summary>
        /// 资源加载完成。
        /// </summary>
        private void OnAssetLoadComplete(object handleObj)
        {
            if (handleObj != null && _inPool && !string.IsNullOrEmpty(_currentPath))
            {
                _audioHandler.AssetHandlePool.TryAdd(_currentPath, handleObj);
            }

            if (_hasPendingLoad)
            {
                if (!_inPool && handleObj != null)
                {
                    ReleaseLeaseObject(handleObj);
                }

                _audioAgentRuntimeState = EAudioAgentRuntimeState.End;
                string path = _pendingPath;
                bool bAsync = _pendingAsync;
                bool bInPool = _pendingInPool;
                _hasPendingLoad = false;
                _pendingPath = null;
                Load(path, default, bAsync, bInPool, restoreHotState: true);
                return;
            }

            if (handleObj != null)
            {
                if (_audioAssetData != null)
                {
                    AudioAssetData.Dealloc(_audioAssetData);
                    _audioAssetData = null;
                }

                _audioAssetData = AudioAssetData.Alloc(handleObj, _inPool);

                if (TryGetLeaseClip(handleObj, out var clip))
                {
                    BeginPlayback(clip);
                }
                else
                {
                    EnterEndState();
                }
            }
            else
            {
                EnterEndState();
            }
        }

        /// <summary>
        /// 异步加载音频租约（世代校验 + CancellationToken）。
        /// </summary>
        private async UniTaskVoid LoadLeaseAsyncInternal(string path, int generation, CancellationToken cancellationToken)
        {
            object lease = null;
            try
            {
                var result = await _resourceService.LoadLeaseAsync<AudioClip>(path, cancellationToken);
                lease = result;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                if (generation == _loadGeneration)
                {
                    LogUtility.Error("[AudioAgent] Async load failed: {0}", e.Message);
                    EnterEndState();
                }

                return;
            }

            if (generation != _loadGeneration || cancellationToken.IsCancellationRequested)
            {
                ReleaseLeaseObject(lease);
                return;
            }

            OnAssetLoadComplete(lease);
        }

        private static bool TryGetLeaseClip(object handleObj, out AudioClip clip)
        {
            switch (handleObj)
            {
                case ResourceAssetLease<AudioClip> typedLease:
                    clip = typedLease.Asset;
                    return clip != null;
                case YooAsset.AssetHandle nativeHandle when nativeHandle.AssetObject is AudioClip audioClip:
                    clip = audioClip;
                    return true;
                default:
                    clip = null;
                    return false;
            }
        }

        private static bool ReleaseLeaseObject(object handleObj)
        {
            if (handleObj is IDisposable disposable)
            {
                disposable.Dispose();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 停止播放音频代理辅助器。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void Stop(float fadeoutDuration = 0f)
        {
            if (fadeoutDuration > 0f && IsPlaying)
            {
                _fadeOutStartTime = GameTime.unscaledTime;
                _fadeOutDuration = fadeoutDuration;
                _audioAgentRuntimeState = EAudioAgentRuntimeState.FadingOut;
            }
            else
            {
                var source = AudioResource;
                if (source != null)
                {
                    source.Stop();
                }

                if (_autoUnSoloOnEnd != default)
                {
                    _autoUnSoloOnEnd.Cancel();
                    _autoUnSoloOnEnd = default;
                }

                EnterEndState();
            }
        }

        /// <summary>
        /// 暂停音频代理辅助器。
        /// </summary>
        public void Pause()
        {
            if (!IsPlaying) return;

            _audioAgentRuntimeState = EAudioAgentRuntimeState.Pausing;
            AudioResource.Pause();
        }

        /// <summary>
        /// 取消暂停音频代理辅助器。
        /// </summary>
        public void Unpause()
        {
            if (_audioAgentRuntimeState != EAudioAgentRuntimeState.Pausing) return;

            _audioAgentRuntimeState = EAudioAgentRuntimeState.Playing;
            AudioResource.UnPause();
        }

        /// <summary>
        /// 取消淡入。
        /// </summary>
        public void CancelFadeIn()
        {
            if (_audioAgentRuntimeState != EAudioAgentRuntimeState.FadingIn) return;

            _audioAgentRuntimeState = EAudioAgentRuntimeState.Playing;
        }

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 独奏 [SOLO]

        private void MuteAudiosOnTrack(EAudioTrack track, bool mute)
        {
            var categories = _audioHandler.AudioCategories;
            if (categories == null) return;

            for (int i = 0; i < categories.Length; i++)
            {
                var category = categories[i];
                if (category == null || category.AudioTrack != track) continue;

                var agents = category.AudioAgents;
                for (int j = 0; j < agents.Count; j++)
                {
                    var agent = agents[j];
                    if (agent?.AudioResource != null)
                    {
                        agent.AudioResource.mute = mute;
                    }
                }
            }
        }

        private void MuteAllAudios(bool mute)
        {
            var categories = _audioHandler.AudioCategories;
            if (categories == null) return;

            for (int i = 0; i < categories.Length; i++)
            {
                var category = categories[i];
                if (category == null) continue;

                var agents = category.AudioAgents;
                for (int j = 0; j < agents.Count; j++)
                {
                    var agent = agents[j];
                    if (agent?.AudioResource != null)
                    {
                        agent.AudioResource.mute = mute;
                    }
                }
            }
        }

        #endregion 独奏 [SOLO]
    }
}
