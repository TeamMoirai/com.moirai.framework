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
        /// GZip 压缩容器字节。
        /// </summary>
        /// <param name="raw">容器字节。</param>
        /// <returns>压缩字节。</returns>
        public override byte[] Compress(byte[] raw)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }

        /// <summary>
        /// GZip 解压为容器字节（数据非法抛 <see cref="InvalidDataException"/>，由读侧归一为 <see cref="SaveError.Corrupted"/>）。
        /// </summary>
        /// <param name="packed">压缩字节。</param>
        /// <returns>容器字节。</returns>
        public override byte[] Decompress(byte[] packed)
        {
            using (MemoryStream input = new MemoryStream(packed, writable: false))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
