using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务外观——截图与元数据镜像分部。
    /// <para>截图管线：帧末捕获屏幕 → GPU Blit 降采样 + 小图回读编码 PNG → 经存储层（<see cref="ISaveStorage"/>，云后端天然跟随）原子写 sidecar
    /// <c>{存档基名}.screenshot.png</c> → 镜像元数据块（缩略图文件名 + 活动场景名）→ 派发 <see cref="ScreenshotCaptured"/> 事件。</para>
    /// <para>截图仅限运行态主线程；存档删除时 sidecar 级联删除（防止同名新档复活陈旧缩略图）。</para>
    /// <para>联动开关：<see cref="SaveServiceSettings.CaptureScreenshotOnSave"/> 开启时，块保存（<see cref="SaveBlockAsync{T}"/>）
    /// 与组件保存（<see cref="SaveComponentsAsync"/>）成功后自动捕获——保留块（<c>__</c> 前缀，含元数据镜像回写）豁免联动。</para>
    /// </summary>
    public partial class SaveService
    {
        #region 截图 [SCREENSHOT]

        /// <summary>
        /// 捕获当前帧屏幕截图并写入存档 sidecar，同时镜像槽位元数据（缩略图文件名/活动场景名），IO 在工作线程执行。
        /// <para>仅限运行态主线程调用（帧末等待 + 屏幕捕获为主线程约束）；非运行态/批处理模式返回 <see cref="SaveError.NotSupported"/>。</para>
        /// <para>截图失败（sidecar 写入异常）返回 <see cref="SaveError.IoFailed"/> 并记录错误日志，不上抛；
        /// 元数据镜像为尽力而为（镜像失败不影响返回码）；处理器未就绪降级为 <see cref="SaveError.HandlerNotReady"/>。</para>
        /// </summary>
        /// <param name="fileName">存档文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>错误码（<see cref="SaveError.None"/> = 截图与元数据镜像完成）。</returns>
        public static async UniTask<SaveError> CaptureScreenshotAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            if (s_Handler is null)
            {
                return SaveError.HandlerNotReady;
            }

            if (!Application.isPlaying || Application.isBatchMode)
            {
                LogUtility.Warning("[SaveService] Screenshot capture requires a running player (not supported in Edit Mode or batch mode).");
                return SaveError.NotSupported;
            }

            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(fileName, folderName);

            // 帧末捕获（屏幕捕获须在帧渲染完成后进行；GPU 降采样 + 小图回读，ImageConversion 主线程编码小图）
            await UniTask.WaitForEndOfFrame(cancellationToken: cancellationToken);
            Color32[] pixels = SaveScreenshotUtility.CaptureThumbnailPixels(SaveServiceSettings.ScreenshotMaxDimension, out int thumbnailWidth, out int thumbnailHeight);
            byte[] pngBytes = SaveScreenshotUtility.EncodeThumbnailPng(pixels, thumbnailWidth, thumbnailHeight, SaveServiceSettings.ScreenshotMaxDimension, out _, out _);

            // 场景名必须在任何线程池 await 之前读取（WriteScreenshotAsync 续体不保证回主线程）
            string sceneName = SceneManager.GetActiveScene().name;

            try
            {
                await s_Handler.WriteScreenshotAsync(paths, pngBytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Screenshot sidecar write failed, path: {0}, message: {1}.", paths.SaveFilePath, exception.Message);
                return SaveError.IoFailed;
            }

            string screenshotFileName = SaveScreenshotUtility.DetermineScreenshotFileName(fileName);
            await MirrorScreenshotMetadataAsync(fileName, folderName, screenshotFileName, sceneName, cancellationToken);
            RaiseScreenshotCaptured(fileName, folderName, screenshotFileName, thumbnailWidth, thumbnailHeight);
            return SaveError.None;
        }

        #endregion

        #region 元数据镜像 [METADATA MIRROR]

        /// <summary>
        /// 异步镜像截图元数据到保留块 <c>__meta</c>（尽力而为：失败记录错误日志，不影响截图结果）。
        /// <para>既有元数据损坏时不覆盖（保留抢救空间）；场景名由调用方在主线程读取后传入。</para>
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="screenshotFileName">截图 sidecar 文件名。</param>
        /// <param name="sceneName">活动场景名（须在主线程读取）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>镜像完成的异步任务。</returns>
        private static async UniTask MirrorScreenshotMetadataAsync(string fileName, string folderName, string screenshotFileName, string sceneName, CancellationToken cancellationToken)
        {
            SaveResult<SaveMetadata> loadResult = await s_Handler.TryLoadBlockAsync<SaveMetadata>(fileName, SaveServiceHandler.META_BLOCK_KEY, folderName, cancellationToken);
            SaveMetadata metadata = MergeScreenshotMetadata(loadResult, screenshotFileName, sceneName, out bool shouldWrite);
            if (!shouldWrite)
            {
                return;
            }

            try
            {
                await s_Handler.SaveBlockAsync(metadata, fileName, SaveServiceHandler.META_BLOCK_KEY, folderName, ESaveBackend.Json, 1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Screenshot metadata mirror failed, file: {0}, message: {1}.", fileName, exception.Message);
            }
        }

        /// <summary>
        /// 合并截图元数据（纯函数）：填充缩略图文件名与场景名，保留其余既有字段。
        /// </summary>
        /// <param name="loadResult">元数据加载结果（<see cref="SaveError.FileNotFound"/> 视为无元数据新建）。</param>
        /// <param name="screenshotFileName">截图 sidecar 文件名。</param>
        /// <param name="sceneName">活动场景名。</param>
        /// <param name="shouldWrite">是否应回写（加载失败且非缺档时为 <c>false</c>——不覆盖损坏元数据）。</param>
        /// <returns>合并后的元数据（不回写时为 <c>null</c>）。</returns>
        internal static SaveMetadata MergeScreenshotMetadata(SaveResult<SaveMetadata> loadResult, string screenshotFileName, string sceneName, out bool shouldWrite)
        {
            if (!loadResult.IsSuccess && loadResult.Error != SaveError.FileNotFound)
            {
                LogUtility.Warning("[SaveService] Screenshot metadata mirror skipped: existing metadata failed to load, error: {0}.", loadResult.Error);
                shouldWrite = false;
                return null;
            }

            SaveMetadata metadata = loadResult.IsSuccess ? loadResult.Data : new SaveMetadata();
            metadata.ThumbnailFileName = screenshotFileName;
            metadata.SceneName = sceneName;
            shouldWrite = true;
            return metadata;
        }

        #endregion

        #region 保存联动 [SAVE LINKAGE]

        /// <summary>
        /// 保存成功后的联动截图（开关 <see cref="SaveServiceSettings.CaptureScreenshotOnSave"/> 控制；仅运行态主线程生效）。
        /// <para>保留块（<c>__</c> 前缀）豁免联动——截图管线的元数据镜像回写不触发递归。
        /// 联动结果不回传（保存已成功）；联动捕获使用独立取消令牌，取消语义不外溢到保存调用方。</para>
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="key">数据块键（<c>null</c> = 整档级保存）。</param>
        /// <returns>联动完成的异步任务。</returns>
        private static async UniTask CaptureScreenshotOnSaveIfEnabledAsync(string fileName, string folderName, string key)
        {
            if (key != null && key.StartsWith(SaveServiceHandler.RESERVED_BLOCK_KEY_PREFIX, StringComparison.Ordinal))
            {
                return;
            }

            // SaveBlock 管线 RunOnThreadPool(configureAwait:false) 后续体可能停留在线程池；
            // Application.isPlaying / Settings / WaitForEndOfFrame / 屏幕捕获均要求主线程，须先切回
            if (!MainThreadDispatcher.IsMainThread)
            {
                await UniTask.SwitchToMainThread();
            }

            if (!SaveServiceSettings.CaptureScreenshotOnSave)
            {
                return;
            }

            await CaptureScreenshotAsync(fileName, folderName, CancellationToken.None);
        }

        #endregion
    }
}
