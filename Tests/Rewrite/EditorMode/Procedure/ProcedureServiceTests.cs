using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Events;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Procedure.Internal;
using ProcedureService = Moirai.Atropos.Procedure.Internal.ProcedureService;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Procedure
{
    public sealed class ProcedureServiceTests
    {
        private sealed class Loop : IPlayerLoop
        {
            internal Action Tick;
            public IDisposable AddUpdateListener(Action listener) { Tick = listener; return new Handle(this); }
            public IDisposable AddFixedUpdateListener(Action listener) => new Handle(this);
            public IDisposable AddLateUpdateListener(Action listener) => new Handle(this);
            private sealed class Handle : IDisposable
            {
                private readonly Loop _loop;
                internal Handle(Loop loop) { _loop = loop; }
                public void Dispose() { _loop.Tick = null; }
            }
        }

        private sealed class Clock : IClock
        {
            public float Time { get; set; }
            public float DeltaTime { get; set; } = 0.1f;
            public double RealtimeSinceStartup { get; set; }
        }

        private sealed class Logger : ILogger
        {
            internal readonly List<string> Fatal = new List<string>();
            public bool IsEnabled(LogLevel level, string tag) => true;
            public void Log(LogLevel level, string tag, string message, Exception exception = null)
            {
                if (level == LogLevel.Fatal) Fatal.Add(message);
            }
        }

        private sealed class Events : IEventBus
        {
            internal readonly List<object> Published = new List<object>();
            public IDisposable Subscribe<T>(Action<T> handler) where T : class => new Handle();
            public void Publish<T>(T eventData) where T : class => Published.Add(eventData);
            private sealed class Handle : IDisposable { public void Dispose() { } }
        }

        private sealed class Probe : IProcedure, IAsyncDisposableService
        {
            internal readonly List<string> Calls = new List<string>();
            internal bool CrashOnUpdate;
            public Probe(string key) { Key = key; }
            public string Key { get; }
            public bool Disposed;
            public UniTask OnEnterAsync(CancellationToken cancellationToken)
            {
                Calls.Add("enter");
                return UniTask.CompletedTask;
            }
            public void OnUpdate(float elapsedSeconds, float realElapsedSeconds)
            {
                Calls.Add("update");
                if (CrashOnUpdate) throw new InvalidOperationException("update crashed");
            }
            public UniTask OnLeaveAsync(bool isShutdown, CancellationToken cancellationToken)
            {
                Calls.Add(isShutdown ? "shutdown" : "leave");
                return UniTask.CompletedTask;
            }
            public UniTask DisposeAsync()
            {
                Disposed = true;
                return UniTask.CompletedTask;
            }
        }

        [UnityTest]
        public IEnumerator Initialize_EntersEntrance_AndTickUpdates() => UniTask.ToCoroutine(async () =>
        {
            var loop = new Loop();
            var events = new Events();
            var first = new Probe("a");
            var catalog = new ProcedureCatalog("a",
                new Dictionary<string, Func<IProcedureSwitcher, IProcedure>>
                {
                    { "a", _ => first }
                });
            var service = new ProcedureService(catalog, new Clock(), loop, new Logger(), events);
            await service.InitializeAsync(CancellationToken.None);
            Assert.AreEqual("a", service.CurrentKey);
            Assert.That(events.Published[0], Is.InstanceOf<ProcedureEnteredEvent>());
            loop.Tick();
            Assert.AreEqual(1, first.Calls.FindAll(x => x == "update").Count);
            await service.StopAsync(CancellationToken.None);
            Assert.IsTrue(first.Disposed);
            Assert.AreEqual(1, first.Calls.FindAll(x => x == "shutdown").Count);
        });

        [UnityTest]
        public IEnumerator Switch_PublishesLeftThenEntered() => UniTask.ToCoroutine(async () =>
        {
            var loop = new Loop();
            var events = new Events();
            var first = new Probe("a");
            var second = new Probe("b");
            var catalog = new ProcedureCatalog("a",
                new Dictionary<string, Func<IProcedureSwitcher, IProcedure>>
                {
                    { "a", _ => first }, { "b", _ => second }
                });
            var service = new ProcedureService(catalog, new Clock(), loop, new Logger(), events);
            await service.InitializeAsync(CancellationToken.None);
            await service.SwitchToAsync("b", CancellationToken.None);
            Assert.AreEqual("b", service.CurrentKey);
            Assert.That(events.Published[events.Published.Count - 2], Is.InstanceOf<ProcedureLeftEvent>());
            Assert.That(events.Published[events.Published.Count - 1], Is.InstanceOf<ProcedureEnteredEvent>());
            await service.StopAsync(CancellationToken.None);
        });

        [UnityTest]
        public IEnumerator UpdateCrash_StopsTick_AndPublishesCrashed() => UniTask.ToCoroutine(async () =>
        {
            var loop = new Loop();
            var events = new Events();
            var logger = new Logger();
            var probe = new Probe("a") { CrashOnUpdate = true };
            var catalog = new ProcedureCatalog("a",
                new Dictionary<string, Func<IProcedureSwitcher, IProcedure>> { { "a", _ => probe } });
            var service = new ProcedureService(catalog, new Clock(), loop, logger, events);
            await service.InitializeAsync(CancellationToken.None);
            loop.Tick();
            Assert.IsNull(loop.Tick);
            Assert.AreEqual(1, probe.Calls.FindAll(x => x == "update").Count);
            Assert.IsTrue(events.Published.Exists(x => x is ProcedureCrashedEvent));
            Assert.AreEqual(1, logger.Fatal.Count);
            Exception stopFailure = null;
            try { await service.StopAsync(CancellationToken.None); }
            catch (Exception failure) { stopFailure = failure; }
            Assert.That(stopFailure, Is.InstanceOf<AggregateException>());
        });
    }
}
