using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云端 KV 条目元信息（枚举返回体：键/尺寸/远端时间戳；值类型，构造后不可变）。
    /// </summary>
    public readonly struct CloudKvEntryInfo
    {
        /// <summary>
        /// 云端键（相对存档根目录，<c>/</c> 分隔）。
        /// </summary>
        public readonly string Key;

        /// <summary>
        /// 条目大小（字节）。
        /// </summary>
        public readonly long SizeBytes;

        /// <summary>
        /// 远端最后写入时间（UTC）。
        /// </summary>
        public readonly DateTime LastWriteTimeUtc;

        /// <summary>
        /// 创建云端条目元信息。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="sizeBytes">条目大小（字节）。</param>
        /// <param name="lastWriteTimeUtc">远端最后写入时间（UTC）。</param>
        public CloudKvEntryInfo(string key, long sizeBytes, DateTime lastWriteTimeUtc)
        {
            Key = key;
            SizeBytes = sizeBytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }
    }
}
