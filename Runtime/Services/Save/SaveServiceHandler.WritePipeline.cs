using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 写管线：容器文件写入（原子替换）。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {
        /// </summary>
        /// <param name="blockErrors">坏块清单（可为 <c>null</c>）。</param>
        /// <param name="key">目标块键。</param>
        /// <returns>错误码：<see cref="SaveError.FileNotFound"/> 或坏块记录的错误码。</returns>
        private static SaveError ClassifyMissingKey(List<SaveBlockError> blockErrors, string key)
        {
            if (blockErrors != null)
            {
                bool hasStructural = false;
                for (int i = 0; i < blockErrors.Count; i++)
                {
                    if (blockErrors[i].Key == null)
                    {
                        hasStructural = true;
                    }
                    else if (string.Equals(blockErrors[i].Key, key, StringComparison.Ordinal))
                    {
                        return blockErrors[i].Error;
                    }
                }

                if (hasStructural)
                {
                    return SaveError.Corrupted;
                }
            }

            return SaveError.FileNotFound;
        }

        /// <summary>
        /// 将容器块集经流式管线写入存档文件（占位头 → CRC 包装 → 载荷加密链 → 压缩包装流 → 容器段流直灌 → 回填真头 → 存储层流式原子提交）。
        /// </summary>
        /// <remarks>
        /// 写路径全程无整档容器/载荷缓冲；载荷 CRC 由 <see cref="Crc32.Crc32Stream"/> 增量累计，头部回填依赖临时流可寻址（存储层流式写契约）。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">数据块列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private void WriteContainerFile(SavePaths paths, List<SaveBlockEntry> blocks, CancellationToken cancellationToken)
        {
            // 版本化激活时盖章当前数据版本（任何落盘文件均为当前版本——写入自愈封闭性）
            blocks = StampSaveVersionIfActive(blocks);
            ICompressionProvider compression = m_CompressionProvider;
            uint flags = compression != null ? SaveFileHeader.FlagCompressed : 0u;
            uint compressionProviderId = compression?.ProviderId ?? 0u;
            SaveFailedEvent.ESaveFailureStage failureStage = SaveFailedEvent.ESaveFailureStage.StorageWrite;
            SaveError failureError = SaveError.IoFailed;
            try
            {
                StorageBackend.WriteAtomic(paths.SaveFilePath, stream =>
                {
                    // 占位头——载荷长度/CRC 在灌流完成后回填
                    long origin = stream.Position;
                    Span<byte> placeholder = stackalloc byte[SaveFileHeader.Size];
                    stream.Write(placeholder);

                    var crcStream = new Crc32.Crc32Stream(stream, leaveOpen: true);
                    Stream payloadStream;
                    try
                    {
                        payloadStream = OpenPayloadWriteStream(crcStream);
                    }
                    catch (Exception exception)
                    {
                        failureStage = SaveFailedEvent.ESaveFailureStage.Transform;
                        failureError = SaveError.TransformFailed;
                        if (exception is GameException)
                        {
                            throw;
                        }

                        throw new GameException(StringUtility.Format("Save payload transform failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
                    }

                    bool ownsPayloadStream = !ReferenceEquals(payloadStream, crcStream);
                    try
                    {
                        try
                        {
                            Stream containerTarget = payloadStream;
                            if (compression != null)
                            {
                                containerTarget = compression.OpenCompressStream(payloadStream);
                            }

                            bool ownsCompressStream = !ReferenceEquals(containerTarget, payloadStream);
                            try
                            {
                                SaveFileContainer.Write(containerTarget, blocks);
                            }
                            finally
                            {
                                if (ownsCompressStream)
                                {
                                    containerTarget.Dispose();
                                }
                            }
                        }
                        catch (Exception exception) when (!(exception is GameException))
                        {
                            failureStage = SaveFailedEvent.ESaveFailureStage.Compress;
                            failureError = SaveError.CompressionFailed;
                            throw new GameException(StringUtility.Format("Save compression failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
                        }
                    }
                    finally
                    {
                        if (ownsPayloadStream)
                        {
                            try
                            {
                                payloadStream.Dispose();
                            }
                            catch (Exception exception) when (!(exception is GameException))
                            {
                                failureStage = SaveFailedEvent.ESaveFailureStage.Transform;
                                failureError = SaveError.TransformFailed;
                                throw new GameException(StringUtility.Format("Save payload transform failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
                            }
                        }
                    }

                    long payloadLength = crcStream.BytesWritten;
                    uint payloadCrc = crcStream.Result;

                    // 回填真头（写盘的是存储载荷——加密/压缩后字节；CRC 与长度均为载荷口径）
                    stream.Position = origin;
                    Span<byte> headerBytes = stackalloc byte[SaveFileHeader.Size];
                    SaveFileHeader.Write(headerBytes, (int)payloadLength, payloadCrc, flags, compressionProviderId);
                    stream.Write(headerBytes);
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GameException)
            {
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, failureStage, failureError);
                throw;
            }
        }

        /// <summary>
        /// 载荷写流包装钩子：子类将存储目标流包装为载荷写入流（明文处理器基类默认直通）。
        /// </summary>
        /// <remarks>
        /// 在工作线程调用（存储层流式写委托内）；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。 <br />
        /// 返回流生命周期由调用方管理（直通时按引用比较跳过释放）；密钥材料不可用抛 <see cref="GameException"/>（fail-fast）。
        /// </remarks>
        /// <param name="target">存储目标流（载荷 CRC 包装层——写入经此增量累计校验值）。</param>
        /// <returns>载荷写入流（只写；调用方负责关闭以收尾加密/变换帧）。</returns>
        protected internal virtual Stream OpenPayloadWriteStream(Stream target)
        {
            return target;
        }

        /// <summary>
        /// 记录读取失败日志（运维可见性：损坏/解密失败等必须留下可追溯痕迹）。
        /// </summary>
        /// <param name="saveFilePath">存档路径。</param>
        /// <param name="error">错误码。</param>
        private static void LogLoadFailure(string saveFilePath, SaveError error)
        {
            LogUtility.Error("[SaveService] Load failed({0}), path: {1}.", error, saveFilePath);
        }

    }
}
