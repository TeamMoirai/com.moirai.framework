using System;
using UnityEngine;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 记录内核的宿主侧：内核句柄的懒建、接缝转发，以及表现层的 ResourceOwner 登记；记账全在 <see cref="ResourceRecordStore"/>。
    /// </summary>
    partial class YooAssetHandler
    {
        [NonSerialized] private ResourceRecordStore _store;

        private ResourceRecordStore Store => _store ??= new ResourceRecordStore(this, () => DefaultPackageName);

        internal int LoadingOperationCount => Store.LoadingOperationCount;

        /// <inheritdoc />
        public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount) =>
            Store.GetAssetInfos(results, startIndex, maxCount);

        /// <inheritdoc />
        public override void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
        {
            // 销毁态兜底回收先于预算判定，也先于内核的到期走查：没有到期记录可处理时，
            // 被销毁对象的槽位照样要收——这条顺序是这段代码存在的理由，别调换。
            _bindingService?.ProcessDestroyedObjects(destroySweepBudget);
            Store.ProcessResourceMaintenance(unscaledTime, expireBudget);
        }

        /// <inheritdoc />
        public override int ReleaseAllUnusedAssetRecords() => Store.ReleaseAllUnusedAssetRecords();

        /// <inheritdoc />
        public override void ForceReleaseAllAssetRecords() => Store.ForceReleaseAllAssetRecords();

        #region 内核句柄接缝 [KERNEL HANDLE SEAM]

        private bool IsHandleValid(object handle)
        {
            return handle is HandleBase { IsValid: true };
        }

        private void DisposeHandle(object handle)
        {
            if (handle is HandleBase { IsValid: true } valid)
            {
                valid.Dispose();
            }
        }

        private Sprite GetSubSprite(object handle, string spriteName)
        {
            return (handle as SubAssetsHandle)?.GetSubAssetObject<Sprite>(spriteName);
        }

        // 接口成员要 public 才能隐式实现；这三个算子是 handler 的内部件，故显式接线。
        bool IResourceRecordHost.IsHandleValid(object handle) => IsHandleValid(handle);

        void IResourceRecordHost.DisposeHandle(object handle) => DisposeHandle(handle);

        Sprite IResourceRecordHost.GetSubSprite(object handle, string spriteName) =>
            GetSubSprite(handle, spriteName);

        #endregion
    }
}
