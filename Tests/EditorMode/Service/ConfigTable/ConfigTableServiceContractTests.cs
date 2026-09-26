using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
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
    /// <para>配置表此前零用例，而它是本地化的默认数据源、启动链上第一个会读表的东西——
    /// 历史故障（本地化先于资源初始化时读表，处理器停在半初始化态并持续 NRE）就落在这里。
    /// 本组钉住三件事：外观在无后端时的降级值、默认后端的兜底语义、以及服务依赖声明的存在性。</para>
    /// <para>不建自定义 Handler 子类：<see cref="ConfigTableServiceHandler"/> 是 [Serializable] 框架基类，
    /// 经 [SerializeReference] 用在设置资产里，测试程序集里的派生类会污染生产资产的 Inspector 下拉框。
    /// 需要"已安装后端"的场景直接用包内默认实现 <c>DefaultConfigTableHandler</c>（internal，InternalsVisibleTo 可见）。</para>
    /// </summary>
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
            ConfigTableService.Internal_UseHandler(_savedHandler);
        }

        #region 无后端时的降级值 [DEGRADED VALUES]

        [Test]
        public void NoHandler_GetAllLocalizedStrings_ReturnsNull()
        {
            Assert.IsNull(ConfigTableService.GetAllLocalizedStrings());
        }

        /// <summary>
        /// 语言自报在未就绪时必须返回<b>空列表而非 null</b>——调用方（本地化）直接遍历它，
        /// 返回 null 会把"数据未就绪"变成 NRE。
        /// </summary>
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
        /// 按语言取列的开关在无后端时必须为 <c>false</c>：本地化侧据此留在整批路径，
        /// 而不是进了列模式却永远取不到列。
        /// </summary>
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
        /// 已安装后端时外观确实转发到后端：默认后端回 <c>string.Empty</c>，与"无后端"的 null 可区分，
        /// 顺带用日志证明是后端被调到（不是外观自己编了个值）。
        /// </summary>
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
        /// 资源依赖必须显式声明：配置表后端（游戏侧 Luban 处理器）经资源系统装载表字节，
        /// 依赖缺失会让初始化序退化为注册序——本地化先于资源初始化时读表即失败并停在半初始化态
        /// （2026-09-22 实证的启动期 NRE 根因）。这条用例就是那个历史故障的回归锁。
        /// </summary>
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

        #region 后端接缝形状 [HANDLER SEAM SHAPE]

        /// <summary>
        /// 后端接缝形状守卫：四个抽象成员。
        /// <para>游戏侧生成代码派生本类并实现这四个成员；少一个或改签名不会有任何编译错误，
        /// 只会在游戏侧装机时才炸。这里把形状钉住。</para>
        /// </summary>
        [Test]
        public void Seam_AbstractMembers_ShapeMatchesContract()
        {
            Type seam = typeof(ConfigTableServiceHandler);

            AssertAbstractMethod(seam, "GetAllLocalizedStrings", typeof(Dictionary<string, List<string>>));
            AssertAbstractMethod(seam, "GetLocalizationLanguageCodes", typeof(IReadOnlyList<string>));
            AssertAbstractMethod(seam, "LoadSpriteByID", typeof(UniTask<Sprite>), typeof(string), typeof(System.Threading.CancellationToken));
            AssertAbstractMethod(seam, "GetUIWindowLocation", typeof(string), typeof(string));

            int abstractCount = seam.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(method => method.IsAbstract && !method.Name.StartsWith("get_", StringComparison.Ordinal));
            Assert.AreEqual(4, abstractCount, "后端接缝的抽象方法数变了；同步本基线并写明收掉了什么。");
        }

        /// <summary>
        /// 按语言取列是<b>可选</b>接缝：两个成员都必须带默认实现，且默认落在整批模式。
        /// <para>默认开成 <c>true</c> 会让存量整批后端进不了列模式却再也拿不到语言头，
        /// 把接缝变抽象则会打断所有游戏侧生成代码——两者都只会在装机时炸，所以钉在这里。</para>
        /// </summary>
        [Test]
        public void Seam_PerLanguageColumnLoad_IsVirtualWithBatchDefaults()
        {
            Type seam = typeof(ConfigTableServiceHandler);

            MethodInfo supportGetter = seam.GetProperty("SupportsPerLanguageLocalizationLoad")?.GetMethod;
            MethodInfo columnLoader = seam.GetMethod("GetLocalizedStringsByLanguage", new[] { typeof(string) });

            Assert.IsNotNull(supportGetter, "按语言取列的开关缺失。");
            Assert.IsNotNull(columnLoader, "按语言取列的取列入口缺失。");
            Assert.IsFalse(supportGetter.IsAbstract, "开关必须带默认实现（存量整批后端零改）。");
            Assert.IsFalse(columnLoader.IsAbstract, "取列入口必须带默认实现。");
            Assert.IsTrue(supportGetter.IsVirtual && columnLoader.IsVirtual);

            var defaultHandler = new DefaultConfigTableHandler();
            Assert.IsFalse(defaultHandler.SupportsPerLanguageLocalizationLoad,
                "未覆写的后端必须留在整批路径。");
            Assert.IsNull(defaultHandler.GetLocalizedStringsByLanguage("en"),
                "未覆写的取列实现必须回 null（视为未就绪），不得回空字典冒充已加载的空列。");
        }

        /// <summary>
        /// 编辑器预览取数挂在外观的静态入口上，后端不带预览专用虚方法。
        /// <para>「非播放态不经服务世界取到表」由外观解决（未注册处理器时经 Settings 里配置的那份实例），
        /// 后端因此只有一条读表路径——多挂一个预览虚方法就是第二条迟早与真路径漂移的路径。</para>
        /// </summary>
        [Test]
        public void Seam_EditorPreview_IsOnFacadeNotOnHandler()
        {
            Assert.IsNull(typeof(ConfigTableServiceHandler).GetMethod("GetLocalizedStringsForEditorPreview"),
                "后端不该再挂预览专用虚方法：读表只有一条路径。");

            MethodInfo preview = typeof(ConfigTableService).GetMethod("GetAllLocalizedStringsForEditor");

            Assert.IsNotNull(preview, "编辑器预览取数入口缺失。");
            Assert.IsTrue(preview.IsStatic, "预览取数是外观的静态入口，不要求服务世界起来。");
        }

        #endregion

        #region 辅助 [HELPERS]

        private static void InstallDefaultHandler()
        {
            ConfigTableService.Internal_UseHandler(new DefaultConfigTableHandler());
        }

        private static void AssertAbstractMethod(Type owner, string name, Type returnType, params Type[] parameters)
        {
            MethodInfo method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);

            Assert.IsNotNull(method, $"{owner.Name}.{name} 缺失或签名已变。");
            Assert.IsTrue(method.IsAbstract, $"{owner.Name}.{name} 应为抽象成员（后端必须实现）。");
            Assert.AreEqual(returnType, method.ReturnType, $"{owner.Name}.{name} 返回类型已变。");
        }

        /// <summary>
        /// 经 <see cref="LogUtility.OnMessageLogged"/> 捕获日志内容（Handler 无关的唯一稳定通道）。
        /// <para>同时经 <see cref="UtfLogExpect"/> 声明一条 UTF 预期：默认后端走 LogUtility.Error，
        /// 当前 Handler 对 UTF 可见时不声明会把测试判成"未处理日志"而红。</para>
        /// </summary>
        private sealed class LogCapture : IDisposable
        {
            private readonly List<string> _messages = new List<string>();

            public LogCapture()
            {
                UtfLogExpect.Error();

                LogUtility.OnMessageLogged += OnLogged;
            }

            public bool Mentions(string fragment)
            {
                return _messages.Exists(message => message != null && message.Contains(fragment, StringComparison.Ordinal));
            }

            public void Dispose()
            {
                LogUtility.OnMessageLogged -= OnLogged;
            }

            private void OnLogged(ELogLevel level, string message, Exception exception)
            {
                _messages.Add(message);
            }
        }

        #endregion
    }
}
