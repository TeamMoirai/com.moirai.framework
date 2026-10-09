using Moirai.Atropos;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Service.Resource
{
    /// <summary>
    /// <see cref="ResourceService"/> 未就绪时的降级行为：配置写入必须抛错，读数必须回空不回 null。
    /// </summary>
    /// <remarks>
    /// 两格都经 <see cref="ResourceService.Internal_PeekHandler"/>/<see cref="ResourceService.Internal_UseHandler"/> 把门面处理器换成 null 再调公共入口，出门还原。<br />
    /// 写侧抛 <see cref="GameException"/> 防「静默丢写」；读侧回空数组防调用方 <c>foreach</c> NRE。
    /// </remarks>
    public sealed class ResourceFacadeUnreadyDegradationTests
    {
        /// <summary>
        /// 配置属性 setter 走 <c>RequireHandler()</c>：未就绪时抛 <see cref="GameException"/>，不得静默丢写。
        /// </summary>
        [Test]
        public void ConfigSetters_WhenHandlerUnready_ThrowInsteadOfSilentDrop()
        {
            ResourceServiceHandler saved = ResourceService.Internal_PeekHandler();
            ResourceService.Internal_UseHandler(null);
            try
            {
                Assert.Throws<GameException>(() => { ResourceService.HostServerURL = "https://example.invalid"; });
                Assert.Throws<GameException>(() => { ResourceService.DefaultPackageName = "Pkg"; });
                Assert.Throws<GameException>(() => { ResourceService.IdleAssetCapacity = 8; });
            }
            finally
            {
                ResourceService.Internal_UseHandler(saved);
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
            ResourceServiceHandler saved = ResourceService.Internal_PeekHandler();
            ResourceService.Internal_UseHandler(null);
            try
            {
                ResourceAssetInfoEntry[] byTag = ResourceService.GetAssetInfos("Preload");
                Assert.IsNotNull(byTag, "GetAssetInfos(tag) 未就绪时不得返回 null。");
                Assert.IsEmpty(byTag);

                ResourceAssetInfoEntry[] byTags = ResourceService.GetAssetInfos(new[] { "Preload" });
                Assert.IsNotNull(byTags, "GetAssetInfos(tags) 未就绪时不得返回 null。");
                Assert.IsEmpty(byTags);
            }
            finally
            {
                ResourceService.Internal_UseHandler(saved);
            }
        }
    }
}
