using System;
using System.Collections.Generic;
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
    /// 写侧抛 <see cref="GameException"/> 防「静默丢写」；读侧回空数组防调用方 <c>foreach</c> NRE。
    /// </remarks>
    public sealed class AssetFacadeUnreadyDegradationTests
    {
        /// <summary>
        /// 配置属性 setter 走 <c>RequireHandler()</c>：未就绪时抛 <see cref="GameException"/>，不得静默丢写。
        /// </summary>
        [Test]
        public void ConfigSetters_WhenHandlerUnready_ThrowInsteadOfSilentDrop()
        {
            IAssetService saved = AssetService.Internal_PeekHandler();
            AssetService.Internal_UseHandler(null);
            try
            {
                Assert.Throws<GameException>(() => { AssetService.HostServerURL = "https://example.invalid"; });
                Assert.Throws<GameException>(() => { AssetService.DefaultPackageName = "Pkg"; });
                Assert.Throws<GameException>(() => { AssetService.IdleAssetCapacity = 8; });
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

        /// <summary>
        /// 未就绪读的告警契约：同一门面在 null 窗口内连续多次读，至多产生一条聚合告警（一次性闩）。
        /// </summary>
        /// <remarks>
        /// 闩是每门面静态、随 Play 会话复位——编辑器同域下可能已被更早测试消耗，故只断言「至多一次」 <br />
        /// 的去重不变量与文案前缀，不断言「至少一次」（那会与执行序耦合）。 <br />
        /// 内容断言走 <see cref="LogUtility.onMessageLogged"/>（与日志处理器无关的通道，UTF 语义见 <c>UtfLogExpect</c>）。
        /// </remarks>
        [Test]
        public void UnreadyRead_WarningIsAggregated_AtMostOncePerFacade()
        {
            IAssetService saved = AssetService.Internal_PeekHandler();
            AssetService.Internal_UseHandler(null);

            var warnings = new List<string>();
            void OnMessage(LogHandler.ELogLevel level, string message, Exception _)
            {
                if (level == LogHandler.ELogLevel.Warning) warnings.Add(message);
            }

            LogUtility.onMessageLogged += OnMessage;
            try
            {
                _ = AssetService.GetAssetInfos("Preload");
                _ = AssetService.GetAssetInfos("Preload");
                _ = AssetService.GetAssetInfos("Preload");
            }
            finally
            {
                LogUtility.onMessageLogged -= OnMessage;
                AssetService.Internal_UseHandler(saved);
            }

            Assert.LessOrEqual(warnings.Count, 1,
                "未就绪读告警必须聚合去重（每门面会话至多一条），不得随读次数放大");
            foreach (string warning in warnings)
            {
                StringAssert.StartsWith("Facade unready read", warning,
                    "告警文案须带稳定前缀，供日志检索与测试过滤");
            }
        }
    }
}
