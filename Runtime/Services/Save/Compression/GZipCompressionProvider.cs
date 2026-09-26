using System;
using System.IO;
using System.IO.Compression;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// GZip 压缩提供方（<see cref="GZipStream"/>，零新依赖，默认压缩提供方）。
    /// <para>无状态实现，任意线程并发调用安全；共享实例 <see cref="Shared"/> 由 <see cref="SaveCompressionRegistry"/> 内建注册。</para>
    /// </summary>
    [Serializable]
    public class GZipCompressionProvider : SaveCompressionProvider
    {
        /// <summary>GZip 提供方标识（写入文件头）。</summary>
        internal const byte PROVIDER_ID = 1;

        /// <summary>
        /// 共享实例（无状态提供方，注册表与缺省场景复用）。
        /// </summary>
        internal static readonly GZipCompressionProvider Shared = new GZipCompressionProvider();

        /// <summary>
        /// 提供方标识（写入文件头）。
        /// </summary>
        public override byte ProviderId => PROVIDER_ID;

        /// <summary>
        /// 打开 GZip 压缩包装流（leaveOpen——关闭包装流收尾压缩帧但不关闭底层目标流）。
        /// </summary>
        /// <param name="target">压缩字节的落点流。</param>
        /// <returns>压缩包装流（只写）。</returns>
        public override Stream OpenCompressStream(Stream target)
        {
            return new GZipStream(target, CompressionLevel.Optimal, leaveOpen: true);
        }

        /// <summary>
        /// 打开 GZip 解压包装流（数据非法抛 <see cref="InvalidDataException"/>，由读侧归一为 <see cref="SaveError.Corrupted"/>）。
        /// </summary>
        /// <param name="source">压缩字节源流。</param>
        /// <returns>解压包装流（只读）。</returns>
        public override Stream OpenDecompressStream(Stream source)
        {
            return new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        }
    }
}
