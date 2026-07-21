using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
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
        public enum EAllAudiosControlEventType
        {
            Pause, Play, Stop, StopAllButPersistent, StopAllLooping
        }

        public EAllAudiosControlEventType EventType { get; private set; }
        
        private static AllAudiosControlEvent GetPooled(EAllAudiosControlEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }

        public static void Trigger(EAllAudiosControlEventType eventType)
        {
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public static void Pause() => Trigger(EAllAudiosControlEventType.Pause);

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public static void Play() => Trigger(EAllAudiosControlEventType.Play);

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        public static void Stop() => Trigger(EAllAudiosControlEventType.Stop);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        public static void AllButPersistent() => Trigger(EAllAudiosControlEventType.StopAllButPersistent);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        public static void StopAllLooping() => Trigger(EAllAudiosControlEventType.StopAllLooping);
    }
}