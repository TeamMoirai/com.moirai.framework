using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档截图完成的 <see cref="EventManager"/> 桥事件（与静态事件 <see cref="SaveService.ScreenshotCaptured"/> 二选一订阅）。
    /// <para>随 P4 先行定义；生产点由截图管线（P8）接线。</para>
    /// </summary>
    public class SaveScreenshotEvent : EventBase<SaveScreenshotEvent>
    {
        /// <summary>
        /// 事件参数。
        /// </summary>
        public SaveScreenshotArgs Args { get; private set; }

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
        public static SaveScreenshotEvent GetPooled(SaveScreenshotArgs args)
        {
            SaveScreenshotEvent evt = GetPooled();
            evt.Args = args;
            return evt;
        }

        /// <summary>
        /// 获取池化事件并广播（订阅侧亦可改用静态事件）。
        /// </summary>
        /// <param name="args">事件参数。</param>
        public static void Trigger(SaveScreenshotArgs args)
        {
            using (SaveScreenshotEvent evt = GetPooled(args))
            {
                EventManager.SendEvent(evt);
            }
        }
    }
}
