using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云端 KV 条目（读返回体：载荷字节 + 远端时间戳 + 远端单调修订号；值类型，构造后不可变）。
    /// </summary>
    public readonly struct CloudKvEntry
    {
        /// <summary>
        /// 载荷字节。
        /// </summary>
        public readonly byte[] Bytes;

        /// <summary>
        /// 远端最后写入时间（UTC，远端存储权威时钟）。
        /// </summary>
        public readonly DateTime LastWriteTimeUtc;

        /// <summary>
        /// 远端单调修订号（etag 语义：每次远端写入递增；<c>0</c> = 后端不提供版本号，裁决回退时间戳比较）。
        /// <para>版本号比较替代跨设备时间戳比较——客户端时钟偏移不参与裁决（去时钟化）。</para>
        /// </summary>
        public readonly long Version;

        /// <summary>
        /// 创建云端条目（无版本号——裁决回退时间戳比较）。
        /// </summary>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="lastWriteTimeUtc">远端最后写入时间（UTC）。</param>
        public CloudKvEntry(byte[] bytes, DateTime lastWriteTimeUtc)
            : this(bytes, lastWriteTimeUtc, 0L)
        {
        }

        /// <summary>
        /// 创建云端条目。
        /// </summary>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="lastWriteTimeUtc">远端最后写入时间（UTC）。</param>
        /// <param name="version">远端单调修订号（<c>0</c> = 不提供版本号）。</param>
        public CloudKvEntry(byte[] bytes, DateTime lastWriteTimeUtc, long version)
        {
            Bytes = bytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
            Version = version;
        }
    }
}
