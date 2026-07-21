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
        private bool _bUnityAudioDisabled;
        private IResourceModule _resourceModule;

        // Master 音轨过渡 Tween ID
        private long _masterFadeTweenId;
        // 音轨过渡 Tween ID
        private readonly Dictionary<AudioTrack, long> _trackFadeTweenIds = new Dictionary<AudioTrack, long>();
        // 音轨暂停状态
        private readonly Dictionary<AudioTrack, bool> _pausedTracks = new Dictionary<AudioTrack, bool>();
        // 音轨 -> Category 缓存，O(1) 查找
        private readonly Dictionary<AudioTrack, AudioCategory> _categoryCache = new Dictionary<AudioTrack, AudioCategory>(4);
        // 音轨 -> AudioGroupConfig 缓存，O(1) 查找
        private readonly Dictionary<AudioTrack, AudioGroupConfig> _configCache = new Dictionary<AudioTrack, AudioGroupConfig>(4);
        // 模块自维护 ID -> AudioAgent 映射
        private readonly Dictionary<ulong, AudioAgent> _agentById = new Dictionary<ulong, AudioAgent>();
        // 用户定义 ID -> 模块句柄 映射（支持事件系统通过用户 ID 查找句柄）
        private readonly Dictionary<int, ulong> _userHandleMap = new Dictionary<int, ulong>();
        // 句柄 -> 用户 ID 反向映射（O(1) 清理）
        private readonly Dictionary<ulong, int> _handleToUserMap = new Dictionary<ulong, int>();
        // 模块自维护 ID 生成器
        private ulong _nextAudioId = 1;
        // 临时列表，用于 FindAgents 系列方法（避免每次分配）
        private readonly List<AudioAgent> _sharedAgentBuffer = new List<AudioAgent>(8);

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
        private readonly List<AudioFadeState> _audioFades = new List<AudioFadeState>(8);
        
        #region 音轨状态 [TRACK STATUS]
        
        public AudioCategory[] AudioCategories { get; private set; }
        
        private float _volume = 1f;
        public float MasterVolume
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return 0f;
                }

                return _volume;
            }
            set
            {
                if (_bUnityAudioDisabled || Mathf.Approximately(_volume, value))
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
                if (_bUnityAudioDisabled)
                {
                    return false;
                }

                return _isMuted;
            }
            set
            {
                if (_bUnityAudioDisabled || _isMuted == value)
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
        
        public float GetTrackVolume(AudioTrack track)
        {
            if (_bUnityAudioDisabled) return 0f;
            return _configCache.TryGetValue(track, out var config) ? config.Volume : 1f;
        }
        
        public void SetTrackVolume(AudioTrack track, float volume)
        {
            if (_bUnityAudioDisabled) return;
            if (_configCache.TryGetValue(track, out var config))
                config.Volume = volume;
        }
        
        public bool GetTrackMute(AudioTrack track)
        {
            if (_bUnityAudioDisabled) return false;
            return _configCache.TryGetValue(track, out var config) && config.Mute;
        }
        
        public void SetTrackMute(AudioTrack track, bool mute)
        {
            if (_bUnityAudioDisabled) return;
            if (_configCache.TryGetValue(track, out var config))
                config.Mute = mute;
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
            EventManager.RegisterCallback<AudioTrackEvent>(OnAudioTrackEvent);
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
                _bUnityAudioDisabled = (bool)propertyInfo.GetValue(null);
                if (_bUnityAudioDisabled)
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
            
            AudioCategories = new AudioCategory[_audioGroupConfigs.Length];
            _categoryCache.Clear();
            _configCache.Clear();
            for (int i = 0; i < _audioGroupConfigs.Length; i++)
            {
                AudioCategories[i] = new AudioCategory(_audioGroupConfigs[i]);
                _categoryCache[_audioGroupConfigs[i].AudioTrack] = AudioCategories[i];
                _configCache[_audioGroupConfigs[i].AudioTrack] = _audioGroupConfigs[i];
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
            int count = _audioFades.Count;
            int writeIndex = 0;
            
            for (int i = 0; i < count; i++)
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
            
            // 移除已完成的项
            if (writeIndex < count)
            {
                _audioFades.RemoveRange(writeIndex, count - writeIndex);
            }
        }
        
        public override void Shutdown()
        {
            if (!Application.isPlaying) return;

            TweenUtility.StopAll(this);
            StopAll(fadeoutDuration: 0f);
            CleanAudioPool();
            _audioFades.Clear();
            _agentById.Clear();
            _userHandleMap.Clear();
            _handleToUserMap.Clear();
            _sharedAgentBuffer.Clear();
            
            // Unregister Events
            EventManager.UnregisterCallback<AudioPlayEvent>(OnAudioPlayEvent);
            EventManager.UnregisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            EventManager.UnregisterCallback<AudioTrackEvent>(OnAudioTrackEvent);
            EventManager.UnregisterCallback<AudioControlEvent>(OnAudioControlEvent);
            EventManager.UnregisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
            EventManager.UnregisterCallback<AudioFadeEvent>(OnAudioFadeEvent);
            EventManager.UnregisterCallback<AllAudiosControlEvent>(OnAllAudiosControlEvent);
            
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        public void Restart()
        {
            if (_bUnityAudioDisabled) return;

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
            if (_bUnityAudioDisabled) return 0;

            AudioCategory category = FindCategory(options.AudioTrack);
            if (category == null)
            {
                Log.Error($"{options.AudioTrack} is not found in AudioCategories.");
                return 0;
            }
            AudioAgent audioAgent = category.GetAvailableAgent(options.DoNotAutoRecycleIfNotDonePlaying);
            if (audioAgent != null)
            {
                ulong handle = _nextAudioId++;
                audioAgent.Play(clip, options);
                _agentById[handle] = audioAgent;
                // 如果用户指定了 ID，建立双向映射
                if (options.ID != 0)
                {
                    _userHandleMap[options.ID] = handle;
                    _handleToUserMap[handle] = options.ID;
                }
                return handle;
            }
            
            return 0;
        }

        [Obsolete("使用 AudioPlayEvent.Trigger() 或 Play(AudioClip, AudioPlayOptions) 代替")]
        public ulong Play(AudioClip clip, AudioTrack track, Vector3 location,
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
                Initialized = true,
                            
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
            if (_bUnityAudioDisabled) return 0UL;
            
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
                _agentById[handle] = audioAgent;
                // 如果用户指定了 ID，建立双向映射
                if (options.ID != 0)
                {
                    _userHandleMap[options.ID] = handle;
                    _handleToUserMap[handle] = options.ID;
                }
                return handle;
            }
            
            return 0UL;
        }

        [Obsolete("使用 AudioPlayEvent.Trigger() 或 Play(string, AudioPlayOptions) 代替")]
        public ulong Play(string path, AudioTrack track, Vector3 location, bool bAsync = false, bool bInPool = false,
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
                Initialized = true,
                
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
            if (_bUnityAudioDisabled) return;
            if (_agentById.TryGetValue(handle, out var agent) && agent.IsPlaying)
                agent.Pause();
        }

        public void UnPause(ulong handle)
        {
            if (_bUnityAudioDisabled) return;
            if (_agentById.TryGetValue(handle, out var agent) && agent.IsPaused)
                agent.UnPause();
        }
        
        public void Stop(ulong handle, float fadeoutDuration = 0f)
        {
            if (_bUnityAudioDisabled) return;
            if (_agentById.TryGetValue(handle, out var agent) && (agent.IsPlaying || agent.IsPaused))
                agent.Stop(fadeoutDuration);
        }
        
        #endregion 音频控制 [AUDIO CONTROLS]
        
        #region 获取 [FIND]

        /// <summary>
        /// 查找指定音轨的 AudioCategory。
        /// </summary>
        private AudioCategory FindCategory(AudioTrack track)
        {
            return _categoryCache.TryGetValue(track, out var category) ? category : null;
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
            return _agentById.TryGetValue(handle, out var agent) ? agent : null;
        }
        
        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        public bool IsPlaying(ulong handle)
        {
            if (!_agentById.TryGetValue(handle, out var agent)) return false;
            return agent.IsPlaying;
        }
        
        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        public bool IsStopped(ulong handle)
        {
            if (!_agentById.TryGetValue(handle, out var agent)) return true;
            return agent.IsFree;
        }
        
        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        public void ReleaseHandle(ulong handle)
        {
            // O(1) 反向查找清理用户 ID 映射
            if (_handleToUserMap.TryGetValue(handle, out int userId))
            {
                _userHandleMap.Remove(userId);
                _handleToUserMap.Remove(handle);
            }
            
            _agentById.Remove(handle);
        }

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        public void Pause(AudioTrack track)
        {
            if (_bUnityAudioDisabled) return;

            _pausedTracks[track] = true;
            AudioCategory category = FindCategory(track);
            category?.PauseAll();
        }
        
        public void UnPause(AudioTrack track)
        {
            if (_bUnityAudioDisabled) return;

            _pausedTracks[track] = false;
            AudioCategory category = FindCategory(track);
            category?.UnPauseAll();
        }

        public bool IsPaused(AudioTrack track)
        {
            if (_pausedTracks.TryGetValue(track, out bool muted))
            {
                return muted;
            }

            return false;
        }

        public void Stop(AudioTrack track, float fadeoutDuration = 0f)
        {
            if (_bUnityAudioDisabled) return;

            AudioCategory category = FindCategory(track);
            category?.StopAll();
        }
        
        #endregion 音轨控制 [TRACK CONTROLS]
        
        #region 所有音频控制 [ALL AUDIO CONTROLS]

        public void PauseAll()
        {
            if (_bUnityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.PauseAll();
            }
        }

        public void UnPauseAll()
        {
            if (_bUnityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.UnPauseAll();
            }
        }

        public void StopAll(float fadeoutDuration = 0f)
        {
            if (_bUnityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAll();
            }
        }

        public void StopAllButPersistent(float fadeoutDuration = 0f)
        {
            if (_bUnityAudioDisabled) return;
            
            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAllButPersistent();
            }
        }
        
        public void StopAllLooping(float fadeoutDuration = 0f)
        {
            if (_bUnityAudioDisabled) return;
            
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
            (m, v) => m.SetTrackVolume(AudioTrack.Sfx, v),
            (m, v) => m.SetTrackVolume(AudioTrack.UI, v),
            (m, v) => m.SetTrackVolume(AudioTrack.Music, v),
            (m, v) => m.SetTrackVolume(AudioTrack.Voice, v),
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

        public void FadeTrack(AudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default)
        {
            if (duration <= 0f) { SetTrackVolume(track, finalVolume); return; }

            StopFadeTrack(track);
            _trackFadeTweenIds[track] = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                s_TrackFadeCallbacks[(int)track], tweenEase, useUnscaledTime: true);
        }

        public void StopFadeTrack(AudioTrack track)
        {
            if (_trackFadeTweenIds.TryGetValue(track, out long id))
            {
                TweenUtility.Stop(id);
                _trackFadeTweenIds.Remove(track);
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
            _audioFades.Add(new AudioFadeState
            {
                Handle = handle,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
            });
        }

        public void StopFadeAudio(ulong handle)
        {
            if (handle == 0) return;

            for (int i = _audioFades.Count - 1; i >= 0; i--)
            {
                if (_audioFades[i].Handle == handle)
                {
                    _audioFades.RemoveAt(i);
                }
            }
        }

        public bool SoundIsFadingOut(ulong handle)
        {
            for (int i = 0; i < _audioFades.Count; i++)
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
            if (_bUnityAudioDisabled) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (!_assetHandlePool.ContainsKey(path))
                {
                    AssetHandle assetData = _resourceModule.LoadAssetAsyncHandle<AudioClip>(path);
                    _assetHandlePool.Add(path, assetData);
                }
            }
        }

        public void RemoveClipFromPool(List<string> list)
        {
            if (_bUnityAudioDisabled) return;

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
            if (_bUnityAudioDisabled) return;

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
        private void OnAudioTrackEvent(AudioTrackEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackEvent.EAudioTrackEventType.MuteTrack:
                        MasterMute = true;
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.UnmuteTrack:
                        MasterMute = false;
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.SetTrackVolume:
                        MasterVolume = evt.Volume;
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.PauseTrack:
                        PauseAll();
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.UnPauseTrack:
                        UnPauseAll();
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.StopTrack:
                        StopAll();
                        break;
                }
            }
            else
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackEvent.EAudioTrackEventType.MuteTrack:
                        SetTrackMute(evt.Track, true);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.UnmuteTrack:
                        SetTrackMute(evt.Track, false);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.SetTrackVolume:
                        SetTrackVolume(evt.Track, evt.Volume);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.PauseTrack:
                        Pause(evt.Track);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.UnPauseTrack:
                        UnPause(evt.Track);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.StopTrack:
                        Stop(evt.Track);
                        break;
                }
            }
        }
        
        /// <summary>
        /// 音频控制事件（通过用户 ID O(1) 查找句柄后操作）
        /// </summary>
        /// <param name="evt"></param>
        private void OnAudioControlEvent(AudioControlEvent evt)
        {
            if (!_userHandleMap.TryGetValue(evt.AudioID, out ulong handle)) return;
            
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
        /// 音频过渡事件（通过用户 ID 查找句柄后操作）
        /// </summary>
        /// <param name="evt"></param>
        private void OnAudioFadeEvent(AudioFadeEvent evt)
        {
            // 通过用户 ID O(1) 查找对应的句柄
            if (!_userHandleMap.TryGetValue(evt.SoundID, out ulong handle)) return;
            
            var agent = GetAgentByHandle(handle);
            if (agent == null) return;
            
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
