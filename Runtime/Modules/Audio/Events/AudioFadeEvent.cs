using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 这个事件会淡出指定音频
    /// </summary>    
    /// <example>
    /// 将在 2 秒内以弹性曲线将 ID 为 33 的音频过渡为 0.3 的音量
    /// AudioFadeEvent.PlayFade(33, 2f, 0.3f, new TweenType(Tween.TweenCurve.EaseInElastic));
    /// </example>>
    public class AudioFadeEvent : EventBase<AudioFadeEvent>, IAudioModuleEvent
    {
        public enum EAudioFadeEventMode { PlayFade, StopFade }

        /// <summary>
        /// 开始淡化（Fade），还是停止现有的淡化（Fade）
        /// </summary>
        public EAudioFadeEventMode Mode { get; private set; }
        /// <summary>
        /// 要淡出的音频的 ID
        /// </summary>
        public int SoundID { get; private set; }
        /// <summary>
        /// 淡出的持续时间（以秒为单位）
        /// </summary>
        public float FadeDuration { get; private set; }
        /// <summary>
        /// 淡出到此音频的音量
        /// </summary>
        public float FinalVolume { get; private set; }
        /// <summary>
        /// 淡出这个音频的补间动画曲线
        /// </summary>
        public TweenEase FadeTweenEase { get; private set; }
        
        private static AudioFadeEvent GetPooled(EAudioFadeEventMode mode, int soundID, float fadeDuration, float finalVolume, TweenEase fadeTweenEase)
        {
            var evt = GetPooled();
            evt.Mode = mode;
            evt.SoundID = soundID;
            evt.FadeDuration = fadeDuration;
            evt.FinalVolume = finalVolume;
            evt.FadeTweenEase = fadeTweenEase;
            return evt;
        }

        /// <summary>
        /// 对指定 ID 的音频进行音量过渡。
        /// </summary>
        public static void PlayFade(int soundID, float duration, float finalVolume, TweenEase ease = default)
        {
            using var evt = GetPooled(EAudioFadeEventMode.PlayFade, soundID, duration, finalVolume, ease);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 停止指定 ID 音频的音量过渡。
        /// </summary>
        public static void StopFade(int soundID)
        {
            using var evt = GetPooled(EAudioFadeEventMode.StopFade, soundID, 0, 0, default);
            EventManager.SendEvent(evt);
        }
    }
}