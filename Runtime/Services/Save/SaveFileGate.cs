using System.Collections.Generic;
using System.Threading;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档文件并发门：按存档文件路径分配串行信号量，防止并发「读-改-写」丢块。
    /// <para>块级写入为读-改-写管线，同文件并发写入会互相覆盖——同一路径的所有读写路径必须持门执行，
    /// 读路径持门换取「不读到写中旧档」的一致性（写入本身原子，读不持门也不会读到半文件）。</para>
    /// <para>信号量按需创建、惰性回收（无持门/等门者时摘除表项），动态槽名场景不累积。</para>
    /// </summary>
    internal static class SaveFileGate
    {
        /// <summary>门表项：串行信号量 + 占用计数（持门者与等门者总数）。</summary>
        private sealed class GateEntry
        {
            /// <summary>串行信号量。</summary>
            internal readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

            /// <summary>占用计数（持门者 + 等门者；受 <see cref="s_Lock"/> 保护）。</summary>
            internal int ActiveCount;
        }

        /// <summary>门表锁（取门/还门极快，非热路径，遵守并发安全 lock 方案）。</summary>
        private static readonly object s_Lock = new object();

        /// <summary>路径 → 门表项。</summary>
        private static readonly Dictionary<string, GateEntry> s_Gates = new Dictionary<string, GateEntry>(System.StringComparer.Ordinal);

        /// <summary>
        /// 取门并登记占用（幂等；必须与 <see cref="Leave"/> 配对，等门期取消也要 Leave）。
        /// </summary>
        /// <param name="saveFilePath">存档文件完整路径。</param>
        /// <returns>该文件的串行信号量。</returns>
        public static SemaphoreSlim Enter(string saveFilePath)
        {
            lock (s_Lock)
            {
                if (!s_Gates.TryGetValue(saveFilePath, out GateEntry entry))
                {
                    entry = new GateEntry();
                    s_Gates.Add(saveFilePath, entry);
                }

                entry.ActiveCount++;
                return entry.Semaphore;
            }
        }

        /// <summary>
        /// 还门并归还占用；无占用者时从表中摘除信号量。
        /// </summary>
        /// <param name="saveFilePath">存档文件完整路径。</param>
        /// <param name="gate"><see cref="Enter"/> 返回的信号量。</param>
        /// <param name="acquired">是否已实际持门（等门期取消传 <c>false</c>，仅归还占用不 Release）。</param>
        public static void Leave(string saveFilePath, SemaphoreSlim gate, bool acquired)
        {
            if (acquired)
            {
                gate.Release();
            }

            lock (s_Lock)
            {
                if (s_Gates.TryGetValue(saveFilePath, out GateEntry entry) && --entry.ActiveCount <= 0)
                {
                    s_Gates.Remove(saveFilePath);
                }
            }
        }
    }
}
