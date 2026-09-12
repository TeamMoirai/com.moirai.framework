using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 槽位变动的 <see cref="EventManager"/> 桥事件（与静态事件 <see cref="SaveService.SlotChanged"/> 二选一订阅）。
    /// </summary>
    public class SaveSlotChangedEvent : EventBase<SaveSlotChangedEvent>
    {
        /// <summary>
        /// 事件参数。
        /// </summary>
        public SaveSlotChangedArgs Args { get; private set; }

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
        public static SaveSlotChangedEvent GetPooled(SaveSlotChangedArgs args)
        {
            SaveSlotChangedEvent evt = GetPooled();
            evt.Args = args;
            return evt;
        }

        /// <summary>
        /// 获取池化事件并广播（订阅侧亦可改用静态事件 <see cref="SaveService.SlotChanged"/>）。
        /// </summary>
        /// <param name="args">事件参数。</param>
        public static void Trigger(SaveSlotChangedArgs args)
        {
            using (SaveSlotChangedEvent evt = GetPooled(args))
            {
                EventManager.SendEvent(evt);
            }
        }
    }
}
