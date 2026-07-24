using Moirai.Atropos.Resource;
using Moirai.Atropos.Schedulers;
using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频代理辅助器。
    /// </summary>
    public class AudioAgent
    {
        private IAudioModule _audioModule;
        private IResourceModule _resourceModule;
        private AudioAssetData _audioAssetData;

        // 当前声源的位置
        private Transform _transform;
        // 是否缓存已加载资源（适用于多次重复加载的资源）。
        private bool _inPool;
        // 音频代理加载请求。
        private LoadRequest _pendingLoad;
        
        // 音频淡入开始时间
        private float _fadeInAt;
        // 音频淡出开始时间
        private float _fadeOutStartTime;
        // 音频淡出默认持续时间
        public const float FADEOUT_DEFAULT_DURATION = 0.2f;
        // 音频淡出持续时间
        private float _fadeOutDuration;

        // 音频的持续时间
        private float _playDuration;
        // 自动取消 Solo 的句柄
        private SchedulerHandle _autoUnSoloOnEnd;
        
        // 音频播放配置
        private AudioPlayOptions _audioPlayOptions;
        // 预设的音频源
        private AudioSource _audioSource;
        // 预设的混音组
        private AudioMixerGroup _audioMixerGroup;
        
        // 音频代理辅助器运行时状态。
        private EAudioAgentRuntimeState _audioAgentRuntimeState = EAudioAgentRuntimeState.None;

        /// <summary>
        /// 音频代理加载请求。
        /// </summary>
        class LoadRequest
        {
            /// <summary>
            /// 音频代理辅助器加载路径。
            /// </summary>
            public string path;
            
            /// <summary>
            /// 是否异步。
            /// </summary>
            public bool bAsync;
            
            /// <summary>
            /// 是否池化。
            /// </summary>
            public bool bInPool;
        }

        #region 公共属性 [PUBLIC PROPRETIES]
        
        /// <summary>
        /// 音频代理辅助器索引。
        /// </summary>
        public int ID => _audioPlayOptions.ID;

        /// <summary>
        /// 资源操作句柄。
        /// </summary>
        public AudioAssetData AudioAssetData => _audioAssetData;
        
        /// <summary>
        /// 音频代理辅助器当前是否空闲。
        /// </summary>
        public bool IsFree => _audioAgentRuntimeState == EAudioAgentRuntimeState.None || _audioAgentRuntimeState == EAudioAgentRuntimeState.End;

        /// <summary>
        /// 音频代理辅助器播放秒数。
        /// </summary>
        public float Duration { get; private set; }
        
        /// <summary>
        /// 音频代理辅助器的当前声源。
        /// </summary>
        /// <returns></returns>
        public AudioSource AudioResource => _audioPlayOptions.RecycleAudioSource == null ? _audioSource : _audioPlayOptions.RecycleAudioSource;

        /// <summary>
        /// 音频代理辅助器当前音频长度。
        /// </summary>
        public float Length
        {
            get
            {
                if (AudioResource != null && AudioResource.clip != null)
                {
                    return AudioResource.clip.length;
                }

                return 0;
            }
        }

        /// <summary>
        /// 音频代理辅助器实例位置。
        /// </summary>
        public Vector3 Position
        {
            get => _transform.position;
            set => _transform.position = value;
        }
        
        /// <summary>
        /// 音频代理辅助器是否正在播放。
        /// </summary>
        internal bool IsPlaying => AudioResource != null && AudioResource.isPlaying;
        
        /// <summary>
        /// 音频代理辅助器是否正在暂停。
        /// </summary>
        internal bool IsPaused => AudioResource != null && _audioAgentRuntimeState == EAudioAgentRuntimeState.Pausing;
        
        /// <summary>
        /// 音频代理辅助器是否循环。
        /// </summary>
        internal bool IsLoop => AudioResource != null && AudioResource.loop;
      
        /// <summary>
        /// 音频代理辅助器是否持久性。
        /// </summary>
        internal bool IsPersistent => _audioPlayOptions.Persistent;

        /// <summary>
        /// 音频代理辅助器的输出混音组
        /// </summary>
        public AudioMixerGroup OutputAudioMixerGroup => _audioPlayOptions.AudioGroup == null ? _audioMixerGroup : _audioPlayOptions.AudioGroup;

        #endregion
        
        #region 模块方法 [MODULE METHOD]

        /// <summary>
        /// 初始化音频代理辅助器。
        /// </summary>
        /// <param name="audioCategory">音频轨道（类别）。</param>
        /// <param name="index">音频代理辅助器编号。</param>
        public void Init(AudioCategory audioCategory, int index = 0)
        {
            _audioModule = ModuleSystem.GetModule<IAudioModule>();
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            GameObject host = new GameObject(StringUtility.Format("{0} - {1}", audioCategory.AudioMixerGroup.name, index));
            host.transform.SetParent(audioCategory.InstanceRoot);
            host.transform.localPosition = Vector3.zero;
            _transform = host.transform;
            _audioSource = host.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            if (audioCategory.AudioMixerGroup == null)
            {
                // 如果不指定通用，则使用预配置音频组，命名方式如下：
                // Master
                //  - Voice
                //      - Voice - 0
                //      - Voice - 1
                //  - Sfx
                //      - Sfx - 0
                AudioMixerGroup[] audioMixerGroups =
                    audioCategory.AudioMixer.FindMatchingGroups(StringUtility.Format("Master/{0}/{1}", audioCategory.AudioMixerGroup.name,
                        $"{audioCategory.AudioMixerGroup.name} - {index}"));
                _audioMixerGroup = audioMixerGroups.Length > 0 ? audioMixerGroups[0] : audioCategory.AudioMixerGroup;
            }
            else
            {
                _audioMixerGroup = audioCategory.AudioMixerGroup;
            }
        }
        
        /// <summary>
        /// 销毁音频代理辅助器。
        /// </summary>
        public void Destroy()
        {
            if (_transform != null)
            {
                Object.Destroy(_transform.gameObject);
            }

            if (_audioAssetData != null)
            {
                AudioAssetData.DeAlloc(_audioAssetData);
            }
        }
        
        /// <summary>
        /// 轮询音频代理辅助器。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        public void Update(float elapseSeconds)
        {
            if (_audioAgentRuntimeState == EAudioAgentRuntimeState.Playing || _audioAgentRuntimeState == EAudioAgentRuntimeState.FadingIn)
            {
                if (!_audioPlayOptions.Loop && Duration >= _playDuration)
                {
                    Stop(FADEOUT_DEFAULT_DURATION);
                }
                else if (_audioAgentRuntimeState == EAudioAgentRuntimeState.FadingIn) // 淡入音频
                {
                    float endTime = _fadeInAt + _audioPlayOptions.FadeInDuration;
                    if (GameTime.unscaledTime <= endTime)
                    {
                        AudioResource.volume = EaseUtility.Tween(GameTime.unscaledTime, _fadeInAt, endTime, _audioPlayOptions.FadeInInitialVolume, _audioPlayOptions.Volume, _audioPlayOptions.FadeInTweenEase);
                    }
                    else
                    {
                        AudioResource.volume =_audioPlayOptions.Volume;
                        _audioAgentRuntimeState = EAudioAgentRuntimeState.Playing;
                    }
                }
                
                // 跟随目标
                if (_audioPlayOptions.AttachToTransform != null)
                {
                    AudioResource.transform.Translate(_audioPlayOptions.AttachToTransform.position, Space.World);
                }
                
                Duration += elapseSeconds;
            }
            else if (_audioAgentRuntimeState == EAudioAgentRuntimeState.FadingOut)
            {
                float elapsed = GameTime.unscaledTime - _fadeOutStartTime;
                if (elapsed >= _fadeOutDuration)
                {
                    Stop();
                    if (_pendingLoad != null)
                    {
                        string path = _pendingLoad.path;
                        bool bAsync = _pendingLoad.bAsync;
                        bool bInPool = _pendingLoad.bInPool;
                        _pendingLoad = null;
                        Load(path, _audioPlayOptions, bAsync, bInPool);
                    }
                }
                else
                {
                    AudioResource.volume = _audioPlayOptions.Volume * (1f - elapsed / _fadeOutDuration);
                }
            }
        }
        
        #endregion 模块方法 [MODULE METHOD]

        #region 音频控制 [AUDIO CONTROLS]
        
        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <remarks>如果超过最大发声数，且 <see cref="AudioPlayOptions.DoNotAutoRecycleIfNotDonePlaying"/> 为<c>false</c>采用fadeout的方式复用最久播放的AudioSource。</remarks>
        /// <param name="clip">音频剪辑</param>
        /// <param name="options">音频播放选项设置</param>
        /// <returns></returns>
        public void Play(AudioClip clip, AudioPlayOptions options)
        {
            _audioPlayOptions = options;
            HandleAudioPlay(clip);
        }

        /// <summary>
        /// 处理音频播放。
        /// </summary>
        /// <param name="clip"></param>
        /// <remarks>注意 bug https://github.com/tuyoogame/YooAsset/issues/225#event-11355675066</remarks>
        private void HandleAudioPlay(AudioClip clip)
        {
            if (clip != null)
            {
                // 音频源设置
                AudioResource.clip = clip;

                AudioResource.pitch = _audioPlayOptions.Pitch;
                AudioResource.spatialBlend = _audioPlayOptions.SpatialBlend;
                AudioResource.panStereo = _audioPlayOptions.PanStereo;
                AudioResource.loop = _audioPlayOptions.Loop;
                AudioResource.bypassEffects = _audioPlayOptions.BypassEffects;
                AudioResource.bypassListenerEffects = _audioPlayOptions.BypassListenerEffects;
                AudioResource.bypassReverbZones = _audioPlayOptions.BypassReverbZones;
                AudioResource.priority = _audioPlayOptions.Priority;
                AudioResource.reverbZoneMix = _audioPlayOptions.ReverbZoneMix;
                AudioResource.dopplerLevel = _audioPlayOptions.DopplerLevel;
                AudioResource.spread = _audioPlayOptions.Spread;
                AudioResource.rolloffMode = _audioPlayOptions.RolloffMode;
                AudioResource.minDistance = _audioPlayOptions.MinDistance;
                AudioResource.maxDistance = _audioPlayOptions.MaxDistance;
                if (AudioResource.clip != null)
                {
                    AudioResource.time = _audioPlayOptions.PlaybackTime;
                }
                
                // 曲线
                if (_audioPlayOptions.UseSpreadCurve) { AudioResource.SetCustomCurve(AudioSourceCurveType.Spread, _audioPlayOptions.SpreadCurve); }
                if (_audioPlayOptions.UseCustomRolloffCurve) { AudioResource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _audioPlayOptions.CustomRolloffCurve); }
                if (_audioPlayOptions.UseSpatialBlendCurve) { AudioResource.SetCustomCurve(AudioSourceCurveType.SpatialBlend, _audioPlayOptions.SpatialBlendCurve); }
                if (_audioPlayOptions.UseReverbZoneMixCurve) { AudioResource.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, _audioPlayOptions.ReverbZoneMixCurve); }
                
                // 位置
                AudioResource.transform.position = _audioPlayOptions.Location;
                
                // 输出混音组
                AudioResource.outputAudioMixerGroup = OutputAudioMixerGroup;
                
                // 音量
                _fadeInAt = GameTime.unscaledTime;
                AudioResource.volume = _audioPlayOptions.FadeInOnPlay ? _audioPlayOptions.FadeInInitialVolume : _audioPlayOptions.Volume;
                
                // 取消任务
                if (_autoUnSoloOnEnd != default) { _autoUnSoloOnEnd.Cancel(); }
                AudioResource.mute = false;
                
                // 开始播放音频
                if (_audioPlayOptions.InitialDelay > 0f)
                {
#if UNITY_6000_0_OR_NEWER
                    AudioResource.PlayDelayed(_audioPlayOptions.InitialDelay);
#else
                    // 相对于 44.1 kHz 参考速率的样本中指定的延迟
                    AudioResource.Play((ulong)_audioPlayOptions.InitialDelay * 44100);
#endif
                }
                else
                {
                    AudioResource.Play();
                }

                _audioAgentRuntimeState = _audioPlayOptions.FadeInOnPlay ? EAudioAgentRuntimeState.FadingIn : EAudioAgentRuntimeState.Playing;
                Duration = 0;
                // Debug.Log($"{clip.name}: {AudioResource.volume}");
                
                // 处理独奏
                _playDuration = (_audioPlayOptions.PlaybackDuration == 0 && AudioResource.clip != null ? AudioResource.clip.length : _audioPlayOptions.PlaybackDuration) - _audioPlayOptions.PlaybackTime;
                _autoUnSoloOnEnd = default;
                if (_audioPlayOptions.SoloSingleTrack)
                {
                    MuteAudiosOnTrack(_audioPlayOptions.AudioTrack, true);
                    AudioResource.mute = false;
                    if (_audioPlayOptions.AutoUnSoloOnEnd)
                    {
                        _autoUnSoloOnEnd = Scheduler.Delay(_playDuration, () => MuteAudiosOnTrack(_audioPlayOptions.AudioTrack, false));
                    }
                }
                else if (_audioPlayOptions.SoloAllTracks)
                {
                    MuteAllAudios(true);
                    AudioResource.mute = false;
                    if (_audioPlayOptions.AutoUnSoloOnEnd)
                    {
                        _autoUnSoloOnEnd = Scheduler.Delay(_playDuration, () => MuteAllAudios(false));
                    }
                }
            }
            else
            {
                _audioAgentRuntimeState = EAudioAgentRuntimeState.End;
            }
        }

        /// <summary>
        /// 加载音频代理辅助器。
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="options">音频播放选项设置。</param>
        /// <param name="bAsync">是否异步加载。</param>
        /// <param name="bInPool">是否缓存已加载资源（适用于多次重复加载的资源）。</param>
        public void Load(string path, AudioPlayOptions options, bool bAsync, bool bInPool = false)
        {
            _audioPlayOptions = options;
            _inPool = bInPool;
            
            if (_audioAgentRuntimeState == EAudioAgentRuntimeState.None || _audioAgentRuntimeState == EAudioAgentRuntimeState.End)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    if (bInPool && _audioModule.AssetHandlePool.TryGetValue(path, out var operationHandle))
                    {
                        OnAssetLoadComplete(operationHandle);
                        return;
                    }

                    if (bAsync)
                    {
                        _audioAgentRuntimeState = EAudioAgentRuntimeState.Loading;
                        AssetHandle handle = _resourceModule.LoadAssetAsyncHandle<AudioClip>(path);
                        handle.Completed += OnAssetLoadComplete;
                    }
                    else
                    {
                        AssetHandle handle = _resourceModule.LoadAssetSyncHandle<AudioClip>(path);
                        OnAssetLoadComplete(handle);
                    }
                }
            }
            else
            {
                _pendingLoad = new LoadRequest { path = path, bAsync = bAsync, bInPool = bInPool };

                if (_audioAgentRuntimeState == EAudioAgentRuntimeState.Playing || _audioAgentRuntimeState == EAudioAgentRuntimeState.FadingIn)
                {
                    Stop(fadeoutDuration:FADEOUT_DEFAULT_DURATION);
                }
            }
        }
        
        /// <summary>
        /// 资源加载完成。
        /// </summary>
        /// <param name="handle">资源操作句柄。</param>
        private void OnAssetLoadComplete(AssetHandle handle)
        {
            if (handle != null)
            {
                if (_inPool)
                {
                    _audioModule.AssetHandlePool.TryAdd(handle.GetAssetInfo().Address, handle);
                }
            }

            if (_pendingLoad != null)
            {
                if (!_inPool && handle != null)
                {
                    handle.Dispose();
                }

                _audioAgentRuntimeState = EAudioAgentRuntimeState.End;
                string path = _pendingLoad.path;
                bool bAsync = _pendingLoad.bAsync;
                bool bInPool = _pendingLoad.bInPool;
                _pendingLoad = null;
                Load(path, _audioPlayOptions, bAsync, bInPool);
            }
            else if (handle != null)
            {
                if (_audioAssetData != null)
                {
                    AudioAssetData.DeAlloc(_audioAssetData);
                    _audioAssetData = null;
                }
                
                _audioAssetData = AudioAssetData.Alloc(handle, _inPool);
                
                HandleAudioPlay(handle.AssetObject as AudioClip);
            }
            else
            {
                _audioAgentRuntimeState = EAudioAgentRuntimeState.End;
            }
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
                AudioResource.Stop();
                _audioAgentRuntimeState = EAudioAgentRuntimeState.End;
                
                // 取消 autoUnSoloOnEnd 的任务 
                if (_autoUnSoloOnEnd != default) { _autoUnSoloOnEnd.Cancel(); }
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
        /// 取消淡入
        /// </summary>
        public void CancelFadeIn()
        {
            if (_audioAgentRuntimeState != EAudioAgentRuntimeState.FadingIn) return;

            _audioAgentRuntimeState = EAudioAgentRuntimeState.Playing;
        }
        
        #endregion 音频控制 [AUDIO CONTROLS]
        
        #region 独奏 [SOLO]

        /// <summary>
        /// 将当前音轨上的所有声音静音（非主动设置）
        /// </summary>
        /// <param name="track"></param>
        /// <param name="mute"></param>
        private void MuteAudiosOnTrack(EAudioTrack track, bool mute)
        {
            foreach (var category in GameModule.Audio.AudioCategories)
            {
                if (category.AudioTrack != track) continue;
                
                foreach (var agent in category.AudioAgents)
                {
                    agent.AudioResource.mute = mute;
                }
            }
        }
        
        /// <summary>
        /// 将所有声音静音（非主动设置）
        /// </summary>
        /// <param name="mute"></param>
        private void MuteAllAudios(bool mute)
        {
            foreach (var category in GameModule.Audio.AudioCategories)
            {
                foreach (var agent in category.AudioAgents)
                {
                    agent.AudioResource.mute = mute;
                }
            }
        }

        #endregion 独奏 [SOLO]
    }
}