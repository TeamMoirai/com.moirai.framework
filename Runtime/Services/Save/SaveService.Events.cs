namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务事件分部：事件触发入口（<see cref="EventBus"/> 单通道，订阅侧经 <c>EventBus.Subscribe&lt;T&gt;</c> 订阅）。
    /// </summary>
    /// <remarks>
    /// 派发契约：全部事件在主线程派发——主线程触发的操作（同步裸名 API）内联派发；异步 API 在工作线程完成后经 <see cref="MainThreadDispatcher"/> 入队派发（下一主线程泵）。 <br />
    /// 事件载荷均为只读值类型（≤32B）；缺档（<see cref="SaveError.FileNotFound"/>）等正常业务流不产生失败事件。
    /// </remarks>
    partial class SaveService
    {
        /// <summary>进度回报批次大小（组件存取每处理满该数回报一次，最终一批必报）。</summary>
        internal const int PROGRESS_BATCH_SIZE = 8;

        #region 事件派发 [EVENT DISPATCH]

        /// <summary>
        /// 进度回报判定（纯函数）：满一批或最后一批回报。
        /// </summary>
        /// <param name="completed">已处理数。</param>
        /// <param name="total">总数。</param>
        /// <returns>应回报返回 <c>true</c>。</returns>
        internal static bool ShouldReportProgress(int completed, int total)
        {
            return completed == total || completed % PROGRESS_BATCH_SIZE == 0;
        }

        /// <summary>
        /// 触发槽位变动事件（任意线程可调；主线程内联派发，工作线程经状态化入队零闭包派发）。
        /// </summary>
        /// <param name="kind">变动类别。</param>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        internal static void RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind kind, string fileName, string folderName)
        {
            var evt = new SaveSlotChangedEvent(kind, fileName, folderName);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发块保存事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        internal static void RaiseBlockSaved(string fileName, string folderName, string key, ushort backend, int sizeBytes)
        {
            var evt = new SaveBlockChangedEvent(SaveBlockChangedEvent.ESaveBlockChangeKind.Saved, fileName, folderName, key, backend, sizeBytes);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发块删除事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        internal static void RaiseBlockDeleted(string fileName, string folderName, string key, ushort backend, int sizeBytes)
        {
            var evt = new SaveBlockChangedEvent(SaveBlockChangedEvent.ESaveBlockChangeKind.Deleted, fileName, folderName, key, backend, sizeBytes);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发保存进度事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="completed">已处理组件数。</param>
        /// <param name="total">组件总数。</param>
        internal static void RaiseSaveProgress(string fileName, string folderName, int completed, int total)
        {
            var evt = new SaveProgressEvent(SaveProgressEvent.ESaveProgressKind.Save, fileName, folderName, completed, total);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发加载进度事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="completed">已处理组件数。</param>
        /// <param name="total">组件总数。</param>
        internal static void RaiseLoadProgress(string fileName, string folderName, int completed, int total)
        {
            var evt = new SaveProgressEvent(SaveProgressEvent.ESaveProgressKind.Load, fileName, folderName, completed, total);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发实体恢复事件（先行定义；生产点为动态实体持久化恢复管线）。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="instance">恢复出的实体实例。</param>
        internal static void RaiseEntityRestored(string entityId, string prefabKey, UnityEngine.GameObject instance)
        {
            var evt = new SaveEntityRestoredEvent(entityId, prefabKey, instance);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发保存失败事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键（整档级失败为 <c>null</c>）。</param>
        /// <param name="stage">失败阶段。</param>
        /// <param name="error">错误码。</param>
        internal static void RaiseSaveFailed(string fileName, string folderName, string key, SaveFailedEvent.ESaveFailureStage stage, SaveError error)
        {
            var evt = new SaveFailedEvent(SaveFailedEvent.ESaveFailureOperation.Save, fileName, folderName, key, stage, error);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发加载失败事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键（整档级失败为 <c>null</c>）。</param>
        /// <param name="stage">失败阶段。</param>
        /// <param name="error">错误码。</param>
        internal static void RaiseLoadFailed(string fileName, string folderName, string key, SaveFailedEvent.ESaveFailureStage stage, SaveError error)
        {
            var evt = new SaveFailedEvent(SaveFailedEvent.ESaveFailureOperation.Load, fileName, folderName, key, stage, error);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        /// <summary>
        /// 触发截图完成事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="screenshotFileName">截图文件名。</param>
        /// <param name="width">截图宽度（像素）。</param>
        /// <param name="height">截图高度（像素）。</param>
        internal static void RaiseScreenshotCaptured(string fileName, string folderName, string screenshotFileName, int width, int height)
        {
            var evt = new SaveScreenshotEvent(fileName, folderName, screenshotFileName, width, height);
            MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e));
        }

        #endregion
    }
}
