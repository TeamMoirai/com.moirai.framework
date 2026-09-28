namespace Moirai.Atropos.ObjectPool
{
    public partial class GameObjectPoolService
    {
        /// <summary>
        /// 获取调试摘要（未就绪时为 default）。
        /// </summary>
        public static GameObjectPoolSummarySnapshot GetDebugSummary() =>
            s_Handler?.GetDebugSummary() ?? default;

        /// <summary>
        /// 获取调试快照（未就绪时为 0）。
        /// </summary>
        public static int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots) =>
            s_Handler?.GetDebugSnapshots(snapshots) ?? 0;

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public static void FillDebugInstances(GameObjectPoolSnapshot snapshot) =>
            s_Handler?.FillDebugInstances(snapshot);
    }
}