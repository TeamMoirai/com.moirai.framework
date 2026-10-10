#if ADDRESSABLES_INSTALLED
namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 记录内核的宿主侧：每帧维护与诊断的接缝转发；记账全在 <see cref="ResourceRecordStore"/>，与 YooAsset 后端共用同一份。
    /// </summary>
    partial class AddressableHandler
    {
        #region 过期回收 [EXPIRY & RECYCLING]

        /// <inheritdoc />
        public override void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
        {
            // 销毁态兜底回收先于预算判定，也先于内核的到期走查——与 YooAsset 侧同一口径，别调换。
            _bindingService?.ProcessDestroyedObjects(destroySweepBudget);
            Store.ProcessResourceMaintenance(unscaledTime, expireBudget);
        }

        /// <inheritdoc />
        public override int ReleaseAllUnusedAssetRecords()
        {
            return Store.ReleaseAllUnusedAssetRecords();
        }

        /// <inheritdoc />
        public override void ForceReleaseAllAssetRecords()
        {
            Store.ForceReleaseAllAssetRecords();
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <inheritdoc />
        public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
        {
            return Store.GetAssetInfos(results, startIndex, maxCount);
        }

        #endregion
    }
}
#endif
