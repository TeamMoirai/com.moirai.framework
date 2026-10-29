using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Plugins;
using NUnit.Framework;

namespace Plugins
{
    public sealed class PluginCatalogTests
    {
        private sealed class Probe : IPlugin
        {
            public Probe(string id) { Id = new PluginId(id); }
            public PluginId Id { get; }
            public UniTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
                => UniTask.CompletedTask;
            public UniTask StartAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
            public void Tick() { }
            public UniTask StopAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask DisposeAsync() => UniTask.CompletedTask;
        }

        private static PluginDescriptor Describe(string id, PluginCapability required,
            Func<IPlugin> factory)
            => new PluginDescriptor(new PluginId(id), new PluginVersion(1, 0, 0), required, factory);

        [Test]
        public void Catalog_RejectsDuplicateIds()
        {
            Assert.Throws<InvalidOperationException>(() => new PluginCatalog(PluginCapability.Resource,
                new[]
                {
                    Describe("a", PluginCapability.None, () => new Probe("a")),
                    Describe("a", PluginCapability.None, () => new Probe("a"))
                }));
        }

        [Test]
        public void Catalog_RejectsUngrantedCapabilities_BeforeFactoryUse()
        {
            int calls = 0;
            Assert.Throws<InvalidOperationException>(() => new PluginCatalog(PluginCapability.Resource,
                new[] { Describe("a", PluginCapability.Audio, () => { calls++; return new Probe("a"); }) }));
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Catalog_ListsFactories_WithoutInvokingThem()
        {
            int calls = 0;
            var catalog = new PluginCatalog(PluginCapability.Resource,
                new[] { Describe("a", PluginCapability.Resource, () => { calls++; return new Probe("a"); }) });
            Assert.AreEqual(1, catalog.Factories.Count);
            Assert.AreEqual(0, calls);
        }
    }
}
