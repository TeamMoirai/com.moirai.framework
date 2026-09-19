#if UNITY_CLOUD_SAVE_INSTALLED
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.Core;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// Unity Gaming Services Cloud Save 云端 KV 存储（整文件经 <c>UNITY_CLOUD_SAVE_INSTALLED</c> 条件编译——项目安装
    /// <c>com.unity.services.cloudsave</c> 后自动激活；Player Files API 承载——单档上限 1GB、每玩家 200 文件）。
    /// <para>前置条件：项目须先完成 <c>UnityServices.InitializeAsync()</c> 且玩家已登录（Authentication）——
    /// 未初始化/未登录/远端失败一律抛异常（远端失败语义——<see cref="CloudSaveStorageBackend"/> 归一为离线降级）；
    /// 缺档非错误（<see cref="CloudSaveExceptionReason.NotFound"/> → <c>null</c>/<c>false</c>/幂等）。</para>
    /// <para>版本通道：UGS 的 WriteLock 为 etag 语义字符串（非单调数值），不提供数值修订号——<see cref="WriteAsync"/> 恒返回 <c>0</c>，
    /// 同步裁决回退时间戳比较（<c>FileItem.Modified</c> 为远端权威时钟）。</para>
    /// <para>取消语义：UGS SDK 不接收取消令牌——调用前协作式检查，已发出的请求无法中止。</para>
    /// </summary>
    [Serializable]
    public class UnityCloudSaveKvStore : CloudSaveKvStore
    {
        /// <summary>单次远端请求超时（秒；透传 UGS <see cref="SaveOptions.RequestTimeout"/>）。</summary>
        [Tooltip("单次远端请求超时（秒；透传 UGS SaveOptions.RequestTimeout）。")]
        [SerializeField, Min(1)] private int m_RequestTimeoutSeconds = 15;

        #region 远端操作 [REMOTE OPERATIONS]

        /// <summary>
        /// 读取远端条目（先取元信息（缺档 → <c>null</c>，免载荷往返），再取载荷字节）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌（调用前协作式检查）。</param>
        /// <returns>条目（远端时间戳取 <c>FileItem.Modified</c>；无版本号）；缺档为 <c>null</c>。</returns>
        public override async UniTask<CloudKvEntry?> ReadAsync(string key, CancellationToken cancellationToken)
        {
            EnsureReady();
            cancellationToken.ThrowIfCancellationRequested();

            DateTime modifiedUtc;
            try
            {
                FileItem metadata = await CloudSaveService.Instance.Files.Player.GetMetadataAsync(key);
                modifiedUtc = metadata.Modified?.ToUniversalTime() ?? DateTime.MinValue;
            }
            catch (CloudSaveException exception) when (exception.Reason == CloudSaveExceptionReason.NotFound)
            {
                return null;
            }

            byte[] bytes = await CloudSaveService.Instance.Files.Player.LoadBytesAsync(key);
            return new CloudKvEntry(bytes, modifiedUtc, 0L);
        }

        /// <summary>
        /// 远端是否存在目标键（元信息探测；缺档 <c>false</c>，其余远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌（调用前协作式检查）。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public override async UniTask<bool> ExistsAsync(string key, CancellationToken cancellationToken)
        {
            EnsureReady();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await CloudSaveService.Instance.Files.Player.GetMetadataAsync(key);
                return true;
            }
            catch (CloudSaveException exception) when (exception.Reason == CloudSaveExceptionReason.NotFound)
            {
                return false;
            }
        }

        /// <summary>
        /// 写入远端条目（整值替换；远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="cancellationToken">取消令牌（调用前协作式检查）。</param>
        /// <returns>恒 <c>0</c>——UGS 不提供单调数值修订号（裁决回退时间戳比较）。</returns>
        public override async UniTask<long> WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken)
        {
            EnsureReady();
            cancellationToken.ThrowIfCancellationRequested();

            var options = new SaveOptions { RequestTimeout = m_RequestTimeoutSeconds };
            await CloudSaveService.Instance.Files.Player.SaveAsync(key, bytes, options);
            return 0L;
        }

        /// <summary>
        /// 删除远端条目（幂等——缺档视为成功；其余远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌（调用前协作式检查）。</param>
        /// <returns>删除完成的异步任务。</returns>
        public override async UniTask DeleteAsync(string key, CancellationToken cancellationToken)
        {
            EnsureReady();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await CloudSaveService.Instance.Files.Player.DeleteAsync(key);
            }
            catch (CloudSaveException exception) when (exception.Reason == CloudSaveExceptionReason.NotFound)
            {
                // 幂等删除——缺档视为成功
            }
        }

        /// <summary>
        /// 枚举远端全部条目（<c>ListAllAsync</c>；远端失败抛异常）。前缀过滤由基类客户端过滤实现（每玩家文件上限 200，全量拉取代价有界）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌（调用前协作式检查）。</param>
        /// <returns>条目元信息数组。</returns>
        public override async UniTask<CloudKvEntryInfo[]> EnumerateAsync(CancellationToken cancellationToken)
        {
            EnsureReady();
            cancellationToken.ThrowIfCancellationRequested();

            List<FileItem> items = await CloudSaveService.Instance.Files.Player.ListAllAsync();
            var results = new CloudKvEntryInfo[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                FileItem item = items[i];
                results[i] = new CloudKvEntryInfo(item.Key, item.Size, item.Modified?.ToUniversalTime() ?? DateTime.MinValue, 0L);
            }

            return results;
        }

        #endregion

        /// <summary>
        /// UGS 就绪前置检查（未初始化视为远端不可用——抛异常走离线降级；登录态缺失由 SDK 抛 <see cref="CloudSaveException"/> 承载）。
        /// </summary>
        private static void EnsureReady()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                throw new InvalidOperationException("[UnityCloudSaveKvStore] Unity Services is not initialized — call UnityServices.InitializeAsync() and sign in the player before cloud save sync.");
            }
        }
    }
}
#endif
