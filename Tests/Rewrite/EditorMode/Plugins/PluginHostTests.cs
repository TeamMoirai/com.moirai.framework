using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Plugins;
using Moirai.Atropos.Plugins.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Plugins
{
    public sealed class PluginHostTests
    {
        private sealed class Services : IPluginServices
        {
            internal readonly FakeLogger LoggerImpl = new FakeLogger();
            public ILogger Logger => LoggerImpl;
            public IClock Clock { get; } = new FakeClock();
            internal sealed class FakeClock : IClock
            {
                public float Time => 0f;
                public float DeltaTime => 0f;
                public double RealtimeSinceStartup => 0d;
            }
            internal sealed class FakeLogger : ILogger
            {
                public bool IsEnabled(LogLevel level, string tag) => true;
                public void Log(LogLevel level, string tag, string message, Exception exception = null) { }
            }
        }

        private sealed class Catalog : IPluginCatalog
        {
            internal Catalog(params Func<IPlugin>[] factories) { Factories = factories; }
            public IReadOnlyList<Func<IPlugin>> Factories { get; }
            public PluginCapability GrantedCapabilities => PluginCapability.Resource;
        }

        private sealed class Probe : IPlugin
        {
            internal readonly List<string> Calls = new List<string>();
            internal bool FailInitialize;
            public Probe(string id) { Id = new PluginId(id); }
            public PluginId Id { get; }
            public UniTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
            {
                Calls.Add("init");
                return FailInitialize
                    ? UniTask.FromException(new InvalidOperationException("init failed"))
                    : UniTask.CompletedTask;
            }
            public UniTask StartAsync(CancellationToken cancellationToken)
            {
                Calls.Add("start");
                return UniTask.CompletedTask;
            }
            public void Tick() => Calls.Add("tick");
            public UniTask StopAsync(CancellationToken cancellationToken)
            {
                Calls.Add("stop");
                return UniTask.CompletedTask;
            }
            public UniTask DisposeAsync()
            {
                Calls.Add("dispose");
                return UniTask.CompletedTask;
            }
        }

        [UnityTest]
        public IEnumerator StartTwice_InitsOnce_AndStopDisposesOnce() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe("a");
            var host = new PluginHost(new Catalog(() => probe), new Services());
            await host.StartAsync(CancellationToken.None);
            await host.StartAsync(CancellationToken.None);
            Assert.AreEqual(1, probe.Calls.FindAll(x => x == "init").Count);
            await host.StopAsync(CancellationToken.None);
            Assert.AreEqual(1, probe.Calls.FindAll(x => x == "stop").Count);
            Assert.AreEqual(1, probe.Calls.FindAll(x => x == "dispose").Count);
        });

        [UnityTest]
        public IEnumerator InitializeFailure_CleansCreatedPluginsInReverse() => UniTask.ToCoroutine(async () =>
        {
            var first = new Probe("a");
            var second = new Probe("b") { FailInitialize = true };
            var host = new PluginHost(new Catalog(() => first, () => second), new Services());
            Exception observed = null;
            try { await host.StartAsync(CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.InstanceOf<InvalidOperationException>());
            CollectionAssert.AreEqual(new[] { "init", "start", "stop", "dispose" }, first.Calls);
            CollectionAssert.AreEqual(new[] { "init", "stop", "dispose" }, second.Calls);
        });

        [UnityTest]
        public IEnumerator Stop_DrainsWorkScope_BeforeDispose() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe("a");
            var host = new PluginHost(new Catalog(() => probe), new Services());
            await host.StartAsync(CancellationToken.None);
            PluginWorkScope.Ticket ticket = host.ScopeOf(probe).Begin();
            UniTask stopping = host.StopAsync(CancellationToken.None);
            Assert.AreEqual(UniTaskStatus.Pending, stopping.Status);
            Assert.AreEqual(0, probe.Calls.FindAll(x => x == "dispose").Count);
            ticket.Complete();
            await stopping;
            Assert.AreEqual(1, probe.Calls.FindAll(x => x == "dispose").Count);
        });
    }
}
