using System.Collections.Concurrent;
using System.Threading;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档文件并发门：按存档文件路径分配串行信号量，防止并发「读-改-写」丢块。
    /// <para>块级写入为读-改-写管线，同文件并发写入会互相覆盖——同一路径的所有写路径必须持门执行。</para>
    /// <para>信号量按需创建、不回收（数量受存档槽数约束，无泄漏风险）。</para>
    /// </summary>
    internal static class SaveFileGate
    {
        /// <summary>路径 → 串行信号量表。</summary>
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_Gates = new ConcurrentDictionary<string, SemaphoreSlim>();

        /// <summary>
        /// 获取指定存档文件的串行门（幂等）。
        /// </summary>
        /// <param name="saveFilePath">存档文件完整路径。</param>
        /// <returns>该文件的串行信号量。</returns>
        public static SemaphoreSlim Get(string saveFilePath)
        {
            return s_Gates.GetOrAdd(saveFilePath, static _ => new SemaphoreSlim(1, 1));
        }
    }
}
