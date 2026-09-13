using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档文件元数据（<c>GetSaveFiles</c> 返回值）：文件名、大小与最后写入时间。
    /// </summary>
    public readonly struct SaveFileInfo
    {
        /// <summary>
        /// 存档文件名（不含扩展名，与 <c>Save</c>/<c>Load</c> 的 <c>fileName</c> 参数同构）。
        /// </summary>
        public readonly string FileName;

        /// <summary>
        /// 文件大小（字节）。
        /// </summary>
        public readonly long SizeBytes;

        /// <summary>
        /// 最后写入时间（UTC）。
        /// </summary>
        public readonly DateTime LastWriteTimeUtc;

        /// <summary>
        /// 创建存档元数据。
        /// </summary>
        /// <param name="fileName">存档文件名（不含扩展名）。</param>
        /// <param name="sizeBytes">文件大小（字节）。</param>
        /// <param name="lastWriteTimeUtc">最后写入时间（UTC）。</param>
        public SaveFileInfo(string fileName, long sizeBytes, DateTime lastWriteTimeUtc)
        {
            FileName = fileName;
            SizeBytes = sizeBytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }
    }
}
