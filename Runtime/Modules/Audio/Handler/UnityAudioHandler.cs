using System;
using System.Collections.Generic;
using Moirai.Atropos.Events;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Schedulers;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using YooAsset;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 基于 Unity 音频系统（<see cref="AudioSource"/>/<see cref="AudioMixer"/>）的默认音频处理器。
    /// <para><see cref="AudioServiceHandler"/> 的内置实现，承载代理池管理、播放状态机、淡入淡出等核心逻辑。</para>
    /// <para>由 <see cref="AudioServiceSettings"/> 序列化配置，可替换为自定义音频后端。</para>
    /// </summary>
    [Serializable]
    public sealed class UnityAudioHandler : AudioServiceHandler
    {
        [NonSerialized] private AudioGroupConfig[] _audioGroupConfigs;
        [NonSerialized] private bool _unityAudioDisabled;

        // Master 音轨过渡 Tween ID
        [NonSerialized] private long _masterFadeTweenId;
        // 音轨过渡 Tween ID（数组索引 = (int)EAudioTrack）
        [NonSerialized] private long[] _trackFadeTweenIds;
        // 音轨暂停状态（数组索引 = (int)EAudioTrack）
        [NonSerialized] private bool[] _pausedTracks;
        // 音轨 -> Category 缓存，O(1) 数组直接访问
        [NonSerialized] private AudioCategory[] _categoryCache;
        // 音轨 -> AudioGroupConfig 缓存，O(1) 数组直接访问
        [NonSerialized] private AudioGroupConfig[] _configCache;
        // 服务自维护 ID -> AudioAgent 映射
        [NonSerialized] private readonly Dictionary<ulong, AudioAgent> _handleToAgent = new Dictionary<ulong, AudioAgent>();
        // 用户定义 ID -> 服务句柄列表 映射（1 对多，支持事件系统通过用户 ID 查找所有句柄）
        [NonSerialized] private readonly Dictionary<int, List<ulong>> _userHandleMap = new Dictionary<int, List<ulong>>();
        // 服务自维护 ID 生成器
        [NonSerialized] private ulong _nextAudioId = 1UL;
        // 临时列表，用于 FindAgents 系列方法（避免每次分配）
        [NonSerialized] private readonly List<AudioAgent> _sharedAgentBuffer = new List<AudioAgent>(4);
        // List<ulong> 对象池，避免频繁分配
        private static readonly Stack<List<ulong>> s_HandleListPool = new Stack<List<ulong>>(4);

        private static List<ulong> AcquireHandleList()
        {
            return s_HandleListPool.Count > 0 ? s_HandleListPool.Pop() : new List<ulong>(2);
        }

        private static void ReleaseHandleList(List<ulong> list)
        {
            list.Clear();
            s_HandleListPool.Push(list);
        }

        [NonSerialized] private AudioMixer _audioMixer;
        /// <summary>
        /// 音频混响器。
        /// </summary>
        public override AudioMixer AudioMixer => _audioMixer;

        [NonSerialized] private Transform _instanceRoot;
        /// <summary>实例化根节点。</summary>
        public override Transform InstanceRoot { get => _instanceRoot; set => _instanceRoot = value; }

        // 资源句柄池，用于缓存资源系统的已加载音频资源。
        // 资源句柄池，用于缓存资源系统的已加载音频资源（后端原生句柄的 object 包装）。
        public override Dictionary<string, object> AssetHandlePool { get; } = new Dictionary<string, object>();

        [NonSerialized] private Action<AudioServiceHandler, float>[] _trackFadeCallbacks;
        /// <summary>每个音轨一个回调，按 (int)EAudioTrack 索引，惰性初始化</summary>
        private Action<AudioServiceHandler, float>[] TrackFadeCallbacks
        {
            get
            {
                if (_trackFadeCallbacks != null) return _trackFadeCallbacks;

                var values = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));
                _trackFadeCallbacks = new Action<AudioServiceHandler, float>[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    EAudioTrack track = values[i];
                    _trackFadeCallbacks[i] = (m, v) => m.SetTrackVolume(track, v);
                }
                return _trackFadeCallbacks;
            }
        }

        // ===== FadeAudio 手动过渡 — 零 GC =====
        private struct AudioFadeState
        {
            public ulong Handle;
            public float StartTime;
            public float Duration;
            public float StartVolume;
            public float EndVolume;
        }
        [NonSerialized] private readonly List<AudioFadeState> _audioFades = new List<AudioFadeState>(4);
        [NonSerialized] private int _audioFadeCount;

        #region 音轨状态 [TRACK STATUS]

        [NonSerialized] private AudioCategory[] _audioCategories;
        /// <summary>
        /// 所有音轨。
        /// </summary>
        public override AudioCategory[] AudioCategories => _audioCategories;

        [NonSerialized] private float _volume = 1f;
        /// <summary>
        /// 主音轨（总音量）音量。
        /// </summary>
        /// <remarks>0-1</remarks>
        public override float MasterVolume
        {
            get
            {
                if (_unityAudioDisabled)
                {
                    return 0f;
                }

                return _volume;
            }
            set
            {
                if (_unityAudioDisabled || Mathf.Approximately(_volume, value))
                {
                    return;
                }

                _volume = value;
                ApplyMasterVolume();
            }
        }

        [NonSerialized] private bool _isMuted;
        /// <summary>
        /// 主音轨（总音量）静音。
        /// </summary>
        public override bool MasterMute
        {
            get
            {
                if (_unityAudioDisabled)
                {
                    return false;
                }

                return _isMuted;
            }
            set
            {
                if (_unityAudioDisabled || _isMuted == value)
                {
                    return;
                }

                _isMuted = value;
                ApplyMasterVolume();
            }
        }

        /// <summary>
        /// 写入主音轨（总音量）配置。
        /// </summary>
        public override void SetMasterSettings()
        {
            SettingUtility.SetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, _volume);
            SettingUtility.SetBool(GameConstant.Setting.AUDIO_MASTER_MUTED, _isMuted);
        }

        /// <summary>
        /// 加载主音轨（总音量）配置。
        /// </summary>
        public override void LoadMasterSettings()
        {
            _isMuted = SettingUtility.GetBool(GameConstant.Setting.AUDIO_MASTER_MUTED, false);
            _volume = SettingUtility.GetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, 1f);

            ApplyMasterVolume();
        }

        /// <summary>
        /// 移除主音轨（总音量）设置。
        /// </summary>
        public override void RemoveMasterSetting()
        {
            SettingUtility.RemoveSetting(GameConstant.Setting.AUDIO_MASTER_MUTED);
            SettingUtility.RemoveSetting(GameConstant.Setting.AUDIO_MASTER_VOLUME);

            _isMuted = false;
            _volume = 1f;
            ApplyMasterVolume();
        }

        /// <summary>
        /// 应用主音轨（总音量）音量
        /// </summary>
        private void ApplyMasterVolume()
        {
            AudioListener.volume = _isMuted ? 0f : Mathf.Clamp(_volume, 0f, 1f);
        }

        /// <summary>
        /// 获取指定音轨的音量。
        /// </summary>
        public override float GetTrackVolume(EAudioTrack track)
        {
            if (_unityAudioDisabled) return 0f;
            int index = (int)track;
            return index >= 0 && index < _configCache.Length ? _configCache[index].Volume : 1f;
        }

        /// <summary>
        /// 设置指定音轨的音量。
        /// </summary>
        public override void SetTrackVolume(EAudioTrack track, float volume)
        {
            if (_unityAudioDisabled) return;
            int index = (int)track;
            if (index >= 0 && index < _configCache.Length)
                _configCache[index].Volume = volume;
        }

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        public override bool GetTrackMute(EAudioTrack track)
        {
            if (_unityAudioDisabled) return false;
            int index = (int)track;
            return index >= 0 && index < _configCache.Length && _configCache[index].Mute;
        }

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        public override void SetTrackMute(EAudioTrack track, bool mute)
        {
            if (_unityAudioDisabled) return;
            int index = (int)track;
            if (index >= 0 && index < _configCache.Length)
                _configCache[index].Mute = mute;
        }

        #endregion 音轨状态 [TRACK STATUS]

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 初始化音频处理器。由 <c>Handler</c> 赋值时自动调用。
        /// </summary>
        protected override void OnInit()
        {
            if (!Application.isPlaying) return;

            Initialize(null, null, null);

            // Register Events
            EventManager.RegisterCallback<AudioPlayEvent>(OnAudioPlayEvent);
            EventManager.RegisterCallback<AudioServiceEvent>(OnAudioServiceEvent);
            EventManager.RegisterCallback<AudioTrackControlEvent>(OnAudioTrackEvent);
            EventManager.RegisterCallback<AudioControlEvent>(OnAudioControlEvent);
            EventManager.RegisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
            EventManager.RegisterCallback<AudioFadeEvent>(OnAudioFadeEvent);
            EventManager.RegisterCallback<AllAudiosControlEvent>(OnAllAudiosControlEvent);

            SceneManager.sceneLoaded += OnSceneLoaded;

            // 加载音频设置，必须等一帧设置才能生效
            Scheduler.WaitFrame(1, AudioServiceEvent.LoadSettings);
        }

        /// <summary>
        /// 关闭音频处理器。由 <c>Handler</c> 置空/替换时自动调用。
        /// </summary>
        protected override void OnShutdown()
        {
            if (!Application.isPlaying) return;

            TweenUtility.StopAll(this);
            StopAll(fadeoutDuration: 0f);
            CleanAudioPool();
            _audioFades.Clear();
            _audioFadeCount = 0;
            _handleToAgent.Clear();

            // 回收所有句柄列表到对象池
            foreach (var handles in _userHandleMap.Values)
            {
                ReleaseHandleList(handles);
            }
            _userHandleMap.Clear();
            _sharedAgentBuffer.Clear();

            // Unregister Events
            EventManager.UnregisterCallback<AudioPlayEvent>(OnAudioPlayEvent);
            EventManager.UnregisterCallback<AudioServiceEvent>(OnAudioServiceEvent);
            EventManager.UnregisterCallback<AudioTrackControlEvent>(OnAudioTrackEvent);
            EventManager.UnregisterCallback<AudioControlEvent>(OnAudioControlEvent);
            EventManager.UnregisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
            EventManager.UnregisterCallback<AudioFadeEvent>(OnAudioFadeEvent);
            EventManager.UnregisterCallback<AllAudiosControlEvent>(OnAllAudiosControlEvent);

            SceneManager.sceneLoaded -= OnSceneLoaded;

            // 释放引用持有；以上缓存均由 Initialize 无条件重建，置空不影响复用实例的再 Init。
            _instanceRoot = null;
            _audioMixer = null;
            _audioGroupConfigs = null;
            _trackFadeTweenIds = null;
            _pausedTracks = null;
            _audioCategories = null;
            _categoryCache = null;
            _configCache = null;
            _trackFadeCallbacks = null;
        }

        /// <summary>
        /// 初始化音频服务。
        /// </summary>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <exception cref="GameException"></exception>
        public override void Initialize(Transform instanceRoot, AudioMixer audioMixer, AudioGroupConfig[] audioGroupConfigs)
        {
            _instanceRoot = instanceRoot;
            if (_instanceRoot == null)
            {
                _instanceRoot = new GameObject("[AudioService]").transform;
                _instanceRoot.localScale = Vector3.one;
                UnityEngine.Object.DontDestroyOnLoad(_instanceRoot);
            }

#if UNITY_EDITOR

            if (!_instanceRoot.GetComponent<AudioDebugger>())
            {
                _instanceRoot.gameObject.AddComponent<AudioDebugger>();
            }

            try
            {
                TypeInfo typeInfo = typeof(UnityEngine.AudioSettings).GetTypeInfo();
                PropertyInfo propertyInfo = typeInfo.GetDeclaredProperty("unityAudioDisabled");
                _unityAudioDisabled = (bool)propertyInfo.GetValue(null);
                if (_unityAudioDisabled)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                LogUtility.Error("[AudioService] Failed to check AudioSettings.unityAudioDisabled via reflection: {0}", e);
            }
#endif

            _audioMixer = audioMixer;
            if (_audioMixer == null)
            {
                _audioMixer = AudioServiceSettings.AudioMixer;
            }

            _audioGroupConfigs = audioGroupConfigs;
            if (_audioGroupConfigs == null)
            {
                _audioGroupConfigs = AudioServiceSettings.AudioGroupConfigs;
            }

            int trackCount = _audioGroupConfigs.Length;
            _trackFadeTweenIds = new long[trackCount];
            _pausedTracks = new bool[trackCount];
            _audioCategories = new AudioCategory[trackCount];
            _categoryCache = new AudioCategory[trackCount];
            _configCache = new AudioGroupConfig[trackCount];
            for (int i = 0; i < trackCount; i++)
            {
                _audioCategories[i] = new AudioCategory(this, _audioGroupConfigs[i]);
                _categoryCache[(int)_audioGroupConfigs[i].AudioTrack] = _audioCategories[i];
                _configCache[(int)_audioGroupConfigs[i].AudioTrack] = _audioGroupConfigs[i];
            }
        }

        /// <summary>
        /// 容器 Tick 驱动——轮询音轨与手动过渡。
        /// </summary>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.Update(elapseSeconds);
            }

            // 手动处理 AudioFade 过渡 — 零 GC
            UpdateAudioFades();
        }

        private void UpdateAudioFades()
        {
            float currentTime = GameTime.unscaledTime;
            int writeIndex = 0;

            for (int i = 0; i < _audioFadeCount; i++)
            {
                var fade = _audioFades[i];
                float elapsed = currentTime - fade.StartTime;

                if (elapsed >= fade.Duration)
                {
                    // 过渡完成，设置最终音量
                    var agent = GetAgentByHandle(fade.Handle);
                    if (agent != null && agent.AudioResource != null)
                        agent.AudioResource.volume = fade.EndVolume;
                }
                else
                {
                    // 过渡进行中，更新音量并保留
                    float t = elapsed / fade.Duration;
                    var agent = GetAgentByHandle(fade.Handle);
                    if (agent != null && agent.AudioResource != null)
                        agent.AudioResource.volume = Mathf.Lerp(fade.StartVolume, fade.EndVolume, t);
                    _audioFades[writeIndex++] = fade;
                }
            }

            _audioFadeCount = writeIndex;
        }

        /// <summary>
        /// 重启音频服务。
        /// </summary>
        public override void Restart()
        {
            if (_unityAudioDisabled) return;

            CleanAudioPool();

            foreach (var category in AudioCategories)
            {
                if (category == null) continue;

                foreach (var audioAgent in category.AudioAgents)
                {
                    audioAgent?.Destroy();
                }
            }

            Initialize(null, null, _audioGroupConfigs);
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public override ulong Play(AudioClip clip, AudioPlayOptions options)
        {
            if (_unityAudioDisabled) return 0UL;

            AudioCategory category = FindCategory(options.AudioTrack);
            if (category == null)
            {
                LogUtility.Error($"{options.AudioTrack} is not found in AudioCategories.");
                return 0UL;
            }

            AudioAgent audioAgent = category.GetAvailableAgent(options.DoNotAutoRecycleIfNotDonePlaying);
            if (audioAgent != null)
            {
                ulong handle = _nextAudioId++;
                audioAgent.Play(clip, options);
                _handleToAgent[handle] = audioAgent;

                // 建立双向映射
                if (!_userHandleMap.TryGetValue(options.ID, out var handles))
                {
                    handles = AcquireHandleList();
                    _userHandleMap[options.ID] = handles;
                }
                handles.Add(handle);

                return handle;
            }

            return 0UL;
        }

        /// <summary>
        /// 播放音频。
        /// </summary>
        public override ulong Play(AudioClip clip, EAudioTrack track, Vector3 location,
            bool loop,
            float volume, int id, bool fade, float fadeInitialVolume, float fadeDuration,
            TweenEase fadeTweenEase, bool persistent, AudioSource recycleAudioSource,
            AudioMixerGroup audioGroup, float pitch, float panStereo, float spatialBlend,
            bool soloSingleTrack, bool soloAllTracks, bool autoUnSoloOnEnd,
            bool bypassEffects,
            bool bypassListenerEffects, bool bypassReverbZones, int priority,
            float reverbZoneMix,
            float dopplerLevel, int spread, AudioRolloffMode rolloffMode,
            float minDistance, float maxDistance, bool doNotAutoRecycleIfNotDonePlaying,
            float playbackTime, float playbackDuration, Transform attachToTransform,
            bool useSpreadCurve,
            AnimationCurve spreadCurve, bool useCustomRolloffCurve,
            AnimationCurve customRolloffCurve,
            bool useSpatialBlendCurve, AnimationCurve spatialBlendCurve,
            bool useReverbZoneMixCurve, AnimationCurve reverbZoneMixCurve,
            float initialDelay
            )
        {
            var option = new AudioPlayOptions
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

            return Play(clip, option);
        }

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        public override ulong Play(string path, AudioPlayOptions options, bool bAsync, bool bInPool)
        {
            if (_unityAudioDisabled) return 0UL;

            AudioCategory category = FindCategory(options.AudioTrack);
            if (category == null)
            {
                LogUtility.Error($"{options.AudioTrack} is not found in AudioCategories.");
                return 0UL;
            }

            AudioAgent audioAgent = category.GetAvailableAgent(options.DoNotAutoRecycleIfNotDonePlaying);
            if (audioAgent != null)
            {
                ulong handle = _nextAudioId++;
                audioAgent.Load(path, options, bAsync, bInPool);
                _handleToAgent[handle] = audioAgent;

                // 建立双向映射
                if (!_userHandleMap.TryGetValue(options.ID, out var handles))
                {
                    handles = AcquireHandleList();
                    _userHandleMap[options.ID] = handles;
                }
                handles.Add(handle);

                return handle;
            }

            return 0UL;
        }

        /// <summary>
        /// 播放音频。
        /// </summary>
        public override ulong Play(string path, EAudioTrack track, Vector3 location, bool bAsync, bool bInPool,
            bool loop, float volume, int id,
            bool fade, float fadeInitialVolume, float fadeDuration, TweenEase fadeTweenEase,
            bool persistent,
            AudioSource recycleAudioSource, AudioMixerGroup audioGroup,
            float pitch, float panStereo, float spatialBlend,
            bool soloSingleTrack, bool soloAllTracks, bool autoUnSoloOnEnd,
            bool bypassEffects, bool bypassListenerEffects, bool bypassReverbZones,
            int priority, float reverbZoneMix,
            float dopplerLevel, int spread, AudioRolloffMode rolloffMode,
            float minDistance, float maxDistance,
            bool doNotAutoRecycleIfNotDonePlaying, float playbackTime, float playbackDuration,
            Transform attachToTransform,
            bool useSpreadCurve, AnimationCurve spreadCurve, bool useCustomRolloffCurve,
            AnimationCurve customRolloffCurve,
            bool useSpatialBlendCurve, AnimationCurve spatialBlendCurve,
            bool useReverbZoneMixCurve, AnimationCurve reverbZoneMixCurve,
            float initialDelay)
        {
            var option = new AudioPlayOptions
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

            return Play(path, option, bAsync, bInPool);
        }

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停指定句柄的音频
        /// </summary>
        public override void Pause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && agent.IsPlaying)
                agent.Pause();
        }

        /// <summary>
        /// 恢复播放指定句柄的音频
        /// </summary>
        public override void Unpause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && agent.IsPaused)
                agent.Unpause();
        }

        /// <summary>
        /// 停止指定句柄的音频
        /// </summary>
        public override void Stop(ulong handle, float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && (agent.IsPlaying || agent.IsPaused))
                agent.Stop(fadeoutDuration);
        }

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <summary>
        /// 查找指定音轨的 AudioCategory。
        /// </summary>
        private AudioCategory FindCategory(EAudioTrack track)
        {
            int index = (int)track;
            return index >= 0 && index < _categoryCache.Length ? _categoryCache[index] : null;
        }

        /// <summary>
        /// 对每个匹配 ID 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public override void ForEachAgentByID(int id, Action<AudioAgent> action)
        {
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                var agents = AudioCategories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    if (agents[j].ID == id)
                        action(agents[j]);
                }
            }
        }

        /// <summary>
        /// 返回播放过指定 ID 的音频代理（零分配：使用共享缓冲区，调用方需在下次调用前消费结果）。
        /// </summary>
        public override IReadOnlyList<AudioAgent> FindAgentsByID(int id)
        {
            _sharedAgentBuffer.Clear();
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                var agents = AudioCategories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    if (agents[j].ID == id)
                        _sharedAgentBuffer.Add(agents[j]);
                }
            }
            return _sharedAgentBuffer;
        }

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。
        /// </summary>
        public override void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action)
        {
            if (clip == null) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                var agents = AudioCategories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    if (agents[j].AudioResource.clip == clip)
                        action(agents[j]);
                }
            }
        }

        /// <summary>
        /// 返回播放过指定 clip 的音频代理（零分配：使用共享缓冲区，调用方需在下次调用前消费结果）。
        /// </summary>
        public override IReadOnlyList<AudioAgent> FindAgentsByClip(AudioClip clip)
        {
            if (clip == null) return Array.Empty<AudioAgent>();

            _sharedAgentBuffer.Clear();
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                var agents = AudioCategories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    if (agents[j].AudioResource.clip == clip)
                        _sharedAgentBuffer.Add(agents[j]);
                }
            }
            return _sharedAgentBuffer;
        }

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量
        /// </summary>
        public override int CurrentlyPlayingCount(AudioClip clip)
        {
            if (clip == null) return 0;

            int count = 0;
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                var agents = AudioCategories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    if (agents[j].AudioResource.clip == clip && agents[j].AudioResource.isPlaying)
                        count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。
        /// </summary>
        public override AudioAgent GetAgentByHandle(ulong handle)
        {
            return _handleToAgent.TryGetValue(handle, out var agent) ? agent : null;
        }

        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        public override bool IsPlaying(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return false;
            return agent.IsPlaying;
        }

        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        public override bool IsStopped(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return true;
            return agent.IsFree;
        }

        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        public override void ReleaseHandle(ulong handle)
        {
            // O(1) 反向查找清理用户 ID 映射
            if (_handleToAgent.TryGetValue(handle, out var agent))
            {
                int userId = agent.ID;

                if (_userHandleMap.TryGetValue(userId, out var handles))
                {
                    handles.Remove(handle);
                    if (handles.Count == 0)
                    {
                        _userHandleMap.Remove(userId);
                        ReleaseHandleList(handles);
                    }
                }

                _handleToAgent.Remove(handle);
            }
        }

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        public override void Pause(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            _pausedTracks[(int)track] = true;
            AudioCategory category = FindCategory(track);
            category?.PauseAll();
        }

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        public override void Unpause(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            _pausedTracks[(int)track] = false;
            AudioCategory category = FindCategory(track);
            category?.UnpauseAll();
        }

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        public override bool IsPaused(EAudioTrack track)
        {
            return _pausedTracks[(int)track];
        }

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        public override void Stop(EAudioTrack track, float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            AudioCategory category = FindCategory(track);
            category?.StopAll(fadeoutDuration);
        }

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public override void PauseAll()
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.PauseAll();
            }
        }

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public override void UnpauseAll()
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.UnpauseAll();
            }
        }

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        public override void StopAll(float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAll(fadeoutDuration);
            }
        }

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        public override void StopAllButPersistent(float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAllButPersistent(fadeoutDuration);
            }
        }

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        public override void StopAllLooping(float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAllLooping(fadeoutDuration);
            }
        }

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        // ===== 静态回调 — 零 GC =====

        private static void OnMasterFadeUpdate(AudioServiceHandler service, float value)
        {
            service.MasterVolume = value;
        }

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量
        /// </summary>
        public override void FadeMasterTrack(float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (duration <= 0f) { MasterVolume = finalVolume; return; }

            TweenUtility.Stop(_masterFadeTweenId);
            _masterFadeTweenId = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                OnMasterFadeUpdate, tweenEase, useUnscaledTime: true);
        }

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）
        /// </summary>
        public override void StopFadeMasterTrack()
        {
            TweenUtility.Stop(_masterFadeTweenId);
        }

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量
        /// </summary>
        public override void FadeTrack(EAudioTrack track, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (duration <= 0f) { SetTrackVolume(track, finalVolume); return; }

            StopFadeTrack(track);
            _trackFadeTweenIds[(int)track] = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                TrackFadeCallbacks[(int)track], tweenEase, useUnscaledTime: true);
        }

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）
        /// </summary>
        public override void StopFadeTrack(EAudioTrack track)
        {
            int index = (int)track;
            if (_trackFadeTweenIds[index] != 0)
            {
                TweenUtility.Stop(_trackFadeTweenIds[index]);
                _trackFadeTweenIds[index] = 0;
            }
        }

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。
        /// </summary>
        /// <remarks>使用手动过渡系统，完全零 GC。</remarks>
        public override void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (duration <= 0f)
            {
                var agent = GetAgentByHandle(handle);
                if (agent != null && agent.AudioResource != null)
                    agent.AudioResource.volume = finalVolume;
                return;
            }

            StopFadeAudio(handle);

            if (_audioFadeCount >= _audioFades.Count)
                _audioFades.Add(default);

            _audioFades[_audioFadeCount++] = new AudioFadeState
            {
                Handle = handle,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
            };
        }

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）
        /// </summary>
        public override void StopFadeAudio(ulong handle)
        {
            if (handle == 0) return;

            for (int i = _audioFadeCount - 1; i >= 0; i--)
            {
                if (_audioFades[i].Handle == handle)
                {
                    // Swap-and-pop: O(1) 移除
                    _audioFadeCount--;
                    if (i < _audioFadeCount)
                    {
                        _audioFades[i] = _audioFades[_audioFadeCount];
                    }
                }
            }
        }

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中
        /// </summary>
        public override bool SoundIsFadingOut(ulong handle)
        {
            for (int i = 0; i < _audioFadeCount; i++)
            {
                if (_audioFades[i].Handle == handle)
                    return true;
            }
            return false;
        }

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        public override void PutInAudioPool(List<string> list)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (AssetHandlePool != null && !AssetHandlePool.ContainsKey(path))
                {
                    var lease = ResourceService.LoadLease<AudioClip>(path);
                    AssetHandlePool?.Add(path, lease);
                }
            }
        }

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        public override void RemoveClipFromPool(List<string> list)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (AssetHandlePool.TryGetValue(path, out var handleObj))
                {
                    ReleaseHandleObject(handleObj);
                    AssetHandlePool.Remove(path);
                }
            }
        }

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        public override void CleanAudioPool()
        {
            if (_unityAudioDisabled) return;

            foreach (var dic in AssetHandlePool)
            {
                ReleaseHandleObject(dic.Value);
            }

            AssetHandlePool.Clear();
        }

        /// <summary>
        /// 释放句柄包装对象持有的租约/原生句柄。
        /// </summary>
        private static void ReleaseHandleObject(object handleObj)
        {
            if (handleObj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        #endregion 资源池 [ASSET POOL]

        #region 事件 [EVENTS]

        /// <summary>
        /// 播放音频事件
        /// </summary>
        private void OnAudioPlayEvent(AudioPlayEvent evt)
        {
            if (evt.Clip != null)
            {
                evt.AudioHandle = Play(evt.Clip, evt.Options);
            }
            else if (!string.IsNullOrEmpty(evt.AudioPath))
            {
                evt.AudioHandle = Play(evt.AudioPath, evt.Options, evt.LoadAssetAsync, evt.CacheAssetHandle);
            }
        }

        /// <summary>
        /// 游戏音频设置相关事件
        /// </summary>
        private void OnAudioServiceEvent(AudioServiceEvent evt)
        {
            switch (evt.Mode)
            {
                case AudioServiceEvent.EMode.SetSettings:
                    SetMasterSettings();
                    for (int i = 0; i < AudioCategories.Length; i++) { AudioCategories[i]?.SetSettings(); }
                    break;
                case AudioServiceEvent.EMode.LoadSettings:
                    LoadMasterSettings();
                    for (int i = 0; i < AudioCategories.Length; i++) { AudioCategories[i]?.LoadSettings(); }
                    break;
                case AudioServiceEvent.EMode.ResetSettings:
                    RemoveMasterSetting();
                    for (int i = 0; i < AudioCategories.Length; i++) { AudioCategories[i]?.RemoveSetting(); }
                    break;
            }
        }

        /// <summary>
        /// 音轨事件
        /// </summary>
        private void OnAudioTrackEvent(AudioTrackControlEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.ControlMode)
                {
                    case AudioTrackControlEvent.EControlMode.Mute:
                        MasterMute = true;
                        break;
                    case AudioTrackControlEvent.EControlMode.Unmute:
                        MasterMute = false;
                        break;
                    case AudioTrackControlEvent.EControlMode.SetVolume:
                        MasterVolume = evt.Volume;
                        break;
                    case AudioTrackControlEvent.EControlMode.Pause:
                        PauseAll();
                        break;
                    case AudioTrackControlEvent.EControlMode.Unpause:
                        UnpauseAll();
                        break;
                    case AudioTrackControlEvent.EControlMode.Stop:
                        StopAll(0f);
                        break;
                }
            }
            else
            {
                switch (evt.ControlMode)
                {
                    case AudioTrackControlEvent.EControlMode.Mute:
                        SetTrackMute(evt.Track, true);
                        break;
                    case AudioTrackControlEvent.EControlMode.Unmute:
                        SetTrackMute(evt.Track, false);
                        break;
                    case AudioTrackControlEvent.EControlMode.SetVolume:
                        SetTrackVolume(evt.Track, evt.Volume);
                        break;
                    case AudioTrackControlEvent.EControlMode.Pause:
                        Pause(evt.Track);
                        break;
                    case AudioTrackControlEvent.EControlMode.Unpause:
                        Unpause(evt.Track);
                        break;
                    case AudioTrackControlEvent.EControlMode.Stop:
                        Stop(evt.Track, 0f);
                        break;
                }
            }
        }

        /// <summary>
        /// 音频控制事件（通过用户 ID 查找所有句柄后操作）
        /// </summary>
        private void OnAudioControlEvent(AudioControlEvent evt)
        {
            if (!_userHandleMap.TryGetValue(evt.AudioID, out var handles)) return;

            // 缓存 count：Stop 可能触发 ReleaseHandle 从 handles 中移除元素，
            // 但索引访问 handles[i] 仍安全，因为 count 只增不减。
            int count = handles.Count;
            LogUtility.Debug("[AudioService] {0} Audio: {1} x{2}", evt.EventType, evt.AudioID, count);
            for (int i = 0; i < count; i++)
            {
                ulong handle = handles[i];
                switch (evt.EventType)
                {
                    case AudioControlEvent.EAudioControlEventType.Pause:
                        Pause(handle);
                        break;
                    case AudioControlEvent.EAudioControlEventType.Unpause:
                        Unpause(handle);
                        break;
                    case AudioControlEvent.EAudioControlEventType.Stop:
                        Stop(handle, 0f);
                        break;
                }
            }
        }

        /// <summary>
        /// 音轨过渡事件
        /// </summary>
        private void OnAudioTrackFadeEvent(AudioTrackFadeEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.Mode)
                {
                    case AudioTrackFadeEvent.EMode.PlayFade:
                        FadeMasterTrack(evt.FadeDuration, GetTrackVolume(evt.Track), evt.FinalVolume, evt.FadeTweenEase);
                        break;
                    case AudioTrackFadeEvent.EMode.StopFade:
                        StopFadeMasterTrack();
                        break;
                }
            }
            else
            {
                switch (evt.Mode)
                {
                    case AudioTrackFadeEvent.EMode.PlayFade:
                        FadeTrack(evt.Track, evt.FadeDuration, GetTrackVolume(evt.Track), evt.FinalVolume, evt.FadeTweenEase);
                        break;
                    case AudioTrackFadeEvent.EMode.StopFade:
                        StopFadeTrack(evt.Track);
                        break;
                }
            }
        }

        /// <summary>
        /// 音频过渡事件（通过用户 ID 查找所有句柄后操作）
        /// </summary>
        private void OnAudioFadeEvent(AudioFadeEvent evt)
        {
            // 通过用户 ID 查找对应的所有句柄
            if (!_userHandleMap.TryGetValue(evt.SoundID, out var handles)) return;

            // 缓存 count：StopFadeAudio 不修改 handles，安全迭代。
            int count = handles.Count;
            for (int i = 0; i < count; i++)
            {
                ulong handle = handles[i];
                var agent = GetAgentByHandle(handle);
                if (agent == null) continue;

                switch (evt.Mode)
                {
                    case AudioFadeEvent.EAudioFadeEventMode.PlayFade:
                        agent.CancelFadeIn();
                        FadeAudio(handle, evt.FadeDuration, agent.AudioResource.volume, evt.FinalVolume, evt.FadeTweenEase);
                        break;
                    case AudioFadeEvent.EAudioFadeEventMode.StopFade:
                        StopFadeAudio(handle);
                        break;
                }
            }
        }

        /// <summary>
        /// 全部音频控制事件
        /// </summary>
        private void OnAllAudiosControlEvent(AllAudiosControlEvent evt)
        {
            switch (evt.ControlMode)
            {
                case AllAudiosControlEvent.EControlMode.Pause:
                    PauseAll();
                    break;
                case AllAudiosControlEvent.EControlMode.Play:
                    UnpauseAll();
                    break;
                case AllAudiosControlEvent.EControlMode.Stop:
                    StopAll(fadeoutDuration: AudioAgent.FADEOUT_DEFAULT_DURATION);
                    break;
                case AllAudiosControlEvent.EControlMode.StopAllButPersistent:
                    StopAllButPersistent(fadeoutDuration: AudioAgent.FADEOUT_DEFAULT_DURATION);
                    break;
                case AllAudiosControlEvent.EControlMode.StopAllLooping:
                    StopAllLooping(fadeoutDuration: AudioAgent.FADEOUT_DEFAULT_DURATION);
                    break;
            }
        }

        /// <summary>
        /// 释放除了持久性的音频之外的所有音频。
        /// </summary>
        /// <remarks>每次加载新场景时触发</remarks>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode loadSceneMode)
        {
            StopAllButPersistent(fadeoutDuration: AudioAgent.FADEOUT_DEFAULT_DURATION);
        }

        #endregion
    }
}
