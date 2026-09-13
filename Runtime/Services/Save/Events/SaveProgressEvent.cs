using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 进度事件的操作类别（<see cref="SaveProgressEvent.Kind"/>）。
    /// </summary>
    public enum ESaveProgressKind
    {
        /// <summary>保存进度（组件捕获）。</summary>
        Save = 0,

        /// <summary>加载进度（组件恢复）。</summary>
        Load,
    }

    /// <summary>
    /// 存取进度的 <see cref="EventManager"/> 桥事件（与静态事件 <see cref="SaveService.SaveProgress"/>/<see cref="SaveService.LoadProgress"/> 二选一订阅）。
    /// </summary>
    public class SaveProgressEvent : EventBase<SaveProgressEvent>
    {
        /// <summary>
        /// 操作类别。
        /// </summary>
        public ESaveProgressKind Kind { get; private set; }

        /// <summary>
        /// 事件参数。
        /// </summary>
        public SaveProgressArgs Args { get; private set; }

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
        /// <param name="kind">操作类别。</param>
        /// <param name="args">事件参数。</param>
        /// <returns>池化事件实例（用后 Dispose 归还）。</returns>
        public static SaveProgressEvent GetPooled(ESaveProgressKind kind, SaveProgressArgs args)
        {
            SaveProgressEvent evt = GetPooled();
            evt.Kind = kind;
            evt.Args = args;
            return evt;
        }

        /// <summary>
        /// 获取池化事件并广播（订阅侧亦可改用静态事件）。
        /// </summary>
        /// <param name="kind">操作类别。</param>
        /// <param name="args">事件参数。</param>
        public static void Trigger(ESaveProgressKind kind, SaveProgressArgs args)
        {
            using (SaveProgressEvent evt = GetPooled(kind, args))
            {
                EventManager.SendEvent(evt);
            }
        }
    }
}
