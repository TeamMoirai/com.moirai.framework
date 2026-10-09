using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Moirai.Atropos;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;

namespace Service.ConfigTable
{
    /// <summary>
    /// <see cref="ConfigTableService"/> 外观与默认后端的契约测试。
    /// </summary>
    /// <remarks>
    /// 钉三件事：外观在无后端时的降级值、默认后端的兜底语义、服务依赖声明的存在性。 <br />
    /// 本组不建自定义 Handler 子类：<see cref="ConfigTableServiceHandler"/> 是 [Serializable] 框架基类，经 [SerializeReference] 用在设置资产里，
    /// 测试程序集里的派生类会污染生产资产的 Inspector 下拉框；需要「已安装后端」的场景直接用包内默认实现 <c>DefaultConfigTableHandler</c>（internal）。
    /// </remarks>
    [TestFixture]
    public sealed class ConfigTableServiceContractTests
    {
        private ConfigTableServiceHandler _savedHandler;

        [SetUp]
        public void SetUp()
        {
            _savedHandler = ConfigTableService.Internal_PeekHandler();
            ConfigTableService.Internal_UseHandler(null);
        }

        [TearDown]
        public void TearDown()
        {
            // 还原外观处理器必须放 finally：本夹具 TearDown 前无其他清理步骤，但 try/finally 形态
            // 保证未来往 TearDown 前插步骤时，null 后端不会被断言异常泄漏给后续用例
            try
            {
            }
            finally
            {
                ConfigTableService.Internal_UseHandler(_savedHandler);
            }
        }

        #region 无后端时的降级值 [DEGRADED VALUES]

        [Test]
        public void NoHandler_GetAllLocalizedStrings_ReturnsNull()
        {
            Assert.IsNull(ConfigTableService.GetAllLocalizedStrings());
        }

        /// <summary>
        /// 语言自报在未就绪时必须返回空列表而非 null。
        /// </summary>
        /// <remarks>调用方（本地化）直接遍历它，返回 null 会把「数据未就绪」变成 NRE。</remarks>
        [Test]
        public void NoHandler_GetLocalizationLanguageCodes_ReturnsEmptyNotNull()
        {
            IReadOnlyList<string> codes = ConfigTableService.GetLocalizationLanguageCodes();

            Assert.IsNotNull(codes, "未就绪时必须回空列表，不得回 null（调用方直接遍历）。");
            Assert.IsEmpty(codes);
        }

        [Test]
        public void NoHandler_GetUIWindowLocation_ReturnsNull()
        {
            Assert.IsNull(ConfigTableService.GetUIWindowLocation("any"));
        }

        [Test]
        public void NoHandler_LoadSpriteByID_ReturnsNullSprite()
        {
            Sprite sprite = ConfigTableService.LoadSpriteByID("any").GetAwaiter().GetResult();

            Assert.IsNull(sprite);
        }

        /// <summary>
        /// 按语言取列的开关在无后端时必须为 <c>false</c>。
        /// </summary>
        /// <remarks>本地化侧据此留在整批路径，而不是进了列模式却永远取不到列。</remarks>
        [Test]
        public void NoHandler_SupportsPerLanguageLocalizationLoad_IsFalse()
        {
            Assert.IsFalse(ConfigTableService.SupportsPerLanguageLocalizationLoad);
        }

        [Test]
        public void NoHandler_GetLocalizedStringsByLanguage_ReturnsNull()
        {
            Assert.IsNull(ConfigTableService.GetLocalizedStringsByLanguage("en"));
        }

        #endregion

        #region 默认后端兜底语义 [DEFAULT BACKEND FALLBACK]

        [Test]
        public void DefaultHandler_GetAllLocalizedStrings_ReturnsNullAndLogs()
        {
            InstallDefaultHandler();

            using (LogCapture capture = new LogCapture())
            {
                Assert.IsNull(ConfigTableService.GetAllLocalizedStrings());

                Assert.IsTrue(capture.Mentions("Generate Config first!"),
                    "默认后端必须把「未生成配置」喊出来，否则读表失败会静默。");
            }
        }

        [Test]
        public void DefaultHandler_GetLocalizationLanguageCodes_ReturnsEmpty()
        {
            InstallDefaultHandler();

            using (LogCapture capture = new LogCapture())
            {
                Assert.IsEmpty(ConfigTableService.GetLocalizationLanguageCodes());
                Assert.IsTrue(capture.Mentions("Generate Config first!"));
            }
        }

        [Test]
        public void DefaultHandler_LoadSpriteByID_ReturnsNullSprite()
        {
            InstallDefaultHandler();

            using (LogCapture capture = new LogCapture())
            {
                Assert.IsNull(ConfigTableService.LoadSpriteByID("any").GetAwaiter().GetResult());
                Assert.IsTrue(capture.Mentions("Generate Config first!"));
            }
        }

