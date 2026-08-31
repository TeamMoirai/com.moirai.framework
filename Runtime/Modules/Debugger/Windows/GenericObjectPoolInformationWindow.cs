using Moirai.Atropos.ObjectPool;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 通用对象池信息窗口。
    /// </summary>
    public sealed class GenericObjectPoolInformationWindow : PollingDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private readonly ObjectPoolBase[] _pools = new ObjectPoolBase[64];
        private readonly ObjectInfo[] _objectInfos = new ObjectInfo[64];

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化通用对象池信息窗口的新实例。
        /// </summary>
        public GenericObjectPoolInformationWindow() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!ObjectPoolService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Generic Object Pool Information"));
                root.Add(DebuggerUI.CreateHintLabel("ObjectPoolService is not registered (opt-in service)."));
                return;
            }

            VisualElement card = AddSection(root, "Generic Object Pool Information");
            AddRow(card, "Pool Count", ObjectPoolService.Count.ToString());

            int poolCount = ObjectPoolService.GetAllObjectPools(false, _pools);
            int drawCount = Mathf.Min(poolCount, _pools.Length);
            for (int i = 0; i < drawCount; i++)
            {
                DrawPool(root, _pools[i]);
            }

            if (poolCount > drawCount)
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("... {0} more pools omitted.", poolCount - drawCount)));
            }
        }

        #endregion

        #region 私有 [PRIVATE]

        private void DrawPool(VisualElement root, ObjectPoolBase pool)
        {
            VisualElement card = AddSection(root, pool.FullName);
            AddRow(card, "Object Type", pool.ObjectType.FullName);
            AddRow(card, "Count", pool.Count.ToString());
            AddRow(card, "Capacity", pool.Capacity == int.MaxValue ? "Unlimited" : pool.Capacity.ToString());
            AddRow(card, "Allow Multi Spawn", pool.AllowMultiSpawn.ToString());
            AddRow(card, "Expire Time", pool.ExpireTime >= float.MaxValue ? "Never" : StringUtility.Format("{0:F1}s", pool.ExpireTime));
            AddRow(card, "Auto Release Interval", pool.AutoReleaseInterval >= float.MaxValue ? "Disabled" : StringUtility.Format("{0:F1}s", pool.AutoReleaseInterval));
            AddRow(card, "Priority", pool.Priority.ToString());

            int objectCount = pool.GetAllObjectInfos(_objectInfos);
            int drawObjectCount = Mathf.Min(objectCount, _objectInfos.Length);
            if (drawObjectCount <= 0)
            {
                return;
            }

            for (int i = 0; i < drawObjectCount; i++)
            {
                ObjectInfo info = _objectInfos[i];
                AddRow(card, info.Name ?? "<unnamed>", StringUtility.Format("InUse {0} | Spawn {1} | Locked {2} | CanRelease {3} | LastUse {4:F1}s",
                    info.IsInUse ? "Yes" : "No", info.SpawnCount, info.Locked ? "Yes" : "No", info.CustomCanReleaseFlag ? "Yes" : "No", info.LastUseTime));
            }

            if (objectCount > drawObjectCount)
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("... {0} more objects omitted.", objectCount - drawObjectCount)));
            }
        }

        #endregion
    }
}
