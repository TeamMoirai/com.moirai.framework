using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 块变动类别（<see cref="SaveBlockChangedEvent.Kind"/>）。
    /// </summary>
    public enum ESaveBlockChangeKind
    {
        /// <summary>块已保存。</summary>
        Saved = 0,

        /// <summary>块已删除。</summary>
        Deleted,
    }

    /// <summary>
    /// 块保存/删除的 <see cref="EventManager"/> 桥事件（与静态事件 <see cref="SaveService.BlockSaved"/>/<see cref="SaveService.BlockDeleted"/> 二选一订阅）。
    /// </summary>
    public class SaveBlockChangedEvent : EventBase<SaveBlockChangedEvent>
    {
        /// <summary>
        /// 变动类别。
        /// </summary>
        public ESaveBlockChangeKind Kind { get; private set; }

        /// <summary>
        /// 事件参数。
        /// </summary>
        public SaveBlockChangedArgs Args { get; private set; }

        /// <summary>
        /// 重置事件成员（池化复用前由 <see cref="EventBase"/> 回调）。
        /// </summary>
        protected override void Init()
        {
            base.Init();
            Kind = default;
            Args = default;
        }

        /// <summary>
        /// 从事件池获取实例并装载参数。
        /// </summary>
        /// <param name="kind">变动类别。</param>
        /// <param name="args">事件参数。</param>
        /// <returns>池化事件实例（用后 Dispose 归还）。</returns>
        public static SaveBlockChangedEvent GetPooled(ESaveBlockChangeKind kind, SaveBlockChangedArgs args)
        {
            SaveBlockChangedEvent evt = GetPooled();
            evt.Kind = kind;
            evt.Args = args;
            return evt;
        }

        /// <summary>
        /// 获取池化事件并广播（订阅侧亦可改用静态事件）。
        /// </summary>
        /// <param name="kind">变动类别。</param>
        /// <param name="args">事件参数。</param>
        public static void Trigger(ESaveBlockChangeKind kind, SaveBlockChangedArgs args)
        {
            using (SaveBlockChangedEvent evt = GetPooled(kind, args))
            {
                EventManager.SendEvent(evt);
            }
        }
    }
}
