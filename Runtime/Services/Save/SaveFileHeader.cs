using System;
using System.Buffers.Binary;
using System.Text;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档文件头（固定 28 字节，小端序）：魔数 + 格式版本 + 保存时间 + 载荷长度 + 载荷 CRC32。
    /// <para>文件布局：<c>[4B 魔数 "MRSA"][4B 格式版本][8B UTC ticks][4B 载荷长度][4B 载荷 CRC32][载荷]</c>。
    /// 文件头始终为明文（元数据无需解密即可读）；加密处理器的载荷内部再含 IV/密文/HMAC。</para>
    /// </summary>
    internal readonly struct SaveFileHeader
    {
        /// <summary>
        /// 文件头固定字节数。
        /// </summary>
        public const int Size = 28;

        /// <summary>
        /// 当前写入的存档格式版本。
        /// </summary>
        public const int CurrentVersion = 1;

        private static readonly byte[] s_Magic = { (byte)'M', (byte)'R', (byte)'S', (byte)'A' };

        /// <summary>
        /// 存档格式版本。
        /// </summary>
        public readonly int FormatVersion;

        /// <summary>
        /// 保存时间（UTC ticks）。
        /// </summary>
        public readonly long SavedAtUtcTicks;

        /// <summary>
        /// 载荷字节数。
        /// </summary>
        public readonly int PayloadLength;

        /// <summary>
        /// 载荷 CRC-32 校验值。
        /// </summary>
        public readonly uint PayloadCrc;

        /// <summary>
        /// 创建文件头。
        /// </summary>
        /// <param name="formatVersion">存档格式版本。</param>
        /// <param name="savedAtUtcTicks">保存时间（UTC ticks）。</param>
        /// <param name="payloadLength">载荷字节数。</param>
        /// <param name="payloadCrc">载荷 CRC-32 校验值。</param>
        private SaveFileHeader(int formatVersion, long savedAtUtcTicks, int payloadLength, uint payloadCrc)
        {
            FormatVersion = formatVersion;
            SavedAtUtcTicks = savedAtUtcTicks;
            PayloadLength = payloadLength;
            PayloadCrc = payloadCrc;
        }

        /// <summary>
        /// 将文件头写入目标缓冲区（必须恰好 <see cref="Size"/> 字节）。
        /// </summary>
        /// <param name="destination">目标缓冲区。</param>
        /// <param name="payloadLength">载荷字节数。</param>
        /// <param name="payloadCrc">载荷 CRC-32 校验值。</param>
        public static void Write(Span<byte> destination, int payloadLength, uint payloadCrc)
        {
            s_Magic.AsSpan().CopyTo(destination);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), CurrentVersion);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(8), DateTime.UtcNow.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), payloadLength);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20), payloadCrc);
        }

        /// <summary>
        /// 从字节序列解析文件头并校验魔数、版本与长度自洽性。
        /// </summary>
        /// <param name="source">文件头字节序列（至少 <see cref="Size"/> 字节）。</param>
        /// <param name="header">解析成功时的文件头。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.InvalidFormat"/>、<see cref="SaveError.UnsupportedVersion"/> 或 <see cref="SaveError.Corrupted"/>。</returns>
        public static SaveError Read(ReadOnlySpan<byte> source, out SaveFileHeader header)
        {
            header = default;
            if (source.Length < Size)
            {
                return SaveError.InvalidFormat;
            }

            if (!source.Slice(0, 4).SequenceEqual(s_Magic))
            {
                return SaveError.InvalidFormat;
            }

            int formatVersion = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4));
            if (formatVersion > CurrentVersion)
            {
                return SaveError.UnsupportedVersion;
            }

            long savedAtUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8));
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(16));
            uint payloadCrc = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(20));

            if (formatVersion < 1 || savedAtUtcTicks <= 0 || payloadLength < 0)
            {
                return SaveError.Corrupted;
            }

            header = new SaveFileHeader(formatVersion, savedAtUtcTicks, payloadLength, payloadCrc);
            return SaveError.None;
        }

        /// <summary>
        /// 保存时间（UTC）。
        /// </summary>
        public DateTime SavedAtUtc => new DateTime(SavedAtUtcTicks, DateTimeKind.Utc);

        /// <summary>
        /// 按文件头校验载荷长度与实际字节数是否自洽。
        /// </summary>
        /// <param name="actualPayloadLength">实际载荷字节数。</param>
        /// <returns>自洽返回 <c>true</c>。</returns>
        public bool IsPayloadLengthConsistent(int actualPayloadLength)
        {
            return actualPayloadLength == PayloadLength;
        }
    }
}
