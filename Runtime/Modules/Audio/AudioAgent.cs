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
    public class AudioAgent : IMemory
    {
        private IAudioModule _audioModule;
        private AudioModule _audioModuleConcrete;
        private IResourceModule _resourceModule;
        private AudioAssetData _audioAssetData;

        // 当前声源的位置
        private Transform _transform;
        // 是否缓存已加载资源（适用于多次重复加载的资源）。
        private bool _inPool;
        // 音频代理加载请求路径。
        private string _pendingPath;
        // 音频代理加载请求是否异步。
        private bool _pendingAsync;
        // 音频代理加载请求是否池化。
        private bool _pendingInPool;
        // 是否有待处理的加载请求。
        private bool _hasPendingLoad;
        
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
        // 自动取消 Solo 的目标音轨
        private EAudioTrack _autoUnSoloTrack;
        // 自动取消 Solo 是否影响所有音轨
        private bool _autoUnSoloIsAllTracks;
        
        // 音频播放配置
        private AudioPlayOptions _audioPlayOptions;
        // 预设的音频源
        private AudioSource _audioSource;
        // 预设的混音组
        private AudioMixerGroup _audioMixerGroup;
        // 所属音轨类别
        private AudioCategory _audioCategory;
        // 代理所属音轨类别的索引
        private int _audioCategoryIndex;
        
        // 音频代理辅助器运行时状态。
        private AudioAgentRuntimeState _audioAgentRuntimeState = AudioAgentRuntimeState.None;

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
        public bool IsFree => _audioAgentRuntimeState == AudioAgentRuntimeState.None || _audioAgentRuntimeState == AudioAgentRuntimeState.End;

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
        internal bool IsPaused => AudioResource != null && _audioAgentRuntimeState == AudioAgentRuntimeState.Pausing;
        
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
        
        #region 内存池 [MEMORY POOL]

        /// <summary>
        /// 清理内存对象回收入池。
        /// </summary>
        public void Clear()
        {
            _audioModule = null;
            _audioModuleConcrete = null;
            _resourceModule = null;
            _audioCategory = null;
            _audioCategoryIndex = 0;
            
            if (_audioAssetData != null)
            {
                AudioAssetData.DeAlloc(_audioAssetData);
                _audioAssetData = null;
            }
            
            if (_transform != null)
            {
                Object.Destroy(_transform.gameObject);
                _transform = null;
            }
            
            _audioSource = null;
            _audioMixerGroup = null;
            _inPool = false;
            _hasPendingLoad = false;
            _pendingPath = null;
            _pendingAsync = false;
            _pendingInPool = false;
            
            _fadeInAt = 0f;
            _fadeOutStartTime = 0f;
            _fadeOutDuration = 0f;
            _playDuration = 0f;
            Duration = 0f;
            
            if (_autoUnSoloOnEnd != default)
            {
                _autoUnSoloOnEnd.Cancel();
                _autoUnSoloOnEnd = default;
            }
            
            _autoUnSoloTrack = EAudioTrack.Sfx;
            _autoUnSoloIsAllTracks = false;
            _audioPlayOptions = default;
            _audioAgentRuntimeState = AudioAgentRuntimeState.None;
        }

        // ===== 零 GC 回调 — 使用 unsafe 函数指针 =====
        private static void AutoUnSolo_Imp(object instance)
        {
            var agent = (AudioAgent)instance;
            if (agent._autoUnSoloIsAllTracks)
                agent.MuteAllAudios(false);
            else
                agent.MuteAudiosOnTrack(agent._autoUnSoloTrack, false);
        }

        /// <summary>
        /// 从内存池中初始化。
        /// </summary>
        public void InitFromPool()
        {
        }

        /// <summary>
        /// 回收到内存池。
        /// </summary>
        public void RecycleToPool()
        {
        }

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
            _audioModuleConcrete = _audioModule as AudioModule;
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            _audioCategory = audioCategory;
            _audioCategoryIndex = index;
            
            GameObject host = new GameObject(StringUtility.Format("{0} - {1}", audioCategory.AudioMixerGroup.name, index));
            host.transform.SetParent(audioCategory.InstanceRoot);
            host.transform.localPosition = Vector3.zero;
            _transform = host.transform;
            _audioSource = host.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioMixerGroup = audioCategory.AudioMixerGroup;
        }
        
        /// <summary>
        /// 销毁音频代理辅助器并重置所有状态。
        /// </summary>
        public void Destroy()
        {
            Clear();
        }
        
        /// <summary>
        /// 轮询音频代理辅助器。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        public void Update(float elapseSeconds)
        {
            if (_audioAgentRuntimeState == AudioAgentRuntimeState.Playing || _audioAgentRuntimeState == AudioAgentRuntimeState.FadingIn)
            {
                if (!_audioPlayOptions.Loop && Duration >= _playDuration)
                {
                    Stop(FADEOUT_DEFAULT_DURATION);
                }
                else if (_audioAgentRuntimeState == AudioAgentRuntimeState.FadingIn) // 淡入音频
                {
                    float endTime = _fadeInAt + _audioPlayOptions.FadeInDuration;
                    if (GameTime.unscaledTime <= endTime)
                    {
                        AudioResource.volume = EaseUtility.Tween(GameTime.unscaledTime, _fadeInAt, endTime, _audioPlayOptions.FadeInInitialVolume, _audioPlayOptions.Volume, _audioPlayOptions.FadeInTweenEase);
                    }
                    else
                    {
                        AudioResource.volume =_audioPlayOptions.Volume;
                        _audioAgentRuntimeState = AudioAgentRuntimeState.Playing;
                    }
                }
                
                // 跟随目标
                if (_audioPlayOptions.AttachToTransform != null)
                {
                    AudioResource.transform.Translate(_audioPlayOptions.AttachToTransform.position, Space.World);
                }
                
                Duration += elapseSeconds;
            }
            else if (_audioAgentRuntimeState == AudioAgentRuntimeState.FadingOut)
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
        private unsafe void HandleAudioPlay(AudioClip clip)
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

                _audioAgentRuntimeState = _audioPlayOptions.FadeInOnPlay ? AudioAgentRuntimeState.FadingIn : AudioAgentRuntimeState.Playing;
                Duration = 0;
                // Debug.Log($"{clip.name}: {AudioResource.volume}");
                
                // 处理独奏
                _playDuration = (_audioPlayOptions.PlaybackDuration == 0 && AudioResource.clip != null ? AudioResource.clip.length : _audioPlayOptions.PlaybackDuration) - _audioPlayOptions.PlaybackTime;
                _autoUnSoloOnEnd = default;
                _autoUnSoloTrack = EAudioTrack.Sfx;
                _autoUnSoloIsAllTracks = false;
                if (_audioPlayOptions.SoloSingleTrack)
                {
                    MuteAudiosOnTrack(_audioPlayOptions.AudioTrack, true);
                    AudioResource.mute = false;
                    if (_audioPlayOptions.AutoUnSoloOnEnd)
                    {
                        _autoUnSoloTrack = _audioPlayOptions.AudioTrack;
                        _autoUnSoloIsAllTracks = false;
                        _autoUnSoloOnEnd = Scheduler.DelayUnsafe(_playDuration, new SchedulerUnsafeBinding(this, &AutoUnSolo_Imp));
                    }
                }
                else if (_audioPlayOptions.SoloAllTracks)
                {
                    MuteAllAudios(true);
                    AudioResource.mute = false;
                    if (_audioPlayOptions.AutoUnSoloOnEnd)
                    {
                        _autoUnSoloIsAllTracks = true;
                        _autoUnSoloOnEnd = Scheduler.DelayUnsafe(_playDuration, new SchedulerUnsafeBinding(this, &AutoUnSolo_Imp));
                    }
                }
            }
            else
            {
                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
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
            
            if (_audioAgentRuntimeState == AudioAgentRuntimeState.None || _audioAgentRuntimeState == AudioAgentRuntimeState.End)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    if (bInPool && _audioModuleConcrete != null && _audioModuleConcrete.TryGetCachedAsset(path, out var operationHandle))
                    {
                        OnAssetLoadComplete(operationHandle);
                        return;
                    }

                    if (bAsync)
                    {
                        _audioAgentRuntimeState = AudioAgentRuntimeState.Loading;
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
                _pendingPath = path;
                _pendingAsync = bAsync;
                _pendingInPool = bInPool;
                _hasPendingLoad = true;

                if (_audioAgentRuntimeState == AudioAgentRuntimeState.Playing || _audioAgentRuntimeState == AudioAgentRuntimeState.FadingIn)
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
                    _audioModuleConcrete.AddCachedAsset(handle.GetAssetInfo().Address, handle);
                }
            }

            if (_hasPendingLoad)
            {
                if (!_inPool && handle != null)
                {
                    handle.Dispose();
                }

                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                string path = _pendingPath;
                bool bAsync = _pendingAsync;
                bool bInPool = _pendingInPool;
                _hasPendingLoad = false;
                _pendingPath = null;
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
                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
            }
        }

        /// <summary>
        /// 停止播放音频代理辅助器。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void Stop(float fadeoutDuration = 0f)
        {
            if (fadeoutDuration > 0f)
            {
                _fadeOutStartTime = GameTime.unscaledTime;
                _fadeOutDuration = fadeoutDuration;
                _audioAgentRuntimeState = AudioAgentRuntimeState.FadingOut;
            }
            else
            {
                AudioResource.Stop();
                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                
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
            
            _audioAgentRuntimeState = AudioAgentRuntimeState.Pausing;
            AudioResource.Pause();
        }

        /// <summary>
        /// 取消暂停音频代理辅助器。
        /// </summary>
        public void UnPause()
        {
            if (_audioAgentRuntimeState != AudioAgentRuntimeState.Pausing) return;
            
            _audioAgentRuntimeState = AudioAgentRuntimeState.Playing;
            AudioResource.UnPause();
        }
        
        /// <summary>
        /// 取消淡入
        /// </summary>
        public void CancelFadeIn()
        {
            if (_audioAgentRuntimeState != AudioAgentRuntimeState.FadingIn) return;

            _audioAgentRuntimeState = AudioAgentRuntimeState.Playing;
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
