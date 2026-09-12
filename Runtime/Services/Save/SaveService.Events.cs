using System;
using Moirai.Atropos.Events;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务事件分部：静态事件（零开销默认通道）+ <see cref="EventManager"/> 桥事件（可选第二通道，订阅侧二选一）。
    /// <para>派发契约：全部事件在主线程派发——主线程触发的操作（同步裸名 API）内联派发；
    /// 异步 API 在工作线程完成后经 <see cref="MainThreadDispatcher"/> 入队派发（下一主线程泵）。</para>
    /// <para>订阅生命周期自负盈亏：<see cref="OnShutdown"/> 不清理订阅者，长时间存活的订阅方须自行退订防泄漏。</para>
    /// <para>事件参数均为只读值类型（≤32B）；缺档（<see cref="SaveError.FileNotFound"/>）等正常业务流不产生失败事件。</para>
    /// </summary>
    public partial class SaveService
    {
        /// <summary>进度回报批次大小（组件存取每处理满该数回报一次，最终一批必报）。</summary>
        internal const int ProgressBatchSize = 8;

        #region 存档事件 [SAVE EVENTS]

        /// <summary>
        /// 槽位变动事件（写入/删除/备份创建/备份恢复；目录级批量删除时 <see cref="SaveSlotChangedArgs.FileName"/> 为 <c>null</c>）。
        /// </summary>
        public static event Action<SaveSlotChangedArgs> SlotChanged;

        /// <summary>
        /// 块保存完成事件（含保留块 <c>__main__</c>/<c>__meta</c> 与组件 KVT 块）。
        /// </summary>
        public static event Action<SaveBlockChangedArgs> BlockSaved;

        /// <summary>
        /// 块删除完成事件（仅目标块真实存在并移除时触发；幂等空删不触发）。
        /// </summary>
        public static event Action<SaveBlockChangedArgs> BlockDeleted;

        /// <summary>
        /// 保存进度事件（组件捕获按批回报；仅 <see cref="SaveComponentsAsync"/> 管线产生）。
        /// </summary>
        public static event Action<SaveProgressArgs> SaveProgress;

        /// <summary>
        /// 加载进度事件（组件恢复按批回报；仅 <see cref="LoadComponentsAsync"/> 管线产生）。
        /// </summary>
        public static event Action<SaveProgressArgs> LoadProgress;

        /// <summary>
        /// 持久化实体恢复事件（先行定义；生产点由动态实体持久化接线）。
        /// </summary>
        public static event Action<SaveEntityRestoredArgs> EntityRestored;

        /// <summary>
        /// 保存失败事件（写路径；失败同时以 <see cref="GameException"/> fail-fast 上抛，事件不替代异常）。
        /// </summary>
        public static event Action<SaveFailedArgs> SaveFailed;

        /// <summary>
        /// 加载失败事件（读路径；错误判别经 <c>TryLoad*</c> 族 <see cref="SaveResult{T}"/> 返回，事件提供被动观测）。
        /// </summary>
        public static event Action<SaveFailedArgs> LoadFailed;

        /// <summary>
        /// 存档截图完成事件（<see cref="CaptureScreenshotAsync"/> 管线成功完成后派发）。
        /// </summary>
        public static event Action<SaveScreenshotArgs> ScreenshotCaptured;

        #endregion

        #region 事件派发 [EVENT DISPATCH]

        /// <summary>
        /// 进度回报判定（纯函数）：满一批或最后一批回报。
        /// </summary>
        /// <param name="completed">已处理数。</param>
        /// <param name="total">总数。</param>
        /// <returns>应回报返回 <c>true</c>。</returns>
        internal static bool ShouldReportProgress(int completed, int total)
        {
            return completed == total || completed % ProgressBatchSize == 0;
        }

        /// <summary>
        /// 派发动作到主线程（主线程内联，工作线程入队下一泵）。
        /// </summary>
        /// <param name="action">派发动作。</param>
        private static void DispatchToMain(Action action)
        {
            if (MainThreadDispatcher.IsMainThread)
            {
                action();
            }
            else
            {
                MainThreadDispatcher.Post(action);
            }
        }

        /// <summary>
        /// 触发槽位变动事件（任意线程可调；主线程内联派发，工作线程入队派发）。
        /// </summary>
        /// <param name="kind">变动类别。</param>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        internal static void RaiseSlotChanged(ESaveSlotChangeKind kind, string fileName, string folderName)
        {
            var args = new SaveSlotChangedArgs(kind, fileName, folderName);
            DispatchToMain(() => PublishSlotChanged(args));
        }

        /// <summary>
        /// 触发块保存事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        internal static void RaiseBlockSaved(string fileName, string folderName, string key, ESaveBackend backend, int sizeBytes)
        {
            var args = new SaveBlockChangedArgs(fileName, folderName, key, backend, sizeBytes);
            DispatchToMain(() => PublishBlockSaved(args));
        }

        /// <summary>
        /// 触发块删除事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        internal static void RaiseBlockDeleted(string fileName, string folderName, string key, ESaveBackend backend, int sizeBytes)
        {
            var args = new SaveBlockChangedArgs(fileName, folderName, key, backend, sizeBytes);
            DispatchToMain(() => PublishBlockDeleted(args));
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
            var args = new SaveProgressArgs(fileName, folderName, completed, total);
            DispatchToMain(() => PublishSaveProgress(args));
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
            var args = new SaveProgressArgs(fileName, folderName, completed, total);
            DispatchToMain(() => PublishLoadProgress(args));
        }

        /// <summary>
        /// 触发实体恢复事件（先行定义；生产点后续接线）。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="instance">恢复出的实体实例。</param>
        internal static void RaiseEntityRestored(string entityId, string prefabKey, UnityEngine.GameObject instance)
        {
            var args = new SaveEntityRestoredArgs(entityId, prefabKey, instance);
            DispatchToMain(() => PublishEntityRestored(args));
        }

        /// <summary>
        /// 触发保存失败事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键（整档级失败为 <c>null</c>）。</param>
        /// <param name="stage">失败阶段。</param>
        /// <param name="error">错误码。</param>
        internal static void RaiseSaveFailed(string fileName, string folderName, string key, ESaveFailureStage stage, SaveError error)
        {
            var args = new SaveFailedArgs(fileName, folderName, key, stage, error);
            DispatchToMain(() => PublishSaveFailed(args));
        }

        /// <summary>
        /// 触发加载失败事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键（整档级失败为 <c>null</c>）。</param>
        /// <param name="stage">失败阶段。</param>
        /// <param name="error">错误码。</param>
        internal static void RaiseLoadFailed(string fileName, string folderName, string key, ESaveFailureStage stage, SaveError error)
        {
            var args = new SaveFailedArgs(fileName, folderName, key, stage, error);
            DispatchToMain(() => PublishLoadFailed(args));
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
            var args = new SaveScreenshotArgs(fileName, folderName, screenshotFileName, width, height);
            DispatchToMain(() => PublishScreenshotCaptured(args));
        }

        #endregion

        #region 主线程发布 [MAIN-THREAD PUBLISH]

        private static void PublishSlotChanged(SaveSlotChangedArgs args)
        {
            SlotChanged?.Invoke(args);
            SaveSlotChangedEvent.Trigger(args);
        }

        private static void PublishBlockSaved(SaveBlockChangedArgs args)
        {
            BlockSaved?.Invoke(args);
            SaveBlockChangedEvent.Trigger(ESaveBlockChangeKind.Saved, args);
        }

        private static void PublishBlockDeleted(SaveBlockChangedArgs args)
        {
            BlockDeleted?.Invoke(args);
            SaveBlockChangedEvent.Trigger(ESaveBlockChangeKind.Deleted, args);
        }

        private static void PublishSaveProgress(SaveProgressArgs args)
        {
            SaveProgress?.Invoke(args);
            SaveProgressEvent.Trigger(ESaveProgressKind.Save, args);
        }

        private static void PublishLoadProgress(SaveProgressArgs args)
        {
            LoadProgress?.Invoke(args);
            SaveProgressEvent.Trigger(ESaveProgressKind.Load, args);
        }

        private static void PublishEntityRestored(SaveEntityRestoredArgs args)
        {
            EntityRestored?.Invoke(args);
            SaveEntityRestoredEvent.Trigger(args);
        }

        private static void PublishSaveFailed(SaveFailedArgs args)
        {
            SaveFailed?.Invoke(args);
            SaveFailedEvent.Trigger(ESaveFailureOperation.Save, args);
        }

        private static void PublishLoadFailed(SaveFailedArgs args)
        {
            LoadFailed?.Invoke(args);
            SaveFailedEvent.Trigger(ESaveFailureOperation.Load, args);
        }

        private static void PublishScreenshotCaptured(SaveScreenshotArgs args)
        {
            ScreenshotCaptured?.Invoke(args);
            SaveScreenshotEvent.Trigger(args);
        }

        #endregion
    }
}
