using System;

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
        /// 计算字节序列的 CRC-32 校验值。
        /// </summary>
        /// <param name="data">待校验字节序列。</param>
        /// <returns>CRC-32 校验值。</returns>
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
            {
                crc = s_Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

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
    }
}
