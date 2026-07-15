using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    public enum AudioTrackFadeEventMode { PlayFade, StopFade }

    /// <summary>
    /// 此事件将命令 <see cref="AudioModule"/> 将整个音轨的音频音量过渡到指定的 FinalVolume
    /// </summary>
    /// <example>
    /// 将在 2 秒内将 Music 音轨的音量过渡至 0.5，使用三次补间中的插值
    /// AudioTrackFadeEvent.Trigger(AudioTrack.Music, 2f, 0.5f, new TweenType(Tween.TweenCurve.EaseInCubic));
    /// </example>>
    public class AudioTrackFadeEvent : EventBase<AudioTrackFadeEvent>, IAudioModuleEvent
    {
        /// <summary>
        /// 开始淡化（Fade），还是停止现有的淡化（Fade）
        /// </summary>
        public AudioTrackFadeEventMode Mode { get; private set; }
        /// <summary>
        /// 操作的音轨
        /// </summary>
        public AudioTrack Track { get; private set; }
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
        public Tween FadeTween { get; private set; }
   
        // ---------- Handle Common Track ----------

        private static AudioTrackFadeEvent GetPooled(AudioTrackFadeEventMode mode, AudioTrack track, float fadeDuration, float finalVolume, Tween fadeTween)
        {
            var evt = GetPooled();
            evt.Mode = mode;
            evt.Track = track;
            evt.IsMaster = false;
            evt.FadeDuration = fadeDuration;
            evt.FinalVolume = finalVolume;
            evt.FadeTween = fadeTween;
            return evt;
        }

        public static void Trigger(AudioTrackFadeEventMode mode, AudioTrack track, float fadeDuration, float finalVolume, Tween fadeTween)
        {
            using var evt = GetPooled(mode, track, fadeDuration, finalVolume, fadeTween);
            EventManager.SendEvent(evt);
        }
        
        // ---------- Handle Master Track ----------

        private static AudioTrackFadeEvent GetPooled(AudioTrackFadeEventMode mode, float fadeDuration, float finalVolume, Tween fadeTween)
        {
            var evt = GetPooled();
            evt.Mode = mode;
            evt.IsMaster = true;
            evt.FadeDuration = fadeDuration;
            evt.FinalVolume = finalVolume;
            evt.FadeTween = fadeTween;
            return evt;
        }

        public static void TriggerMaster(AudioTrackFadeEventMode mode, float fadeDuration, float finalVolume, Tween fadeTween)
        {
            using var evt = GetPooled(mode, fadeDuration, finalVolume, fadeTween);
            EventManager.SendEvent(evt);
        }
    }
}