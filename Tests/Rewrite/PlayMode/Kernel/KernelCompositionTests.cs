using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Events;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Kernel.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Kernel
{
    public sealed class KernelCompositionTests
    {
        private sealed class Sink : ILogSink
        {
            internal readonly List<string> Lines = new List<string>();
            public void Write(LogLevel level, string tag, string message, Exception exception)
                => Lines.Add(tag + ":" + message);
        }

        [UnityTest]
        public IEnumerator Modules_BootAndServeKernelServices() => UniTask.ToCoroutine(async () =>
        {
            var sink = new Sink();
            var container = new ServiceContainer(new IServiceModule[]
            {
                new KernelModule(new ILogSink[] { sink }, new LoggerOptions()),
                new KernelUnityModule()
            });
            try
            {
                await container.InitializeAsync(CancellationToken.None);
                var logger = container.Get<ILogger>();
                var events = container.Get<IEventBus>();
                Assert.That(container.Get<IClock>(), Is.Not.Null);
                Assert.That(container.Get<IPlayerLoop>(), Is.Not.Null);
                Assert.That(container.Get<ITimeScaleControl>(), Is.Not.Null);
                int received = 0;
                using (events.Subscribe<string>(_ => received++))
                {
                    events.Publish("ping");
                }
                logger.Log(LogLevel.Info, "test", "hello");
                Assert.AreEqual(1, received);
                Assert.That(sink.Lines, Does.Contain("test:hello"));
            }
            finally
            {
                await container.ShutdownAsync(CancellationToken.None);
            }
        });
    }
}
