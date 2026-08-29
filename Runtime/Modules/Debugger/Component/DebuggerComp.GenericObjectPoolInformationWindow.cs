using Moirai.Atropos.ObjectPool;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class GenericObjectPoolInformationWindow : ScrollableDebuggerWindowBase
        {
            #region 字段 [FIELDS]

            private readonly ObjectPoolBase[] _pools = new ObjectPoolBase[64];
            private readonly ObjectInfo[] _objectInfos = new ObjectInfo[64];

            #endregion

            #region 绘制 [DRAW]

            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Generic Object Pool Information</b>");

                if (!ObjectPoolService.IsValid)
                {
                    GUILayout.Label("ObjectPoolService is not registered (opt-in service).");
                    return;
                }

                DrawItem("Pool Count", ObjectPoolService.Count.ToString());

                int poolCount = ObjectPoolService.GetAllObjectPools(false, _pools);
                int drawCount = Mathf.Min(poolCount, _pools.Length);
                for (int i = 0; i < drawCount; i++)
                {
                    DrawPool(_pools[i]);
                }

                if (poolCount > drawCount)
                {
                    DrawItem("Omitted Pools", (poolCount - drawCount).ToString());
                }
            }

            private void DrawPool(ObjectPoolBase pool)
            {
                GUILayout.Label(StringUtility.Format("<b>{0}</b>", pool.FullName));
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Object Type", pool.ObjectType.FullName);
                    DrawItem("Count", pool.Count.ToString());
                    DrawItem("Capacity", pool.Capacity == int.MaxValue ? "Unlimited" : pool.Capacity.ToString());
                    DrawItem("Allow Multi Spawn", pool.AllowMultiSpawn.ToString());
                    DrawItem("Expire Time", pool.ExpireTime >= float.MaxValue ? "Never" : StringUtility.Format("{0:F1}s", pool.ExpireTime));
                    DrawItem("Auto Release Interval", pool.AutoReleaseInterval >= float.MaxValue ? "Disabled" : StringUtility.Format("{0:F1}s", pool.AutoReleaseInterval));
                    DrawItem("Priority", pool.Priority.ToString());

                    int objectCount = pool.GetAllObjectInfos(_objectInfos);
                    int drawObjectCount = Mathf.Min(objectCount, _objectInfos.Length);
                    if (drawObjectCount > 0)
                    {
                        GUILayout.Label("<b>In Use | Spawn Count | Locked | Can Release | Last Use (s)</b>");
                        for (int i = 0; i < drawObjectCount; i++)
                        {
                            ObjectInfo info = _objectInfos[i];
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.Label(info.Name ?? "<unnamed>", GUILayout.Width(220f));
                                GUILayout.Label(info.IsInUse ? "Yes" : "No", GUILayout.Width(50f));
                                GUILayout.Label(info.SpawnCount.ToString(), GUILayout.Width(90f));
                                GUILayout.Label(info.Locked ? "Yes" : "No", GUILayout.Width(60f));
                                GUILayout.Label(info.CustomCanReleaseFlag ? "Yes" : "No", GUILayout.Width(80f));
                                GUILayout.Label(info.LastUseTime.ToString("F1"));
                            }
                            GUILayout.EndHorizontal();
                        }

                        if (objectCount > drawObjectCount)
                        {
                            DrawItem("Omitted Objects", (objectCount - drawObjectCount).ToString());
                        }
                    }
                }
                GUILayout.EndVertical();
            }

            #endregion
        }
    }
}
