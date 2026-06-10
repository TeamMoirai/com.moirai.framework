using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    public enum AllAudiosControlEventType
    {
        Pause, Play, Stop, StopAllButPersistent, StopAllLooping
    }
    
    /// <summary>
    /// 此事件将通过 AudioModule 暂停/播放/停止/释放的所有音频
    /// 
    /// <example>
    /// 立即停止所有音频播放
    /// <code>AllAudiosControlEvent.Trigger(AllAudiosControlEventType.Stop);</code>
    /// </example>>
    /// </summary>
    public class AllAudiosControlEvent : EventBase<AllAudiosControlEvent>, IAudioModuleEvent
    {
        public AllAudiosControlEventType EventType { get; private set; }
        
        private static AllAudiosControlEvent GetPooled(AllAudiosControlEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }

        public static void Trigger(AllAudiosControlEventType eventType)
        {
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }
    }
}