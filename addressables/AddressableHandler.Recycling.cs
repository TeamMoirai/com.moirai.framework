#if ADDRESSABLES_INSTALLED
namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的回收面：强制档回收。
    /// </summary>
    /// <remarks>非强制档在 YooAsset 侧推进的是 bundle 卸载操作，Addressables 没有对应物，只接"强制档还掉零引用记录"这一半。</remarks>
    partial class AddressableHandler
    {
        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public override void ForceUnloadAllAssets()
        {
            ReleaseAllUnusedAssetRecords();
        }

        /// <inheritdoc />
        public override void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            ReleaseAllUnusedAssetRecords();
        }

        #endregion
    }
}
#endif
