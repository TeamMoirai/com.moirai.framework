using Moirai.Atropos.Events;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 此事件将通过 AudioModule 暂停/播放/停止/释放的所有音频
    /// 
    /// <example>
    /// 立即停止所有音频播放
    /// <code>AllAudiosControlEvent.Trigger(AllAudiosControlEvent.EAllAudiosControlEventType.Stop);</code>
    /// </example>>
    /// </summary>
    public class AllAudiosControlEvent : EventBase<AllAudiosControlEvent>, IAudioModuleEvent
    {
        public enum EControlMode
        {
            Pause, Play, Stop, StopAllButPersistent, StopAllLooping
        }

        public EControlMode ControlMode { get; private set; }
        
        private static AllAudiosControlEvent GetPooled(EControlMode controlMode)
        {
            var evt = GetPooled();
            evt.ControlMode = controlMode;
            return evt;
        }

        public static void Trigger(EControlMode controlMode)
        {
            using var evt = GetPooled(controlMode);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 暂停所有音频。
        /// </summary>
        public static void Pause()
            => Trigger(EControlMode.Pause);

        /// <summary>
        /// 恢复所有音频。
        /// </summary>
        public static void Play()
            => Trigger(EControlMode.Play);

        /// <summary>
        /// 停止所有音频。
        /// </summary>
        public static void Stop()
            => Trigger(EControlMode.Stop);

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        public static void AllButPersistent()
            => Trigger(EControlMode.StopAllButPersistent);

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        public static void StopAllLooping()
            => Trigger(EControlMode.StopAllLooping);
    }
}