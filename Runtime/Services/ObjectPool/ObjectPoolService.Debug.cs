namespace Moirai.Atropos.ObjectPool
{
    partial class ObjectPoolService
    {
        /// <summary>
        /// 获取调试摘要（未就绪时为 default）。
        /// </summary>
        public static ObjectPoolSummarySnapshot GetDebugSummary() =>
            Internal_PeekHandler()?.GetDebugSummary() ?? default;

        /// <summary>
        /// 获取调试快照（未就绪时为 0）。
        /// </summary>
        public static int GetDebugSnapshots(ObjectPoolSnapshot[] snapshots) =>
            Internal_PeekHandler()?.GetDebugSnapshots(snapshots) ?? 0;

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public static void FillDebugInstances(ObjectPoolSnapshot snapshot) =>
            Internal_PeekHandler()?.FillDebugInstances(snapshot);
    }
}