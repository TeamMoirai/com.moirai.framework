using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 持久化实体恢复的 <see cref="EventManager"/> 桥事件（与静态事件 <see cref="SaveService.EntityRestored"/> 二选一订阅）。
    /// <para>随 P4 先行定义；生产点由动态实体持久化（P7）接线。</para>
    /// </summary>
    public class SaveEntityRestoredEvent : EventBase<SaveEntityRestoredEvent>
    {
        /// <summary>
        /// 事件参数。
        /// </summary>
        public SaveEntityRestoredArgs Args { get; private set; }

        /// <summary>
        /// 重置事件成员（池化复用前由 <see cref="EventBase"/> 回调）。
        /// </summary>
        protected override void Init()
        {
            base.Init();
            Args = default;
        }

        /// <summary>
        /// 从事件池获取实例并装载参数。
        /// </summary>
        /// <param name="args">事件参数。</param>
        /// <returns>池化事件实例（用后 Dispose 归还）。</returns>
        public static SaveEntityRestoredEvent GetPooled(SaveEntityRestoredArgs args)
        {
            SaveEntityRestoredEvent evt = GetPooled();
            evt.Args = args;
            return evt;
        }

        /// <summary>
        /// 获取池化事件并广播（订阅侧亦可改用静态事件）。
        /// </summary>
        /// <param name="args">事件参数。</param>
        public static void Trigger(SaveEntityRestoredArgs args)
        {
            using (SaveEntityRestoredEvent evt = GetPooled(args))
            {
                EventManager.SendEvent(evt);
            }
        }
    }
}
