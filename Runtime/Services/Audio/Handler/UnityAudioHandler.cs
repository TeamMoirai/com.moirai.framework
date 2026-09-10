using System;
using System.Collections.Generic;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
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
        // List<ulong> 对象池，避免频繁分配
        private readonly Stack<List<ulong>> _handleListPool = new Stack<List<ulong>>(4);

        private List<ulong> AcquireHandleList()
        {
            return _handleListPool.Count > 0 ? _handleListPool.Pop() : new List<ulong>(2);
        }

        private void ReleaseHandleList(List<ulong> list)
        {
            list.Clear();
            _handleListPool.Push(list);
        }

        [NonSerialized] private AudioMixer _audioMixer;
        /// <inheritdoc />
        public override AudioMixer AudioMixer => _audioMixer;

        [NonSerialized] private Transform _instanceRoot;
        /// <inheritdoc />
        public override Transform InstanceRoot { get => _instanceRoot; set => _instanceRoot = value; }

        /// <inheritdoc />
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
        /// <inheritdoc />
        public override AudioCategory[] AudioCategories => _audioCategories;

        [NonSerialized] private float _volume = 1f;
        /// <inheritdoc />
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
        /// <inheritdoc />
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

        /// <inheritdoc />
        public override void SetMasterSettings()
        {
            SettingUtility.SetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, _volume);
            SettingUtility.SetBool(GameConstant.Setting.AUDIO_MASTER_MUTED, _isMuted);
        }

        /// <inheritdoc />
        public override void LoadMasterSettings()
        {
            _isMuted = SettingUtility.GetBool(GameConstant.Setting.AUDIO_MASTER_MUTED, false);
            _volume = SettingUtility.GetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, 1f);

            ApplyMasterVolume();
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public override float GetTrackVolume(EAudioTrack track)
        {
            if (_unityAudioDisabled) return 0f;
            int index = (int)track;
            return index >= 0 && index < _configCache.Length ? _configCache[index].Volume : 1f;
        }

        /// <inheritdoc />
        public override void SetTrackVolume(EAudioTrack track, float volume)
        {
            if (_unityAudioDisabled) return;
            int index = (int)track;
            if (index >= 0 && index < _configCache.Length)
                _configCache[index].Volume = volume;
        }

        /// <inheritdoc />
        public override bool GetTrackMute(EAudioTrack track)
        {
            if (_unityAudioDisabled) return false;
            int index = (int)track;
            return index >= 0 && index < _configCache.Length && _configCache[index].Mute;
        }

        /// <inheritdoc />
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

            SceneManager.sceneLoaded += OnSceneLoaded;
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <summary>
        /// 初始化音频服务。
        /// </summary>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <exception cref="GameException"></exception>
        private void Initialize(Transform instanceRoot = null, AudioMixer audioMixer = null, AudioGroupConfig[] audioGroupConfigs = null)
        {
            _instanceRoot = instanceRoot;
            if (_instanceRoot == null)
            {
                _instanceRoot = new GameObject("[AudioService]").transform;
                _instanceRoot.localScale = Vector3.one;
                UnityEngine.Object.DontDestroyOnLoad(_instanceRoot);
            }

#if UNITY_EDITOR
            _instanceRoot.gameObject.GetOrAddComponent<AudioDebugger>();

            try
            {
                TypeInfo typeInfo = typeof(UnityEngine.AudioSettings).GetTypeInfo();
                PropertyInfo propertyInfo = typeInfo.GetDeclaredProperty("unityAudioDisabled");
                _unityAudioDisabled = (bool)propertyInfo.GetValue(null);
                if (_unityAudioDisabled)
                {
                    LogUtility.Warning("[AudioService] Unity Audio Disabled!");
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

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <inheritdoc />
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

        /// <inheritdoc />
        public override ulong Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false)
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

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <inheritdoc />
        public override void Pause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && agent.IsPlaying)
                agent.Pause();
        }

        /// <inheritdoc />
        public override void Unpause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && agent.IsPaused)
                agent.Unpause();
        }

        /// <inheritdoc />
        public override void Stop(ulong handle, float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;
            if (_handleToAgent.TryGetValue(handle, out var agent) && (agent.IsPlaying || agent.IsPaused))
                agent.Stop(fadeoutDuration);
        }

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public override void ForEachHandleByID(int id, Action<ulong> action)
        {
            // 通过用户 ID 查找对应的所有句柄
            if (!_userHandleMap.TryGetValue(id, out var handles)) return;

            // 缓存 count：StopFadeAudio 不修改 handles，安全迭代。
            int count = handles.Count;
            for (int i = 0; i < count; i++)
            {
                action(handles[i]);
            }
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public override AudioAgent GetAgentByHandle(ulong handle) => _handleToAgent.GetValueOrDefault(handle);

        /// <inheritdoc />
        public override bool IsPlaying(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return false;
            return agent.IsPlaying;
        }

        /// <inheritdoc />
        public override bool IsStopped(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return true;
            return agent.IsFree;
        }

        /// <inheritdoc />
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

        /// <summary>
        /// 查找指定音轨的 AudioCategory。
        /// </summary>
        private AudioCategory FindCategory(EAudioTrack track)
        {
            int index = (int)track;
            return index >= 0 && index < _categoryCache.Length ? _categoryCache[index] : null;
        }

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <inheritdoc />
        public override void PauseTrack(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            _pausedTracks[(int)track] = true;
            AudioCategory category = FindCategory(track);
            category?.PauseAll();
        }

        /// <inheritdoc />
        public override void UnpauseTrack(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            _pausedTracks[(int)track] = false;
            AudioCategory category = FindCategory(track);
            category?.UnpauseAll();
        }

        /// <inheritdoc />
        public override bool IsPaused(EAudioTrack track)
        {
            return _pausedTracks[(int)track];
        }

        /// <inheritdoc />
        public override void StopTrack(EAudioTrack track, float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            AudioCategory category = FindCategory(track);
            category?.StopAll(fadeoutDuration);
        }

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <inheritdoc />
        public override void PauseAll()
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.PauseAll();
            }
        }

        /// <inheritdoc />
        public override void UnpauseAll()
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.UnpauseAll();
            }
        }

        /// <inheritdoc />
        public override void StopAll(float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAll(fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopAllButPersistent(float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAllButPersistent(fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopAllLooping(float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            for (int i = 0; i < AudioCategories.Length; i++)
            {
                AudioCategories[i]?.StopAllLooping(fadeoutDuration);
            }
        }

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <inheritdoc />
        public override void FadeMasterTrack(float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (duration <= 0f) { MasterVolume = finalVolume; return; }

            TweenUtility.Stop(_masterFadeTweenId);
            _masterFadeTweenId = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                OnMasterFadeUpdate, tweenEase, useUnscaledTime: true);
            return;

            // ===== 静态回调 — 零 GC =====
            static void OnMasterFadeUpdate(AudioServiceHandler service, float value)
            {
                service.MasterVolume = value;
            }
        }

        /// <inheritdoc />
        public override void StopFadeMasterTrack()
        {
            TweenUtility.Stop(_masterFadeTweenId);
        }

        /// <inheritdoc />
        public override void FadeTrack(EAudioTrack track, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (duration <= 0f) { SetTrackVolume(track, finalVolume); return; }

            StopFadeTrack(track);
            _trackFadeTweenIds[(int)track] = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                TrackFadeCallbacks[(int)track], tweenEase, useUnscaledTime: true);
        }

        /// <inheritdoc />
        public override void StopFadeTrack(EAudioTrack track)
        {
            int index = (int)track;
            if (_trackFadeTweenIds[index] != 0)
            {
                TweenUtility.Stop(_trackFadeTweenIds[index]);
                _trackFadeTweenIds[index] = 0;
            }
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
        private void ReleaseHandleObject(object handleObj)
        {
            if (handleObj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        #endregion 资源池 [ASSET POOL]

        #region 事件 [EVENTS]

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
