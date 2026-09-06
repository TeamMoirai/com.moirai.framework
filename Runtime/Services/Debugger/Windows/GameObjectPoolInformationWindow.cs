using Moirai.Atropos.ObjectPool;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// GameObject 池信息窗口。
    /// </summary>
    public sealed class GameObjectPoolInformationWindow : PollingDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private readonly GameObjectPoolSnapshot[] _snapshots = new GameObjectPoolSnapshot[64];

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 GameObject 池信息窗口的新实例。
        /// </summary>
        public GameObjectPoolInformationWindow() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            GameObjectPoolSummarySnapshot summary = GameObjectPoolService.GetDebugSummary();
            VisualElement summaryCard = AddSection(root, "GameObject Pool Information");
            AddRow(summaryCard, "Pool Count", summary.PoolCount.ToString());
            AddRow(summaryCard, "Loaded Prefab Count", summary.LoadedPrefabCount.ToString());
            AddRow(summaryCard, "Total Instance Count", summary.TotalInstanceCount.ToString());
            AddRow(summaryCard, "Active Instance Count", summary.ActiveInstanceCount.ToString());
            AddRow(summaryCard, "Inactive Instance Count", summary.InactiveInstanceCount.ToString());
            AddRow(summaryCard, "Pending Maintenance Count", summary.PendingMaintenanceCount.ToString());

            if (!GameObjectPoolService.IsValid)
            {
                root.Add(DebuggerUI.CreateHintLabel("GameObjectPoolService is not registered (opt-in service)."));
                return;
            }

            int count = GameObjectPoolService.GetDebugSnapshots(_snapshots);
            int drawCount = Mathf.Min(count, _snapshots.Length);
            for (int i = 0; i < drawCount; i++)
            {
                DrawPoolSnapshot(root, _snapshots[i]);
            }

            for (int i = 0; i < count; i++)
            {
                MemoryPool.Release(_snapshots[i]);
            }

            if (count > drawCount)
            {
                root.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("... {0} more pools omitted.", count - drawCount)));
            }
        }

        #endregion

        #region 私有 [PRIVATE]

        private static void DrawPoolSnapshot(VisualElement root, GameObjectPoolSnapshot snapshot)
        {
            VisualElement card = AddSection(root, StringUtility.Format("Pool: {0}", snapshot.location));
            AddRow(card, "Entry Name", snapshot.entryName);
            AddRow(card, "Group", snapshot.group);
            AddRow(card, "Policy", snapshot.policy.ToString());
            AddRow(card, "Min Idle", snapshot.minIdle.ToString());
            AddRow(card, "Retain Target", snapshot.retainTarget.ToString());
            AddRow(card, "Soft Capacity", snapshot.softCapacity.ToString());
            AddRow(card, "Hard Capacity", snapshot.hardCapacity.ToString());
            AddRow(card, "Unload Prefab", snapshot.unloadPrefab.ToString());
            AddRow(card, "Total Count", snapshot.totalCount.ToString());
            AddRow(card, "Active Count", snapshot.activeCount.ToString());
            AddRow(card, "Inactive Count", snapshot.inactiveCount.ToString());
            AddRow(card, "Prefab Loaded", snapshot.prefabLoaded.ToString());
            AddRow(card, "Next Maintenance In", snapshot.nextMaintenanceIn >= 0 ? StringUtility.Format("{0:F2}s", snapshot.nextMaintenanceIn) : "N/A");
            AddRow(card, "Spawn / Despawn", StringUtility.Format("{0} / {1}", snapshot.spawnCount, snapshot.despawnCount));
            AddRow(card, "Hit / Miss", StringUtility.Format("{0} / {1}", snapshot.hitCount, snapshot.missCount));
            AddRow(card, "Expand / Destroy", StringUtility.Format("{0} / {1}", snapshot.expandCount, snapshot.destroyCount));
            AddRow(card, "Peak Active", snapshot.peakActive.ToString());
        }

        #endregion
    }
}
