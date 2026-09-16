using System;
using System.IO;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// CRC-32（IEEE 802.3，多项式 0xEDB88320）查表实现，用于存档载荷的存储损坏检测。
    /// <para>仅面向意外损坏（位翻转/截断）的完整性校验；防篡改由加密处理器的 HMAC 层承担。</para>
    /// </summary>
    internal static class Crc32
    {
        private const uint Polynomial = 0xEDB88320u;

        private static readonly uint[] s_Table = BuildTable();

        /// <summary>
        /// 增量计算的寄存器起始值（首段喂入前以此初始化）。
        /// </summary>
        public const uint INITIAL_STATE = 0xFFFFFFFFu;

        /// <summary>
        /// 计算字节序列的 CRC-32 校验值。
        /// </summary>
        /// <param name="data">待校验字节序列。</param>
        /// <returns>CRC-32 校验值。</returns>
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Finalize(Update(INITIAL_STATE, data));
        }

        /// <summary>
        /// 增量喂入字节序列（寄存器形式——多段数据可跨调用连续喂入，语义等价于拼接后一次性计算）。
        /// </summary>
        /// <param name="crc">当前 CRC 寄存器值（首段传 <see cref="INITIAL_STATE"/>）。</param>
        /// <param name="data">待喂入字节序列。</param>
        /// <returns>更新后的 CRC 寄存器值。</returns>
        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                crc = s_Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        /// <summary>
        /// 完成增量计算（异或终值得到最终 CRC-32 校验值）。
        /// </summary>
        /// <param name="crc">CRC 寄存器值。</param>
        /// <returns>CRC-32 校验值。</returns>
        public static uint Finalize(uint crc)
        {
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// 构建查表法所需的 256 项 CRC 表（进程内一次性）。
        /// </summary>
        /// <returns>CRC 表。</returns>
        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? Polynomial ^ (value >> 1) : value >> 1;
                }

                table[i] = value;
            }

            return table;
        }

        /// <summary>
        /// CRC-32 增量计算包装流（写透传到底层流并增量累计校验值——流式写管线的载荷 CRC 累计载体）。
        /// <para>只写、禁寻址（Seek 会破坏增量语义）；校验结果在全部写入完成后经 <see cref="Result"/> 读取。</para>
        /// </summary>
        internal sealed class Crc32Stream : Stream
        {
            /// <summary>底层目标流。</summary>
            private readonly Stream _inner;

            /// <summary>Dispose 时是否保留底层流（嵌套包装链的外层关闭权）。</summary>
            private readonly bool _leaveOpen;

            /// <summary>CRC 寄存器值。</summary>
            private uint _crc = INITIAL_STATE;

            /// <summary>已透传字节数。</summary>
            private long _bytesWritten;

            /// <summary>
            /// 创建 CRC 包装流。
            /// </summary>
            /// <param name="inner">底层目标流。</param>
            /// <param name="leaveOpen">Dispose 时是否保留底层流。</param>
            internal Crc32Stream(Stream inner, bool leaveOpen = false)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _leaveOpen = leaveOpen;
            }

            /// <summary>
            /// 全部写入完成后的 CRC-32 校验值。
            /// </summary>
            public uint Result => Crc32.Finalize(_crc);

            /// <summary>
            /// 已透传写入的字节总数。
            /// </summary>
            public long BytesWritten => _bytesWritten;

            /// <inheritdoc />
            public override bool CanRead => false;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => true;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => _bytesWritten;
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
                _crc = Update(_crc, buffer.AsSpan(offset, count));
                _bytesWritten += count;
            }

            /// <inheritdoc />
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                _inner.Write(buffer);
                _crc = Update(_crc, buffer);
                _bytesWritten += buffer.Length;
            }

            /// <inheritdoc />
            public override void Flush()
            {
                _inner.Flush();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void SetLength(long value) => throw new NotSupportedException();

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_leaveOpen)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
