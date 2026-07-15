using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace Moirai.Atropos.Audio
{
    public interface IAudioModule
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot { get;}
        
        /// <summary>
        /// 音频混响器。
        /// </summary>
        public AudioMixer AudioMixer { get;}
        
        /// <summary>
        /// 资源句柄池，用于缓存资源系统的已加载音频资源。
        /// </summary>
        public Dictionary<string, AssetHandle> AssetHandlePool  { get; }

        #endregion
        
        #region 音轨状态 [TRACK STATUS]
        
        /// <summary>
        /// 所有音轨。
        /// </summary>
        public AudioCategory[] AudioCategories { get; }
        
        /// <summary>
        /// 主音轨（总音量）音量。
        /// </summary>
        /// <remarks>0-1</remarks>
        public float MasterVolume { get; set; }
        
        /// <summary>
        /// 主音轨（总音量）静音。
        /// </summary>
        public bool MasterMute { get; set; }

        /// <summary>
        /// 写入主音轨（总音量）配置。
        /// </summary>
        public void SetMasterSettings();
        
        /// <summary>
        /// 加载主音轨（总音量）配置。
        /// </summary>
        public void LoadMasterSettings();
        
        /// <summary>
        /// 移除主音轨（总音量）设置。
        /// </summary>
        public void RemoveMasterSetting();
        
        /// <summary>
        /// 获取指定音轨的音量。
        /// </summary>
        /// <param name="track"></param>
        /// <returns></returns>
        public float GetTrackVolume(AudioTrack track);

        /// <summary>
        /// 设置指定音轨的音量。
        /// </summary>
        /// <param name="track"></param>
        /// <param name="volume"></param>
        public void SetTrackVolume(AudioTrack track, float volume);

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        /// <param name="track"></param>
        /// <returns></returns>
        public bool GetTrackMute(AudioTrack track);

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        /// <param name="track"></param>
        /// <param name="mute"></param>
        public void SetTrackMute(AudioTrack track, bool mute);
        
        #endregion 音轨状态 [TRACK STATUS]

        #region 模块方法 [MODULE METHOD]

        /// <summary>
        /// 初始化音频模块。
        /// </summary>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <exception cref="GameException"></exception>
        public void Initialize(Transform instanceRoot = null, AudioMixer audioMixer = null, AudioGroupConfig[] audioGroupConfigs = null);

        /// <summary>
        /// 重启音频模块。
        /// </summary>
        public void Restart();

        #endregion 模块方法 [MODULE METHOD]
        
        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <remarks>如果超过最大发声数，且 <see cref="AudioPlayOptions.DoNotAutoRecycleIfNotDonePlaying"/> 为<c>false</c>采用fadeout的方式复用最久播放的AudioSource。</remarks>
        /// <param name="clip">音频剪辑</param>
        /// <param name="options">音频播放选项设置</param>
        /// <returns></returns>
        public AudioAgent Play(AudioClip clip, AudioPlayOptions options);

        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <remarks>如果超过最大发声数，且 <see cref="AudioPlayOptions.DoNotAutoRecycleIfNotDonePlaying"/> 为<c>false</c>采用fadeout的方式复用最久播放的AudioSource。</remarks>
        /// <param name="clip">音频剪辑</param>
        /// <param name="track">音频类型</param>
        /// <param name="location">播放音频的位置</param>
        /// <param name="loop">音频是否循环</param>
        /// <param name="volume">播放音频的音量（0-1.0）</param>
        /// <param name="id">音频的 ID，用于之后再次找到该音频，eg：sound control</param>
        /// <param name="fade">播放音频时是否淡入</param>
        /// <param name="fadeInitialVolume">音频淡入之前的初始音量</param>
        /// <param name="fadeDuration">音频淡入的持续时间（以秒为单位）</param>
        /// <param name="fadeTween">音频淡入的补间动画</param>
        /// <param name="persistent">音频是否应该在场景转换中持续存在</param>
        /// <param name="recycleAudioSource">如果不想从音频系统的音频池中选择，则使用此处的 AudioSource</param>
        /// <param name="audioGroup">如果不想在任意预设音轨上播放，则在此音频组上播放音频</param>
        /// <param name="pitch">音调，音调。播放音频时速度的变化量，默认值1，表示正常的播放速度。（当&lt;1时，慢速播放；当&gt;1时，快速播放。速度越快，音调越高。）</param>
        /// <param name="panStereo">声像。以立体声方式（左或右）平移音频。这仅适用于单声道或立体声的音频</param>
        /// <param name="spatialBlend">AudioSource 受 3D 空间化计算（衰减、多普勒等）影响的程度。0.0 使音频全 2D，1.0 使其全 3D</param>
        /// <param name="soloSingleTrack">AudioSource 是否应在其目标音轨上以 Solo 模式播放。如果是，则当该音频开始播放时，该音轨上的所有其他音频将被静音</param>
        /// <param name="soloAllTracks">AudioSource 是否应在所有其他音轨上以 Solo 模式播放。如果是，则当此音频开始播放时，所有其他音轨都将静音</param>
        /// <param name="autoUnSoloOnEnd">如果在 Solo 独奏模式下，AutoUnSoloOnEnd 将在音频停止播放后自动取消静音</param>
        /// <param name="bypassEffects">音源滤波开关，是否打开音频特效（从滤波器 filter 组件或全局监听器滤波器 listener filter 应用）</param>
        /// <param name="bypassListenerEffects">在 AudioListener 上设置全局效果时，不会将其应用于 AudioSource 生成的音频信号。如果 AudioSource 正在播放到混音器组，则不适用</param>
        /// <param name="bypassReverbZones">不将来自 AudioSource 的信号发送到与混响区域关联的全局混响中</param>
        /// <param name="priority">当播放的 AudioSource 数量多于可用硬件声道数时，Unity 将对 AudioSource 进行虚拟化处理。先对优先级（和可听度）最低的 AudioSource 进行虚拟化处理。</param>
        /// <param name="reverbZoneMix">将 AudioSource 的信号混合到与混响区域相关联的全局混响中的量</param>
        /// <param name="dopplerLevel">AudioSource 的多普勒比例</param>
        /// <param name="spread">扬声器空间中 3d 立体声或多声道音频的传播角度（以度为单位）</param>
        /// <param name="rolloffMode">AudioSource 如何随距离衰减</param>
        /// <param name="minDistance">AudioSource 的音量停止增大的最小距离</param>
        /// <param name="maxDistance">（对数衰减）音频停止衰减的最大距离</param>
        /// <param name="doNotAutoRecycleIfNotDonePlaying">即使没有可用的 agent，也不会打断当前正在播放的音频</param>
        /// <param name="playbackTime">开始播放音频的时间（以秒为单位），相当于 AudioSource API 的 Time</param>
        /// <param name="playbackDuration">"播放音频的持续时间（以秒为单位）</param>
        /// <param name="attachToTransform">播放时跟随该 Transform</param>
        /// <param name="useSpreadCurve">使用自定义扩散曲线</param>
        /// <param name="spreadCurve">自定义扩散的曲线</param>
        /// <param name="useCustomRolloffCurve">自定义音量衰减曲线</param>
        /// <param name="customRolloffCurve">AudioSource 的音量如何随与 AudioListener 的距离变化而衰减</param>
        /// <param name="useSpatialBlendCurve">使用自定义空间混合曲线</param>
        /// <param name="spatialBlendCurve">自定义空间混合的曲线</param>
        /// <param name="useReverbZoneMixCurve">使用自定义混响区域混音曲线</param>
        /// <param name="reverbZoneMixCurve">自定义混响区域混音曲线</param>
        /// <param name="initialDelay"></param>
        public AudioAgent Play(AudioClip clip, AudioTrack track, Vector3 location,
            bool loop = false, float volume = 1.0f, int id = 0,
            bool fade = false, float fadeInitialVolume = 0f, float fadeDuration = 1f, Tween fadeTween = null,
            bool persistent = false,
            AudioSource recycleAudioSource = null, AudioMixerGroup audioGroup = null,
            float pitch = 1f, float panStereo = 0f, float spatialBlend = 0.0f,  
            bool soloSingleTrack = false, bool soloAllTracks = false, bool autoUnSoloOnEnd = false,  
            bool bypassEffects = false, bool bypassListenerEffects = false, bool bypassReverbZones = false, int priority = 128, float reverbZoneMix = 1f,
            float dopplerLevel = 1f, int spread = 0, AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic, float minDistance = 1f, float maxDistance = 500f,
            bool doNotAutoRecycleIfNotDonePlaying = false, float playbackTime = 0f, float playbackDuration = 0f, Transform attachToTransform = null,
            bool useSpreadCurve = false, AnimationCurve spreadCurve = null, bool useCustomRolloffCurve = false, AnimationCurve customRolloffCurve = null,
            bool useSpatialBlendCurve = false, AnimationCurve spatialBlendCurve = null, bool useReverbZoneMixCurve = false, AnimationCurve reverbZoneMixCurve = null,
            float initialDelay = 0f
            );

        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <remarks>如果超过最大发声数，且 <see cref="AudioPlayOptions.DoNotAutoRecycleIfNotDonePlaying"/> 为<c>false</c>采用fadeout的方式复用最久播放的AudioSource。</remarks>
        /// <param name="path">音频文件路径</param>
        /// <param name="options">音频播放选项设置</param>
        /// <param name="bAsync">是否异步加载</param>
        /// <param name="bInPool">是否缓存已加载资源（适用于多次重复加载的资源）。</param>
        /// <returns></returns>
        public AudioAgent Play(string path, AudioPlayOptions options, bool bAsync = false, bool bInPool = false);

        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <remarks>如果超过最大发声数，且 <see cref="AudioPlayOptions.DoNotAutoRecycleIfNotDonePlaying"/> 为<c>false</c>采用fadeout的方式复用最久播放的AudioSource。</remarks>
        /// <param name="path">音频文件路径</param>
        /// <param name="track">音频类型</param>
        /// <param name="location">播放音频的位置</param>
        /// <param name="bAsync">是否异步加载</param>
        /// <param name="bInPool">是否缓存已加载资源（适用于多次重复加载的资源）。</param>
        /// <param name="loop">音频是否循环</param>
        /// <param name="volume">播放音频的音量（0-1.0）</param>
        /// <param name="id">音频的 ID，用于之后再次找到该音频，eg：sound control</param>
        /// <param name="fade">播放音频时是否淡入</param>
        /// <param name="fadeInitialVolume">音频淡入之前的初始音量</param>
        /// <param name="fadeDuration">音频淡入的持续时间（以秒为单位）</param>
        /// <param name="fadeTween">音频淡入的补间动画</param>
        /// <param name="persistent">音频是否应该在场景转换中持续存在</param>
        /// <param name="recycleAudioSource">如果不想从音频系统的音频池中选择，则使用此处的 AudioSource</param>
        /// <param name="audioGroup">如果不想在任意预设音轨上播放，则在此音频组上播放音频</param>
        /// <param name="pitch">音调，音调。播放音频时速度的变化量，默认值1，表示正常的播放速度。（当&lt;1时，慢速播放；当&gt;1时，快速播放。速度越快，音调越高。）</param>
        /// <param name="panStereo">声像。以立体声方式（左或右）平移音频。这仅适用于单声道或立体声的音频</param>
        /// <param name="spatialBlend">AudioSource 受 3D 空间化计算（衰减、多普勒等）影响的程度。0.0 使音频全 2D，1.0 使其全 3D</param>
        /// <param name="soloSingleTrack">AudioSource 是否应在其目标音轨上以 Solo 模式播放。如果是，则当该音频开始播放时，该音轨上的所有其他音频将被静音</param>
        /// <param name="soloAllTracks">AudioSource 是否应在所有其他音轨上以 Solo 模式播放。如果是，则当此音频开始播放时，所有其他音轨都将静音</param>
        /// <param name="autoUnSoloOnEnd">如果在 Solo 独奏模式下，AutoUnSoloOnEnd 将在音频停止播放后自动取消静音</param>
        /// <param name="bypassEffects">音源滤波开关，是否打开音频特效（从滤波器 filter 组件或全局监听器滤波器 listener filter 应用）</param>
        /// <param name="bypassListenerEffects">在 AudioListener 上设置全局效果时，不会将其应用于 AudioSource 生成的音频信号。如果 AudioSource 正在播放到混音器组，则不适用</param>
        /// <param name="bypassReverbZones">不将来自 AudioSource 的信号发送到与混响区域关联的全局混响中</param>
        /// <param name="priority">当播放的 AudioSource 数量多于可用硬件声道数时，Unity 将对 AudioSource 进行虚拟化处理。先对优先级（和可听度）最低的 AudioSource 进行虚拟化处理。</param>
        /// <param name="reverbZoneMix">将 AudioSource 的信号混合到与混响区域相关联的全局混响中的量</param>
        /// <param name="dopplerLevel">AudioSource 的多普勒比例</param>
        /// <param name="spread">扬声器空间中 3d 立体声或多声道音频的传播角度（以度为单位）</param>
        /// <param name="rolloffMode">AudioSource 如何随距离衰减</param>
        /// <param name="minDistance">AudioSource 的音量停止增大的最小距离</param>
        /// <param name="maxDistance">（对数衰减）音频停止衰减的最大距离</param>
        /// <param name="doNotAutoRecycleIfNotDonePlaying">即使没有可用的 agent，也不会打断当前正在播放的音频</param>
        /// <param name="playbackTime">开始播放音频的时间（以秒为单位），相当于 AudioSource API 的 Time</param>
        /// <param name="playbackDuration">"播放音频的持续时间（以秒为单位）</param>
        /// <param name="attachToTransform">播放时跟随该 Transform</param>
        /// <param name="useSpreadCurve">使用自定义扩散曲线</param>
        /// <param name="spreadCurve">自定义扩散的曲线</param>
        /// <param name="useCustomRolloffCurve">自定义音量衰减曲线</param>
        /// <param name="customRolloffCurve">AudioSource 的音量如何随与 AudioListener 的距离变化而衰减</param>
        /// <param name="useSpatialBlendCurve">使用自定义空间混合曲线</param>
        /// <param name="spatialBlendCurve">自定义空间混合的曲线</param>
        /// <param name="useReverbZoneMixCurve">使用自定义混响区域混音曲线</param>
        /// <param name="reverbZoneMixCurve">自定义混响区域混音曲线</param>
        /// <param name="initialDelay"></param>
        public AudioAgent Play(string path, AudioTrack track, Vector3 location, bool bAsync = false, bool bInPool = false,
            bool loop = false, float volume = 1.0f, int id = 0,
            bool fade = false, float fadeInitialVolume = 0f, float fadeDuration = 1f, Tween fadeTween = null,
            bool persistent = false,
            AudioSource recycleAudioSource = null, AudioMixerGroup audioGroup = null,
            float pitch = 1f, float panStereo = 0f, float spatialBlend = 0.0f,  
            bool soloSingleTrack = false, bool soloAllTracks = false, bool autoUnSoloOnEnd = false,  
            bool bypassEffects = false, bool bypassListenerEffects = false, bool bypassReverbZones = false, int priority = 128, float reverbZoneMix = 1f,
            float dopplerLevel = 1f, int spread = 0, AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic, float minDistance = 1f, float maxDistance = 500f,
            bool doNotAutoRecycleIfNotDonePlaying = false, float playbackTime = 0f, float playbackDuration = 0f, Transform attachToTransform = null,
            bool useSpreadCurve = false, AnimationCurve spreadCurve = null, bool useCustomRolloffCurve = false, AnimationCurve customRolloffCurve = null,
            bool useSpatialBlendCurve = false, AnimationCurve spatialBlendCurve = null, bool useReverbZoneMixCurve = false, AnimationCurve reverbZoneMixCurve = null,
            float initialDelay = 0f);

        #endregion 播放音频 [PLAY AUDIO]
        
        #region 音频控制 [AUDIO CONTROLS]
        
        /// <summary>
        /// 暂停指定的音频源
        /// </summary>
        /// <param name="id"></param>
        public void Pause(int id);

        /// <summary>
        /// 恢复播放指定的音频源
        /// </summary>
        /// <param name="id"></param>
        public void UnPause(int id);

        /// <summary>
        /// 停止指定的音频源
        /// </summary>
        /// <param name="id"></param>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void Stop(int id, float fadeoutDuration = 0f);
        
        #endregion 音频控制 [AUDIO CONTROLS]
        
        #region 获取 [FIND]

        /// <summary>
        /// 返回播放过指定 ID 的音频代理
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public AudioAgent[] FindAgentsByID(int id);
        
        /// <summary>
        /// 返回播放过指定 clip 的音频代理
        /// </summary>
        /// <param name="clip"></param>
        /// <returns></returns>
        public AudioAgent[] FindAgentsByClip(AudioClip clip);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量
        /// </summary>
        /// <param name="clip"></param>
        /// <returns></returns>
        public int CurrentlyPlayingCount(AudioClip clip);
        
        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        /// <param name="track">音频类型。</param>
        public void Pause(AudioTrack track);
        
        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        /// <param name="track">音频类型。</param>
        public void UnPause(AudioTrack track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        /// <param name="track"></param>
        /// <returns></returns>
        public bool IsPaused(AudioTrack track);

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        /// <param name="track">音频类型。</param>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void Stop(AudioTrack track, float fadeoutDuration = 0f);
       
        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]
        
        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public void PauseAll();
        
        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public void UnPauseAll();
        
        /// <summary>
        /// 停止所有音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAll(float fadeoutDuration = 0f);
        
        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAllButPersistent(float fadeoutDuration = 0f);
        
        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAllLooping(float fadeoutDuration = 0f);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]
        
        #region 过渡 [FADES]

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量
        /// </summary>
        /// <param name="duration"></param>
        /// <param name="initialVolume"></param>
        /// <param name="finalVolume"></param>
        /// <param name="tween"></param>
        public void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, Tween tween = null);

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）
        /// </summary>
        public void StopFadeMasterTrack();

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量
        /// </summary>
        /// <param name="track"></param>
        /// <param name="duration"></param>
        /// <param name="initialVolume"></param>
        /// <param name="finalVolume"></param>
        /// <param name="tween"></param>
        public void FadeTrack(AudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, Tween tween = null);

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）
        /// </summary>
        /// <param name="track"></param>        
        public void StopFadeTrack(AudioTrack track);
        
        /// <summary>
        /// 在指定的持续时间内，将目标声音过渡到指定音量
        /// </summary>
        /// <param name="source"></param>
        /// <param name="duration"></param>
        /// <param name="initialVolume"></param>
        /// <param name="finalVolume"></param>
        /// <param name="tween"></param>
        public void FadeAudio(AudioSource source, float duration, float initialVolume, float finalVolume, Tween tween);

        /// <summary>
        /// 停止指定音频源上所有当前的淡化（Fade）
        /// </summary>
        /// <param name="source"></param>
        public void StopFadeAudio(AudioSource source);
        
        /// <summary>
        /// 如果指定的源已经在过渡中，则返回 <c>true</c>，否则返回 <c>false</c>
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public bool SoundIsFadingOut(AudioSource source);
        
        #endregion 过渡 [FADES]
        
        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        /// <param name="list">AudioClip 的 AssetPath 集合。</param>
        public void PutInAudioPool(List<string> list);

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        /// <param name="list">AudioClip 的 AssetPath 集合。</param>
        public void RemoveClipFromPool(List<string> list);

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        public void CleanAudioPool();
        
        #endregion 资源池 [ASSET POOL]

    }
}