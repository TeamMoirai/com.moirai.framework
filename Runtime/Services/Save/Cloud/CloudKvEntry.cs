using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云端 KV 条目（读返回体：载荷字节 + 远端时间戳；值类型，构造后不可变）。
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
        /// 创建云端条目。
        /// </summary>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="lastWriteTimeUtc">远端最后写入时间（UTC）。</param>
        public CloudKvEntry(byte[] bytes, DateTime lastWriteTimeUtc)
        {
            Bytes = bytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }
    }
}
