using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Kernel
{
    public sealed class ServiceContainerTests
    {
        private interface IRoot { }
        private interface IScoped { }
        private interface IConsumer { }
        private sealed class Root : IRoot { }
        private sealed class Scoped : IScoped { }
        private sealed class Consumer : IConsumer
        {
            internal Consumer(IRoot root) { Root = root; }
            internal IRoot Root { get; }
        }

        private sealed class Module : IServiceModule
        {
            private readonly ServiceDescriptor[] _descriptors;
            internal Module(params ServiceDescriptor[] descriptors) { _descriptors = descriptors; }
            public void Register(IServiceRegistrar registrar)
            {
                foreach (ServiceDescriptor descriptor in _descriptors) registrar.Add(descriptor);
            }
        }

        private sealed class Lifecycle : IRoot, IScoped, IConsumer,
            IAsyncInitializable, IAsyncStoppable, IAsyncDisposableService, IDisposable
        {
            internal readonly List<string> Calls = new List<string>();
            internal Func<CancellationToken, UniTask> Initializing;
            internal Func<UniTask> Stopping;
            internal Func<UniTask> Disposing;
            internal Action<string> Recorded;
            public UniTask InitializeAsync(CancellationToken token)
            {
                Record("initialize");
                return Initializing == null ? UniTask.CompletedTask : Initializing(token);
            }
            public UniTask StopAsync(CancellationToken token)
            {
                Assert.That(token.IsCancellationRequested, Is.False);
                Record("stop");
                return Stopping == null ? UniTask.CompletedTask : Stopping();
            }
            public UniTask DisposeAsync()
            {
                Record("disposeAsync");
                return Disposing == null ? UniTask.CompletedTask : Disposing();
            }
            public void Dispose() { Record("dispose"); }
            private void Record(string call)
            {
                Calls.Add(call);
                Recorded?.Invoke(call);
            }
        }

        private static ServiceDescriptor Describe<T>(ServiceLifetime lifetime,
            Func<IServiceProvider, object> factory, params Type[] dependencies)
            => new ServiceDescriptor(typeof(T), dependencies, lifetime, factory);

        private static ServiceContainer Create(params ServiceDescriptor[] descriptors)
            => new ServiceContainer(new IServiceModule[] { new Module(descriptors) });

        [UnityTest]
        public IEnumerator Get_TwoScopes_SharesRootAndSeparatesScoped() => UniTask.ToCoroutine(async () =>
        {
            var root = Create(Describe<IRoot>(ServiceLifetime.Root, _ => new Root()),
                Describe<IScoped>(ServiceLifetime.Scoped, _ => new Scoped()));
            ServiceContainer first = root.CreateScope();
            ServiceContainer second = root.CreateScope();
            try
            {
                Assert.That(first.Get<IRoot>(), Is.SameAs(second.Get<IRoot>()));
                Assert.That(first.Get<IScoped>(), Is.SameAs(first.Get<IScoped>()));
                Assert.That(first.Get<IScoped>(), Is.Not.SameAs(second.Get<IScoped>()));
                Assert.Throws<InvalidOperationException>(() => root.Get<IScoped>());
            }
            finally { await root.ShutdownAsync(CancellationToken.None); }
            Assert.Throws<ObjectDisposedException>(() => first.Get<IRoot>());
        });

        [Test]
        public void Register_DuplicateContract_RejectsWithContractAndModule()
        {
            var error = Assert.Throws<InvalidOperationException>(() => Create(
                Describe<IRoot>(ServiceLifetime.Root, _ => new Root()),
                Describe<IRoot>(ServiceLifetime.Root, _ => new Root())));
            StringAssert.Contains(nameof(IRoot), error.Message);
            StringAssert.Contains(nameof(Module), error.Message);
        }

        [Test]
        public void Register_MissingDependency_RejectsBeforeFactoriesRun()
        {
            int calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() => Create(
                Describe<IConsumer>(ServiceLifetime.Root, _ => { calls++; return new Lifecycle(); }, typeof(IRoot))));
            Assert.That(calls, Is.Zero);
            StringAssert.Contains(nameof(IRoot), error.Message);
        }

        [Test]
        public void Register_Cycle_ReportsClosedDependencyPath()
        {
            var error = Assert.Throws<InvalidOperationException>(() => Create(
                Describe<IRoot>(ServiceLifetime.Root, _ => new Root(), typeof(IConsumer)),
                Describe<IConsumer>(ServiceLifetime.Root, _ => new Lifecycle(), typeof(IRoot))));
            StringAssert.Contains(nameof(IRoot) + " -> " + nameof(IConsumer) + " -> " + nameof(IRoot), error.Message);
        }

        [Test]
        public void Register_RootCapturesScoped_Rejects()
        {
            Assert.Throws<InvalidOperationException>(() => Create(
                Describe<IRoot>(ServiceLifetime.Root, _ => new Root(), typeof(IConsumer)),
                Describe<IConsumer>(ServiceLifetime.Root, _ => new Lifecycle(), typeof(IScoped)),
                Describe<IScoped>(ServiceLifetime.Scoped, _ => new Scoped())));
        }

        [UnityTest]
        public IEnumerator Get_UndeclaredFactoryDependency_RejectsHiddenResolution() => UniTask.ToCoroutine(async () =>
        {
            var root = Create(Describe<IRoot>(ServiceLifetime.Root, _ => new Root()),
                Describe<IConsumer>(ServiceLifetime.Root,
                    provider => new Consumer((IRoot)provider.GetService(typeof(IRoot)))));
            try { Assert.Throws<InvalidOperationException>(() => root.Get<IConsumer>()); }
            finally { await root.ShutdownAsync(CancellationToken.None); }
        });

        [UnityTest]
        public IEnumerator Initialize_RepeatedCall_InitializesOnceAndReverseDisposes() => UniTask.ToCoroutine(async () =>
        {
            var order = new List<string>();
            var dependency = new Lifecycle { Recorded = call => order.Add("dependency." + call) };
            var consumer = new Lifecycle { Recorded = call => order.Add("consumer." + call) };
            var root = Create(Describe<IConsumer>(ServiceLifetime.Root, _ => consumer, typeof(IRoot)),
                Describe<IRoot>(ServiceLifetime.Root, _ => dependency));
            await root.InitializeAsync(CancellationToken.None);
            await root.InitializeAsync(CancellationToken.None);
            await root.ShutdownAsync(CancellationToken.None);
            await root.ShutdownAsync(CancellationToken.None);
            Assert.That(order, Is.EqualTo(new[] { "dependency.initialize", "consumer.initialize",
                "consumer.stop", "consumer.disposeAsync", "dependency.stop", "dependency.disposeAsync" }));
        });

        [UnityTest]
        public IEnumerator Shutdown_CallerCancels_OtherWaiterStillDrains() => UniTask.ToCoroutine(async () =>
        {
            var gate = new UniTaskCompletionSource();
            var service = new Lifecycle { Stopping = () => gate.Task };
            var root = Create(Describe<IRoot>(ServiceLifetime.Root, _ => service));
            root.Get<IRoot>();
            using (var caller = new CancellationTokenSource())
            {
                UniTask cancelled = root.ShutdownAsync(caller.Token);
                UniTask waiting = root.ShutdownAsync(CancellationToken.None);
                caller.Cancel();
                Assert.That(await Capture(cancelled), Is.InstanceOf<OperationCanceledException>());
                Assert.That(waiting.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.Throws<ObjectDisposedException>(() => root.Get<IRoot>());
                Assert.Throws<ObjectDisposedException>(() => root.CreateScope());
                gate.TrySetResult();
                await waiting;
            }
            Assert.That(service.Calls, Is.EqualTo(new[] { "stop", "disposeAsync" }));
        });

        [UnityTest]
        public IEnumerator Shutdown_OneScopeFails_OtherDrainsBeforeRootRelease() => UniTask.ToCoroutine(async () =>
        {
            var failure = new InvalidOperationException("stop failed");
            var gate = new UniTaskCompletionSource();
            var dependency = new Lifecycle();
            int scopes = 0;
            var root = Create(Describe<IRoot>(ServiceLifetime.Root, _ => dependency),
                Describe<IScoped>(ServiceLifetime.Scoped, _ => ++scopes == 1
                    ? new Lifecycle { Stopping = () => UniTask.FromException(failure) }
                    : new Lifecycle { Stopping = () => gate.Task }, typeof(IRoot)));
            root.CreateScope().Get<IScoped>();
            root.CreateScope().Get<IScoped>();
            UniTask stopping = root.ShutdownAsync(CancellationToken.None);
            Assert.That(stopping.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(dependency.Calls, Is.Empty);
            gate.TrySetResult();
            await Capture(stopping);
            Assert.That(dependency.Calls, Is.EqualTo(new[] { "stop", "disposeAsync" }));
        });

        [UnityTest]
        public IEnumerator Shutdown_ReleasesOwnedReferences() => UniTask.ToCoroutine(async () =>
        {
            var root = Create(Describe<IRoot>(ServiceLifetime.Root, _ => new Lifecycle()));
            root.Get<IRoot>();
            Assert.That(root.IsReleased, Is.False);
            await root.ShutdownAsync(CancellationToken.None);
            Assert.That(root.IsReleased, Is.True);
        });

        private static async UniTask<Exception> Capture(UniTask operation)
        {
            try { await operation; return null; }
            catch (Exception failure) { return failure; }
        }
    }
}
