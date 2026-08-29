namespace Moirai.Atropos.ObjectPool
{
    public partial class GameObjectPoolService
    {
        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        public static GameObjectPoolSummarySnapshot GetDebugSummary() =>
            Handler.GetDebugSummary();

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public static int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots) =>
            Handler.GetDebugSnapshots(snapshots);

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public static void FillDebugInstances(GameObjectPoolSnapshot snapshot) =>
            Handler.FillDebugInstances(snapshot);
    }
}