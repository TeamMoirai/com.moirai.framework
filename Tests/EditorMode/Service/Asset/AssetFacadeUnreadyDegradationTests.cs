using Moirai.Atropos;
using Moirai.Atropos.Asset;
using NUnit.Framework;

namespace Service.Asset
{
    /// <summary>
    /// <see cref="AssetService"/> 未就绪时的降级行为：配置写入必须抛错，读数必须回空不回 null。
    /// </summary>
    /// <remarks>
    /// 两格都经 <see cref="AssetService.Internal_PeekHandler"/>/<see cref="AssetService.Internal_UseHandler"/> 把门面处理器换成 null 再调公共入口，出门还原。<br />
    /// 写侧抛 <see cref="MoiraiException"/> 防「静默丢写」；读侧回空数组防调用方 <c>foreach</c> NRE。
    /// </remarks>
    public sealed class AssetFacadeUnreadyDegradationTests
    {
        /// <summary>
        /// 配置属性 setter 走 <c>RequireHandler()</c>：未就绪时抛 <see cref="MoiraiException"/>，不得静默丢写。
        /// </summary>
        [Test]
        public void ConfigSetters_WhenHandlerUnready_ThrowInsteadOfSilentDrop()
        {
            IAssetService saved = AssetService.Internal_PeekHandler();
            AssetService.Internal_UseHandler(null);
            try
            {
                Assert.Throws<MoiraiException>(() => { AssetService.HostServerURL = "https://example.invalid"; });
                Assert.Throws<MoiraiException>(() => { AssetService.DefaultPackageName = "Pkg"; });
                Assert.Throws<MoiraiException>(() => { AssetService.IdleAssetCapacity = 8; });
            }
            finally
            {
                AssetService.Internal_UseHandler(saved);
            }
        }

        /// <summary>
        /// 服务未就绪时 <c>GetAssetInfos(tag/tags)</c> 必须回空数组而不是 null，调用方 <c>foreach</c> 不得 NRE。
        /// </summary>
        /// <remarks>
        /// 读降级口径与 <c>HasAsset</c> 给 NotExist、<c>IsLocationValid</c> 给 false 对齐。
        /// </remarks>
        [Test]
        public void GetAssetInfos_WhenHandlerUnready_ReturnsEmptyNotNull()
        {
            IAssetService saved = AssetService.Internal_PeekHandler();
            AssetService.Internal_UseHandler(null);
            try
            {
                AssetInfoEntry[] byTag = AssetService.GetAssetInfos("Preload");
                Assert.IsNotNull(byTag, "GetAssetInfos(tag) 未就绪时不得返回 null。");
                Assert.IsEmpty(byTag);

                AssetInfoEntry[] byTags = AssetService.GetAssetInfos(new[] { "Preload" });
                Assert.IsNotNull(byTags, "GetAssetInfos(tags) 未就绪时不得返回 null。");
                Assert.IsEmpty(byTags);
            }
            finally
            {
                AssetService.Internal_UseHandler(saved);
            }
        }
    }
}
