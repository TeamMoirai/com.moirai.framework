using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 同步裁决条目的元信息（一侧存在性/时间戳/尺寸；值类型，构造后不可变）。
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
        /// 创建裁决条目元信息。
        /// </summary>
        /// <param name="exists">该侧是否存在条目。</param>
        /// <param name="lastWriteTimeUtc">最后写入时间（UTC）。</param>
        /// <param name="sizeBytes">条目大小（字节）。</param>
        public SaveSyncEntryInfo(bool exists, DateTime lastWriteTimeUtc, long sizeBytes)
        {
            Exists = exists;
            LastWriteTimeUtc = lastWriteTimeUtc;
            SizeBytes = sizeBytes;
        }
    }
}
