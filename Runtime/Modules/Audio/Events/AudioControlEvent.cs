using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 控制 AudioModule 上指定音频的事件。
    /// 可以通过 ID 搜索它，（如果有）也可以直接发送音频源。
    /// </summary>    
    /// <example>
    /// 使 ID 为 33 的音频停止播放
    /// AudioControlEvent.Trigger(AudioControlEvent.EAudioControlEventType.Stop, 33);
    /// </example>>
    public class AudioControlEvent : EventBase<AudioControlEvent>, IAudioModuleEvent
    {
        public enum EAudioControlEventType
        {
            Pause,
            Unpause,
            Stop,
        }

        /// <summary>
        /// 要控制的音频的 ID（必须与用于播放的音频相匹配）
        /// </summary>
        public int AudioID { get; private set; }
        /// <summary>
        /// 音频的控制方式
        /// </summary>
        public EAudioControlEventType EventType { get; private set; }

        private static AudioControlEvent GetPooled(EAudioControlEventType eventType, int audioID)
        {
            var evt = GetPooled();
            evt.AudioID = audioID;
            evt.EventType = eventType;
            return evt;
        }

        public static void Trigger(EAudioControlEventType eventType, int soundID)
        {
            using var evt = GetPooled(eventType, soundID);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 暂停指定 ID 的音频。
        /// </summary>
        public static void Pause(int soundID)
            => Trigger(EAudioControlEventType.Pause, soundID);

        /// <summary>
        /// 恢复指定 ID 的音频。
        /// </summary>
        public static void Unpause(int soundID)
            => Trigger(EAudioControlEventType.Unpause, soundID);

        /// <summary>
        /// 停止指定 ID 的音频。
        /// </summary>
        public static void Stop(int soundID)
            => Trigger(EAudioControlEventType.Stop, soundID);
    }
}