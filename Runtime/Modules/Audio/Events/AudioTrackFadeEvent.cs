using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 此事件将命令 <see cref="AudioModule"/> 将整个音轨的音频音量过渡到指定的 FinalVolume
    /// </summary>
    /// <example>
    /// 将在 2 秒内将 Music 音轨的音量过渡至 0.5，使用三次补间中的插值
    /// AudioTrackFadeEvent.PlayFade(AudioTrack.Music, 2f, 0.5f, new TweenType(Tween.TweenCurve.EaseInCubic));
    /// </example>>
    public class AudioTrackFadeEvent : EventBase<AudioTrackFadeEvent>, IAudioModuleEvent
    {
        public enum EAudioTrackFadeEventMode { PlayFade, StopFade }

        /// <summary>
        /// 开始淡化（Fade），还是停止现有的淡化（Fade）
        /// </summary>
        public EAudioTrackFadeEventMode Mode { get; private set; }
        /// <summary>
        /// 操作的音轨
        /// </summary>
        public EAudioTrack Track { get; private set; }
        /// <summary>
        /// 是否是主音轨（总音量）
        /// </summary>
        public bool IsMaster { get; private set; }
        /// <summary>
        /// 淡出的持续时间（以秒为单位）
        /// </summary>
        public float FadeDuration { get; private set; }
        /// <summary>
        /// 最终音量
        /// </summary>
        public float FinalVolume { get; private set; }
        /// <summary>
        /// 淡出时使用的补间动画曲线
        /// </summary>
        public TweenEase FadeTweenEase { get; private set; }
   
        // ---------- Handle Common Track ----------

        private static AudioTrackFadeEvent GetPooled(EAudioTrackFadeEventMode mode, EAudioTrack track, float fadeDuration, float finalVolume, TweenEase fadeTweenEase)
        {
            var evt = GetPooled();
            evt.Mode = mode;
            evt.Track = track;
            evt.IsMaster = false;
            evt.FadeDuration = fadeDuration;
            evt.FinalVolume = finalVolume;
            evt.FadeTweenEase = fadeTweenEase;
            return evt;
        }

        /// <summary>
        /// 对指定音轨进行音量过渡。
        /// </summary>
        public static void PlayFade(EAudioTrack track, float duration, float finalVolume, TweenEase ease = default)
        {
            using var evt = GetPooled(EAudioTrackFadeEventMode.PlayFade, track, duration, finalVolume, ease);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 停止指定音轨的音量过渡。
        /// </summary>
        public static void StopFade(EAudioTrack track)
        {
            using var evt = GetPooled(EAudioTrackFadeEventMode.StopFade, track, 0, 0, default);
            EventManager.SendEvent(evt);
        }

        // ---------- Handle Master Track ----------

        private static AudioTrackFadeEvent GetPooled(EAudioTrackFadeEventMode mode, float fadeDuration, float finalVolume, TweenEase fadeTweenEase)
        {
            var evt = GetPooled();
            evt.Mode = mode;
            evt.IsMaster = true;
            evt.FadeDuration = fadeDuration;
            evt.FinalVolume = finalVolume;
            evt.FadeTweenEase = fadeTweenEase;
            return evt;
        }

        /// <summary>
        /// 对主音轨进行音量过渡。
        /// </summary>
        public static void PlayMasterFade(float duration, float finalVolume, TweenEase ease = default)
        {
            using var evt = GetPooled(EAudioTrackFadeEventMode.PlayFade, duration, finalVolume, ease);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 停止主音轨的音量过渡。
        /// </summary>
        public static void StopMasterFade()
        {
            using var evt = GetPooled(EAudioTrackFadeEventMode.StopFade, 0, 0, default);
            EventManager.SendEvent(evt);
        }
    }
}