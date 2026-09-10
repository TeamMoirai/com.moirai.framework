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
    /// <para>句柄生命周期：Play 绑定 → 结束/抢占时 <see cref="OnAgentPlaybackEnded"/> 自动释放，杜绝无界增长与旧句柄别名。</para>
    /// <para>FadeAudio 使用手动紧凑列表 + <see cref="TweenEase"/>，零 GC 且曲线语义正确。</para>
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
        // 用户定义 ID -> 服务句柄列表 映射（1 对多）
        [NonSerialized] private readonly Dictionary<int, List<ulong>> _userHandleMap = new Dictionary<int, List<ulong>>();
        // 服务自维护 ID 生成器
        [NonSerialized] private ulong _nextAudioId = 1UL;
        // List<ulong> 对象池
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

        // ===== FadeAudio 手动过渡 — 零 GC，支持 TweenEase =====
        private struct AudioFadeState
        {
            public ulong Handle;
            public float StartTime;
            public float Duration;
            public float StartVolume;
            public float EndVolume;
            public TweenEase Ease;
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
        /// 应用主音轨（总音量）音量。
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
            AudioMixService.Initialize(_audioMixer);

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

            foreach (var handles in _userHandleMap.Values)
            {
                ReleaseHandleList(handles);
            }
            _userHandleMap.Clear();

            AudioAgentHostPool.Clear();
            AudioPlayColdParamsPool.Clear();

            SceneManager.sceneLoaded -= OnSceneLoaded;

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
            var categories = _audioCategories;
            if (categories == null) return;

            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.Update(elapseSeconds);
            }

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

                var agent = GetAgentByHandle(fade.Handle);
                if (agent == null || agent.AudioResource == null)
                {
                    // 句柄已释放 → 丢弃该过渡
                    continue;
                }

                if (elapsed >= fade.Duration)
                {
                    agent.AudioResource.volume = fade.EndVolume;
                }
                else
                {
                    float t = fade.Duration > 0f ? elapsed / fade.Duration : 1f;
                    float eased = fade.Ease.Evaluate(t);
                    agent.AudioResource.volume = fade.StartVolume + eased * (fade.EndVolume - fade.StartVolume);
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

            _handleToAgent.Clear();
            foreach (var handles in _userHandleMap.Values)
            {
                ReleaseHandleList(handles);
            }
            _userHandleMap.Clear();
            _audioFades.Clear();
            _audioFadeCount = 0;

            Initialize(null, null, _audioGroupConfigs);
        }

        /// <summary>
        /// 初始化音频服务。
        /// </summary>
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
                if (propertyInfo != null)
                {
                    _unityAudioDisabled = (bool)propertyInfo.GetValue(null);
                    if (_unityAudioDisabled)
                    {
                        LogUtility.Warning("[AudioService] Unity Audio Disabled!");
                        return;
                    }
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

            if (_audioGroupConfigs == null)
            {
                LogUtility.Error("[AudioService] AudioGroupConfigs is null.");
                _audioCategories = Array.Empty<AudioCategory>();
                _categoryCache = Array.Empty<AudioCategory>();
                _configCache = Array.Empty<AudioGroupConfig>();
                return;
            }

            int trackCount = _audioGroupConfigs.Length;
            _trackFadeTweenIds = new long[trackCount];
            _pausedTracks = new bool[trackCount];
            _audioCategories = new AudioCategory[trackCount];

            // 以 EAudioTrack 枚举上界建缓存，保证 (int)track 索引安全
            int enumCount = Enum.GetValues(typeof(EAudioTrack)).Length;
            _categoryCache = new AudioCategory[enumCount];
            _configCache = new AudioGroupConfig[enumCount];

            for (int i = 0; i < trackCount; i++)
            {
                _audioCategories[i] = new AudioCategory(this, _audioGroupConfigs[i]);
                int trackIndex = (int)_audioGroupConfigs[i].AudioTrack;
                if (trackIndex >= 0 && trackIndex < enumCount)
                {
                    _categoryCache[trackIndex] = _audioCategories[i];
                    _configCache[trackIndex] = _audioGroupConfigs[i];
                }
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
                LogUtility.Error("{0} is not found in AudioCategories.", options.AudioTrack);
                return 0UL;
            }

            AudioAgent audioAgent = category.GetAvailableAgent(
                options.DoNotAutoRecycleIfNotDonePlaying, options.Priority, options.Persistent);
            if (audioAgent == null)
            {
                return 0UL;
            }

            // 换播前释放旧句柄，防止别名
            ReleaseAgentHandle(audioAgent);

            ulong handle = _nextAudioId++;
            if (handle == 0UL) handle = _nextAudioId++;

            // 先登记再播放：立即失败时 EnterEndState → OnAgentPlaybackEnded 能命中字典
            audioAgent.BindHandle(handle);
            _handleToAgent[handle] = audioAgent;
            RegisterUserHandle(options.ID, handle);

            var request = options.ToRequest();
            var cold = AudioPlayColdParams.FromOptions(options);
            audioAgent.PlayWithRequest(clip, request, cold);

            if (audioAgent.IsFree && audioAgent.CurrentHandle == 0UL)
            {
                return 0UL;
            }

            return handle;
        }

        /// <inheritdoc />
        public override ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold)
        {
            if (_unityAudioDisabled) return 0UL;

            AudioCategory category = FindCategory(request.Track);
            if (category == null)
            {
                LogUtility.Error("{0} is not found in AudioCategories.", request.Track);
                AudioPlayColdParamsPool.Release(cold);
                return 0UL;
            }

            AudioAgent audioAgent = category.GetAvailableAgent(
                request.DoNotAutoRecycleIfNotDonePlaying, request.Priority, request.Persistent);
            if (audioAgent == null)
            {
                AudioPlayColdParamsPool.Release(cold);
                return 0UL;
            }

            ReleaseAgentHandle(audioAgent);

            ulong handle = _nextAudioId++;
            if (handle == 0UL) handle = _nextAudioId++;

            audioAgent.BindHandle(handle);
            _handleToAgent[handle] = audioAgent;
            RegisterUserHandle(request.Id, handle);
            audioAgent.PlayWithRequest(clip, request, cold);

            if (audioAgent.IsFree && audioAgent.CurrentHandle == 0UL)
            {
                return 0UL;
            }

            return handle;
        }

        /// <inheritdoc />
        public override ulong Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false)
        {
            if (_unityAudioDisabled) return 0UL;

            AudioCategory category = FindCategory(options.AudioTrack);
            if (category == null)
            {
                LogUtility.Error("{0} is not found in AudioCategories.", options.AudioTrack);
                return 0UL;
            }

            AudioAgent audioAgent = category.GetAvailableAgent(
                options.DoNotAutoRecycleIfNotDonePlaying, options.Priority, options.Persistent);
            if (audioAgent == null)
            {
                return 0UL;
            }

            ReleaseAgentHandle(audioAgent);

            ulong handle = _nextAudioId++;
            if (handle == 0UL) handle = _nextAudioId++;

            audioAgent.BindHandle(handle);
            _handleToAgent[handle] = audioAgent;
            RegisterUserHandle(options.ID, handle);
            audioAgent.LoadWithOptions(path, options, bAsync, bInPool);

            // 同步加载失败会立刻 End 并自动释放
            if (audioAgent.IsFree && audioAgent.CurrentHandle == 0UL)
            {
                return 0UL;
            }

            return handle;
        }

        private void RegisterUserHandle(int userId, ulong handle)
        {
            if (!_userHandleMap.TryGetValue(userId, out var handles))
            {
                handles = AcquireHandleList();
                _userHandleMap[userId] = handles;
            }
            handles.Add(handle);
        }

        /// <summary>
        /// 释放 Agent 上仍绑定的旧句柄（换播/抢占前调用）。
        /// </summary>
        private void ReleaseAgentHandle(AudioAgent agent)
        {
            ulong oldHandle = agent.CurrentHandle;
            if (oldHandle != 0UL)
            {
                ReleaseHandle(oldHandle);
            }
        }

        /// <summary>
        /// Agent 进入 End 时回调——自动释放句柄，防止字典无界增长与旧句柄误用。
        /// </summary>
        internal override void OnAgentPlaybackEnded(AudioAgent agent)
        {
            if (agent == null) return;

            ulong handle = agent.CurrentHandle;
            if (handle != 0UL)
            {
                ReleaseHandle(handle);
            }
        }

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <inheritdoc />
        public override void Pause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (TryGetBoundAgent(handle, out var agent) && agent.IsPlaying)
                agent.Pause();
        }

        /// <inheritdoc />
        public override void Unpause(ulong handle)
        {
            if (_unityAudioDisabled) return;
            if (TryGetBoundAgent(handle, out var agent) && agent.IsPaused)
                agent.Unpause();
        }

        /// <inheritdoc />
        public override void Stop(ulong handle, float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            if (!TryGetBoundAgent(handle, out var agent))
            {
                ReleaseHandle(handle);
                return;
            }

            // 已 Free：直接清句柄，避免字典残留
            if (agent.IsFree)
            {
                ReleaseHandle(handle);
                return;
            }

            agent.Stop(fadeoutDuration);
        }

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <inheritdoc />
        public override void ForEachAgentByID(int id, Action<AudioAgent> action)
        {
            var categories = _audioCategories;
            if (categories == null || action == null) return;

            for (int i = 0; i < categories.Length; i++)
            {
                var agents = categories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    var agent = agents[j];
                    if (agent != null && agent.ID == id)
                        action(agent);
                }
            }
        }

        /// <inheritdoc />
        public override void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action)
        {
            if (clip == null || action == null) return;

            var categories = _audioCategories;
            if (categories == null) return;

            for (int i = 0; i < categories.Length; i++)
            {
                var agents = categories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    var agent = agents[j];
                    if (agent?.AudioResource != null && agent.AudioResource.clip == clip)
                        action(agent);
                }
            }
        }

        /// <inheritdoc />
        public override void ForEachHandleByID(int id, Action<ulong> action)
        {
            if (action == null) return;
            if (!_userHandleMap.TryGetValue(id, out var handles)) return;

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
            var categories = _audioCategories;
            if (categories == null) return 0;

            for (int i = 0; i < categories.Length; i++)
            {
                var agents = categories[i]?.AudioAgents;
                if (agents == null) continue;

                for (int j = 0; j < agents.Count; j++)
                {
                    var agent = agents[j];
                    if (agent?.AudioResource != null &&
                        agent.AudioResource.clip == clip &&
                        agent.AudioResource.isPlaying)
                        count++;
                }
            }
            return count;
        }

        /// <inheritdoc />
        public override AudioAgent GetAgentByHandle(ulong handle)
        {
            if (!_handleToAgent.TryGetValue(handle, out var agent)) return null;
            // 世代校验：句柄必须仍是 Agent 当前绑定值
            return agent != null && agent.CurrentHandle == handle ? agent : null;
        }

        private bool TryGetBoundAgent(ulong handle, out AudioAgent agent)
        {
            agent = GetAgentByHandle(handle);
            return agent != null;
        }

        /// <inheritdoc />
        public override bool IsPlaying(ulong handle)
        {
            var agent = GetAgentByHandle(handle);
            return agent != null && agent.IsPlaying;
        }

        /// <inheritdoc />
        public override bool IsStopped(ulong handle)
        {
            var agent = GetAgentByHandle(handle);
            return agent == null || agent.IsFree;
        }

        /// <inheritdoc />
        public override void ReleaseHandle(ulong handle)
        {
            if (handle == 0UL) return;

            if (_handleToAgent.TryGetValue(handle, out var agent))
            {
                int userId = agent != null ? agent.ID : 0;

                if (_userHandleMap.TryGetValue(userId, out var handles))
                {
                    handles.Remove(handle);
                    if (handles.Count == 0)
                    {
                        _userHandleMap.Remove(userId);
                        ReleaseHandleList(handles);
                    }
                }

                if (agent != null && agent.CurrentHandle == handle)
                {
                    agent.UnbindHandle();
                }

                _handleToAgent.Remove(handle);
            }

            StopFadeAudio(handle);
        }

        /// <summary>
        /// 查找指定音轨的 AudioCategory。
        /// </summary>
        private AudioCategory FindCategory(EAudioTrack track)
        {
            if (_categoryCache == null) return null;
            int index = (int)track;
            return index >= 0 && index < _categoryCache.Length ? _categoryCache[index] : null;
        }

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <inheritdoc />
        public override void PauseTrack(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            if (_pausedTracks != null)
            {
                int index = (int)track;
                if (index >= 0 && index < _pausedTracks.Length)
                    _pausedTracks[index] = true;
            }

            FindCategory(track)?.PauseAll();
        }

        /// <inheritdoc />
        public override void UnpauseTrack(EAudioTrack track)
        {
            if (_unityAudioDisabled) return;

            if (_pausedTracks != null)
            {
                int index = (int)track;
                if (index >= 0 && index < _pausedTracks.Length)
                    _pausedTracks[index] = false;
            }

            FindCategory(track)?.UnpauseAll();
        }

        /// <inheritdoc />
        public override bool IsPaused(EAudioTrack track)
        {
            if (_pausedTracks == null) return false;
            int index = (int)track;
            return index >= 0 && index < _pausedTracks.Length && _pausedTracks[index];
        }

        /// <inheritdoc />
        public override void StopTrack(EAudioTrack track, float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            FindCategory(track)?.StopAll(fadeoutDuration);
        }

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <inheritdoc />
        public override void PauseAll()
        {
            if (_unityAudioDisabled) return;

            var categories = _audioCategories;
            if (categories == null) return;
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.PauseAll();
            }
        }

        /// <inheritdoc />
        public override void UnpauseAll()
        {
            if (_unityAudioDisabled) return;

            var categories = _audioCategories;
            if (categories == null) return;
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.UnpauseAll();
            }
        }

        /// <inheritdoc />
        public override void StopAll(float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            var categories = _audioCategories;
            if (categories == null) return;
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.StopAll(fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopAllButPersistent(float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            var categories = _audioCategories;
            if (categories == null) return;
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.StopAllButPersistent(fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopAllLooping(float fadeoutDuration = 0)
        {
            if (_unityAudioDisabled) return;

            var categories = _audioCategories;
            if (categories == null) return;
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.StopAllLooping(fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopByID(int id, float fadeoutDuration = 0f)
        {
            if (_unityAudioDisabled) return;
            // 冷路径：分层替换/UI，lambda 可读性优先
            ForEachHandleByID(id, handle => Stop(handle, fadeoutDuration));
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
            int index = (int)track;
            if (_trackFadeTweenIds == null || index < 0 || index >= _trackFadeTweenIds.Length) return;

            _trackFadeTweenIds[index] = TweenUtility.Custom(this, initialVolume, finalVolume, duration,
                TrackFadeCallbacks[index], tweenEase, useUnscaledTime: true);
        }

        /// <inheritdoc />
        public override void StopFadeTrack(EAudioTrack track)
        {
            if (_trackFadeTweenIds == null) return;
            int index = (int)track;
            if (index < 0 || index >= _trackFadeTweenIds.Length) return;

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
                Ease = tweenEase,
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

        /// <inheritdoc />
        public override void PlayFadeByID(int id, float duration, float finalVolume, TweenEase ease)
        {
            // 冷路径：UI 淡入淡出
            ForEachHandleByID(id, handle =>
            {
                var agent = GetAgentByHandle(handle);
                if (agent == null || agent.AudioResource == null) return;
                agent.CancelFadeIn();
                FadeAudio(handle, duration, agent.AudioResource.volume, finalVolume, ease);
            });
        }

        /// <inheritdoc />
        public override void StopFadeByID(int id)
        {
            ForEachHandleByID(id, StopFadeAudio);
        }

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <inheritdoc />
        public override void PutInAudioPool(List<string> list)
        {
            if (_unityAudioDisabled || list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (!string.IsNullOrEmpty(path) && !AssetHandlePool.ContainsKey(path))
                {
                    var lease = ResourceService.LoadLease<AudioClip>(path);
                    AssetHandlePool.Add(path, lease);
                }
            }
        }

        /// <inheritdoc />
        public override void RemoveClipFromPool(List<string> list)
        {
            if (_unityAudioDisabled || list == null) return;

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
        /// 释放除了持久性的音频之外的所有音频。
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode loadSceneMode)
        {
            StopAllButPersistent(fadeoutDuration: AudioAgent.FADEOUT_DEFAULT_DURATION);
        }

        #endregion
    }
}
