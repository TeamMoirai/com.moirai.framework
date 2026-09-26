using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 同步裁决条目的元信息（一侧存在性/时间戳/尺寸/版本号；值类型，构造后不可变）。
    /// </summary>
    public readonly struct SaveSyncEntryInfo
    {
        /// <summary>
        /// 该侧是否存在条目。
        /// </summary>
        public readonly bool Exists;

        /// <summary>
        /// 最后写入时间（UTC；不存在时为 <c>default</c>）。
        /// </summary>
        public readonly DateTime LastWriteTimeUtc;

        /// <summary>
        /// 条目大小（字节；不存在时为 0）。
        /// </summary>
        public readonly long SizeBytes;

        /// <summary>
        /// 修订号（远端侧 = 远端单调修订号；本地侧 = 镜像已同步的远端修订号；<c>0</c> = 无版本信息，裁决器应回退时间戳比较）。
        /// </summary>
        public readonly long Version;

        /// <summary>
        /// 创建裁决条目元信息（无版本号）。
        /// </summary>
        /// <param name="exists">该侧是否存在条目。</param>
        /// <param name="lastWriteTimeUtc">最后写入时间（UTC）。</param>
        /// <param name="sizeBytes">条目大小（字节）。</param>
        public SaveSyncEntryInfo(bool exists, DateTime lastWriteTimeUtc, long sizeBytes)
            : this(exists, lastWriteTimeUtc, sizeBytes, 0L)
        {
        }

        /// <summary>
        /// 创建裁决条目元信息。
        /// </summary>
        /// <param name="exists">该侧是否存在条目。</param>
        /// <param name="lastWriteTimeUtc">最后写入时间（UTC）。</param>
        /// <param name="sizeBytes">条目大小（字节）。</param>
        /// <param name="version">修订号（<c>0</c> = 无版本信息）。</param>
        public SaveSyncEntryInfo(bool exists, DateTime lastWriteTimeUtc, long sizeBytes, long version)
        {
            Exists = exists;
            LastWriteTimeUtc = lastWriteTimeUtc;
            SizeBytes = sizeBytes;
            Version = version;
        }
    }
}