        /// <summary>
        /// 已安装后端时外观确实转发到后端：默认后端回 <c>string.Empty</c>，与「无后端」的 null 可区分。
        /// </summary>
        /// <remarks>顺带用日志证明是后端被调到，而不是外观自己编了个值。</remarks>
        [Test]
        public void DefaultHandler_GetUIWindowLocation_ForwardsToHandler()
        {
            InstallDefaultHandler();

            using (LogCapture capture = new LogCapture())
            {
                Assert.AreEqual(string.Empty, ConfigTableService.GetUIWindowLocation("any"),
                    "无后端是 null，有后端应转发到后端的 string.Empty。");
                Assert.IsTrue(capture.Mentions("Generate Config first!"), "转发证据：后端确实被调到。");
            }
        }

        /// <summary>
        /// 默认后端按语言取列：开关停在整批模式，未覆写的取列实现回 null（视为未就绪）。
        /// </summary>
        /// <remarks>回空字典会冒充「已加载的空列」，本地化会把空列当真；回 null 让调用方留在整批路径。</remarks>
        [Test]
        public void DefaultHandler_GetLocalizedStringsByLanguage_ReturnsNullAndStaysInBatchMode()
        {
            var defaultHandler = new DefaultConfigTableHandler();

            Assert.IsFalse(defaultHandler.SupportsPerLanguageLocalizationLoad,
                "未覆写的后端必须留在整批路径。");
            Assert.IsNull(defaultHandler.GetLocalizedStringsByLanguage("en"),
                "未覆写的取列实现必须回 null（视为未就绪），不得回空字典冒充已加载的空列。");
        }

        #endregion

        #region 关闭语义 [SHUTDOWN]

        [Test]
        public void OnShutdown_ClearsHandler_AndFacadeFallsBackToDegraded()
        {
            InstallDefaultHandler();

            ConfigTableService service = new ConfigTableService();
            service.OnShutdown();

            Assert.IsNull(ConfigTableService.Internal_PeekHandler(), "关闭后必须摘掉后端引用。");
            Assert.IsNull(ConfigTableService.GetUIWindowLocation("any"),
                "关闭后外观应回到降级值（null），而不是继续用已关闭的后端。");
        }

        [Test]
        public void OnShutdown_WithoutHandler_IsNoOp()
        {
            ConfigTableService service = new ConfigTableService();

            Assert.DoesNotThrow(() => service.OnShutdown());
            Assert.IsNull(ConfigTableService.Internal_PeekHandler());
        }

        #endregion

        #region 服务契约 [SERVICE CONTRACT]

        /// <summary>
        /// 资源依赖必须显式声明，否则初始化序会退化为注册序。
        /// </summary>
        /// <remarks>配置表后端（游戏侧 Luban 处理器）经资源系统装载表字节；依赖缺失时本地化先于资源初始化读表即失败，并停在半初始化态持续报 NRE。</remarks>
        [Test]
        public void Service_DeclaresResourceDependency()
        {
            ServiceDependencyAttribute[] declared = typeof(ConfigTableService)
                .GetCustomAttributes(typeof(ServiceDependencyAttribute), false)
                .Cast<ServiceDependencyAttribute>()
                .ToArray();

            Assert.IsNotEmpty(declared, "ConfigTableService 必须声明服务依赖（首读表依赖资源系统就绪）。");

            Type[] dependencies = declared.SelectMany(attribute => attribute.DependencyTypes).ToArray();
            CollectionAssert.Contains(dependencies, typeof(ResourceService),
                "配置表首读表经资源系统装载，ResourceService 依赖必须显式声明。");
        }

        [Test]
        public void Service_IsAutoRegistered()
        {
            Assert.IsNotNull(typeof(ConfigTableService).GetCustomAttribute<AutoRegisterServiceAttribute>(),
                "内置 App 服务必须带 [AutoRegisterService]，否则不会进生成的注册清单。");
        }

        [Test]
        public void Service_Priority_IsMidTier()
        {
            Assert.AreEqual(ServicePriorityOrder.MID_TIER, new ConfigTableService().Priority);
        }

        #endregion

        #region 辅助 [HELPERS]

        private static void InstallDefaultHandler()
        {
            ConfigTableService.Internal_UseHandler(new DefaultConfigTableHandler());
        }

        /// <summary>
        /// 经 <see cref="LogUtility.onMessageLogged"/> 捕获日志内容（Handler 无关的唯一稳定通道）。
        /// </summary>
        /// <remarks>同时经 <see cref="UtfLogExpect"/> 声明一条 UTF 预期：默认后端走 LogUtility 的 Error 级发射， <br />
        /// 当前 Handler 对 UTF 可见时不声明会把测试判成「未处理日志」而失败。</remarks>
        private sealed class LogCapture : IDisposable
        {
            private readonly List<string> _messages = new List<string>();

            public LogCapture()
            {
                UtfLogExpect.Error();

                LogUtility.onMessageLogged += OnLogged;
            }

            public bool Mentions(string fragment)
            {
                return _messages.Exists(message => message != null && message.Contains(fragment, StringComparison.Ordinal));
            }

            public void Dispose()
            {
                LogUtility.onMessageLogged -= OnLogged;
            }

            private void OnLogged(ELogLevel level, string message, Exception exception)
            {
                _messages.Add(message);
            }
        }

        #endregion
    }
}
