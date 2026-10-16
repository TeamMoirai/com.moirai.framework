using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Editor;
using NUnit.Framework;

namespace Editor.Core.Attributes
{
    /// <summary>
    /// Provider 下拉的归行判据用例集：只测纯函数，不碰 IMGUI 与磁盘。
    /// </summary>
    internal sealed class ProviderCatalogIndexTests
    {
        /// <summary>归行判据的基类夹具：不作 sealed——下面 <see cref="LiveProvider"/> 要继承它，两类都 sealed 会撞 CS0509。</summary>
        private class FakeBase { }

        private sealed class LiveProvider : FakeBase { }

        private static PluginManifest Manifest(string id, string name, string providerType,
            string providerBase = "Editor.Core.Attributes.ProviderCatalogIndexTests+FakeBase",
            string title = "样例后端", string requiresUpm = "com.example.sdk")
        {
            return new PluginManifest
            {
                id = id,
                name = name,
                function = "资源",
                version = "0.1.0",
                minUnityVersion = "2022.3.0",
                minFrameworkVersion = "1.0.2",
                providers = new[]
                {
                    new PluginProvider
                    {
                        providerBase = providerBase,
                        providerType = providerType,
                        title = title,
                        requiresUpm = requiresUpm
                    }
                }
            };
        }

        [Test]
        public void Build_UninstalledPlugin_YieldsOneGreyedRowWithThePluginId()
        {
            var catalog = new List<PluginManifest> { Manifest("sample", "样例插件", "Zed.SampleProvider") };

            ProviderRow[] rows = ProviderCatalogIndex.Build(typeof(FakeBase), Type.EmptyTypes, catalog, Array.Empty<string>());

            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(ProviderRowState.PluginNotInstalled, rows[0].State);
            Assert.AreEqual("样例后端", rows[0].Title);
            Assert.That(rows[0].Description, Does.Contain("sample"));
            Assert.IsNull(rows[0].Candidate, "未安装的行不得带可选类型");
            Assert.IsFalse(rows[0].Selectable);
        }

        [Test]
        public void Build_InstalledAndTypeLoaded_CatalogRowIsSkipped()
        {
            var catalog = new List<PluginManifest> { Manifest("sample", "样例插件", typeof(LiveProvider).FullName) };
            var installed = new List<string> { "sample" };

            ProviderRow[] rows = ProviderCatalogIndex.Build(typeof(FakeBase), new[] { typeof(LiveProvider) }, catalog, installed);

            Assert.AreEqual(1, rows.Length, "已装且类型在场时只留编译候选那一行，不重复铺");
            Assert.AreEqual(ProviderRowState.Usable, rows[0].State);
        }

        /// <summary>
        /// 去重判据不看台账：类型已经编译进域时，账本记没记这条 id 都不再铺清单行——已编译的候选才是权威。
        /// </summary>
        /// <remarks>
        /// 未装但类型在场是本开发工作树的常态：插件包内嵌在 <c>Packages/</c> 里（类型编得过），
        /// 而账本 <c>installed</c> 一条没记。此时若判据还要先看台账，同一后端会同时冒出 Usable 行与一条假「未安装」行。
        /// </remarks>
        [Test]
        public void Build_NotInstalledButTypeLoaded_CatalogRowIsSkipped()
        {
            var catalog = new List<PluginManifest> { Manifest("sample", "样例插件", typeof(LiveProvider).FullName) };

            ProviderRow[] rows = ProviderCatalogIndex.Build(
                typeof(FakeBase), new[] { typeof(LiveProvider) }, catalog, Array.Empty<string>());

            Assert.AreEqual(1, rows.Length, "类型已在场就不得再铺假未安装行：一个后端一行");
            Assert.AreEqual(ProviderRowState.Usable, rows[0].State);
        }

