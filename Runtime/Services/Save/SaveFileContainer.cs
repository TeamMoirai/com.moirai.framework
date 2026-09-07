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
    /// 存档多块容器（手写二进制布局，零第三方依赖）。
    /// <para>布局（小端序）：<c>[4B 魔数 "MRSB"][4B 容器版本][4B 块数]{逐块：[4B 键字节长][键 UTF8][4B 模式版本][2B 后端][4B 载荷长][载荷]}</c>。
    /// 逐块独立序列化——块级后端/版本/迁移互不影响，容器结构稳定（切换后端只改变块内字节，不破坏文件格式）。</para>
    /// <para>纯函数式读写；解析全程跨度边界校验，截断/越界按 <see cref="SaveError.Corrupted"/> 分型。</para>
    /// </summary>
    internal static class SaveFileContainer
    {
        /// <summary>
        /// 容器格式当前版本。
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// 块数合理性上限（防御损坏文件的解析循环放大）。
        /// </summary>
        private const int MaxBlockCount = 4096;

        /// <summary>
        /// 块键字节长度上限（键名校验层已限 64 字符，此处为解析层冗余防御）。
        /// </summary>
        private const int MaxKeyByteCount = 4096;

        /// <summary>
        /// 单块定长字段字节数（模式版本 4B + 后端 2B + 载荷长 4B）。
        /// </summary>
        private const int BlockFixedFieldSize = 10;

        /// <summary>容器魔数。</summary>
        private static readonly byte[] s_Magic = { (byte)'M', (byte)'R', (byte)'S', (byte)'B' };

        /// <summary>容器定长头部字节数（魔数 + 版本 + 块数）。</summary>
        private const int HeaderSize = 12;

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
                size += 4 + Encoding.UTF8.GetByteCount(blocks[i].Key) + 4 + 2 + 4 + blocks[i].Bytes.Length;
            }

            return size;
        }

        /// <summary>
        /// 将数据块列表序列化为容器字节（目标缓冲区须恰好为 <see cref="GetSize"/> 计算的长度）。
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
                entry.Bytes.AsSpan().CopyTo(destination.Slice(offset));
                offset += entry.Bytes.Length;
            }
        }

        /// <summary>
        /// 从字节序列解析容器。
        /// </summary>
        /// <param name="source">容器字节序列。</param>
        /// <param name="blocks">解析成功时的数据块列表。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.InvalidFormat"/> 或 <see cref="SaveError.Corrupted"/>。</returns>
        public static SaveError Read(ReadOnlySpan<byte> source, out List<SaveBlockEntry> blocks)
        {
            blocks = null;
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
                return SaveError.UnsupportedVersion;
            }

            int blockCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8));
            if (blockCount < 0 || blockCount > MaxBlockCount)
            {
                return SaveError.Corrupted;
            }

            var entries = new List<SaveBlockEntry>(blockCount);
            int offset = HeaderSize;
            for (int i = 0; i < blockCount; i++)
            {
                if (!TryReadBlock(source, ref offset, out SaveBlockEntry entry))
                {
                    return SaveError.Corrupted;
                }

                entries.Add(entry);
            }

            blocks = entries;
            return SaveError.None;
        }

        /// <summary>
        /// 从当前偏移解析单个数据块（边界不足即失败）。
        /// </summary>
        /// <param name="source">容器字节序列。</param>
        /// <param name="offset">解析偏移（成功后推进至下一块）。</param>
        /// <param name="entry">解析成功时的数据块条目。</param>
        /// <returns>解析成功返回 <c>true</c>。</returns>
        private static bool TryReadBlock(ReadOnlySpan<byte> source, ref int offset, out SaveBlockEntry entry)
        {
            entry = default;
            if (offset + 4 > source.Length)
            {
                return false;
            }

            int keyByteCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
            offset += 4;
            if (keyByteCount < 0 || keyByteCount > MaxKeyByteCount || offset + keyByteCount + BlockFixedFieldSize > source.Length)
            {
                return false;
            }

            string key = Encoding.UTF8.GetString(source.Slice(offset, keyByteCount));
            offset += keyByteCount;

            int dataVersion = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
            offset += 4;
            var backend = (ESaveBackend)BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset));
            offset += 2;
            int byteCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
            offset += 4;
            if (byteCount < 0 || byteCount > source.Length - offset)
            {
                return false;
            }

            byte[] bytes = new byte[byteCount];
            source.Slice(offset, byteCount).CopyTo(bytes);
            offset += byteCount;

            entry = new SaveBlockEntry(key, dataVersion, backend, bytes);
            return true;
        }
    }
}
