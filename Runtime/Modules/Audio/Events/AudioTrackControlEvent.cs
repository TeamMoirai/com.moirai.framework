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
        public enum EControlMode
        {
            Mute,
            Unmute,
            SetVolume,
            Pause,
            Unpause,
            Stop,
        }

        /// <summary>
        /// 操作音轨类型
        /// </summary>
        public EControlMode ControlMode { get; private set; }
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

        
        // ---------- Handle Master Track ----------
        
        private static AudioTrackControlEvent GetPooled(EControlMode trackEventType, float volume = 1f)
        {
            var evt = GetPooled();
            evt.IsMaster = true;
            evt.ControlMode = trackEventType;
            evt.Volume = volume;
            return evt;
        }

        /// <summary>
        /// 触发 Master 音轨相关事件
        /// </summary>
        /// <param name="trackEventType">操作音轨类型</param>
        /// <param name="volume">如果在 SetTrackVolume 模式下，将音频设置为的音量</param>
        public static void TriggerMaster(EControlMode trackEventType, float volume = 1f)
        {
            using var evt = GetPooled(trackEventType, volume);
            EventManager.SendEvent(evt);
        }
        
        public static void MuteMaster()
            => TriggerMaster(EControlMode.Mute);

        public static void UnmuteMaster()
            => TriggerMaster(EControlMode.Unmute);

        public static void SetMasterVolume(float volume = 1f)
            => TriggerMaster(EControlMode.SetVolume, volume);

        public static void PauseMaster()
            => TriggerMaster(EControlMode.Pause);

        public static void UnpauseMaster()
            => TriggerMaster(EControlMode.Unpause);

        public static void StopMaster()
            => TriggerMaster(EControlMode.Stop);

        // ---------- Handle Common Track ----------

        private static AudioTrackControlEvent GetPooled(EControlMode trackEventType, EAudioTrack track, float volume = 1f)
        {
            var evt = GetPooled();
            evt.IsMaster = false;
            evt.ControlMode = trackEventType;
            evt.Track = track;
            evt.Volume = volume;
            return evt;
        }

        /// <summary>
        /// 触发音轨相关事件
        /// </summary>
        /// <param name="trackEventType">操作音轨类型</param>
        /// <param name="track">要操作的音轨</param>
        /// <param name="volume">如果在 SetTrackVolume 模式下，将音频设置为的音量</param>
        public static void TriggerTrack(EControlMode trackEventType, EAudioTrack track = EAudioTrack.Sfx, float volume = 1f)
        {
            using var evt = GetPooled(trackEventType, track, volume);
            EventManager.SendEvent(evt);
        }

        public static void MuteTrack(EAudioTrack track = EAudioTrack.Sfx)
            => TriggerTrack(EControlMode.Mute, track);

        public static void UnmuteTrack(EAudioTrack track = EAudioTrack.Sfx)
            => TriggerTrack(EControlMode.Unmute, track);

        public static void SetTrackVolume(EAudioTrack track = EAudioTrack.Sfx, float volume = 1f)
            => TriggerTrack(EControlMode.SetVolume, track, volume);

        public static void PauseTrack(EAudioTrack track = EAudioTrack.Sfx)
            => TriggerTrack(EControlMode.Pause, track);

        public static void UnpauseTrack(EAudioTrack track = EAudioTrack.Sfx)
            => TriggerTrack(EControlMode.Unpause, track);

        public static void StopTrack(EAudioTrack track = EAudioTrack.Sfx)
            => TriggerTrack(EControlMode.Stop, track);
    }
}