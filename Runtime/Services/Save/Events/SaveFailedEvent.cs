using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 失败事件的操作类别（<see cref="SaveFailedEvent.Operation"/>）。
    /// </summary>
    public enum ESaveFailureOperation
    {
        /// <summary>保存（写路径）。</summary>
        Save = 0,

        /// <summary>加载（读路径）。</summary>
        Load,
    }

    /// <summary>
    /// 存取失败的 <see cref="EventManager"/> 桥事件（与静态事件 <see cref="SaveService.SaveFailed"/>/<see cref="SaveService.LoadFailed"/> 二选一订阅）。
    /// </summary>
    public class SaveFailedEvent : EventBase<SaveFailedEvent>
    {
        /// <summary>
        /// 操作类别。
        /// </summary>
        public ESaveFailureOperation Operation { get; private set; }

        /// <summary>
        /// 事件参数。
        /// </summary>
        public SaveFailedArgs Args { get; private set; }

        /// <summary>
        /// 重置事件成员（池化复用前由 <see cref="EventBase"/> 回调）。
        /// </summary>
        protected override void Init()
        {
            base.Init();
            Operation = default;
            Args = default;
        }

        /// <summary>
        /// 从事件池获取实例并装载参数。
        /// </summary>
        /// <param name="operation">操作类别。</param>
        /// <param name="args">事件参数。</param>
        /// <returns>池化事件实例（用后 Dispose 归还）。</returns>
        public static SaveFailedEvent GetPooled(ESaveFailureOperation operation, SaveFailedArgs args)
        {
            SaveFailedEvent evt = GetPooled();
            evt.Operation = operation;
            evt.Args = args;
            return evt;
        }

        /// <summary>
        /// 获取池化事件并广播（订阅侧亦可改用静态事件）。
        /// </summary>
        /// <param name="operation">操作类别。</param>
        /// <param name="args">事件参数。</param>
        public static void Trigger(ESaveFailureOperation operation, SaveFailedArgs args)
        {
            using (SaveFailedEvent evt = GetPooled(operation, args))
            {
                EventManager.SendEvent(evt);
            }
        }
    }
}
