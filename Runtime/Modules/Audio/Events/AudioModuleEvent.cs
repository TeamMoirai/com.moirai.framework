using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 此事件触发 AudioModule 设置的保存/加载/重置
    /// </summary>
    /// <example>
    /// 通过事件触发保存音量设置
    /// <code>AudioModuleEvent.Trigger(AudioModuleEvent.EAudioModuleEventType.SaveSettings);</code>
    /// </example>>
    public class AudioModuleEvent : EventBase<AudioModuleEvent>, IAudioModuleEvent
    {
        public enum EAudioModuleEventType
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

        public EAudioModuleEventType EventType { get; private set; }

        private static AudioModuleEvent GetPooled(EAudioModuleEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }
        
        public static void Trigger(EAudioModuleEventType eventType)
        {
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }
    }
}