using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 字节缓冲区视图（缓冲区 + 有效区间）：载荷变换钩子（<see cref="SaveServiceHandler.OnTransformContainer"/> /
    /// <see cref="SaveServiceHandler.OnRestorePayload"/>）的输入/输出载体。
    /// <para>明文处理器经视图别名直通（读写路径零整档拷贝）；加密处理器输出新缓冲区（Offset 为 0）。
    /// 视图为只读引用，不转移缓冲区所有权。</para>
    /// </summary>
    public readonly struct SaveBufferSegment
    {
        /// <summary>承载缓冲区（长度可能大于有效区间——池化租赁缓冲区）。</summary>
        public readonly byte[] Buffer;

        /// <summary>有效区间起始偏移。</summary>
        public readonly int Offset;

        /// <summary>有效区间字节数。</summary>
        public readonly int Length;

        /// <summary>
        /// 创建缓冲区视图。
        /// </summary>
        /// <param name="buffer">承载缓冲区。</param>
        /// <param name="offset">有效区间起始偏移。</param>
        /// <param name="length">有效区间字节数。</param>
        public SaveBufferSegment(byte[] buffer, int offset, int length)
        {
            Buffer = buffer;
            Offset = offset;
            Length = length;
        }

        /// <summary>
        /// 以恰好容纳有效区间的缓冲区创建视图（Offset 为 0）。
        /// </summary>
        /// <param name="buffer">恰好为有效内容的缓冲区。</param>
        /// <returns>缓冲区视图。</returns>
        public static SaveBufferSegment FromExact(byte[] buffer)
        {
            return new SaveBufferSegment(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 有效区间的只读跨度。
        /// </summary>
        /// <returns>只读跨度。</returns>
        public ReadOnlySpan<byte> AsSpan()
        {
            return Buffer.AsSpan(Offset, Length);
        }

        /// <summary>
        /// 取精确长度的字节数组（视图已覆盖整个缓冲区时直接返回原缓冲区，否则拷贝有效区间）。
        /// </summary>
        /// <returns>精确长度字节数组。</returns>
        public byte[] ToExactArray()
        {
            return Offset == 0 && Length == Buffer.Length ? Buffer : AsSpan().ToArray();
        }
    }
}
