using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 静音、取消静音、播放、暂停、停止或设置所选音轨的音量
    /// </summary>
    /// <example>
    /// 暂停整个 UI 音轨
    /// AudioTrackControlEvent.Trigger(AudioTrackControlEvent.EAudioTrackEventType.PauseTrack, AudioTrack.UI);
    /// </example>>
    public class AudioTrackControlEvent : EventBase<AudioTrackControlEvent>, IAudioModuleEvent
    {
        public enum EAudioTrackEventType
        {
            MuteTrack,
            UnmuteTrack,
            SetTrackVolume,
            PauseTrack,
            UnPauseTrack,
            StopTrack,
        }

        /// <summary>
        /// 操作音轨类型
        /// </summary>
        public EAudioTrackEventType TrackEventType { get; private set; }
        /// <summary>
        /// 要操作的音轨
        /// </summary>
        public EAudioTrack Track { get; private set; }
        /// <summary>
        /// 是否是主音轨（总音量）
        /// </summary>
        public bool IsMaster { get; private set; }
        /// <summary>
        /// 如果在 SetVolume 模式下，将音频设置为的音量
        /// </summary>
        public float Volume { get; private set; }
        
        // ---------- Handle Common Track ----------

        private static AudioTrackControlEvent GetPooled(EAudioTrackEventType trackEventType, EAudioTrack track, float volume)
        {
            var evt = GetPooled();
            evt.TrackEventType = trackEventType;
            evt.Track = track;
            evt.IsMaster = false;
            evt.Volume = volume;
            return evt;
        }

        /// <summary>
        /// 触发音轨相关事件
        /// </summary>
        /// <param name="trackEventType">操作音轨类型</param>
        /// <param name="track">要操作的音轨</param>
        /// <param name="volume">如果在 SetTrackVolume 模式下，将音频设置为的音量</param>
        public static void Trigger(EAudioTrackEventType trackEventType, EAudioTrack track = EAudioTrack.Sfx, float volume = 1f)
        {
            using var evt = GetPooled(trackEventType, track, volume);
            EventManager.SendEvent(evt);
        }
        
        // ---------- Handle Master Track ----------
        
        private static AudioTrackControlEvent GetPooled(EAudioTrackEventType trackEventType, float volume)
        {
            var evt = GetPooled();
            evt.TrackEventType = trackEventType;
            evt.IsMaster = true;
            evt.Volume = volume;
            return evt;
        }
        
        /// <summary>
        /// 触发 Master 音轨相关事件
        /// </summary>
        /// <param name="trackEventType">操作音轨类型</param>
        /// <param name="volume">如果在 SetTrackVolume 模式下，将音频设置为的音量</param>
        public static void TriggerMaster(EAudioTrackEventType trackEventType, float volume = 1f)
        {
            using var evt = GetPooled(trackEventType, volume);
            EventManager.SendEvent(evt);
        }
    }
}