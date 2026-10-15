#if UNITY_EDITOR
using System;
using System.Diagnostics;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源租约来源与卸载账（仅编辑器参与编译）：开关关掉时取用与卸载路径既不建调用栈也不建字符串。
    /// </summary>
    /// <remarks>
    /// 要答的是两件事后问：这条没还的租约是哪一行取的（引用计数只说"还欠着"，不说谁欠的），
    /// 以及这个资产是在哪一刻被回收的（过期轮只留当下待过期的那些，卸掉的什么都不剩）。
    /// 两者合起来才把"超保活窗口的资产被回收、派生精灵变空白"这类事后现象归到具体一次取用上。 <br />
    /// 本模块整体在 <c>#if UNITY_EDITOR</c> 内：玩家构建不编译、不占内存，也没有可开启的入口。 <br />
    /// 线程契约：跟随资源内核的取用与卸载线程（主线程为常态），环本身不做同步——与它所记录的槽位表同线程。
    /// </remarks>
    internal static class ResourceLeaseProvenance
    {
        /// <summary>卸载账容量：满覆盖最旧，只保证"最近这么多次"可查，不保证全量。</summary>
        internal const int UNLOAD_CAPACITY = 64;

        /// <summary>来源采集开关：默认关闭。</summary>
        internal static bool Enabled { get; set; }

        /// <summary>一次取用的调用来源。</summary>
        internal struct LeaseSource
        {
            /// <summary>类型全名.方法名；采不到时为空串。</summary>
            internal string Display;

            /// <summary>源文件路径（正斜杠）；采不到时为空串。</summary>
            internal string File;

            /// <summary>行号；采不到时为 0。</summary>
            internal int Line;
        }

        /// <summary>一条卸载账。</summary>
        internal readonly struct UnloadRecord
        {
            internal UnloadRecord(string location, string typeName, DateTime unloadTimeUtc)
            {
                Location = location;
                TypeName = typeName;
                UnloadTimeUtc = unloadTimeUtc;
            }

            internal string Location { get; }
            internal string TypeName { get; }
            internal DateTime UnloadTimeUtc { get; }
        }

        private static readonly UnloadRecord[] s_Records = new UnloadRecord[UNLOAD_CAPACITY];
        private static int s_Start;
        private static int s_Count;

        /// <summary>当前留存条数。</summary>
        internal static int UnloadCount => s_Count;

        /// <summary>因容量满而被覆盖掉的条数——读数被截断时靠它知道丢过多少。</summary>
        internal static long UnloadDropped { get; private set; }

        /// <summary>
        /// 采一次调用来源：从本方法的上一层开始，取第一个不属于资源内核的帧。
        /// </summary>
        /// <remarks>没有类型名的帧算真实调用点（照原样收），只有资源内核自身与 <c>System.Runtime</c> 派生帧算内部帧跳过——内部帧判据写成"命名空间前缀"，空类型名不落到这条上。</remarks>
        internal static LeaseSource Capture()
        {
            if (!Enabled)
            {
                return default;
            }

            try
            {
                var stack = new StackTrace(1, true);
                for (int i = 0; i < stack.FrameCount; i++)
                {
                    var method = stack.GetFrame(i)?.GetMethod();
                    if (method == null)
                    {
                        continue;
                    }

                    Type declaringType = method.DeclaringType;
                    string typeName = declaringType != null ? declaringType.FullName : null;
                    if (IsInternalFrame(typeName, declaringType))
                    {
                        continue;
                    }

                    return new LeaseSource
                    {
                        Display = string.IsNullOrEmpty(typeName) ? method.Name : typeName + "." + method.Name,
                        File = (stack.GetFrame(i)?.GetFileName() ?? string.Empty).Replace('\\', '/'),
                        Line = stack.GetFrame(i)?.GetFileLineNumber() ?? 0,
                    };
                }
            }
            catch (Exception)
            {
                // 采集本身失败不能把取用打断：交回空来源，诊断面少一行而已
            }

            return default;
        }

        /// <summary>
        /// 记一条卸载账（满则覆盖最旧）。
        /// </summary>
        internal static void AddUnload(string location, string typeName)
        {
            var record = new UnloadRecord(location ?? string.Empty, typeName ?? string.Empty, DateTime.UtcNow);
            if (s_Count < UNLOAD_CAPACITY)
            {
                s_Records[(s_Start + s_Count) % UNLOAD_CAPACITY] = record;
                s_Count++;
                return;
            }

            s_Records[s_Start] = record;
            s_Start = (s_Start + 1) % UNLOAD_CAPACITY;
            UnloadDropped++;
        }

        /// <summary>按最新优先读一条。</summary>
        internal static UnloadRecord GetNewestUnload(int index)
        {
            if (index < 0 || index >= s_Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return s_Records[(s_Start + s_Count - 1 - index) % UNLOAD_CAPACITY];
        }

        /// <summary>清空卸载账：连引用一起断掉，让被记录的字符串能立刻回收。</summary>
        internal static void ClearUnload()
        {
            Array.Clear(s_Records, 0, s_Records.Length);
            s_Start = 0;
            s_Count = 0;
            UnloadDropped = 0;
        }

        private static bool IsInternalFrame(string typeName, Type declaringType)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            if (typeName.StartsWith("Moirai.Atropos.Resource", StringComparison.Ordinal))
            {
                return true;
            }

            string ns = declaringType?.Namespace;
            return ns != null && ns.StartsWith("System.Runtime", StringComparison.Ordinal);
        }
    }
}
#endif
