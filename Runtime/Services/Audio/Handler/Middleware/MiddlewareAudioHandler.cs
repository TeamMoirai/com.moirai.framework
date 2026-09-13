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
    public abstract class MiddlewareAudioHandler : AudioServiceHandler, IAudioFadeTarget
    {
        #region 声部 [VOICE]

        private sealed class Voice : IAudioVoiceRef
        {
            public ulong Handle;
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
        }

        #endregion 声部 [VOICE]

        [NonSerialized] private IAudioMiddlewareBridge _bridge;
        [NonSerialized] private Transform _instanceRoot;
        [NonSerialized] private bool _backendFailed;

        // 服务句柄注册表（句柄生成、句柄→Voice、用户 ID 映射、列表池）
        [NonSerialized] private readonly AudioHandleRegistry<Voice> _handles = new AudioHandleRegistry<Voice>();
        // 音量过渡调度器（声部 + Master/音轨总线伪句柄共用）
        [NonSerialized] private readonly AudioFadeScheduler _fades = new AudioFadeScheduler();
        [NonSerialized] private readonly Dictionary<ulong, float> _pendingStopAt = new Dictionary<ulong, float>(8);

        [NonSerialized] private float _masterVolume = 1f;
        [NonSerialized] private bool _masterMute;
        [NonSerialized] private float[] _trackVolumes;
        [NonSerialized] private bool[] _trackMutes;
        [NonSerialized] private bool[] _pausedTracks;

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
        public override Dictionary<string, object> AssetHandlePool { get; } = new Dictionary<string, object>();

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
            => _bridge?.SetBusVolume("bus:/Master", _masterMute ? 0f : Mathf.Clamp01(_masterVolume));

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
            _masterVolume = SettingUtility.GetFloat(GameConstant.Setting.AUDIO_MASTER_VOLUME, 1f);
            ApplyMasterVolume();

            EnsureTrackArrays();
            for (int i = 0; i < _trackVolumes.Length; i++)
            {
                var track = (EAudioTrack)i;
                _trackMutes[i] = SettingUtility.GetBool(
                    StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_MUTED, track), false);
                _trackVolumes[i] = SettingUtility.GetFloat(
                    StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_VOLUME, track), 1f);
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
            float v = _trackMutes[index] ? 0f : Mathf.Clamp01(_trackVolumes[index]);
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

            _backendFailed = !_bridge.Initialize(_instanceRoot);
            if (_backendFailed)
            {
                LogUtility.Error("[MiddlewareAudio] Bridge initialize failed.");
                return;
            }

            EnsureTrackArrays();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            if (!Application.isPlaying) return;

            StopAll(0f);
            _fades.Clear();
            _pendingStopAt.Clear();
            _handles.Clear();

            _bridge?.Shutdown();
            _bridge = null;
            _instanceRoot = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <inheritdoc />
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (_bridge == null) return;
            _bridge.Update(Time.unscaledDeltaTime);
            _fades.Update(GameTime.unscaledTime, this);
            ProcessPendingStops();
            ReleaseFinishedOneshots();
        }

        private void ProcessPendingStops()
        {
            if (_pendingStopAt.Count == 0) return;

            List<ulong> done = null;
            float now = GameTime.unscaledTime;
            foreach (var kv in _pendingStopAt)
            {
                if (now >= kv.Value)
                {
                    done ??= new List<ulong>(4);
                    done.Add(kv.Key);
                }
            }

            if (done == null) return;
            for (int i = 0; i < done.Count; i++)
            {
                ulong handle = done[i];
                _pendingStopAt.Remove(handle);
                if (_handles.TryGet(handle, out var voice))
                {
                    voice.Playing = false;
                    _bridge?.StopInstance(voice.InstanceId, true);
                }

                ReleaseHandle(handle);
            }
        }

        private void ReleaseFinishedOneshots()
        {
            List<ulong> dead = null;
            foreach (var kv in _handles.Map)
            {
                var voice = kv.Value;
                // 暂停中的 oneshot 不算播完（否则 Pause 后下一帧即被误回收）
                if (!voice.Playing || voice.Paused || voice.Loop) continue;
                if (_bridge != null && _bridge.IsPlaying(voice.InstanceId)) continue;
                dead ??= new List<ulong>(4);
                dead.Add(kv.Key);
            }

            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++)
            {
                ReleaseHandle(dead[i]);
            }
        }

        /// <inheritdoc />
        public override void Restart()
        {
            StopAll(0f);
            CleanAudioPool();
            _fades.Clear();
            _pendingStopAt.Clear();
            _handles.Clear();
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 播放音频 [PLAY AUDIO]

        /// <inheritdoc />
        public override ulong Play(AudioClip clip, AudioPlayOptions options)
            => PlayWithRequest(clip, options.ToRequest(), AudioPlayColdParams.FromOptions(options));

        /// <inheritdoc />
        public override ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold)
            => PlayWithRequest(clip, request, cold);

        /// <inheritdoc />
        public override ulong Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false)
            => PlayEventPath(path, options.ToRequest(), AudioPlayColdParams.FromOptions(options));

        /// <summary>
        /// 按事件路径 + 16B 热请求播放（中间件推荐入口）。
        /// </summary>
        public ulong Play(string eventPath, in AudioPlayRequest request, AudioPlayColdParams cold)
            => PlayEventPath(eventPath, request, cold);

        private ulong PlayWithRequest(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold)
            => PlayEventPath(_bridge?.GetEventPathFromClip(clip), request, cold);

        private ulong PlayEventPath(string eventPath, in AudioPlayRequest request, AudioPlayColdParams cold)
        {
            if (_backendFailed || _bridge == null || string.IsNullOrEmpty(eventPath))
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
            if (instanceId == 0UL) return 0UL;

            ulong handle = _handles.NextHandle();

            var voice = new Voice
            {
                Handle = handle,
                InstanceId = instanceId,
                UserId = request.Id,
                EventPath = eventPath,
                CurrentVolume = request.FadeInOnPlay ? fadeInFrom : request.Volume,
                Loop = request.Loop,
                Persistent = request.Persistent,
                Priority = request.Priority,
                Track = request.Track,
                Playing = true,
            };

            _handles.Bind(handle, voice);
            _handles.RegisterUser(request.Id, handle);

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
        public override int CurrentlyPlayingCount(AudioClip clip)
        {
            string path = _bridge?.GetEventPathFromClip(clip);
            if (string.IsNullOrEmpty(path)) return 0;

            int count = 0;
            foreach (var kv in _handles.Map)
            {
                if (kv.Value.Playing && kv.Value.EventPath == path) count++;
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

            _handles.Release(handle, out _);
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

            foreach (var kv in _handles.Map)
            {
                if (kv.Value.Track == track) Pause(kv.Key);
            }
        }

        /// <inheritdoc />
        public override void UnpauseTrack(EAudioTrack track)
        {
            EnsureTrackArrays();
            int index = (int)track;
            if (index >= 0 && index < _pausedTracks.Length) _pausedTracks[index] = false;

            foreach (var kv in _handles.Map)
            {
                if (kv.Value.Track == track) Unpause(kv.Key);
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
            List<ulong> toStop = null;
            foreach (var kv in _handles.Map)
            {
                if (kv.Value.Track != track) continue;
                toStop ??= new List<ulong>(8);
                toStop.Add(kv.Key);
            }

            if (toStop == null) return;
            for (int i = 0; i < toStop.Count; i++)
            {
                Stop(toStop[i], fadeoutDuration);
            }
        }

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <inheritdoc />
        public override void PauseAll()
        {
            foreach (var kv in _handles.Map) Pause(kv.Key);
        }

        /// <inheritdoc />
        public override void UnpauseAll()
        {
            foreach (var kv in _handles.Map) Unpause(kv.Key);
        }

        /// <inheritdoc />
        public override void StopAll(float fadeoutDuration = 0f)
        {
            List<ulong> all = null;
            foreach (var kv in _handles.Map)
            {
                all ??= new List<ulong>(_handles.Map.Count);
                all.Add(kv.Key);
            }

            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                Stop(all[i], fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopAllButPersistent(float fadeoutDuration = 0f)
        {
            List<ulong> all = null;
            foreach (var kv in _handles.Map)
            {
                if (kv.Value.Persistent) continue;
                all ??= new List<ulong>(8);
                all.Add(kv.Key);
            }

            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                Stop(all[i], fadeoutDuration);
            }
        }

        /// <inheritdoc />
        public override void StopAllLooping(float fadeoutDuration = 0f)
        {
            List<ulong> all = null;
            foreach (var kv in _handles.Map)
            {
                if (!kv.Value.Loop) continue;
                all ??= new List<ulong>(8);
                all.Add(kv.Key);
            }

            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                Stop(all[i], fadeoutDuration);
            }
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
                if (!string.IsNullOrEmpty(path) && !AssetHandlePool.ContainsKey(path))
                {
                    AssetHandlePool.Add(path, path);
                }
            }
        }

        /// <inheritdoc />
        public override void RemoveClipFromPool(List<string> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                AssetHandlePool.Remove(list[i]);
            }
        }

        /// <inheritdoc />
        public override void CleanAudioPool() => AssetHandlePool.Clear();

        #endregion 资源池 [ASSET POOL]

        #region 事件 [EVENTS]

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
            => StopAllButPersistent(0.2f);

        #endregion 事件 [EVENTS]
    }
}
