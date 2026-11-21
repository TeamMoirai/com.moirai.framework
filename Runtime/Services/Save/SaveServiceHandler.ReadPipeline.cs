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
    /// 读管线：损坏隔离、精确读取与块错误分类。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {

        /// <summary>不可读旧档隔离后缀（写路径覆写前侧车拷贝，保留可抢救字节）。</summary>
        internal const string CorruptFileSuffix = ".corrupt";

        /// <summary>
        /// 将不可读旧档隔离拷贝为 <c>xxx.sav.corrupt</c> sidecar（尽力而为：失败仅记错误日志，不阻断后续覆写）。
        /// </summary>
        /// <remarks>
        /// 写路径读失败（头损坏/解密失败/瞬时 IO）时调用，避免下一次保存用「仅含新块」的文件替换旧档。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="error">读失败错误码（仅用于日志上下文）。</param>
        private void QuarantineUnreadableSave(SavePaths paths, SaveError error)
        {
            SaveError ioError = StorageBackend.TryReadAllBytes(paths.SaveFilePath, out byte[] fileBytes);
            if (ioError != SaveError.None || fileBytes == null)
            {
                LogUtility.Error("[SaveService] Unreadable save cannot be quarantined (raw read failed), path: {0}, readError: {1}, quarantineError: {2}.",
                    paths.SaveFilePath, error, ioError);
                return;
            }

            string quarantinePath = paths.SaveFilePath + CorruptFileSuffix;
            try
            {
                StorageBackend.WriteAtomic(quarantinePath, fileBytes, CancellationToken.None);
                LogUtility.Warning("[SaveService] Unreadable save quarantined before overwrite, path: {0} -> {1}, error: {2}.",
                    paths.SaveFilePath, quarantinePath, error);
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Unreadable save quarantine failed, path: {0}, quarantinePath: {1}, exception: {2}.",
                    paths.SaveFilePath, quarantinePath, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 读取并还原存档容器：缺档返回空块集（<see cref="SaveError.None"/>），其余错误分型返回（调用方决定日志与兜底语义）。
        /// </summary>
        /// <remarks>
        /// 管线：读文件 → 校验头/CRC → 载荷还原（子类：解密/直通）→ 容器解析。 <br />
        /// 容器 v2 逐块校验放行的坏块记入 <paramref name="blockErrors"/> 并统一记告警日志（部分恢复不阻断业务）。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">成功或缺档时的健康数据块列表（缺档为空列表；坏块已剔除）。</param>
        /// <param name="blockErrors">坏块清单（无坏块或整档失败为 <c>null</c>）。</param>
        /// <returns>错误码。</returns>
        private SaveError ReadContainerOrEmpty(SavePaths paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors)
        {
            blocks = new List<SaveBlockEntry>();
            blockErrors = null;
            SaveError ioError = StorageBackend.TryOpenRead(paths.SaveFilePath, out Stream stream);
            if (ioError == SaveError.FileNotFound)
            {
                // 缺档 = 空块集（正常业务流，不记录日志）
                return SaveError.None;
            }

            if (ioError != SaveError.None)
            {
                // IO 失败的详细日志已由存储层记录；事件统一走 SaveFailedEvent（写路径的合并读失败同样经此观测）
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.StorageRead, ioError);
                return ioError;
            }

            using (stream)
            {
                SaveError restoreError = ReadAndRestoreContainer(paths, stream, out blocks, out blockErrors);
                if (restoreError != SaveError.None)
                {
                    LogLoadFailure(paths.SaveFilePath, restoreError);
                    return restoreError;
                }

                if (blockErrors != null)
                {
                    LogUtility.Warning("[SaveService] {0} save block(s) corrupted and skipped, path: {1}.", blockErrors.Count, paths.SaveFilePath);
                }

                return SaveError.None;
            }
        }

        /// <summary>
        /// 将存档流（头 + 载荷）经流式管线还原为容器块列表（头读取 → CRC 增量包装 → 解密链 → 解压包装流 → 段池拉取 → 整档 CRC 把关 → 容器跨段解析； <br />
        /// 错误分型触发 <see cref="SaveFailedEvent"/> 事件）。
        /// </summary>
        /// <remarks>
        /// 读路径全程无整档文件/容器驻留：解密/解压链输出经 256KB 池化段拉取（归还于 finally），峰值与文件大小解耦。
        /// </remarks>
        /// <param name="paths">已解析的路径集合（事件参数上下文）。</param>
        /// <param name="stream">存档只读流（调用方管理生命周期；当前位置即文件起点）。</param>
        /// <param name="blocks">成功时的健康数据块列表。</param>
        /// <param name="blockErrors">坏块清单（无坏块为 <c>null</c>）。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>（含部分恢复）、<see cref="SaveError.InvalidFormat"/>、 <br />
        /// <see cref="SaveError.UnsupportedVersion"/>、<see cref="SaveError.Corrupted"/>、 <br />
        /// <see cref="SaveError.DecryptionFailed"/>、 <br />
        /// <see cref="SaveError.IntegrityCheckFailed"/> 或 <see cref="SaveError.IoFailed"/>。</returns>
        private SaveError ReadAndRestoreContainer(SavePaths paths, Stream stream, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors)
        {
            blocks = null;
            blockErrors = null;

            Span<byte> headerBytes = stackalloc byte[SaveFileHeader.Size];
            if (!TryReadExactly(stream, headerBytes))
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.HeaderValidation, SaveError.InvalidFormat);
                return SaveError.InvalidFormat;
            }

            SaveError headerError = SaveFileHeader.Read(headerBytes, out SaveFileHeader header);
            if (headerError != SaveError.None)
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.HeaderValidation, headerError);
                return headerError;
            }

            bool compressed = (header.Flags & SaveFileHeader.FlagCompressed) != 0;
            if (!compressed && header.CompressionProviderId != 0)
            {
                // 标志与提供方 ID 不一致（篡改/写中断）
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.HeaderValidation, SaveError.Corrupted);
                return SaveError.Corrupted;
            }

            ICompressionProvider compression = null;
            if (compressed)
            {
                if (header.CompressionProviderId == 0 || header.CompressionProviderId > byte.MaxValue)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.HeaderValidation, SaveError.Corrupted);
                    return SaveError.Corrupted;
                }

                if (!SaveCompressionRegistry.TryGet((byte)header.CompressionProviderId, out compression))
                {
                    // 未注册的提供方 ID（未来格式/依赖未接入保护）
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Decompress, SaveError.UnsupportedVersion);
                    return SaveError.UnsupportedVersion;
                }
            }

            var crcStream = new Crc32.Crc32ReadStream(stream, leaveOpen: true);
            List<byte[]> rentedBuffers = null;
            try
            {
                Stream payloadStream;
                try
                {
                    payloadStream = OpenPayloadReadStream(crcStream, header.PayloadLength);
                }
                catch (SaveEncryptor.SaveDecryptStreamException exception)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Restore, exception.Error);
                    return exception.Error;
                }

                bool ownsPayloadStream = !ReferenceEquals(payloadStream, crcStream);
                ReadOnlySequence<byte> container;
                try
                {
                    Stream containerSource = payloadStream;
                    bool ownsDecompressStream = false;
                    if (compressed)
                    {
                        containerSource = compression.OpenDecompressStream(payloadStream);
                        ownsDecompressStream = true;
                    }

                    try
                    {
                        container = ReadAllToSequence(containerSource, out rentedBuffers);
                    }
                    finally
                    {
                        if (ownsDecompressStream)
                        {
                            containerSource.Dispose();
                        }
                    }
                }
                catch (SaveEncryptor.SaveDecryptStreamException exception)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Restore, exception.Error);
                    return exception.Error;
                }
                catch (CryptographicException)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Restore, SaveError.DecryptionFailed);
                    return SaveError.DecryptionFailed;
                }
                catch (IOException)
                {
                    // Mono GZipStream 对损坏压缩数据抛 IOException（"Corrupted data ..."）——压缩链内异常归存储损坏；
                    // 仅未压缩直通路径的底层存储流 IO 中断归 IoFailed（压缩档的底层 IO 中断随压缩链归损坏，分型保守）
                    if (compressed)
                    {
                        SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Decompress, SaveError.Corrupted);
                        return SaveError.Corrupted;
                    }

                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.StorageRead, SaveError.IoFailed);
                    return SaveError.IoFailed;
                }
                catch (Exception)
                {
                    // 压缩帧破坏等（GZip InvalidDataException）——归一为存储损坏分型
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null,
                        compressed ? SaveFailedEvent.ESaveFailureStage.Decompress : SaveFailedEvent.ESaveFailureStage.ContainerParse, SaveError.Corrupted);
                    return SaveError.Corrupted;
                }
                finally
                {
                    if (ownsPayloadStream)
                    {
                        payloadStream.Dispose();
                    }
                }

                // 整档 CRC 先行把关（存储载荷口径——解密链之前增量累计；长度与校验值双重自洽）
                if (crcStream.BytesRead != header.PayloadLength || crcStream.Result != header.PayloadCrc)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.HeaderValidation, SaveError.Corrupted);
                    return SaveError.Corrupted;
                }

                SaveError containerError = SaveFileContainer.Read(container, out blocks, out blockErrors);
                if (containerError != SaveError.None)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null,
                        compressed ? SaveFailedEvent.ESaveFailureStage.Decompress : SaveFailedEvent.ESaveFailureStage.ContainerParse, containerError);
                    return containerError;
                }

                return SaveError.None;
            }
            finally
            {
                if (rentedBuffers != null)
                {
                    for (int i = 0; i < rentedBuffers.Count; i++)
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffers[i]);
                    }
                }
            }
        }

        /// <summary>
        /// 自源流精确读满缓冲区（流式管线的定长头/段读取）。
        /// </summary>
        /// <param name="stream">源流。</param>
        /// <param name="buffer">目标缓冲区（读满为止）。</param>
        /// <returns>读满返回 <c>true</c>；流提前结束返回 <c>false</c>。</returns>
        private static bool TryReadExactly(Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(total));
                if (read == 0)
                {
                    return false;
                }

                total += read;
            }

            return true;
        }

        /// <summary>
        /// 将源流全部内容拉取为多段只读序列（256KB 池化段链——解密/解压流式链输出的承接形态；段缓冲经 <paramref name="rentedBuffers"/> 外借，调用方归还）。
        /// </summary>
        /// <param name="source">源流。</param>
        /// <param name="rentedBuffers">拉取产生的池化租赁缓冲清单（调用方消费完序列后逐一归还 <see cref="ArrayPool{T}"/>）。</param>
        /// <returns>多段只读序列（源为空流时为空序列）。</returns>
        private static ReadOnlySequence<byte> ReadAllToSequence(Stream source, out List<byte[]> rentedBuffers)
        {
            const int SegmentSize = 256 * 1024;
            rentedBuffers = new List<byte[]>();
            PooledSequenceSegment first = null;
            PooledSequenceSegment last = null;
            long runningIndex = 0L;
            for (;;)
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(SegmentSize);
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = source.Read(buffer, offset, buffer.Length - offset);
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }

                if (offset == 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    break;
                }

                rentedBuffers.Add(buffer);
                var node = new PooledSequenceSegment(buffer, offset, last, runningIndex);
                if (first == null)
                {
                    first = node;
                }

                last = node;
                runningIndex += offset;
                if (offset < buffer.Length)
                {
                    // 流提前结束（未满段即终段）
                    break;
                }
            }

            if (first == null)
            {
                return ReadOnlySequence<byte>.Empty;
            }

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        /// <summary>
        /// 池化段链表节点（<see cref="ReadOnlySequenceSegment{T}"/> 载体 + ArrayPool 归还句柄）。
        /// </summary>
        private sealed class PooledSequenceSegment : ReadOnlySequenceSegment<byte>
        {
            /// <summary>池化租赁缓冲（归还句柄）。</summary>
            internal readonly byte[] Buffer;

            /// <summary>有效字节数。</summary>
            internal readonly int Length;

            /// <summary>
            /// 创建段节点并链接到前驱（protected 成员须经同类实例访问——链接在此内聚）。
            /// </summary>
            /// <param name="buffer">池化租赁缓冲。</param>
            /// <param name="length">有效字节数。</param>
            /// <param name="previous">前驱节点（首段传 <c>null</c>）。</param>
            /// <param name="runningIndex">本段在序列中的起始偏移。</param>
            internal PooledSequenceSegment(byte[] buffer, int length, PooledSequenceSegment previous, long runningIndex)
            {
                Buffer = buffer;
                Length = length;
                Memory = buffer.AsMemory(0, length);
                RunningIndex = runningIndex;
                if (previous != null)
                {
                    previous.Next = this;
                }
            }
        }

        /// <summary>
        /// 载荷读流包装钩子：子类将存储载荷源流包装为解密读流（明文处理器基类默认直通）。
        /// </summary>
        /// <remarks>
        /// 在工作线程调用（流式读管线）；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。 <br />
        /// 长度非法/密钥材料不可用抛 <see cref="SaveEncryptor.SaveDecryptStreamException"/>（读管线归一为对应错误码）；源流不要求可寻址（解密链全程顺序读）。
        /// </remarks>
        /// <param name="source">存储载荷源流（当前位置即载荷起点；载荷 CRC 增量包装层——读取经此累计校验值）。</param>
        /// <param name="payloadLength">存储载荷总字节数（文件头口径——含 IV/密文/MAC 尾等加密布局开销）。</param>
        /// <returns>解密读流（只读；调用方负责关闭）。</returns>
        protected internal virtual Stream OpenPayloadReadStream(Stream source, long payloadLength)
        {
            return source;
        }
    }
}
