using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频后端共享语义的实现收纳处（原 <c>AudioServiceHandler</c> 抽象基类的 virtual 默认实现落点）。
    /// </summary>
    /// <remarks>
    /// 契约接口 <see cref="IAudioService"/> 不携带状态，依赖过渡调度器（internal 类型）的共享实现收在这里， <br />
    /// 由各后端一行转发（传入自身持有的 <see cref="AudioFadeScheduler"/> 实例）； <br />
    /// "this 传 <see cref="IAudioService"/>、scheduler 作参数"的组合形态替代了继承基类。 <br />
    /// 音轨暂停标记数组（原基类 <c>_pausedTracks</c>）的分配时机留在各后端（Unity 在 Initialize/OnShutdown、中间件在 EnsureTrackArrays）， <br />
    /// <see cref="SetTrackPaused"/>/<see cref="IsTrackPaused"/> 只做越界守卫的读写。
    /// </remarks>
    internal static class AudioBackendPolicy
    {
        #region 过渡 [FADES]

        /// <summary>
        /// 淡入 Master 音轨到最终音量。时长为 0 等价于直接赋值；后端 inert 时不排过渡。
        /// </summary>
        internal static void FadeMasterTrack(IAudioService backend, AudioFadeScheduler fades,
            float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (backend.IsBackendInert) return;
            if (duration <= 0f) { backend.MasterVolume = finalVolume; return; }

            fades.Stop(AudioFadeScheduler.MASTER_FADE_HANDLE);
            fades.Add(new AudioFadeState
            {
                Handle = AudioFadeScheduler.MASTER_FADE_HANDLE,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
                Ease = tweenEase,
            });
        }

        /// <summary>
        /// 淡入整个音轨到最终音量。语义同 <see cref="FadeMasterTrack"/>，句柄按音轨派发。
        /// </summary>
        internal static void FadeTrack(IAudioService backend, AudioFadeScheduler fades,
            EAudioTrack track, float duration, float initialVolume, float finalVolume, TweenEase tweenEase)
        {
            if (backend.IsBackendInert) return;
            if (duration <= 0f) { backend.SetTrackVolume(track, finalVolume); return; }

            ulong fadeHandle = AudioFadeScheduler.TrackFadeHandle((int)track);
            fades.Stop(fadeHandle);
            fades.Add(new AudioFadeState
            {
                Handle = fadeHandle,
                StartTime = GameTime.unscaledTime,
                Duration = duration,
                StartVolume = initialVolume,
                EndVolume = finalVolume,
                Ease = tweenEase,
            });
        }

        /// <summary>
        /// 停止一条总线（Master 或音轨）上所有当前的淡化。
        /// </summary>
        internal static void StopFadeBus(AudioFadeScheduler fades, ulong busHandle) => fades.Stop(busHandle);

        /// <summary>
        /// 停止指定声部句柄上的所有淡化。
        /// </summary>
        internal static void StopFadeHandle(AudioFadeScheduler fades, ulong handle) => fades.Stop(handle);

        /// <summary>
        /// 检查指定句柄的音频是否正在过渡中。
        /// </summary>
        internal static bool SoundIsFadingOut(AudioFadeScheduler fades, ulong handle) => fades.IsFading(handle);

        #endregion 过渡 [FADES]

        #region 声部音高 [VOICE PITCH]

        /// <summary>
        /// 设置指定句柄声部的基础音高（不含音轨倍率）。未知句柄为空操作；值不夹取。
        /// </summary>
        internal static void SetVoicePitch(IAudioService backend, ulong handle, float pitch)
            => backend.GetAgentByHandle(handle)?.SetPitch(pitch);

        #endregion 声部音高 [VOICE PITCH]

        #region 音轨暂停标记 [TRACK PAUSE FLAGS]

        /// <summary>
        /// 置位/复位音轨暂停标记。数组尚未分配或下标越界一律忽略。
        /// </summary>
        internal static void SetTrackPaused(bool[] flags, EAudioTrack track, bool paused)
        {
            if (flags == null) return;

            int index = (int)track;
            if (index >= 0 && index < flags.Length) flags[index] = paused;
        }

        /// <summary>
        /// 音轨是否被标记为暂停。数组未分配或下标越界按未暂停处理。
        /// </summary>
        internal static bool IsTrackPaused(bool[] flags, EAudioTrack track)
        {
            int index = (int)track;
            return flags != null && index >= 0 && index < flags.Length && flags[index];
        }

        #endregion 音轨暂停标记 [TRACK PAUSE FLAGS]

        #region 场景切换 [SCENE LOAD]

        /// <summary>
        /// 场景加载后是否停掉所有非持久音频：仅 <see cref="LoadSceneMode.Single"/>（整景切换）恒停。
        /// </summary>
        /// <remarks>
        /// Additive（流式分区/关卡分片）加载永不触发自动停音；需要收口的流程应在明确的切换点显式调用 <see cref="IAudioService.StopAllButPersistent"/>。
        /// </remarks>
        internal static bool ShouldStopNonPersistentOnSceneLoad(LoadSceneMode mode)
        {
            return mode == LoadSceneMode.Single;
        }

        #endregion 场景切换 [SCENE LOAD]
    }
}
