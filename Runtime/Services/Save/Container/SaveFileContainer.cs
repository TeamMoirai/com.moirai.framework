using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块条目（容器内单块）：键 + 模式版本 + 后端标识 + 序列化字节。
    /// </summary>
    internal readonly struct SaveBlockEntry
    {
        /// <summary>
        /// 数据块键。
        /// </summary>
        public readonly string Key;

        /// <summary>
        /// 数据块模式版本。
        /// </summary>
        public readonly int DataVersion;

        /// <summary>
        /// 序列化后端标识。
        /// </summary>
        public readonly ESaveBackend Backend;

        /// <summary>
        /// 序列化后的块载荷字节。
        /// </summary>
        public readonly byte[] Bytes;

        /// <summary>
        /// 创建数据块条目。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="bytes">序列化后的块载荷字节。</param>
        public SaveBlockEntry(string key, int dataVersion, ESaveBackend backend, byte[] bytes)
        {
            Key = key;
            DataVersion = dataVersion;
            Backend = backend;
            Bytes = bytes;
        }
    }

    /// <summary>
    /// 存档坏块记录（容器 v2 逐块校验的失败明细）：键 + 错误码 + 框架元数据（CRC 坏块时可信）。
    /// <para><see cref="Key"/> 为 <c>null</c> 表示块边界不可读（结构性损坏——长度/键字段越界，解析在该处终止，
    /// 其后的块全部不可达）；<see cref="HasMetadata"/> 为 <c>true</c> 时版本/后端/尺寸字段有效（框架完好、载荷 CRC 不符的坏块）。</para>
    /// </summary>
    internal readonly struct SaveBlockError
    {
        /// <summary>
        /// 坏块键（<c>null</c> = 块边界不可读的结构性损坏）。
        /// </summary>
        public readonly string Key;

        /// <summary>
        /// 逐块错误码（v2 恒为 <see cref="SaveError.Corrupted"/>；保留字段面向未来分型扩展）。
        /// </summary>
        public readonly SaveError Error;

        /// <summary>
        /// 数据块模式版本（仅 <see cref="HasMetadata"/> 为 <c>true</c> 时有效）。
        /// </summary>
        public readonly int DataVersion;

        /// <summary>
        /// 序列化后端标识（仅 <see cref="HasMetadata"/> 为 <c>true</c> 时有效）。
        /// </summary>
        public readonly ESaveBackend Backend;

        /// <summary>
        /// 块载荷字节数（仅 <see cref="HasMetadata"/> 为 <c>true</c> 时有效）。
        /// </summary>
        public readonly int SizeBytes;

        /// <summary>
        /// 块框架是否完整可读（<c>true</c> = CRC 坏块，版本/后端/尺寸字段可信；<c>false</c> = 结构性损坏，元数据字段为零值）。
        /// </summary>
        public readonly bool HasMetadata;

        /// <summary>
        /// 创建结构性坏块记录（块框架越界/截断，解析终止）。
        /// </summary>
        /// <param name="key">坏块键（键字段本身不可读时为 <c>null</c>）。</param>
        public SaveBlockError(string key)
        {
            Key = key;
            Error = SaveError.Corrupted;
            DataVersion = 0;
            Backend = default;
            SizeBytes = 0;
            HasMetadata = false;
        }

        /// <summary>
        /// 创建 CRC 坏块记录（框架完好、载荷校验不符；块已被跳过，后续块照常解析）。
        /// </summary>
        /// <param name="key">坏块键。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="sizeBytes">块载荷字节数。</param>
        public SaveBlockError(string key, int dataVersion, ESaveBackend backend, int sizeBytes)
        {
            Key = key;
            Error = SaveError.Corrupted;
            DataVersion = dataVersion;
            Backend = backend;
            SizeBytes = sizeBytes;
            HasMetadata = true;
        }
    }

    /// <summary>
    /// 存档多块容器 v2（手写二进制布局，零第三方依赖）。
    /// <para>布局（小端序）：<c>[4B 魔数 "MRSB"][4B 容器版本][4B 块数]{逐块：[4B 键字节长][键 UTF8][4B 模式版本][2B 后端][4B 载荷长][4B 载荷CRC32][载荷]}</c>。
    /// 逐块独立序列化——块级后端/版本/迁移互不影响，容器结构稳定（切换后端只改变块内字节，不破坏文件格式）。</para>
    /// <para>v2 引入逐块 CRC32 自校验：载荷校验不符的坏块跳过并记入坏块清单、其余块照常可救（部分恢复）；
    /// 块框架（长度/键字段）越界的结构性损坏因后续块边界不可知，保留已解析前缀后终止解析并记终结坏块。
    /// 容器版本 1 旧档硬切作废——读取判别为 <see cref="SaveError.UnsupportedVersion"/>，不做双格式兼容读。</para>
    /// <para>纯函数式读写；解析全程跨度边界校验。注意整档 CRC 由文件头层（<see cref="SaveFileHeader"/>）先行把关，
    /// 逐块 CRC 是头校验放行后的第二道细粒度隔离层。</para>
    /// </summary>
    internal static class SaveFileContainer
    {
        /// <summary>
        /// 容器格式当前版本。
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// 块数合理性上限（防御损坏文件的解析循环放大）。
        /// </summary>
        private const int MaxBlockCount = 4096;

        /// <summary>
        /// 块键字节长度上限（键名校验层已限 64 字符，此处为解析层冗余防御）。
        /// </summary>
        private const int MaxKeyByteCount = 4096;

        /// <summary>
        /// 单块定长字段字节数（模式版本 4B + 后端 2B + 载荷长 4B + 载荷 CRC32 4B）。
        /// </summary>
        private const int BlockFixedFieldSize = 14;

        /// <summary>容器魔数。</summary>
        private static readonly byte[] s_Magic = { (byte)'M', (byte)'R', (byte)'S', (byte)'B' };

        /// <summary>容器定长头部字节数（魔数 + 版本 + 块数）。</summary>
        private const int HeaderSize = 12;

        /// <summary>单块解析结果。</summary>
        private enum EBlockParseResult
        {
            /// <summary>解析成功（含载荷 CRC 校验通过）。</summary>
            Parsed,

            /// <summary>载荷 CRC 校验不符（框架完好）——块跳过，解析继续。</summary>
            PayloadCorrupted,

            /// <summary>结构性损坏（框架字段越界/截断）——后续块边界不可知，解析终止。</summary>
            Structural,
        }

        /// <summary>
        /// 计算容器序列化后的总字节数。
        /// </summary>
        /// <param name="blocks">数据块条目列表。</param>
        /// <returns>容器总字节数。</returns>
        public static int GetSize(List<SaveBlockEntry> blocks)
        {
            int size = HeaderSize;
            for (int i = 0; i < blocks.Count; i++)
            {
                size += 4 + Encoding.UTF8.GetByteCount(blocks[i].Key) + BlockFixedFieldSize + blocks[i].Bytes.Length;
            }

            return size;
        }

        /// <summary>
        /// 将数据块列表序列化为容器字节（目标缓冲区须至少为 <see cref="GetSize"/> 计算的长度）。
        /// </summary>
        /// <param name="destination">目标缓冲区。</param>
        /// <param name="blocks">数据块条目列表。</param>
        public static void Write(Span<byte> destination, List<SaveBlockEntry> blocks)
        {
            s_Magic.AsSpan().CopyTo(destination);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), CurrentVersion);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), blocks.Count);

            int offset = HeaderSize;
            for (int i = 0; i < blocks.Count; i++)
            {
                SaveBlockEntry entry = blocks[i];
                int keyByteCount = Encoding.UTF8.GetByteCount(entry.Key);
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), keyByteCount);
                offset += 4;
                offset += Encoding.UTF8.GetBytes(entry.Key, destination.Slice(offset));
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), entry.DataVersion);
                offset += 4;
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset), (ushort)entry.Backend);
                offset += 2;
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), entry.Bytes.Length);
                offset += 4;
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset), Crc32.Compute(entry.Bytes));
                offset += 4;
                entry.Bytes.AsSpan().CopyTo(destination.Slice(offset));
                offset += entry.Bytes.Length;
            }
        }

        /// <summary>
        /// 从字节序列解析容器（v2 逐块自校验：坏块跳过记入 <paramref name="blockErrors"/>，健康块照常返回）。
        /// </summary>
        /// <param name="source">容器字节序列。</param>
        /// <param name="blocks">解析成功时的健康数据块列表（坏块已剔除）。</param>
        /// <param name="blockErrors">坏块清单（无坏块为 <c>null</c>；结构性损坏以单条终结记录收尾，其后的块不可达）。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>（含部分恢复）、<see cref="SaveError.InvalidFormat"/>、
        /// <see cref="SaveError.UnsupportedVersion"/> 或 <see cref="SaveError.Corrupted"/>（块数字段不可信）。</returns>
        public static SaveError Read(ReadOnlySpan<byte> source, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors)
        {
            blocks = null;
            blockErrors = null;
            if (source.Length < HeaderSize)
            {
                return SaveError.InvalidFormat;
            }

            if (!source.Slice(0, 4).SequenceEqual(s_Magic))
            {
                return SaveError.InvalidFormat;
            }

            int containerVersion = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4));
            if (containerVersion != CurrentVersion)
            {
                // 容器 v1 旧档与未来版本一律拒绝（用户裁定硬切，不做兼容读）
                return SaveError.UnsupportedVersion;
            }

            int blockCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8));
            if (blockCount < 0 || blockCount > MaxBlockCount)
            {
                return SaveError.Corrupted;
            }

            var entries = new List<SaveBlockEntry>(blockCount);
            List<SaveBlockError> errors = null;
            int offset = HeaderSize;
            for (int i = 0; i < blockCount; i++)
            {
                EBlockParseResult result = TryParseBlock(source, ref offset, out SaveBlockEntry entry, out SaveBlockError blockError);
                if (result == EBlockParseResult.Parsed)
                {
                    entries.Add(entry);
                    continue;
                }

                (errors ??= new List<SaveBlockError>()).Add(blockError);
                if (result == EBlockParseResult.Structural)
                {
                    // 结构性损坏后续块边界不可知——保留已解析前缀，终止解析
                    break;
                }
            }

            blocks = entries;
            blockErrors = errors;
            return SaveError.None;
        }

        /// <summary>
        /// 从当前偏移解析单个数据块（全程跨度边界校验 + 载荷 CRC32 校验）。
        /// </summary>
        /// <param name="source">容器字节序列。</param>
        /// <param name="offset">解析偏移（边界可信时推进至下一块——含 CRC 坏块；结构性损坏时不保证推进有效）。</param>
        /// <param name="entry">解析成功时的数据块条目。</param>
        /// <param name="blockError">解析失败时的坏块记录。</param>
        /// <returns>解析结果。</returns>
        private static EBlockParseResult TryParseBlock(ReadOnlySpan<byte> source, ref int offset, out SaveBlockEntry entry, out SaveBlockError blockError)
        {
            entry = default;
            blockError = default;

            if (offset + 4 > source.Length)
            {
                blockError = new SaveBlockError(null);
                return EBlockParseResult.Structural;
            }

            int keyByteCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
            if (keyByteCount < 0 || keyByteCount > MaxKeyByteCount || offset + 4 + keyByteCount + BlockFixedFieldSize > source.Length)
            {
                blockError = new SaveBlockError(null);
                return EBlockParseResult.Structural;
            }

            offset += 4;
            string key = Encoding.UTF8.GetString(source.Slice(offset, keyByteCount));
            offset += keyByteCount;

            int dataVersion = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
            offset += 4;
            var backend = (ESaveBackend)BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset));
            offset += 2;
            int byteCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
            offset += 4;
            uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset));
            offset += 4;
            if (byteCount < 0 || byteCount > source.Length - offset)
            {
                // 载荷长度越界（截断/篡改）——键已知但后续边界不可信
                blockError = new SaveBlockError(key);
                return EBlockParseResult.Structural;
            }

            ReadOnlySpan<byte> payloadSpan = source.Slice(offset, byteCount);
            offset += byteCount;
            if (Crc32.Compute(payloadSpan) != expectedCrc)
            {
                // 载荷损坏但框架自洽——跳过该块，解析继续
                blockError = new SaveBlockError(key, dataVersion, backend, byteCount);
                return EBlockParseResult.PayloadCorrupted;
            }

            byte[] bytes = new byte[byteCount];
            payloadSpan.CopyTo(bytes);

            entry = new SaveBlockEntry(key, dataVersion, backend, bytes);
            return EBlockParseResult.Parsed;
        }
    }
}
