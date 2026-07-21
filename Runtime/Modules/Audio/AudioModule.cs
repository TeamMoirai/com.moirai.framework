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
    /// 音效管理，为游戏提供统一的音效播放接口。
    /// </summary>
    /// <remarks>场景3D音效挂到场景物件、技能3D音效挂到技能特效上，并在 <see cref="AudioSource"/> 的Output上设置对应分类的 <see cref="AudioMixerGroup"/></remarks>
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed class AudioModule : Module, IAudioModule, IUpdateModule
    {
        private AudioGroupConfig[] _audioGroupConfigs;
        private bool _unityAudioDisabled;
        private IResourceModule _resourceModule;

        // Master 音轨过渡 Tween ID
        private long _masterFadeTweenId;
        // 音轨过渡 Tween ID（数组索引 = (int)EAudioTrack）
        private long[] _trackFadeTweenIds;
        // 音轨暂停状态（数组索引 = (int)EAudioTrack）
        private bool[] _pausedTracks;
        // 音轨 -> Category 缓存，O(1) 数组直接访问
        private AudioCategory[] _categoryCache;
        // 音轨 -> AudioGroupConfig 缓存，O(1) 数组直接访问
        private AudioGroupConfig[] _configCache;
        // 模块自维护 ID -> AudioAgent 映射
        private readonly Dictionary<ulong, AudioAgent> _handleToAgent = new Dictionary<ulong, AudioAgent>();
        // 用户定义 ID -> 模块句柄列表 映射（1 对多，支持事件系统通过用户 ID 查找所有句柄）
        private readonly Dictionary<int, List<ulong>> _userHandleMap = new Dictionary<int, List<ulong>>();
        // 模块自维护 ID 生成器
        private ulong _nextAudioId = 1UL;
        // 临时列表，用于 FindAgents 系列方法（避免每次分配）
        private readonly List<AudioAgent> _sharedAgentBuffer = new List<AudioAgent>(4);
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

        private AudioMixer _audioMixer;
        public AudioMixer AudioMixer => _audioMixer;

        private Transform _instanceRoot;
        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot
        {
            get => _instanceRoot;
            set => _instanceRoot = value;
        }
        
        // 资源句柄池，用于缓存资源系统的已加载音频资源。
        private readonly Dictionary<string, AssetHandle> _assetHandlePool = new Dictionary<string, AssetHandle>();
        
        // ===== FadeAudio 手动过渡 — 零 GC =====
        private struct AudioFadeState
        {
            public ulong Handle;
            public float StartTime;
            public float Duration;
            public float StartVolume;
            public float EndVolume;
        }
        private readonly List<AudioFadeState> _audioFades = new List<AudioFadeState>(4);
        private int _audioFadeCount;
        
        #region 音轨状态 [TRACK STATUS]
        
        public AudioCategory[] AudioCategories { get; private set; }
        
        private float _volume = 1f;
        public float MasterVolume
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

        private bool _isMuted;
        public bool MasterMute
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
        
        public void SetMasterSettings()
        {
            SettingUtility.SetFloat(Constant.Setting.AUDIO_MASTER_VOLUME, _volume);
            SettingUtility.SetBool(Constant.Setting.AUDIO_MASTER_MUTED, _isMuted);
        }

        public void LoadMasterSettings()
        {
            _isMuted = SettingUtility.GetBool(Constant.Setting.AUDIO_MASTER_MUTED, false);
            _volume = SettingUtility.GetFloat(Constant.Setting.AUDIO_MASTER_VOLUME, 1f);
            
            ApplyMasterVolume();
        }

        public void RemoveMasterSetting()
        {
            SettingUtility.RemoveSetting(Constant.Setting.AUDIO_MASTER_MUTED);
            SettingUtility.RemoveSetting(Constant.Setting.AUDIO_MASTER_VOLUME);
            
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
        
        public float GetTrackVolume(EAudioTrack track)
        {
            if (_unityAudioDisabled) return 0f;
            int index = (int)track;
            return index >= 0 && index < _configCache.Length ? _configCache[index].Volume : 1f;
        }
        
        public void SetTrackVolume(EAudioTrack track, float volume)
        {
            if (_unityAudioDisabled) return;
            int index = (int)track;
            if (index >= 0 && index < _configCache.Length)
                _configCache[index].Volume = volume;
        }
        
        public bool GetTrackMute(EAudioTrack track)
        {
            if (_unityAudioDisabled) return false;
            int index = (int)track;
            return index >= 0 && index < _configCache.Length && _configCache[index].Mute;
        }
        
        public void SetTrackMute(EAudioTrack track, bool mute)
        {
            if (_unityAudioDisabled) return;
            int index = (int)track;
            if (index >= 0 && index < _configCache.Length)
                _configCache[index].Mute = mute;
        }
        
        #endregion 音轨状态 [TRACK STATUS
        
        #region 模块方法 [MODULE METHOD]

        public override void OnInit()
        {
            if (!Application.isPlaying) return;
            
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            
            Initialize();
            
            // Register Events
            EventManager.RegisterCallback<AudioPlayEvent>(OnAudioPlayEvent);
            EventManager.RegisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            EventManager.RegisterCallback<AudioTrackControlEvent>(OnAudioTrackEvent);
            EventManager.RegisterCallback<AudioControlEvent>(OnAudioControlEvent);
            EventManager.RegisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
            EventManager.RegisterCallback<AudioFadeEvent>(OnAudioFadeEvent);
            EventManager.RegisterCallback<AllAudiosControlEvent>(OnAllAudiosControlEvent);
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // 加载音频设置，必须等一帧设置才能生效
            Scheduler.WaitFrame(1, OnSettingsLoadDelay);
        }

        private static void OnSettingsLoadDelay()
        {
            AudioModuleEvent.Trigger(AudioModuleEvent.EAudioModuleEventType.LoadSettings);
        }
        
        public void Initialize(Transform instanceRoot = null, AudioMixer audioMixer = null, AudioGroupConfig[] audioGroupConfigs = null)
        {
            _instanceRoot = instanceRoot;
            if (_instanceRoot == null)
            {
                _instanceRoot = new GameObject("[AudioModule]").transform;
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
                Log.Error($"[AudioModule] Failed to check AudioSettings.unityAudioDisabled via reflection: {e}");
            }
#endif
            
            _audioMixer = audioMixer;
            if (_audioMixer == null)
            {
                _audioMixer = AudioSettings.AudioMixer;
            }
            
            _audioGroupConfigs = audioGroupConfigs;
            if (_audioGroupConfigs == null)
            {
                _audioGroupConfigs = AudioSettings.AudioGroupConfigs;
            }

            int trackCount = _audioGroupConfigs.Length;
            _trackFadeTweenIds = new long[trackCount];
            _pausedTracks = new bool[trackCount];
            AudioCategories = new AudioCategory[trackCount];
            _categoryCache = new AudioCategory[trackCount];
            _configCache = new AudioGroupConfig[trackCount];
            for (int i = 0; i < trackCount; i++)
            {
                AudioCategories[i] = new AudioCategory(_audioGroupConfigs[i]);
                _categoryCache[(int)_audioGroupConfigs[i].AudioTrack] = AudioCategories[i];
                _configCache[(int)_audioGroupConfigs[i].AudioTrack] = _audioGroupConfigs[i];
            }
        }
        
        public void Update(float elapseSeconds, float realElapseSeconds)
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
        
        public override void Shutdown()
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
            EventManager.UnregisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            EventManager.UnregisterCallback<AudioTrackControlEvent>(OnAudioTrackEvent);
            EventManager.UnregisterCallback<AudioControlEvent>(OnAudioControlEvent);
            EventManager.UnregisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
            EventManager.UnregisterCallback<AudioFadeEvent>(OnAudioFadeEvent);
            EventManager.UnregisterCallback<AllAudiosControlEvent>(OnAllAudiosControlEvent);
            
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        public void Restart()
        {
            if (_unityAudioDisabled) return;

            CleanAudioPool();

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                var category = AudioCategories[i];
                if (category == null) continue;
                
                category.DestroyAll();
            }
            
            Initialize(audioGroupConfigs:_audioGroupConfigs);
        }
        
        #endregion 模块方法 [MODULE METHOD]

        #region 播放音频 [PLAY AUDIO]
        
        /// <summary>
        /// 播放音频，返回模块自维护的音频句柄。
        /// </summary>
        public ulong Play(AudioClip clip, AudioPlayOptions options)
        {
            if (_unityAudioDisabled) return 0UL;

            AudioCategory category = FindCategory(options.AudioTrack);
            if (category == null)
            {
                Log.Error($"{options.AudioTrack} is not found in AudioCategories.");
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

        public ulong Play(AudioClip clip, EAudioTrack track, Vector3 location,
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
        /// 播放音频，返回模块自维护的音频句柄。
        /// </summary>
        public ulong Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false)
        {
            if (_unityAudioDisabled) return 0UL;

            AudioCategory category = FindCategory(options.AudioTrack);
            if (category == null)
            {
                Log.Error($"{options.AudioTrack} is not found in AudioCategories.");
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

        public ulong Play(string path, EAudioTrack track, Vector3 location, bool bAsync = false, bool bInPool = false,
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
            float initialDelay = 0f)
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
         
        public void Pause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && agent.IsPlaying)
                agent.Pause();
        }

        public void UnPause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && agent.IsPaused)
                agent.UnPause();
        }
        
        public void Stop(ulong handle, float fadeoutDuration = 0f)
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
        public void ForEachAgentByID(int id, Action<AudioAgent> action)
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
        public IReadOnlyList<AudioAgent> FindAgentsByID(int id)
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
        public void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action)
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
        public IReadOnlyList<AudioAgent> FindAgentsByClip(AudioClip clip)
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

        public int CurrentlyPlayingCount(AudioClip clip)
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
        public AudioAgent GetAgentByHandle(ulong handle)
        {
            return _handleToAgent.TryGetValue(handle, out var agent) ? agent : null;
        }
        
        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        public bool IsPlaying(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return false;
            return agent.IsPlaying;
        }
        
        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        public bool IsStopped(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return true;
            return agent.IsFree;
        }
        
        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        public void ReleaseHandle(ulong handle)
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

        public void Pause(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            _pausedTracks[(int)track] = true;
            AudioCategory category = FindCategory(track);
            category?.PauseAll();
        }
        
        public void UnPause(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            _pausedTracks[(int)track] = false;
            AudioCategory category = FindCategory(track);
            category?.UnPauseAll();
        }

        public bool IsPaused(EAudioTrack track)
        {
            return _pausedTracks[(int)track];
        }

        public void Stop(EAudioTrack track, float fadeoutDuration = 0f)
        {
            if (_unityAudioDisabled) return;

            AudioCategory category = FindCategory(track);
            category?.StopAll(fadeoutDuration);
        }
        
        #endregion 音轨控制 [TRACK CONTROLS]
        
        #region 所有音频控制 [ALL AUDIO CONTROLS]

        public void PauseAll()
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.PauseAll();
            }
        }

        public void UnPauseAll()
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.UnPauseAll();
            }
        }

        public void StopAll(float fadeoutDuration = 0f)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAll(fadeoutDuration);
            }
        }

        public void StopAllButPersistent(float fadeoutDuration = 0f)
        {
            if (_unityAudioDisabled) return;
            
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAllButPersistent(fadeoutDuration);
            }
        }
        
        public void StopAllLooping(float fadeoutDuration = 0f)
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

        private static void OnMasterFadeUpdate(AudioModule module, float value)
        {
            module.MasterVolume = value;
        }

        // 每个音轨一个静态回调，避免闭包捕获 track
        private static readonly Action<AudioModule, float>[] s_TrackFadeCallbacks = new Action<AudioModule, float>[]
        {
            (m, v) => m.SetTrackVolume(EAudioTrack.Sfx, v),
            (m, v) => m.SetTrackVolume(EAudioTrack.UI, v),
            (m, v) => m.SetTrackVolume(EAudioTrack.Music, v),
            (m, v) => m.SetTrackVolume(EAudioTrack.Voice, v),
        };

        public void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default)
        {
            if (duration <= 0f) { MasterVolume = finalVolume; return; }

            TweenUtility.Stop(_masterFadeTweenId);
            _masterFadeTweenId = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                OnMasterFadeUpdate, tweenEase, useUnscaledTime: true);
        }

        public void StopFadeMasterTrack()
        {
            TweenUtility.Stop(_masterFadeTweenId);
        }

        public void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default)
        {
            if (duration <= 0f) { SetTrackVolume(track, finalVolume); return; }

            StopFadeTrack(track);
            _trackFadeTweenIds[(int)track] = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                s_TrackFadeCallbacks[(int)track], tweenEase, useUnscaledTime: true);
        }

        public void StopFadeTrack(EAudioTrack track)
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
        public void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
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

        public void StopFadeAudio(ulong handle)
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

        public bool SoundIsFadingOut(ulong handle)
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

        public void PutInAudioPool(List<string> list)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (!_assetHandlePool.ContainsKey(path))
                    _assetHandlePool[path] = _resourceModule.LoadAssetAsyncHandle<AudioClip>(path);
            }
        }

        public void RemoveClipFromPool(List<string> list)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (_assetHandlePool.TryGetValue(path, out var handle))
                {
                    handle.Dispose();
                    _assetHandlePool.Remove(path);
                }
            }
        }

        public void CleanAudioPool()
        {
            if (_unityAudioDisabled) return;

            var enumerator = _assetHandlePool.GetEnumerator();
            while (enumerator.MoveNext())
            {
                enumerator.Current.Value.Dispose();
            }
            enumerator.Dispose();

            _assetHandlePool.Clear();
        }
        
        /// <summary>
        /// 获取缓存的音频资源句柄。
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="handle">输出的资源句柄。</param>
        /// <returns>是否找到。</returns>
        internal bool TryGetCachedAsset(string path, out AssetHandle handle)
        {
            return _assetHandlePool.TryGetValue(path, out handle);
        }
        
        /// <summary>
        /// 添加缓存的音频资源句柄。
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="handle">资源句柄。</param>
        internal void AddCachedAsset(string path, AssetHandle handle)
        {
            _assetHandlePool.TryAdd(path, handle);
        }
        
        #endregion 资源池 [ASSET POOL]
        
        #region 事件 [EVENTS]

        /// <summary>
        /// 播放音频事件
        /// </summary>
        /// <param name="evt"></param>
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
        /// <param name="evt"></param>
        private void OnAudioModuleEvent(AudioModuleEvent evt)
        {
            switch (evt.EventType)
            {
                case AudioModuleEvent.EAudioModuleEventType.SetSettings:
                    SetMasterSettings();
                    for (int i = 0; i < AudioCategories.Length; i++) { AudioCategories[i]?.SetSettings(); }
                    break;
                case AudioModuleEvent.EAudioModuleEventType.LoadSettings:
                    LoadMasterSettings();
                    for (int i = 0; i < AudioCategories.Length; i++) { AudioCategories[i]?.LoadSettings(); }
                    break;
                case AudioModuleEvent.EAudioModuleEventType.ResetSettings:
                    RemoveMasterSetting();
                    for (int i = 0; i < AudioCategories.Length; i++) { AudioCategories[i]?.RemoveSetting(); }
                    break;
            }
        }
        
        /// <summary>
        /// 音轨事件
        /// </summary>
        /// <param name="evt"></param>
        private void OnAudioTrackEvent(AudioTrackControlEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackControlEvent.EAudioTrackEventType.MuteTrack:
                        MasterMute = true;
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.UnmuteTrack:
                        MasterMute = false;
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.SetTrackVolume:
                        MasterVolume = evt.Volume;
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.PauseTrack:
                        PauseAll();
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.UnPauseTrack:
                        UnPauseAll();
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.StopTrack:
                        StopAll();
                        break;
                }
            }
            else
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackControlEvent.EAudioTrackEventType.MuteTrack:
                        SetTrackMute(evt.Track, true);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.UnmuteTrack:
                        SetTrackMute(evt.Track, false);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.SetTrackVolume:
                        SetTrackVolume(evt.Track, evt.Volume);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.PauseTrack:
                        Pause(evt.Track);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.UnPauseTrack:
                        UnPause(evt.Track);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.StopTrack:
                        Stop(evt.Track);
                        break;
                }
            }
        }
        
        /// <summary>
        /// 音频控制事件（通过用户 ID 查找所有句柄后操作）
        /// </summary>
        /// <param name="evt"></param>
        private void OnAudioControlEvent(AudioControlEvent evt)
        {
            if (!_userHandleMap.TryGetValue(evt.AudioID, out var handles)) return;
            
            // 缓存 count：Stop 可能触发 ReleaseHandle 从 handles 中移除元素，
            // 但索引访问 handles[i] 仍安全，因为 count 只增不减。
            int count = handles.Count;
            Log.Debug("[AudioModule] {0} Audio: {1} x{2}", evt.EventType, evt.AudioID, count);
            for (int i = 0; i < count; i++)
            {
                ulong handle = handles[i];
                switch (evt.EventType)
                {
                    case AudioControlEvent.EAudioControlEventType.Pause:
                        Pause(handle);
                        break;
                    case AudioControlEvent.EAudioControlEventType.UnPause:
                        UnPause(handle);
                        break;
                    case AudioControlEvent.EAudioControlEventType.Stop:
                        Stop(handle);
                        break;
                }
            }
        }
        
        /// <summary>
        /// 音轨过渡事件
        /// </summary>
        /// <param name="evt"></param>
        private void OnAudioTrackFadeEvent(AudioTrackFadeEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.Mode)
                {
                    case AudioTrackFadeEvent.EAudioTrackFadeEventMode.PlayFade:
                        FadeMasterTrack(evt.FadeDuration, GetTrackVolume(evt.Track), evt.FinalVolume, evt.FadeTweenEase);
                        break;
                    case AudioTrackFadeEvent.EAudioTrackFadeEventMode.StopFade:
                        StopFadeMasterTrack();
                        break;
                }
            }
            else
            {
                switch (evt.Mode)
                {
                    case AudioTrackFadeEvent.EAudioTrackFadeEventMode.PlayFade:
                        FadeTrack(evt.Track, evt.FadeDuration, GetTrackVolume(evt.Track), evt.FinalVolume, evt.FadeTweenEase);
                        break;
                    case AudioTrackFadeEvent.EAudioTrackFadeEventMode.StopFade:
                        StopFadeTrack(evt.Track);
                        break;
                }
            }
        }
        
        /// <summary>
        /// 音频过渡事件（通过用户 ID 查找所有句柄后操作）
        /// </summary>
        /// <param name="evt"></param>
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
        /// <param name="evt"></param>
        private void OnAllAudiosControlEvent(AllAudiosControlEvent evt)
        {
            switch (evt.EventType)
            {
                case AllAudiosControlEvent.EAllAudiosControlEventType.Pause:
                    PauseAll();
                    break;
                case AllAudiosControlEvent.EAllAudiosControlEventType.Play:
                    UnPauseAll();
                    break;
                case AllAudiosControlEvent.EAllAudiosControlEventType.Stop:
                    StopAll(fadeoutDuration:AudioAgent.FADEOUT_DEFAULT_DURATION);
                    break;
                case AllAudiosControlEvent.EAllAudiosControlEventType.StopAllButPersistent:
                    StopAllButPersistent(fadeoutDuration:AudioAgent.FADEOUT_DEFAULT_DURATION);
                    break;
                case AllAudiosControlEvent.EAllAudiosControlEventType.StopAllLooping:
                    StopAllLooping(fadeoutDuration:AudioAgent.FADEOUT_DEFAULT_DURATION);
                    break;
            }
        }
        
        /// <summary>
        /// 释放除了持久性的音频之外的所有音频。
        /// </summary>
        /// <remarks>每次加载新场景时触发</remarks>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode loadSceneMode)
        {
            StopAllButPersistent(fadeoutDuration:AudioAgent.FADEOUT_DEFAULT_DURATION);
        }

        #endregion
    }
}
