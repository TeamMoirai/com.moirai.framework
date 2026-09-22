using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 中间件音频处理器基类——FMOD / Wwise 共用。
    /// <para>统一：句柄生命周期（<see cref="AudioHandleRegistry{TVoice}"/>）、用户 ID 映射、
    /// 声部与总线 Fade（<see cref="AudioFadeScheduler"/>）、总线音量/静音/暂停、场景切换清理。</para>
    /// <para>子类只需提供 <see cref="CreateDefaultBridge"/> 与可选总线路径覆盖。</para>
    /// <para>冷路径 API（PlayFadeByID / StopByID 等）直接用 lambda，不做委托缓存——UI 触发频率低，可读性优先。</para>
    /// <para>语义与 Unity 后端对齐：暂停轨拦截新播放、MasterVolume getter 始终返回未静音值、
    /// Master/音轨 Fade 带缓动（经共享过渡调度器驱动总线音量）。</para>
    /// <para>不支持项：InitialDelay / PlaybackDuration / Solo（中间件事件由工程侧编排）。</para>
    /// </summary>
    [Serializable]
    internal abstract class MiddlewareAudioHandler : AudioServiceHandler, IAudioFadeTarget
    {
        #region 声部 [VOICE]

        private sealed class Voice : IAudioVoiceRef
        {
            public ulong Handle;

            /// <summary>句柄注册表槽位（-1 = 未注册）；只由注册表写，随 Reset 归位以免复用时带着旧下标。</summary>
            public int Slot = -1;
            public ulong InstanceId;
            public int UserId;
            public string EventPath;
            public float CurrentVolume = 1f;
            public bool Loop;
            public bool Persistent;
            public byte Priority = 128;
            public EAudioTrack Track;
            public bool Paused;
            public bool Playing;

            int IAudioVoiceRef.UserId => UserId;

            ulong IAudioVoiceRef.BoundHandle
            {
                get => Handle;
                set => Handle = value;
            }

            int IAudioVoiceRef.VoiceSlot
            {
                get => Slot;
                set => Slot = value;
            }

            /// <summary>归还池前复位全部字段，避免脏状态随复用泄漏。</summary>
            public void Reset()
            {
                Handle = 0UL;
                Slot = -1;
                InstanceId = 0UL;
                UserId = 0;
                EventPath = null;
                CurrentVolume = 1f;
                Loop = false;
                Persistent = false;
                Priority = 128;
                Track = default;
                Paused = false;
                Playing = false;
            }
        }

        /// <summary>Voice 栈池——Play 热路径零分配（Rent/Return）。</summary>
        [NonSerialized] private readonly Stack<Voice> _voicePool = new Stack<Voice>(16);

        /// <summary>Voice 池当前缓存数量（诊断用，类似句柄注册表 Count）。</summary>
        internal int VoicePoolCount => _voicePool.Count;

        /// <summary>当前注册句柄数（诊断用）。</summary>
        internal int ActiveHandleCount => _handles.Count;

        private Voice RentVoice()
        {
            return _voicePool.Count > 0 ? _voicePool.Pop() : new Voice();
        }

        private void ReturnVoice(Voice voice)
        {
            if (voice == null) return;
            voice.Reset();
            _voicePool.Push(voice);
        }

        /// <summary>卸绑全部句柄并把 Voice 归还池（Reset 字段防脏状态随复用泄漏）。</summary>
        private void ReleaseAllVoicesToPool()
        {
            foreach (var slot in _handles.Slots)
            {
                ReturnVoice(slot.Voice);
            }

            _handles.Clear();
        }

        #endregion 声部 [VOICE]

        [NonSerialized] private IAudioMiddlewareBridge _bridge;
        [NonSerialized] private Transform _instanceRoot;

        // 服务句柄注册表（句柄生成、句柄→Voice、用户 ID 映射、列表池）
        [NonSerialized] private readonly AudioHandleRegistry<Voice> _handles = new AudioHandleRegistry<Voice>();
        // 音量过渡调度器（声部 + Master/音轨总线伪句柄共用）
        [NonSerialized] private readonly AudioFadeScheduler _fades = new AudioFadeScheduler();
        [NonSerialized] private readonly Dictionary<ulong, float> _pendingStopAt = new Dictionary<ulong, float>(8);
        // 批量控制与 Tick 回收共用的句柄暂存：先收集再改表，避免遍历中释放句柄；复用以免每帧分配。
        // 用点之间不得嵌套（ReleaseHandle/FadeAudio 都不会再取它，成立）。
        [NonSerialized] private readonly List<ulong> _handleScratch = new List<ulong>(16);

        [NonSerialized] private float _masterVolume = 1f;
        [NonSerialized] private bool _masterMute;
        [NonSerialized] private float[] _trackVolumes;
        [NonSerialized] private bool[] _trackMutes;
        [NonSerialized] private bool[] _pausedTracks;

        [Header("事件映射表 [Event Map]")]
        [Tooltip("AudioClip → 事件路径。命中即用，不再按 clip.name 推导；未命中回落到推导并提示一次。")]
        [SerializeField] private AudioEventMapping[] m_EventMappings = Array.Empty<AudioEventMapping>();
        [NonSerialized] private Dictionary<AudioClip, string> _eventByClip;
        [NonSerialized] private HashSet<string> _derivedPathWarned;

        /// <summary>默认总线路径，按 EAudioTrack 索引。</summary>
        private static readonly string[] s_DefaultBusPaths =
        {
            "bus:/Sfx", "bus:/UI", "bus:/Music", "bus:/Voice", "bus:/Ambience"
        };

        /// <summary>
        /// 创建默认桥接（子类实现：按 XXX_INSTALLED 选 Native 或 Stub）。
        /// </summary>
        protected abstract IAudioMiddlewareBridge CreateDefaultBridge();

        /// <summary>
        /// 覆盖总线路径（可选）。默认 bus:/{Track}。
        /// </summary>
        protected virtual string GetBusPath(EAudioTrack track)
        {
            int index = (int)track;
            return index >= 0 && index < s_DefaultBusPaths.Length ? s_DefaultBusPaths[index] : "bus:/Master";
        }

        /// <summary>
        /// 注入自定义桥接（测试）。
        /// </summary>
        public void SetBridge(IAudioMiddlewareBridge bridge) => _bridge = bridge;

        /// <summary>
        /// 当前桥接。
        /// </summary>
        public IAudioMiddlewareBridge Bridge => _bridge;

        private void EnsureTrackArrays()
        {
            if (_trackVolumes != null) return;
            int count = Enum.GetValues(typeof(EAudioTrack)).Length;
            _trackVolumes = new float[count];
            _trackMutes = new bool[count];
            _pausedTracks = new bool[count];
            for (int i = 0; i < count; i++) _trackVolumes[i] = 1f;
        }

        #region 处理器属性 [PROPERTIES]

        /// <inheritdoc />
        public override AudioMixer AudioMixer => null;

        /// <inheritdoc />
        public override Transform InstanceRoot
        {
            get => _instanceRoot;
            set => _instanceRoot = value;
        }

        /// <inheritdoc />
        /// <summary>占位表：中间件按事件路径播放，不需要 clip 租约，这里只保留“已登记”的键集合。</summary>
        private readonly Dictionary<string, object> _assetHandles = new Dictionary<string, object>();
        private System.Collections.ObjectModel.ReadOnlyDictionary<string, object> _assetHandlesView;

        /// <inheritdoc />
        /// <remarks>
        /// 返回包装而非底表：裸 <c>Dictionary</c> 虽以 <c>IReadOnlyDictionary</c> 出现，仍可被 cast 回去改写，
        /// 而包装类型 cast 不回 <c>Dictionary</c>，占位表的外部可写面就此关闭。
        /// </remarks>
        public override IReadOnlyDictionary<string, object> AssetHandlePool
            => _assetHandlesView ??= new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(_assetHandles);

        /// <inheritdoc />
        public override AudioCategory[] AudioCategories => Array.Empty<AudioCategory>();

        #endregion 处理器属性 [PROPERTIES]

        #region 音轨状态 [TRACK STATUS]

        /// <inheritdoc />
        public override float MasterVolume
        {
            // 与 Unity 后端语义一致：静音只影响实际总线输出，getter 始终返回设置值
            get => _masterVolume;
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                ApplyMasterVolume();
            }
        }

        /// <inheritdoc />
        public override bool MasterMute
        {
            get => _masterMute;
            set
            {
                _masterMute = value;
                ApplyMasterVolume();
            }
        }

        private void ApplyMasterVolume()
            => _bridge?.SetBusVolume("bus:/Master", _masterMute ? 0f : _masterVolume);

        /// <inheritdoc />
        public override void SetMasterSettings()
        {
            SettingUtility.SetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, _masterVolume);
            SettingUtility.SetBool(GameConstant.Setting.AUDIO_MASTER_MUTED, _masterMute);
        }

        /// <inheritdoc />
        public override void LoadMasterSettings()
        {
            _masterMute = SettingUtility.GetBool(GameConstant.Setting.AUDIO_MASTER_MUTED, false);
            // 存量值可能写于上限还是 10 的年代，读回来先落到契约值域
            _masterVolume = Mathf.Clamp01(SettingUtility.GetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, 1f));
            ApplyMasterVolume();

            EnsureTrackArrays();
            for (int i = 0; i < _trackVolumes.Length; i++)
            {
                var track = (EAudioTrack)i;
                _trackMutes[i] = SettingUtility.GetBool(
                    StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_MUTED, track), false);
                _trackVolumes[i] = Mathf.Clamp01(SettingUtility.GetFloat(
                    StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_VOLUME, track), 1f));
                ApplyTrackBus(track);
            }
        }

        /// <inheritdoc />
        public override void RemoveMasterSetting()
        {
            SettingUtility.RemoveSetting(GameConstant.Setting.AUDIO_MASTER_MUTED);
            SettingUtility.RemoveSetting(GameConstant.Setting.AUDIO_MASTER_VOLUME);
            _masterMute = false;
            _masterVolume = 1f;
            ApplyMasterVolume();
        }

        /// <inheritdoc />
        public override float GetTrackVolume(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            return index >= 0 && index < _trackVolumes.Length ? _trackVolumes[index] : 1f;
        }

        /// <inheritdoc />
        public override void SetTrackVolume(EAudioTrack track, float volume)
        {
            EnsureTrackArrays();
            int index = (int)track;
            if (index < 0 || index >= _trackVolumes.Length) return;
            _trackVolumes[index] = Mathf.Clamp(volume, 0f, AudioGroupConfig.MAXIMAL_VOLUME);
            ApplyTrackBus(track);
        }

        /// <inheritdoc />
        public override bool GetTrackMute(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            return index >= 0 && index < _trackMutes.Length && _trackMutes[index];
        }

        /// <inheritdoc />
        public override void SetTrackMute(EAudioTrack track, bool mute)
        {
            EnsureTrackArrays();
            int index = (int)track;
            if (index < 0 || index >= _trackMutes.Length) return;
            _trackMutes[index] = mute;
            ApplyTrackBus(track);
        }

        private void ApplyTrackBus(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            // 存的就是 0..1（SetTrackVolume 夹过一次），这里不再偷偷夹第二遍：
            // 上一版正是这个二次夹取让"Unity 能给到 10、中间件只能到 1"的分歧隐身了
            float v = _trackMutes[index] ? 0f : _trackVolumes[index];
            _bridge?.SetBusVolume(GetBusPath(track), v);
        }

        #endregion 音轨状态 [TRACK STATUS]

        #region 服务方法 [SERVICE METHOD]

        /// <inheritdoc />
        protected override void OnInit()
        {
            if (!Application.isPlaying) return;

            _bridge ??= CreateDefaultBridge();

            if (_instanceRoot == null)
            {
                _instanceRoot = new GameObject("[MiddlewareAudio]").transform;
                UnityEngine.Object.DontDestroyOnLoad(_instanceRoot);
            }

            if (_bridge.Initialize(_instanceRoot))
            {
                EnsureTrackArrays();
                SceneManager.sceneLoaded += OnSceneLoaded;
                return;
            }

            // 上线门槛 G5 的回退决策：原生引擎没起来就整体禁用，不留「半初始化」状态。
            // 留着引用等于让 Tick 的 Update/IsPlaying/StopInstance 与总线音量、Bank 写入继续打到未初始化的
            // 原生层——那是无 SDK 机器上跑不出来、线上无法归因的崩溃面。丢引用后各处的判空分支自动退化成
            // 静默 no-op（Play 返回 0、Bank/RTPC 空操作、Shutdown 不再触达），既不刷屏也不卡主线程。
            // 恢复只在重启进程时发生：本方法不做重试，Restart 也不重开引擎，避免健康后端被二次 Init。
            LogUtility.Error(
                "[MiddlewareAudio] 桥接初始化失败，本次运行音频已禁用（Play 返回 0、Bank/RTPC 空操作）。" +
                "请检查 SDK 插件是否导入、*_INSTALLED 宏与 Handler 选择是否成对配置。");
            _bridge = null;
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            if (!Application.isPlaying) return;

            StopAll(0f);
            AudioVoiceDucking.Reset();
            _fades.Clear();
            _pendingStopAt.Clear();
            // 终态关停：Reset 后丢弃池（实例不再复用），避免留下无主缓存
            ReleaseAllVoicesToPool();
            _voicePool.Clear();

            _bridge?.Shutdown();
            _bridge = null;
            _instanceRoot = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <inheritdoc />
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            // 不碰 AudioBlockingLoadGate：该门禁强制点在 Unity 后端的路径 Play 上，
            // 中间件按事件路径即时下发、没有同步资源加载可拦，调 Close/Open 只会给出一个并不存在的保证。
            if (_bridge == null) return;
            _bridge.Update(Time.unscaledDeltaTime);
            _fades.Update(GameTime.unscaledTime, this);
            ProcessPendingStops();
            ReleaseFinishedOneshots();

            try
            {
                AudioVoiceDucking.Evaluate(this);
            }
            catch (Exception e)
            {
                AudioFault.Report($"{nameof(MiddlewareAudioHandler)}.{nameof(Tick)}:ducking", e);
            }
        }

        /// <summary>
        /// 音轨上是否有仍在播/暂停中的声部（句柄表实算，不另设计数器）。
        /// </summary>
        internal override bool HasActiveAudioOn(EAudioTrack track)
        {
            foreach (var slot in _handles.Slots)
            {
                var voice = slot.Voice;
                if (voice != null && voice.Playing && voice.Track == track) return true;
            }

            return false;
        }

        private void ProcessPendingStops()
        {
            if (_pendingStopAt.Count == 0) return;

            float now = GameTime.unscaledTime;
            _handleScratch.Clear();
            foreach (var pending in _pendingStopAt)
            {
                if (now >= pending.Value) _handleScratch.Add(pending.Key);
            }

            if (_handleScratch.Count == 0) return;
            for (int i = 0; i < _handleScratch.Count; i++)
            {
                ulong handle = _handleScratch[i];
                _pendingStopAt.Remove(handle);
                if (_handles.TryGet(handle, out var voice))
                {
                    voice.Playing = false;
                    _bridge?.StopInstance(voice.InstanceId, true);
                }

                ReleaseHandle(handle);
            }

            _handleScratch.Clear();
        }

        private void ReleaseFinishedOneshots()
        {
            _handleScratch.Clear();
            foreach (var slot in _handles.Slots)
            {
                var voice = slot.Voice;
                // 暂停中的 oneshot 不算播完（否则 Pause 后下一帧即被误回收）
                if (!voice.Playing || voice.Paused || voice.Loop) continue;
                if (_bridge != null && _bridge.IsPlaying(voice.InstanceId)) continue;
                _handleScratch.Add(slot.Handle);
            }

            for (int i = 0; i < _handleScratch.Count; i++)
            {
                ulong handle = _handleScratch[i];
                // 通知桥接清实例映射/发射体（oneshot 自然结束不会走 Stop）
                if (_handles.TryGet(handle, out var voice))
                {
                    voice.Playing = false;
                    _bridge?.StopInstance(voice.InstanceId, true);
                }

                ReleaseHandle(handle);
            }

            _handleScratch.Clear();
        }

        /// <inheritdoc />
        public override void Restart()
        {
            StopAll(0f);
            CleanAudioPool();
            AudioVoiceDucking.Reset();
            _fades.Clear();
            _pendingStopAt.Clear();
            // 可复用重置：归还池供热复用，刻意不 Clear
            ReleaseAllVoicesToPool();
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <inheritdoc />
        public override ulong Play(AudioClip clip, in AudioPlayOptions options)
            => PlayWithRequest(clip, options.ToRequest(), AudioPlayColdParams.FromOptions(options));

        /// <inheritdoc />
        public override ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold)
            => PlayWithRequest(clip, request, cold);

        /// <inheritdoc />
        public override ulong Play(string path, in AudioPlayOptions options, bool bAsync, bool bInPool)
            => PlayEventPath(path, options.ToRequest(), AudioPlayColdParams.FromOptions(options));

        /// <summary>
        /// 按事件路径 + 16B 热请求播放（中间件推荐入口）。
        /// </summary>
        public ulong Play(string eventPath, in AudioPlayRequest request, AudioPlayColdParams cold)
            => PlayEventPath(eventPath, request, cold);

        private ulong PlayWithRequest(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold)
            => PlayEventPath(ResolveEventPath(clip), request, cold);

        /// <summary>
        /// 解析 clip 对应的事件路径：先查 <see cref="AudioEventMapping"/> 映射表，未命中才回落到
        /// 桥接按 <c>clip.name</c> 推导（并就该 clip 提示一次——事件名与 clip 名不一致时静默推导出错最难查）。
        /// </summary>
        private string ResolveEventPath(AudioClip clip)
        {
            if (clip == null) return null;

            var map = EnsureEventMap();
            if (map != null && map.TryGetValue(clip, out var mapped) && !string.IsNullOrEmpty(mapped))
            {
                return mapped;
            }

            string derived = _bridge != null ? _bridge.GetEventPathFromClip(clip) : null;
            if (string.IsNullOrEmpty(derived)) return null;

            _derivedPathWarned ??= new HashSet<string>(StringComparer.Ordinal);
            if (_derivedPathWarned.Add(clip.name))
            {
                LogUtility.Warning(
                    "[Audio] 事件路径由 clip.name 推导为 {0}；若与音效师的事件名不一致，请在后端的事件映射表里显式登记。", derived);
            }

            return derived;
        }

        private Dictionary<AudioClip, string> EnsureEventMap()
        {
            if (_eventByClip != null) return _eventByClip;
            if (m_EventMappings == null || m_EventMappings.Length == 0) return null;

            var map = new Dictionary<AudioClip, string>(m_EventMappings.Length);
            for (int i = 0; i < m_EventMappings.Length; i++)
            {
                var entry = m_EventMappings[i];
                if (entry == null || entry.Clip == null || string.IsNullOrEmpty(entry.EventPath)) continue;

                if (map.ContainsKey(entry.Clip))
                {
                    LogUtility.Warning("[Audio] 事件映射表里 {0} 重复登记，采用先出现的一条。", entry.Clip.name);
                    continue;
                }

                map.Add(entry.Clip, entry.EventPath);
            }

            _eventByClip = map;
            return map;
        }

        /// <summary>映射表变更后需重建缓存（编辑器/热更里改配置时调用）。</summary>
        public void InvalidateEventMap()
        {
            _eventByClip = null;
            _derivedPathWarned = null;
        }

        /// <summary>
        /// 运行期整体替换事件映射表（读自音效侧导表/配置时使用），并作废已建好的索引。
        /// </summary>
        /// <remarks>会覆盖 Inspector 上配置的条目；传入 null 等价于清空。</remarks>
        public void SetEventMappings(AudioEventMapping[] mappings)
        {
            m_EventMappings = mappings ?? Array.Empty<AudioEventMapping>();
            InvalidateEventMap();
        }

        #region 音效师接入面 [AUTHORING APIS]

        [NonSerialized] private bool _bankApiWarned;
        [NonSerialized] private bool _rtpcApiWarned;

        /// <inheritdoc />
        /// <remarks>
        /// 需要桥接实现 <see cref="IAudioMiddlewareBankControl"/>；未实现时提示一次并返回 false。
        /// 只有 <see cref="EAudioBankLoadResult.Failed"/> 才告警（同一库一次）——幂等命中与「插件启动时
        /// 自行加载过的 master/Init 库」都是正常路径，报出来只会把真失败淹成噪音。
        /// 返回 <c>true</c> 仅表示本次调用真的完成了加载。
        /// </remarks>
        public override bool LoadBank(string bankPath)
        {
            if (string.IsNullOrEmpty(bankPath) || _bridge == null) return false;

            if (_bridge is not IAudioMiddlewareBankControl banks)
            {
                WarnOnce(ref _bankApiWarned, nameof(IAudioMiddlewareBankControl));
                return false;
            }

            switch (banks.LoadBank(bankPath))
            {
                case EAudioBankLoadResult.Loaded:
                    return true;
                case EAudioBankLoadResult.AlreadyLoaded:
                    return false;
                default:
                    AudioWarnOnce.Warning($"bank:load-failed:{bankPath}",
                        "[Audio] 声音库 {0} 加载失败（路径写错、文件未随包，或引擎未就绪）。该库内的事件在加载成功前不会出声。",
                        bankPath);
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool UnloadBank(string bankPath)
        {
            if (string.IsNullOrEmpty(bankPath) || _bridge == null) return false;

            if (_bridge is not IAudioMiddlewareBankControl banks)
            {
                WarnOnce(ref _bankApiWarned, nameof(IAudioMiddlewareBankControl));
                return false;
            }

            return banks.UnloadBank(bankPath);
        }

        /// <inheritdoc />
        /// <remarks><paramref name="handle"/> 为 0 时作用于工程/全局参数，否则定位到该句柄的原生实例。</remarks>
        public override void SetRtpc(string name, float value, ulong handle = 0UL)
        {
            if (string.IsNullOrEmpty(name) || _bridge == null) return;

            if (_bridge is not IAudioMiddlewareRtpcControl rtpc)
            {
                WarnOnce(ref _rtpcApiWarned, nameof(IAudioMiddlewareRtpcControl));
                return;
            }

            ulong instanceId = 0UL;
            if (handle != 0UL)
            {
                if (!_handles.TryGet(handle, out var voice)) return;
                instanceId = voice.InstanceId;
            }

            rtpc.SetRtpc(name, value, instanceId);
        }

        private static void WarnOnce(ref bool warned, string capability)
        {
            if (warned) return;
            warned = true;
            LogUtility.Warning(
                "[Audio] 当前桥接未实现 {0}，相关调用为空操作。换用支持的桥接，或在真 SDK 桥里补上该能力。", capability);
        }

        #endregion 音效师接入面 [AUTHORING APIS]

        private ulong PlayEventPath(string eventPath, in AudioPlayRequest request, AudioPlayColdParams cold)
        {
            // 声部表与句柄注册表无跨线程保护：后台线程里回调 Play 不会立刻崩，
            // 而是留下偶发错音/失联句柄这类线上无法归因的症状，所以开发期直接断言。
            AudioMainThread.AssertMainThread(nameof(PlayEventPath));

            if (_bridge == null || string.IsNullOrEmpty(eventPath))
            {
                AudioPlayColdParamsPool.Release(cold);
                return 0UL;
            }

            EnsureTrackArrays();
            int trackIndex = (int)request.Track;
            if (trackIndex < 0 || trackIndex >= _pausedTracks.Length || _pausedTracks[trackIndex])
            {
                AudioPlayColdParamsPool.Release(cold);
                return 0UL;
            }

            float fadeInDuration = cold?.FadeInDuration ?? 0f;
            float fadeInFrom = cold?.FadeInInitialVolume ?? 0f;
            TweenEase fadeInEase = cold?.FadeInTweenEase ?? default;
            Vector3? pos = cold != null && cold.SpatialBlend > 0.5f ? cold.Location : (Vector3?)null;
            AudioPlayColdParamsPool.Release(cold);

            ulong instanceId = _bridge.PlayEvent(eventPath, request.Volume, request.Pitch, request.Loop, pos);
            if (instanceId == 0UL)
            {
                // SDK 侧播放失败只给一个 0，不留痕就等于线上「这个音效偶尔不响」永远无从归因：
                // 路径写错、所在声音库没加载、工程声部到达上限三类原因都落在这里。
                AudioWarnOnce.Warning($"event:play-failed:{eventPath}",
                    "[Audio] 事件 {0} 播放失败（路径写错、所在声音库未加载，或工程声部已到上限）。", eventPath);
                return 0UL;
            }

            var voice = RentVoice();
            voice.InstanceId = instanceId;
            voice.UserId = request.Id;
            voice.EventPath = eventPath;
            voice.CurrentVolume = request.FadeInOnPlay ? fadeInFrom : request.Volume;
            voice.Loop = request.Loop;
            voice.Persistent = request.Persistent;
            voice.Priority = request.Priority;
            voice.Track = request.Track;
            voice.Playing = true;

            // 句柄由注册表分配（低 20 位槽、高位代次），声部侧句柄与槽位都由 Bind 单点写
            ulong handle = _handles.Bind(voice);
            if (handle == 0UL)
            {
                // 槽位用尽（2^20 只并发声部）：这一声宁可不出，也不留一只没有记账的实例
                _bridge.StopInstance(instanceId, true);
                ReturnVoice(voice);
                return 0UL;
            }

            _handles.RegisterUser(handle, request.Id);

            if (request.FadeInOnPlay && fadeInDuration > 0f)
            {
                FadeAudio(handle, fadeInDuration, fadeInFrom, request.Volume, fadeInEase);
            }

            return handle;
        }

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <inheritdoc />
        public override void Pause(ulong handle)
        {
            if (_handles.TryGet(handle, out var voice) && voice.Playing)
            {
                voice.Paused = true;
                _bridge?.SetPaused(voice.InstanceId, true);
            }
        }

        /// <inheritdoc />
        public override void Unpause(ulong handle)
        {
            if (_handles.TryGet(handle, out var voice) && voice.Paused)
            {
                voice.Paused = false;
                _bridge?.SetPaused(voice.InstanceId, false);
            }
        }

        /// <inheritdoc />
        public override void Stop(ulong handle, float fadeoutDuration = 0f)
        {
            if (!_handles.TryGet(handle, out var voice)) return;

            if (fadeoutDuration > 0f && voice.Playing && !voice.Paused)
            {
                FadeAudio(handle, fadeoutDuration, voice.CurrentVolume, 0f, default);
                _pendingStopAt[handle] = GameTime.unscaledTime + fadeoutDuration;
                return;
            }

            voice.Playing = false;
            _bridge?.StopInstance(voice.InstanceId, true);
            ReleaseHandle(handle);
        }

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <inheritdoc />
        public override void ForEachAgentByID(int id, Action<AudioAgent> action)
        {
            // 中间件后端无 Unity AudioSource Agent
        }

        /// <inheritdoc />
        public override void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action)
        {
        }

        /// <inheritdoc />
        public override void ForEachHandleByID(int id, Action<ulong> action)
            => _handles.ForEachHandleByUser(id, action);

        /// <inheritdoc />
        /// <remarks>
        /// 走与 <c>Play(clip, …)</c> 同一条事件路径解析（映射表优先、回落按名推导）：
        /// 直接用桥的推导会把命中映射表的 clip 恒判成 0。
        /// </remarks>
        public override int CurrentlyPlayingCount(AudioClip clip)
        {
            string path = ResolveEventPath(clip);
            if (string.IsNullOrEmpty(path)) return 0;

            int count = 0;
            foreach (var slot in _handles.Slots)
            {
                if (slot.Voice.Playing && slot.Voice.EventPath == path) count++;
            }

            return count;
        }

        /// <inheritdoc />
        public override AudioAgent GetAgentByHandle(ulong handle) => null;

        /// <inheritdoc />
        public override bool IsPlaying(ulong handle)
            => _handles.TryGet(handle, out var voice) && voice.Playing && !voice.Paused;

        /// <inheritdoc />
        public override bool IsStopped(ulong handle) => !_handles.IsRegistered(handle);

        /// <inheritdoc />
        public override void ReleaseHandle(ulong handle)
        {
            if (handle == 0UL) return;

            if (_handles.Release(handle, out var voice))
            {
                // Registry.Release 已清 BoundHandle；这里复位后入池，保证 IAudioVoiceRef 绑定语义不外泄
                ReturnVoice(voice);
            }

            _fades.Stop(handle);
            _pendingStopAt.Remove(handle);
        }

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <inheritdoc />
        public override void PauseTrack(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            if (index >= 0 && index < _pausedTracks.Length) _pausedTracks[index] = true;

            foreach (var slot in _handles.Slots)
            {
                if (slot.Voice.Track == track) Pause(slot.Handle);
            }
        }

        /// <inheritdoc />
        public override void UnpauseTrack(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            if (index >= 0 && index < _pausedTracks.Length) _pausedTracks[index] = false;

            foreach (var slot in _handles.Slots)
            {
                if (slot.Voice.Track == track) Unpause(slot.Handle);
            }
        }

        /// <inheritdoc />
        public override bool IsPaused(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            return index >= 0 && index < _pausedTracks.Length && _pausedTracks[index];
        }

        /// <inheritdoc />
        public override void StopTrack(EAudioTrack track, float fadeoutDuration = 0f)
        {
            _handleScratch.Clear();
            foreach (var slot in _handles.Slots)
            {
                if (slot.Voice.Track == track) _handleScratch.Add(slot.Handle);
            }

            for (int i = 0; i < _handleScratch.Count; i++)
            {
                Stop(_handleScratch[i], fadeoutDuration);
            }

            _handleScratch.Clear();
        }

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <inheritdoc />
        public override void PauseAll()
        {
            foreach (var slot in _handles.Slots) Pause(slot.Handle);
        }

        /// <inheritdoc />
        public override void UnpauseAll()
        {
            foreach (var slot in _handles.Slots) Unpause(slot.Handle);
        }

        /// <inheritdoc />
        public override void StopAll(float fadeoutDuration = 0f)
        {
            _handleScratch.Clear();
            foreach (var slot in _handles.Slots)
            {
                _handleScratch.Add(slot.Handle);
            }

            for (int i = 0; i < _handleScratch.Count; i++)
            {
                Stop(_handleScratch[i], fadeoutDuration);
            }

            _handleScratch.Clear();
        }

        /// <inheritdoc />
        public override void StopAllButPersistent(float fadeoutDuration = 0f)
        {
            _handleScratch.Clear();
            foreach (var slot in _handles.Slots)
            {
                if (slot.Voice.Persistent) continue;
                _handleScratch.Add(slot.Handle);
            }

            for (int i = 0; i < _handleScratch.Count; i++)
            {
                Stop(_handleScratch[i], fadeoutDuration);
            }

            _handleScratch.Clear();
        }

        /// <inheritdoc />
        public override void StopAllLooping(float fadeoutDuration = 0f)
        {
            _handleScratch.Clear();
            foreach (var slot in _handles.Slots)
            {
                if (!slot.Voice.Loop) continue;
                _handleScratch.Add(slot.Handle);
            }

            for (int i = 0; i < _handleScratch.Count; i++)
            {
                Stop(_handleScratch[i], fadeoutDuration);
            }

            _handleScratch.Clear();
        }

        /// <inheritdoc />
        public override void StopByID(int id, float fadeoutDuration = 0f)
        {
            // 冷路径：设置面板/分层替换，lambda 分配可忽略
            _handles.ForEachHandleByUser(id, handle => Stop(handle, fadeoutDuration));
        }

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <inheritdoc />
        public override void FadeMasterTrack(float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
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

        /// <inheritdoc />
        public override void StopFadeMasterTrack() => _fades.Stop(AudioFadeScheduler.MASTER_FADE_HANDLE);

        /// <inheritdoc />
        public override void FadeTrack(EAudioTrack track, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
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

        /// <inheritdoc />
        public override void StopFadeTrack(EAudioTrack track)
            => _fades.Stop(AudioFadeScheduler.TrackFadeHandle((int)track));

        /// <inheritdoc />
        public override void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (duration <= 0f)
            {
                ApplyFadeVolume(handle, finalVolume, finished: true);
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
        public override void StopFadeAudio(ulong handle) => _fades.Stop(handle);

        /// <inheritdoc />
        public override bool SoundIsFadingOut(ulong handle) => _fades.IsFading(handle);

        /// <inheritdoc />
        public override void PlayFadeByID(int id, float duration, float finalVolume, TweenEase ease)
        {
            _handles.ForEachHandleByUser(id, handle =>
            {
                if (!_handles.TryGet(handle, out var voice)) return;
                FadeAudio(handle, duration, voice.CurrentVolume, finalVolume, ease);
            });
        }

        /// <inheritdoc />
        public override void StopFadeByID(int id)
            => _handles.ForEachHandleByUser(id, _fades.Stop);

        /// <summary>
        /// 过渡应用：总线伪句柄走音量属性（含 Clamp 与总线写入），声部句柄经桥接写实例音量。
        /// </summary>
        bool IAudioFadeTarget.ApplyFade(ulong handle, float volume, bool finished)
            => ApplyFadeVolume(handle, volume, finished);

        private bool ApplyFadeVolume(ulong handle, float volume, bool finished)
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

            if (!_handles.TryGet(handle, out var voice)) return false;

            voice.CurrentVolume = volume;
            _bridge?.SetInstanceVolume(voice.InstanceId, volume);
            return true;
        }

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <inheritdoc />
        public override void PutInAudioPool(List<string> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                if (!string.IsNullOrEmpty(path) && !_assetHandles.ContainsKey(path))
                {
                    _assetHandles.Add(path, path);
                }
            }
        }

        /// <inheritdoc />
        public override void RemoveClipFromPool(List<string> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                _assetHandles.Remove(list[i]);
            }
        }

        /// <inheritdoc />
        public override void CleanAudioPool() => _assetHandles.Clear();

        /// <inheritdoc />
        /// <remarks>中间件无 clip 租约；仅登记键值占位。</remarks>
        public override bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Pin)
        {
            if (string.IsNullOrEmpty(address)) return false;
            _assetHandles[address] = address;
            return true;
        }

        /// <inheritdoc />
        public override void PreloadAsync(string address, AudioCachePolicy policy, Action<bool> completed = null)
        {
            completed?.Invoke(Preload(address, policy));
        }

        /// <inheritdoc />
        public override bool UnloadClipCache(string address, bool force = false)
        {
            if (string.IsNullOrEmpty(address)) return false;
            return _assetHandles.Remove(address);
        }

        /// <inheritdoc />
        public override void ClearClipCache(bool force = false) => _assetHandles.Clear();

        #endregion 资源池 [ASSET POOL]

        #region 事件 [EVENTS]

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
            => StopAllButPersistent(0.2f);

        #endregion 事件 [EVENTS]
    }
}
