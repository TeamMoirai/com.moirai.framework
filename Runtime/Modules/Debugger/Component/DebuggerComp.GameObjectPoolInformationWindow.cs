using Moirai.Atropos.GameObjectPool;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class GameObjectPoolInformationWindow : ScrollableDebuggerWindowBase
        {
            private IGameObjectPoolService _gameObjectPoolService = null;

            public override void Initialize(params object[] args)
            {
                _gameObjectPoolService = GameServices.Provider?.GetService<IGameObjectPoolService>();
                if (_gameObjectPoolService == null)
                {
                    LogUtility.Fatal("GameObjectPool service is invalid.");
                    return;
                }
            }

            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>GameObject Pool Information</b>");

                if (!(_gameObjectPoolService is GameObjectPoolService service))
                {
                    GUILayout.Label("<i>Service implementation does not support debug interface.</i>");
                    return;
                }

                GameObjectPoolSummarySnapshot summary = service.GetDebugSummary();
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Pool Count", summary.PoolCount.ToString());
                    DrawItem("Loaded Prefab Count", summary.LoadedPrefabCount.ToString());
                    DrawItem("Total Instance Count", summary.TotalInstanceCount.ToString());
                    DrawItem("Active Instance Count", summary.ActiveInstanceCount.ToString());
                    DrawItem("Inactive Instance Count", summary.InactiveInstanceCount.ToString());
                    DrawItem("Pending Maintenance Count", summary.PendingMaintenanceCount.ToString());
                }
                GUILayout.EndVertical();

                GameObjectPoolSnapshot[] snapshots = new GameObjectPoolSnapshot[64];
                int count = service.GetDebugSnapshots(snapshots);
                for (int i = 0; i < count; i++)
                {
                    DrawPoolSnapshot(snapshots[i]);
                }

                for (int i = 0; i < count; i++)
                {
                    MemoryPool.Release(snapshots[i]);
                }
            }

            private void DrawPoolSnapshot(GameObjectPoolSnapshot snapshot)
            {
                GUILayout.Label(StringUtility.Format("<b>Pool: {0}</b>", snapshot.location));
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Entry Name", snapshot.entryName);
                    DrawItem("Group", snapshot.group);
                    DrawItem("Policy", snapshot.policy.ToString());
                    DrawItem("Min Idle", snapshot.minIdle.ToString());
                    DrawItem("Retain Target", snapshot.retainTarget.ToString());
                    DrawItem("Soft Capacity", snapshot.softCapacity.ToString());
                    DrawItem("Hard Capacity", snapshot.hardCapacity.ToString());
                    DrawItem("Unload Prefab", snapshot.unloadPrefab.ToString());
                    DrawItem("Total Count", snapshot.totalCount.ToString());
                    DrawItem("Active Count", snapshot.activeCount.ToString());
                    DrawItem("Inactive Count", snapshot.inactiveCount.ToString());
                    DrawItem("Prefab Loaded", snapshot.prefabLoaded.ToString());
                    DrawItem("Next Maintenance In", snapshot.nextMaintenanceIn >= 0 ? snapshot.nextMaintenanceIn.ToString("F2") + "s" : "N/A");
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Label("<b>Spawn</b>", GUILayout.Width(80f));
                        GUILayout.Label("<b>Despawn</b>", GUILayout.Width(80f));
                        GUILayout.Label("<b>Hit</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Miss</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Expand</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Destroy</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Peak</b>", GUILayout.Width(60f));
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Label(snapshot.spawnCount.ToString(), GUILayout.Width(80f));
                        GUILayout.Label(snapshot.despawnCount.ToString(), GUILayout.Width(80f));
                        GUILayout.Label(snapshot.hitCount.ToString(), GUILayout.Width(60f));
                        GUILayout.Label(snapshot.missCount.ToString(), GUILayout.Width(60f));
                        GUILayout.Label(snapshot.expandCount.ToString(), GUILayout.Width(60f));
                        GUILayout.Label(snapshot.destroyCount.ToString(), GUILayout.Width(60f));
                        GUILayout.Label(snapshot.peakActive.ToString(), GUILayout.Width(60f));
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }
    }
}
