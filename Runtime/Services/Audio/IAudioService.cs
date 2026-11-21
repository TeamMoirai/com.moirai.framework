using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频服务契约（策略模式契约接口）：音频后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 默认实现为 <see cref="UnityAudioHandler"/>，中间件家族共享基座为 <see cref="MiddlewareAudioHandler"/>； <br />
    /// 可在 <see cref="AudioServiceSettings"/> 中替换。实现约束：实现类须继承 <see cref="Moirai.Atropos.FrameworkHandler"/> <br />
    /// （生命周期与序列化基座——<c>Internal_Init</c>/<c>Internal_Shutdown</c> 由宿主生成代码强制调用）。 <br />
    /// 跨后端语义约定：暂停音轨拦截新播放（<see cref="Play"/> 返回 0）；<see cref="MasterVolume"/> getter 报未静音的设置值； <br />
    /// 主音量与音轨音量都是线性 <c>0..1</c>、在契约入口夹取一次；句柄生命周期与用户 ID 映射由 <see cref="AudioHandleRegistry{TVoice}"/> 保证； <br />
    /// 后端整体失效时（<see cref="IsBackendInert"/>）音量面读作 0、写作无效。 <br />
    /// 带默认实现（接口默认方法）的成员是"可选能力/共享语义"：后端不覆写时按默认走； <br />
    /// 依赖过渡调度器的六个 Fade 成员与 <see cref="SetVoicePitch"/> 的共享实现收在 <see cref="AudioBackendPolicy"/>（internal，后端一行转发）。 <br />
    /// Unity 专属成员（中间件返回 null/空操作）见各成员 remarks；延迟起播/时长裁剪/独奏三项能力由后端自报。
    /// </remarks>
    public interface IAudioService
    {
        #region 处理器属性 [HANDLER PROPERTIES]

        /// <summary>音频混响器。</summary>
        /// <remarks>Unity 专属；中间件后端返回 null。</remarks>
        AudioMixer AudioMixer { get; }

        /// <summary>实例化根节点。</summary>
        Transform InstanceRoot { get; set; }

        /// <summary>已缓存音频资源的只读视图（后端原生句柄/租约的 object 包装）。</summary>
        /// <remarks>
        /// 条目增删与租约释放由服务内部配对管理（<see cref="PutInAudioPool"/> / <see cref="RemoveClipFromPool"/> /
        /// <see cref="CleanAudioPool"/> 与 Clip 缓存自身的驱逐卸载）；外部摘走一项等于留下一条无人释放的后端引用。
        /// 中间件后端仅作键值占位，不持有真实资源句柄。
        /// </remarks>
        IReadOnlyDictionary<string, object> AssetHandlePool { get; }

        #endregion 处理器属性 [HANDLER PROPERTIES]

        #region 音轨状态 [TRACK STATUS]


        /// <summary>所有音轨。</summary>
        /// <remarks>Unity 专属；中间件后端返回空数组。</remarks>
        AudioCategory[] AudioCategories { get; }

        /// <summary>主音轨（总音量）音量。</summary>
        /// <remarks>线性 <c>0..1</c>（1 = 满刻度）；越界值在 setter 处夹取，getter 报回的就是实际生效值。该值域对 Unity 与中间件后端一致。</remarks>
        float MasterVolume { get; set; }

        /// <summary>主音轨（总音量）静音。</summary>
        bool MasterMute { get; set; }

        /// <summary>
        /// 写入主音轨（总音量）配置。
        /// </summary>
        void SetMasterSettings();

        /// <summary>
        /// 加载主音轨（总音量）配置。
        /// </summary>
        void LoadMasterSettings();

        /// <summary>
        /// 移除主音轨（总音量）设置。
        /// </summary>
        void RemoveMasterSetting();

        /// <summary>
        /// 获取指定音轨的音量（线性 <c>0..1</c>）。
        /// </summary>
        float GetTrackVolume(EAudioTrack track);

        /// <summary>
        /// 设置指定音轨的音量，线性 <c>0..1</c>；越界在入口夹取，不静默改变语义。
        /// </summary>
        void SetTrackVolume(EAudioTrack track, float volume);

        /// <summary>
        /// 获取指定音轨的音高倍率（运行期，默认 1）。
        /// </summary>
        float GetTrackPitch(EAudioTrack track);

        /// <summary>
        /// 设置指定音轨的音高倍率，并让该音轨上正在播放的声部按新倍率重算音高。
        /// </summary>
        void SetTrackPitch(EAudioTrack track, float pitch);

        /// <summary>
        /// 获取指定音轨的静音状态。
        /// </summary>
        bool GetTrackMute(EAudioTrack track);

        /// <summary>
        /// 设置指定音轨的静音状态。
        /// </summary>
        void SetTrackMute(EAudioTrack track, bool mute);

        #endregion 音轨状态 [TRACK STATUS]

        #region 后端能力 [BACKEND CAPABILITIES]




        #endregion 后端能力 [BACKEND CAPABILITIES]

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 容器 Tick 驱动——轮询音轨与手动过渡。
        /// </summary>
        void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 重启音频服务。
        /// </summary>
        void Restart();




        #endregion 服务方法 [SERVICE METHOD]

        #region 中间件接入面 [AUTHORING APIS]




        #endregion 中间件接入面 [AUTHORING APIS]

        #region 播放音频 [PLAY AUDIO]

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        ulong Play(AudioClip clip, in AudioPlayOptions options);

        /// <summary>
        /// 16 字节热请求 + 冷参数播放（推荐热路径 API）。冷参数所有权转移给本方法：每条早退路径都必须先归还。
        /// </summary>
        /// <remarks><paramref name="cold"/> 为 null 表示本次播放不写 AudioSource 空间整形，语义见 <see cref="AudioPlayColdParams"/>。</remarks>
        ulong Play(AudioClip clip, in AudioPlayRequest request, AudioPlayColdParams cold);

        /// <summary>
        /// 播放音频，返回服务自维护的音频句柄。
        /// </summary>
        /// <remarks>默认异步加载；同步加载（<paramref name="bAsync"/>=false）会阻塞主线程，仅限启动期/预加载场景使用。</remarks>
        ulong Play(string path, in AudioPlayOptions options, bool bAsync = true);

        #endregion 播放音频 [PLAY AUDIO]

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停指定句柄的音频。
        /// </summary>
        void Pause(ulong handle);

        /// <summary>
        /// 恢复播放指定句柄的音频。
        /// </summary>
        void Unpause(ulong handle);

        /// <summary>
        /// 停止指定句柄的音频。
        /// </summary>
        void Stop(ulong handle, float fadeoutDuration = 0f);

        /// <summary>
        /// 设置指定句柄声部的基础音高（不含音轨倍率），并按当前音轨倍率重算实际音高。
        /// </summary>
        /// <remarks>
        /// 默认经 <see cref="GetAgentByHandle"/> 走 Unity Agent；中间件后端覆写为按声部表直推原生实例。 <br />
        /// 未知句柄为空操作；值不夹取，与 <c>AudioAgent.SetPitch</c> 的直通语义一致。共享实现收在 <see cref="AudioBackendPolicy"/>。
        /// </remarks>
        void SetVoicePitch(ulong handle, float pitch);

        #endregion 音频控制 [AUDIO CONTROLS]

        #region 获取 [FIND]

        /// <summary>
        /// 对每个匹配 ID 的 AudioAgent 执行操作（零分配）。Unity 专属；中间件后端为空操作。
        /// </summary>
        void ForEachAgentByID(int id, Action<AudioAgent> action);

        /// <summary>
        /// 对每个匹配 Clip 的 AudioAgent 执行操作（零分配）。Unity 专属；中间件后端为空操作。
        /// </summary>
        void ForEachAgentByClip(AudioClip clip, Action<AudioAgent> action);

        /// <summary>
        /// 对每个匹配 ID 的 AudioHandle 执行操作（零分配）。
        /// </summary>
        void ForEachHandleByID(int id, Action<ulong> action);

        /// <summary>
        /// 返回当前正在播放的指定 clip 数量。中间件后端按 clip 名映射的事件路径统计。
        /// </summary>
        int CurrentlyPlayingCount(AudioClip clip);

        /// <summary>
        /// 通过句柄获取 AudioAgent（用于访问 AudioResource 等内部属性）。Unity 专属；中间件后端返回 null。
        /// </summary>
        AudioAgent GetAgentByHandle(ulong handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在播放。
        /// </summary>
        bool IsPlaying(ulong handle);

        /// <summary>
        /// 检查指定句柄的音频是否已停止。
        /// </summary>
        bool IsStopped(ulong handle);

        /// <summary>
        /// 移除已停止音频的句柄映射。
        /// </summary>
        void ReleaseHandle(ulong handle);

        #endregion 获取 [FIND]

        #region 音轨控制 [TRACK CONTROLS]

        /// <summary>
        /// 暂停某类音频的播放。
        /// </summary>
        void PauseTrack(EAudioTrack track);

        /// <summary>
        /// 恢复某类音频的播放。
        /// </summary>
        void UnpauseTrack(EAudioTrack track);

        /// <summary>
        /// 如果指定音轨当前处于暂停状态则返回 <c>true</c>，否则返回 <c>false</c>。
        /// </summary>
        bool IsPaused(EAudioTrack track);

        /// <summary>
        /// 停止某类音频的播放。
        /// </summary>
        void StopTrack(EAudioTrack track, float fadeoutDuration = 0f);

        #endregion 音轨控制 [TRACK CONTROLS]

        #region 所有音频控制 [ALL AUDIO CONTROLS]

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        void PauseAll();

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        void UnpauseAll();

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        void StopAll(float fadeoutDuration = 0f);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        void StopAllButPersistent(float fadeoutDuration = 0f);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        void StopAllLooping(float fadeoutDuration = 0f);

        /// <summary>
        /// 停止匹配用户 ID 的全部句柄（零 lambda 分配）。用于分层替换，不影响其它 ID。
        /// </summary>
        void StopByID(int id, float fadeoutDuration = 0f);

        #endregion 所有音频控制 [ALL AUDIO CONTROLS]

        #region 过渡 [FADES]

        /// <summary>
        /// 在指定的持续时间内，淡入 Master 音轨到最终音量。
        /// </summary>
        /// <remarks>时长为 0 等价于直接赋值；总线伪句柄与声部句柄共用同一张调度表。后端 inert 时不排过渡。共享实现收在 <see cref="AudioBackendPolicy"/>。</remarks>
        void FadeMasterTrack(float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default);

        /// <summary>
        /// 停止 Master 音轨上所有当前的淡化（Fade）。只撤过渡，不还原已写出去的音量——停在哪儿就是哪儿。
        /// </summary>
        void StopFadeMasterTrack();

        /// <summary>
        /// 在指定的持续时间内，淡入整个音轨到最终音量。
        /// </summary>
        void FadeTrack(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f, TweenEase tweenEase = default);

        /// <summary>
        /// 停止指定音轨上所有当前的淡化（Fade）。
        /// </summary>
        void StopFadeTrack(EAudioTrack track);

        /// <summary>
        /// 对指定句柄的音频进行音量过渡。使用手动过渡系统，完全零 GC；应用 <paramref name="tweenEase"/> 曲线。
        /// </summary>
        void FadeAudio(ulong handle, float duration, float initialVolume, float finalVolume, TweenEase tweenEase);

        /// <summary>
        /// 停止指定句柄音频上所有当前的淡化（Fade）。
        /// </summary>
        void StopFadeAudio(ulong handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中。
        /// </summary>
        bool SoundIsFadingOut(ulong handle);

        /// <summary>
        /// 对匹配用户 ID 的全部句柄执行淡入/音量过渡（零 lambda 分配）。
        /// </summary>
        void PlayFadeByID(int id, float duration, float finalVolume, TweenEase ease);

        /// <summary>
        /// 停止匹配用户 ID 的全部句柄上的过渡（零 lambda 分配）。
        /// </summary>
        void StopFadeByID(int id);

        #endregion 过渡 [FADES]

        #region 资源池 [ASSET POOL]

        /// <summary>
        /// 预先加载 <c>AudioClip</c>，并放入对象池。
        /// </summary>
        void PutInAudioPool(List<string> list);

        /// <summary>
        /// 将部分 <c>AudioClip</c> 从对象池移出。
        /// </summary>
        void RemoveClipFromPool(List<string> list);

        /// <summary>
        /// 清空 <c>AudioClip</c> 的对象池。
        /// </summary>
        void CleanAudioPool();

        /// <summary>
        /// 预加载地址（策略默认 Pin 常驻）。已加载完成返回 true；加载中或失败返回 false。
        /// </summary>
        bool Preload(string address, EAudioCachePolicy policy = EAudioCachePolicy.Pin);

        /// <summary>
        /// 异步预加载地址。
        /// </summary>
        void PreloadAsync(string address, EAudioCachePolicy policy, Action<bool> completed = null);

        /// <summary>
        /// 卸载地址缓存。force=true 时忽略引用计数。
        /// </summary>
        bool UnloadClipCache(string address, bool force = false);

        /// <summary>
        /// 清空 Clip 缓存。force=true 时连 Pin 一并清。
        /// </summary>
        void ClearClipCache(bool force = false);

        #endregion 资源池 [ASSET POOL]
		
        #region 接口默认实现 [DEFAULT IMPLEMENTATIONS]

        /// <summary><see cref="IsBackendInert"/> 默认：未知后端不自称失效。</summary>
        bool IsBackendInert => false;

        /// <summary><see cref="SupportsDelayedStart"/> 默认：未知后端不得自称支持。</summary>
        bool SupportsDelayedStart => false;

        /// <summary><see cref="SupportsPlaybackTrim"/> 默认：语义同 <see cref="SupportsDelayedStart"/>。</summary>
        bool SupportsPlaybackTrim => false;

        /// <summary><see cref="SupportsSoloControl"/> 默认：语义同 <see cref="SupportsDelayedStart"/>。</summary>
        bool SupportsSoloControl => false;

        /// <summary>
        /// <see cref="OnAgentPlaybackEnded"/> 默认：空实现。
        /// </summary>
        void OnAgentPlaybackEnded(AudioAgent agent)
        {
        }

        /// <summary>
        /// <see cref="HasActiveAudioOn"/> 默认：无概念返回 false。
        /// </summary>
        bool HasActiveAudioOn(EAudioTrack track) => false;

        /// <summary>
        /// <see cref="OnApplicationPaused"/> 默认：空实现。
        /// </summary>
        void OnApplicationPaused(bool paused)
        {
        }

        /// <summary>
        /// <see cref="LoadBank"/> 默认：非中间件后端不支持，返回 false。
        /// </summary>
        bool LoadBank(string bankPath) => false;

        /// <summary>
        /// <see cref="UnloadBank"/> 默认：语义同 <see cref="LoadBank"/>。
        /// </summary>
        bool UnloadBank(string bankPath) => false;

        /// <summary>
        /// <see cref="SetRtpc"/> 默认：非中间件后端空操作。
        /// </summary>
        void SetRtpc(string name, float value, ulong handle = 0UL)
        {
        }

        #endregion 接口默认实现 [DEFAULT IMPLEMENTATIONS]
    }
}
