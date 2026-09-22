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
    /// <para>句柄注册与音量过渡复用 <see cref="AudioHandleRegistry{TVoice}"/> / <see cref="AudioFadeScheduler"/>，
    /// 与 <see cref="Middleware.MiddlewareAudioHandler"/> 共享同一套语义。</para>
    /// </summary>
    [Serializable]
    internal sealed class UnityAudioHandler : AudioServiceHandler, IAudioFadeTarget
    {
        [NonSerialized] private AudioGroupConfig[] _audioGroupConfigs;
        [NonSerialized] private bool _unityAudioDisabled;

        // 音轨暂停状态（数组索引 = (int)EAudioTrack）
        [NonSerialized] private bool[] _pausedTracks;
        // 音轨 -> Category 缓存，O(1) 数组直接访问
        [NonSerialized] private AudioCategory[] _categoryCache;
        // 音轨 -> AudioGroupConfig 缓存，O(1) 数组直接访问
        [NonSerialized] private AudioGroupConfig[] _configCache;
        // 服务句柄注册表（句柄生成、句柄→Agent、用户 ID 映射、列表池）
        [NonSerialized] private readonly AudioHandleRegistry<AudioAgent> _handles = new AudioHandleRegistry<AudioAgent>();
        // Clip 缓存（Lease + LRU + TTL + Pin + lowMemory）——路径播放单一真相源
        [NonSerialized] private readonly AudioClipCache _clipCache = new AudioClipCache();

        [NonSerialized] private AudioMixer _audioMixer;
        /// <inheritdoc />
        public override AudioMixer AudioMixer => _audioMixer;

        [NonSerialized] private Transform _instanceRoot;
        // 是否由本后端把 AudioListener 挂起：关停时必须解冻，否则带着后台状态退出会留下全局静音
        [NonSerialized] private bool _pausedByFramework;
        /// <inheritdoc />
        public override Transform InstanceRoot { get => _instanceRoot; set => _instanceRoot = value; }

        /// <summary>Clip 缓存（Agent 路径加载经此取租约）。</summary>
        internal AudioClipCache ClipCache => _clipCache;

        /// <inheritdoc />
        /// <remarks>Clip 缓存的只读投影；租约由缓存持有，外部无法经此视图改写记账或释放租约。</remarks>
        public override IReadOnlyDictionary<string, object> AssetHandlePool => _clipCache.PoolReadOnly;

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
                if (_unityAudioDisabled)
                {
                    return;
                }

                // 值域统一在线性 0..1（与中间件后端同一条线）：夹取发生在这里而不是等到写 AudioListener 时，
                // getter 才因此始终能报回"实际生效的那个值"
                float volume = Mathf.Clamp01(value);
                if (Mathf.Approximately(_volume, volume))
                {
                    return;
                }

                _volume = volume;
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
        /// 应用主音轨（总音量）音量。<see cref="MasterVolume"/> 的 setter 已保证 0..1，这里不再二次夹取。
        /// </summary>
        private void ApplyMasterVolume()
        {
            AudioListener.volume = _isMuted ? 0f : _volume;
        }

        /// <inheritdoc />
        public override float GetTrackVolume(EAudioTrack track)
        {
            if (_unityAudioDisabled) return 0f;
            var config = FindConfig(track);
            return config != null ? config.Volume : 1f;
        }

        /// <inheritdoc />
        public override void SetTrackVolume(EAudioTrack track, float volume)
        {
            if (_unityAudioDisabled) return;
            var config = FindConfig(track);
            if (config != null) config.Volume = volume;
        }

        /// <inheritdoc />
        public override bool GetTrackMute(EAudioTrack track)
        {
            if (_unityAudioDisabled) return false;
            return FindConfig(track)?.Mute ?? false;
        }

        /// <inheritdoc />
        public override void SetTrackMute(EAudioTrack track, bool mute)
        {
            if (_unityAudioDisabled) return;
            var config = FindConfig(track);
            if (config != null) config.Mute = mute;
        }

        /// <summary>
        /// 查找音轨配置（未配置的音轨返回 null，而非越界/空引用）。
        /// </summary>
        private AudioGroupConfig FindConfig(EAudioTrack track)
        {
            if (_configCache == null) return null;

            int index = (int)track;
            return index >= 0 && index < _configCache.Length ? _configCache[index] : null;
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
            ApplyMixSnapshotSettings();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 关闭音频处理器。由 <c>Handler</c> 置空/替换时自动调用。
        /// </summary>
        protected override void OnShutdown()
        {
            if (!Application.isPlaying) return;

            StopAll(fadeoutDuration: 0f);
            CleanAudioPool();
            _clipCache.Dispose();
            AudioVoiceDucking.Reset();

            // 在后台被退出/关停时要把解冻补上，否则 AudioListener.pause=true 会留给下一个场景或编辑器会话
            if (_pausedByFramework)
            {
                AudioListener.pause = false;
                _pausedByFramework = false;
            }
            _fades.Clear();
            _handles.Clear();

            AudioAgentHostPool.Clear();
            AudioPlayColdParamsPool.Clear();

            SceneManager.sceneLoaded -= OnSceneLoaded;

            // 销毁 DDOL 实例根整棵树（含 Category 根）；池中宿主已被 Clear 销毁，活动宿主随树销毁
            if (_instanceRoot != null)
            {
                UnityEngine.Object.Destroy(_instanceRoot.gameObject);
            }

            _instanceRoot = null;
            _audioMixer = null;
            _audioGroupConfigs = null;
            _pausedTracks = null;
            _audioCategories = null;
            _categoryCache = null;
            _configCache = null;
        }

        /// <inheritdoc />
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            // 首帧后关闭阻塞加载窗口
            AudioBlockingLoadGate.Close();

            var categories = _audioCategories;
            if (categories == null) return;

            // 音频按真实时间播放（AudioSource 不受 timeScale 影响），自然结束计时必须用 unscaled，
            // 否则 timeScale>1 会提前淡出、timeScale=0（暂停）时播完的 agent 悬挂不释放
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i]?.Update(realElapseSeconds);
            }

            _fades.Update(GameTime.unscaledTime, this);

            // 两个清扫型子系统单独隔离：缓存 TTL 扫描或 ducking 判定抛一次，不该带走在播声部的推进
            try
            {
                _clipCache.Tick();
            }
            catch (Exception e)
            {
                AudioFault.Report($"{nameof(UnityAudioHandler)}.{nameof(Tick)}:clipCache", e);
            }

            try
            {
                AudioVoiceDucking.Evaluate(this);
            }
            catch (Exception e)
            {
                AudioFault.Report($"{nameof(UnityAudioHandler)}.{nameof(Tick)}:ducking", e);
            }
        }

        /// <summary>
        /// 前后台切换：冻结/解冻 <see cref="AudioListener"/>，保留各 <see cref="AudioSource"/> 的播放位置。
        /// </summary>
        /// <remarks>
        /// 不用 <c>StopAllButPersistent</c>：后台回来时 BGM/环境音应当从断点继续，而不是被重起或静音。
        /// <para>淡入淡出按未缩放真实时间推进，因此挂起期间开始的斜坡在恢复时会直接落到目标音量——
        /// 与"真实时间已经过去"一致，不做补帧。</para>
        /// </remarks>
        public override void OnApplicationPaused(bool paused)
        {
            if (_unityAudioDisabled) return;

            AudioListener.pause = paused;
            _pausedByFramework = paused;
        }

        /// <summary>
        /// 音轨上是否有 Agent 未回空闲（含加载中与淡出中——它们马上或仍在出声）。
        /// </summary>
        internal override bool HasActiveAudioOn(EAudioTrack track)
        {
            var agents = FindCategory(track)?.AudioAgents;
            if (agents == null) return false;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && !agent.IsFree) return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Restart()
        {
            // 窗口重开要在 disabled 提前返回之前：停过一轮音频再启用时，启动期的同步加载才不会被误判成运行期而改异步
            AudioBlockingLoadGate.Open();
            if (_unityAudioDisabled) return;

            CleanAudioPool();
            AudioVoiceDucking.Reset();

            foreach (var category in AudioCategories)
            {
                if (category == null) continue;

                foreach (var audioAgent in category.AudioAgents)
                {
                    audioAgent?.Destroy();
                }
            }

            _handles.Clear();
            _fades.Clear();

            // 销毁旧实例根整棵树（含 Category 根），避免 Restart 泄漏空壳 GameObject。
            // 已归还栈池的宿主仍挂在旧树下会一并销毁——池 Acquire 的 null 检查会惰性剔除这些失效引用。
            if (_instanceRoot != null)
            {
                UnityEngine.Object.Destroy(_instanceRoot.gameObject);
                _instanceRoot = null;
            }

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

            // 缺 Mixer 不是致命错误（主音量仍走 AudioListener.volume），但音轨音量与静音
            // 全靠 Mixer 上暴露的 {组名}Volume 参数，缺了就是滑杆拉到底也没声音变化——必须显式报一次
            if (_audioMixer == null)
            {
                AudioWarnOnce.Error("unity.mixer-missing",
                    "[AudioService] 未配置 AudioMixer（AudioServiceSettings.AudioMixer 为空）。" +
                    "音轨音量/静音与混音快照将静默无效，仅主音轨 AudioListener.volume 可用。");
            }

            // Clip 缓存：容量/TTL/默认策略来自 Settings，租约来源经窄接缝转发到资源后端
            _clipCache.Configure(
                new ResourceClipLeaseSource(),
                AudioServiceSettings.ClipCacheCapacity,
                AudioServiceSettings.ClipCacheTtl,
                AudioServiceSettings.DefaultClipCachePolicy);

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
            _audioCategories = new AudioCategory[trackCount];

            // 以 EAudioTrack 枚举上界建缓存/暂停表，保证 (int)track 索引安全（配置数可能少于枚举数）
            int enumCount = Enum.GetValues(typeof(EAudioTrack)).Length;
            _pausedTracks = new bool[enumCount];
            _categoryCache = new AudioCategory[enumCount];
            _configCache = new AudioGroupConfig[enumCount];

            // 压实数组：重复音轨拒绝后不留 null 空槽（AudioCategories 公开暴露，外部可能直接遍历）
            var validCategories = new List<AudioCategory>(trackCount);
            for (int i = 0; i < trackCount; i++)
            {
                var config = _audioGroupConfigs[i];

                // 空配置槽跳过（防御序列化数组留空），避免构造期 NRE
                if (config == null)
                {
                    LogUtility.Warning("[AudioService] AudioGroupConfigs[{0}] is null; the slot is ignored.", i);
                    continue;
                }

                int trackIndex = (int)config.AudioTrack;

                // 重复音轨拒绝：后者会覆盖 O(1) 缓存并使前一个 Category 不可达
                if (trackIndex >= 0 && trackIndex < enumCount && _categoryCache[trackIndex] != null)
                {
                    LogUtility.Warning(
                        "[AudioService] Duplicate AudioTrack {0} in AudioGroupConfigs; the later config is ignored.",
                        config.AudioTrack);
                    continue;
                }

                var category = new AudioCategory(this, config);
                validCategories.Add(category);
                if (trackIndex >= 0 && trackIndex < enumCount)
                {
                    _categoryCache[trackIndex] = category;
                    _configCache[trackIndex] = config;
                }
            }

            _audioCategories = validCategories.ToArray();
        }

        /// <summary>
        /// 应用 <see cref="AudioServiceSettings"/> 中的混音快照映射（状态 → Snapshot + 优先级）。
        /// </summary>
        private static void ApplyMixSnapshotSettings()
        {
            var entries = AudioServiceSettings.MixSnapshots;
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Snapshot == null) continue;
                AudioMixService.RegisterSnapshot(entry.State, entry.Snapshot, entry.Priority);
            }
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <inheritdoc />
        public override ulong Play(AudioClip clip, in AudioPlayOptions options)
        {
            if (_unityAudioDisabled || IsTrackPaused(options.AudioTrack)) return 0UL;

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

            // 先登记再播放：注册表 Bind 单点写入 Agent 侧句柄与槽位；
            // 立即失败时 EnterEndState → OnAgentPlaybackEnded 能命中映射
            ulong handle = _handles.Bind(audioAgent);
            if (handle == 0UL) return 0UL;
            _handles.RegisterUser(handle, options.ID);

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
            if (_unityAudioDisabled || IsTrackPaused(request.Track))
            {
                AudioPlayColdParamsPool.Release(cold);
                return 0UL;
            }

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

            ulong handle = _handles.Bind(audioAgent);
            if (handle == 0UL)
            {
                // 还没交给 Agent，冷参归我们收
                AudioPlayColdParamsPool.Release(cold);
                return 0UL;
            }
            _handles.RegisterUser(handle, request.Id);
            audioAgent.PlayWithRequest(clip, request, cold);

            if (audioAgent.IsFree && audioAgent.CurrentHandle == 0UL)
            {
                return 0UL;
            }

            return handle;
        }

        /// <inheritdoc />
        public override ulong Play(string path, in AudioPlayOptions options, bool bAsync, bool bInPool)
        {
            if (_unityAudioDisabled || IsTrackPaused(options.AudioTrack)) return 0UL;

            // 物理隔离：启动窗口关闭后阻塞加载强制改异步，防止运行时 IO 卡主线程
            if (!bAsync && !AudioBlockingLoadGate.IsOpen)
            {
                AudioWarnOnce.Warning(
                    "blocking-play",
                    "[AudioService] Play(path, bAsync:false) 在启动窗口关闭后被强制改异步；请用 Preload 做启动期同步加载。");
                bAsync = true;
            }

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

            ulong handle = _handles.Bind(audioAgent);
            if (handle == 0UL) return 0UL;
            _handles.RegisterUser(handle, options.ID);
            audioAgent.LoadWithOptions(path, options, bAsync, bInPool);

            // 同步加载失败会立刻 End 并自动释放
            if (audioAgent.IsFree && audioAgent.CurrentHandle == 0UL)
            {
                return 0UL;
            }

            return handle;
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

            // Stop 的内部淡出接管音量：先清掉调度器在同句柄上的手动过渡，避免双写
            _fades.Stop(handle);
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
            => _handles.ForEachHandleByUser(id, action);

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
            // 世代校验：句柄必须仍是 Agent 当前绑定值
            return _handles.TryGet(handle, out var agent) && agent.CurrentHandle == handle ? agent : null;
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

            // 注册表 Release 单点解除映射与 Agent 侧绑定
            _handles.Release(handle, out _);
            _fades.Stop(handle);
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
        public override bool IsPaused(EAudioTrack track) => IsTrackPaused(track);

        /// <inheritdoc />
        public override void StopTrack(EAudioTrack track, float fadeoutDuration)
        {
            if (_unityAudioDisabled) return;

            FindCategory(track)?.StopAll(fadeoutDuration);
        }

        /// <summary>
        /// 音轨是否处于暂停态。暂停轨会同时拦截新播放（与中间件后端语义一致）。
        /// </summary>
        private bool IsTrackPaused(EAudioTrack track)
        {
            if (_pausedTracks == null) return false;

            int index = (int)track;
            return index >= 0 && index < _pausedTracks.Length && _pausedTracks[index];
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
            // 冷路径：分层替换/UI；注册表快照迭代保证 Stop 内释放句柄不跳元素
            _handles.ForEachHandleByUser(id, handle => Stop(handle, fadeoutDuration));
        }

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <inheritdoc />
        public override void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            // 调度器接管音量：取消 Agent 内部淡入状态机，避免同句柄双写
            GetAgentByHandle(handle)?.CancelFadeIn();

            if (duration <= 0f)
            {
                var agent = GetAgentByHandle(handle);
                if (agent != null && agent.AudioResource != null)
                    agent.AudioResource.volume = finalVolume;
                return;
            }

            _fades.Stop(handle);
            _fades.Add(new AudioFadeState
            {
                Handle = handle,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
                Ease = tweenEase,
            });
        }

        /// <inheritdoc />
        public override void PlayFadeByID(int id, float duration, float finalVolume, TweenEase ease)
        {
            // 冷路径：UI 淡入淡出
            _handles.ForEachHandleByUser(id, handle =>
            {
                var agent = GetAgentByHandle(handle);
                if (agent == null || agent.AudioResource == null) return;
                agent.CancelFadeIn();
                FadeAudio(handle, duration, agent.AudioResource.volume, finalVolume, ease);
            });
        }

        /// <inheritdoc />
        public override void StopFadeByID(int id)
            => _handles.ForEachHandleByUser(id, _fades.Stop);

        /// <summary>
        /// 过渡应用：总线伪句柄走音量属性（含 Clamp 与 Mixer/总线写入），声部句柄写 AudioSource 音量。
        /// </summary>
        bool IAudioFadeTarget.ApplyFade(ulong handle, float volume, bool finished)
        {
            if (handle == AudioFadeScheduler.MASTER_FADE_HANDLE)
            {
                MasterVolume = volume;
                return true;
            }

            if (AudioFadeScheduler.TryGetBusTrackIndex(handle, out int trackIndex))
            {
                SetTrackVolume((EAudioTrack)trackIndex, volume);
                return true;
            }

            var agent = GetAgentByHandle(handle);
            if (agent == null || agent.AudioResource == null)
            {
                // 句柄已释放 → 丢弃该过渡
                return false;
            }

            agent.AudioResource.volume = volume;
            return true;
        }

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <inheritdoc />
        /// <remarks>兼容旧 API：预加载 = Pin 常驻进 Clip 缓存。</remarks>
        public override void PutInAudioPool(List<string> list)
        {
            if (_unityAudioDisabled || list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (!string.IsNullOrEmpty(path))
                {
                    _clipCache.Preload(path, AudioCachePolicy.Pin);
                }
            }
        }

        /// <inheritdoc />
        public override void RemoveClipFromPool(List<string> list)
        {
            if (_unityAudioDisabled || list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                _clipCache.Unload(list[i], force: true);
            }
        }

        /// <inheritdoc />
        public override void CleanAudioPool()
        {
            if (_unityAudioDisabled) return;
            _clipCache.ClearCache(force: true);
        }

        /// <inheritdoc />
        public override bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Pin)
            => !_unityAudioDisabled && _clipCache.Preload(address, policy);

        /// <inheritdoc />
        public override void PreloadAsync(string address, AudioCachePolicy policy, Action<bool> completed = null)
        {
            if (_unityAudioDisabled)
            {
                completed?.Invoke(false);
                return;
            }

            _clipCache.PreloadAsync(address, policy, completed);
        }

        /// <inheritdoc />
        public override bool UnloadClipCache(string address, bool force = false)
            => !_unityAudioDisabled && _clipCache.Unload(address, force);

        /// <inheritdoc />
        public override void ClearClipCache(bool force = false)
        {
            if (_unityAudioDisabled) return;
            _clipCache.ClearCache(force);
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
