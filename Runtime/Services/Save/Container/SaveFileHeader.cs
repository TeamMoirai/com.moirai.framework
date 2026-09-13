using System;
using System.Buffers.Binary;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档文件头（固定 32 字节，小端序）：魔数 + 格式版本 + 保存时间 + 载荷长度 + 载荷 CRC32 + 压缩提供方 ID + 特性标志。
    /// <para>文件布局：<c>[4B 魔数 "MRSA"][4B 格式版本][8B UTC ticks][4B 载荷长度][4B 载荷 CRC32][4B 压缩提供方 ID][4B 标志][载荷]</c>。
    /// 文件头始终为明文（元数据无需解密即可读）；载荷为多块容器经压缩（可选）+ 加密处理器变换后的字节。</para>
    /// <para>v2 相对 v1 扩展 8 字节（压缩提供方 ID + 标志位——v2 早期写出的零值段即「未压缩」语义，天然前后兼容）；
    /// v1 旧档（单块无容器）不兼容，读取判别为 <see cref="SaveError.UnsupportedVersion"/> 作废。</para>
    /// </summary>
    internal readonly struct SaveFileHeader
    {
        /// <summary>
        /// 文件头固定字节数。
        /// </summary>
        public const int Size = 32;

        /// <summary>
        /// 当前写入的存档格式版本。
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// 标志位：载荷经压缩（压缩在加密前；读取时先解密再解压）。
        /// </summary>
        public const uint FlagCompressed = 1u << 0;

        /// <summary>已定义标志位掩码（读侧拒识未知位：未来特性被旧运行时静默忽略会写坏档）。</summary>
        private const uint KNOWN_FLAGS_MASK = FlagCompressed;

        /// <summary>魔数。</summary>
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
        /// 压缩提供方标识（0 = 未压缩；非零时经 <see cref="SaveCompressionRegistry"/> 查表解压，未知 ID 判别为 <see cref="SaveError.UnsupportedVersion"/>）。
        /// </summary>
        public readonly uint CompressionProviderId;

        /// <summary>
        /// 特性标志位（<see cref="FlagCompressed"/> 等）。
        /// </summary>
        public readonly uint Flags;

        /// <summary>
        /// 创建文件头。
        /// </summary>
        /// <param name="formatVersion">存档格式版本。</param>
        /// <param name="savedAtUtcTicks">保存时间（UTC ticks）。</param>
        /// <param name="payloadLength">载荷字节数。</param>
        /// <param name="payloadCrc">载荷 CRC-32 校验值。</param>
        /// <param name="compressionProviderId">压缩提供方标识。</param>
        /// <param name="flags">特性标志位。</param>
        private SaveFileHeader(int formatVersion, long savedAtUtcTicks, int payloadLength, uint payloadCrc, uint compressionProviderId, uint flags)
        {
            FormatVersion = formatVersion;
            SavedAtUtcTicks = savedAtUtcTicks;
            PayloadLength = payloadLength;
            PayloadCrc = payloadCrc;
            CompressionProviderId = compressionProviderId;
            Flags = flags;
        }

        /// <summary>
        /// 将文件头写入目标缓冲区（必须恰好 <see cref="Size"/> 字节）。
        /// </summary>
        /// <param name="destination">目标缓冲区。</param>
        /// <param name="payloadLength">载荷字节数。</param>
        /// <param name="payloadCrc">载荷 CRC-32 校验值。</param>
        /// <param name="flags">特性标志位。</param>
        /// <param name="compressionProviderId">压缩提供方标识（未压缩传 0）。</param>
        public static void Write(Span<byte> destination, int payloadLength, uint payloadCrc, uint flags, uint compressionProviderId)
        {
            s_Magic.AsSpan().CopyTo(destination);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), CurrentVersion);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(8), DateTime.UtcNow.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), payloadLength);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20), payloadCrc);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24), compressionProviderId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28), flags);
        }

        /// <summary>
        /// 从字节序列解析文件头并校验魔数、版本与长度自洽性。
        /// <para>v1 旧档（多块容器化之前）判别为 <see cref="SaveError.UnsupportedVersion"/>（用户裁定作废，不做双格式兼容读）。</para>
        /// <para>压缩标志与提供方 ID 的一致性校验在编排队（Handler 读路径）：标志置位但 ID 为零、或 ID 非零但标志未置位，均判别为 <see cref="SaveError.Corrupted"/>。</para>
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
            if (formatVersion != CurrentVersion)
            {
                // 高于当前版本 = 未来格式拒绝加载；低于当前版本（v1 单块旧档）= 用户裁定作废
                return SaveError.UnsupportedVersion;
            }

            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(28));
            if ((flags & ~KNOWN_FLAGS_MASK) != 0)
            {
                // 未知特性位 = 该档由更新版本的运行时写出，旧运行时静默忽略会写坏档——拒绝加载
                return SaveError.UnsupportedVersion;
            }

            long savedAtUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8));
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(16));
            uint payloadCrc = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(20));
            uint compressionProviderId = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(24));

            if (savedAtUtcTicks <= 0 || payloadLength < 0)
            {
                return SaveError.Corrupted;
            }

            header = new SaveFileHeader(formatVersion, savedAtUtcTicks, payloadLength, payloadCrc, compressionProviderId, flags);
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
