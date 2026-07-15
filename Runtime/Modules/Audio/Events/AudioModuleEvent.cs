using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    public enum AudioModuleEventType
    {
        /// <summary>
        /// 写入设置
        /// </summary>
        /// <remarks>如果需要保存，直接调用 <see cref="SettingUtility.Save"/></remarks>
        SetSettings,
        /// <summary>
        /// 加载设置
        /// </summary>
        LoadSettings,
        /// <summary>
        /// 重置设置
        /// </summary>
        ResetSettings,
    }

    /// <summary>
    /// 此事件触发 AudioModule 设置的保存/加载/重置
    /// </summary>
    /// <example>
    /// 通过事件触发保存音量设置
    /// <code>AudioModuleEvent.Trigger(AudioModuleEventType.SaveSettings);</code>
    /// </example>>
    public class AudioModuleEvent : EventBase<AudioModuleEvent>, IAudioModuleEvent
    {
        public AudioModuleEventType EventType { get; private set; }

        private static AudioModuleEvent GetPooled(AudioModuleEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }
        
        public static void Trigger(AudioModuleEventType eventType)
        {
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }
    }
}