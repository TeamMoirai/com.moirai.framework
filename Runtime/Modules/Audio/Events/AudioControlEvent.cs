using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    public enum AudioControlEventType
    {
        Pause,
        UnPause,
        Stop,
    }
    
    /// <summary>
    /// 控制 AudioModule 上指定音频的事件。
    /// 可以通过 ID 搜索它，（如果有）也可以直接发送音频源。
    /// </summary>    
    /// <example>
    /// 使 ID 为 33 的音频停止播放
    /// AudioControlEvent.Trigger(AudioControlEventType.Stop, 33);
    /// </example>>
    public class AudioControlEvent : EventBase<AudioControlEvent>, IAudioModuleEvent
    {
        /// <summary>
        /// 要控制的音频的 ID（必须与用于播放的音频相匹配）
        /// </summary>
        public int AudioID { get; private set; }
        /// <summary>
        /// 音频的控制方式
        /// </summary>
        public AudioControlEventType AudioControlEventType { get; private set; }

        private static AudioControlEvent GetPooled(AudioControlEventType eventType, int audioID)
        {
            var evt = GetPooled();
            evt.AudioID = audioID;
            evt.AudioControlEventType = eventType;
            return evt;
        }

        public static void Trigger(AudioControlEventType eventType, int soundID)
        {
            using var evt = GetPooled(eventType, soundID);
            EventManager.SendEvent(evt);
        }
    }
}