        /// <summary>
        /// <c>providerType</c> 缺席的清单不得抛：走未装一侧时铺一条未安装行，走已装一侧时报未生效。
        /// </summary>
        /// <remarks>
        /// 账本缓存与手工夹具都不保证这个字段非空（<c>PluginManifest.Validate</c> 只覆盖 <c>TryParse</c> 的清单），
        /// 所以这里两侧都投：任一侧抛出 <see cref="ArgumentNullException"/> 都会让本格直接失败，而不是「看着能过」。
        /// </remarks>
        [Test]
        public void Build_NullProviderType_LaysRowsWithoutThrowing()
        {
            var catalog = new List<PluginManifest> { Manifest("sample", "样例插件", null) };

            ProviderRow[] uninstalled = ProviderCatalogIndex.Build(
                typeof(FakeBase), Type.EmptyTypes, catalog, Array.Empty<string>());
            Assert.AreEqual(1, uninstalled.Length);
            Assert.AreEqual(ProviderRowState.PluginNotInstalled, uninstalled[0].State, "类型名缺席时不去重，按未安装铺行");
            Assert.That(uninstalled[0].Description, Does.Contain("sample"));

            // 已装一侧：候选集非空 + 类型名缺席，正是去掉台账短路后新暴露的调用面。
            // 类型名缺席无法判「这个后端已在场」，所以不去重：编译候选一条 Usable，清单行一条未生效。
            ProviderRow[] installed = ProviderCatalogIndex.Build(
                typeof(FakeBase), new[] { typeof(LiveProvider) }, catalog, new List<string> { "sample" });
            Assert.AreEqual(2, installed.Length, "providerType 缺席时不得当成「类型已在场」去重");
            Assert.AreEqual(ProviderRowState.Usable, installed[0].State);
            Assert.AreEqual(ProviderRowState.BackendUnavailable, installed[1].State);
        }

        [Test]
        public void Build_InstalledButTypeAbsent_ReportsTheMissingUpmPackage()
        {
            var catalog = new List<PluginManifest> { Manifest("sample", "样例插件", "Zed.SampleProvider") };
            var installed = new List<string> { "sample" };

            ProviderRow[] rows = ProviderCatalogIndex.Build(typeof(FakeBase), Type.EmptyTypes, catalog, installed);

            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(ProviderRowState.BackendUnavailable, rows[0].State);
            Assert.That(rows[0].Description, Does.Contain("com.example.sdk"));
        }

        [Test]
        public void Build_OtherBaseProvidersIgnored()
        {
            var catalog = new List<PluginManifest>
            {
                Manifest("sample", "样例插件", "Zed.SampleProvider", providerBase: "Moirai.Atropos.SomeOtherHandler")
            };

            ProviderRow[] rows = ProviderCatalogIndex.Build(typeof(FakeBase), Type.EmptyTypes, catalog, Array.Empty<string>());

            Assert.IsEmpty(rows, "providerBase 对不上这个字段的基类就不该冒出来：跨服务串行是假象的来源");
        }

        [Test]
        public void Build_NullCatalog_DegradesToCompiledOnly()
        {
            ProviderRow[] rows = ProviderCatalogIndex.Build(typeof(FakeBase), new[] { typeof(LiveProvider) }, null, null);

            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(ProviderRowState.Usable, rows[0].State);
        }

        [Test]
        public void ActivationIndex_NonSelectableRowReturnsMinusOne()
        {
            var catalog = new List<PluginManifest> { Manifest("sample", "样例插件", "Zed.SampleProvider") };
            ProviderRow[] rows = ProviderCatalogIndex.Build(
                typeof(FakeBase), new[] { typeof(LiveProvider) }, catalog, Array.Empty<string>());

            Assert.AreEqual(0, ProviderCatalogIndex.ActivationIndex(rows, 0));
            Assert.AreEqual(-1, ProviderCatalogIndex.ActivationIndex(rows, 1));
            Assert.AreEqual(-1, ProviderCatalogIndex.ActivationIndex(rows, 99), "越界不得当成可选");
        }
    }
}
