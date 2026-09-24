using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 记录内核的宿主侧——内核句柄的懒建、几个接缝转发，以及属于表现层的 ResourceOwner 登记。
    /// <para>记账实现全在 <see cref="ResourceRecordStore"/>；这里每多一行，就说明内核的接缝还差一度。</para>
    /// </summary>
    partial class YooAssetHandler
    {
        [NonSerialized] private ResourceRecordStore _store;

        private ResourceRecordStore Store => _store ??= new ResourceRecordStore(this, () => DefaultPackageName);

        internal int LoadingOperationCount => Store.LoadingOperationCount;

        private ResourceOwner EnsureResourceOwner(GameObject root)
        {
            ResourceOwner owner = root.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = root.AddComponent<ResourceOwner>();
            }

            _bindingService.RegisterOwner(owner);
            return owner;
        }

        /// <inheritdoc />
        public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount) =>
            Store.GetAssetInfos(results, startIndex, maxCount);

        /// <inheritdoc />
        internal override void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
        {
            // 销毁态兜底回收先于预算判定，也先于内核的到期走查：没有到期记录可处理时，
            // 被销毁对象的槽位照样要收——这条顺序是这段代码存在的理由，别调换。
            _bindingService?.ProcessDestroyedObjects(destroySweepBudget);
            Store.ProcessResourceMaintenance(unscaledTime, expireBudget);
        }

        /// <inheritdoc />
        internal override int ReleaseAllUnusedAssetRecords() => Store.ReleaseAllUnusedAssetRecords();

        /// <inheritdoc />
        internal override void ForceReleaseAllAssetRecords() => Store.ForceReleaseAllAssetRecords();
    }
}
