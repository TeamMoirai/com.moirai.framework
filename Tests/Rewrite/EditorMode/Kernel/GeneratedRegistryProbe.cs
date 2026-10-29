using System.Collections;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Kernel
{
    internal interface IProbeClock { }

    internal interface IProbeConsumer
    {
        IProbeClock Clock { get; }
    }

    [Service(typeof(IProbeClock))]
    internal sealed class ProbeClock : IProbeClock { }

    [Service(typeof(IProbeConsumer))]
    internal sealed class ProbeConsumer : IProbeConsumer
    {
        public ProbeConsumer(IProbeClock clock) { Clock = clock; }
        public IProbeClock Clock { get; }
    }

    public sealed class GeneratedRegistryProbeTests
    {
        [UnityTest]
        public IEnumerator GeneratedModule_ResolvesDeclaredDependencies() => UniTask.ToCoroutine(async () =>
        {
            var root = new ServiceContainer(new IServiceModule[] { new CompiledModule_Moirai_Tests_EditorMode() });
            try
            {
                Assert.That(root.Get<IProbeConsumer>().Clock, Is.SameAs(root.Get<IProbeClock>()));
            }
            finally
            {
                await root.ShutdownAsync(default);
            }
        });
    }
}
