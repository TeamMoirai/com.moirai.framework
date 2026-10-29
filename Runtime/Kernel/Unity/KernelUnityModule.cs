using System;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Kernel.Unity
{
    public sealed class KernelUnityModule : IServiceModule
    {
        public void Register(IServiceRegistrar registrar)
        {
            registrar.Add(new ServiceDescriptor(typeof(IClock), Type.EmptyTypes,
                ServiceLifetime.Root, static _ => new UnityClock()));
            registrar.Add(new ServiceDescriptor(typeof(ITimeScaleControl), Type.EmptyTypes,
                ServiceLifetime.Root, static _ => new UnityTimeScaleControl()));
            registrar.Add(new ServiceDescriptor(typeof(IPlayerLoop), new[] { typeof(ILogger) },
                ServiceLifetime.Root,
                static provider => new UnityPlayerLoop((ILogger)provider.GetService(typeof(ILogger)))));
            registrar.Add(new ServiceDescriptor(typeof(ILifecycleEvents), new[] { typeof(ILogger) },
                ServiceLifetime.Root,
                static provider => new UnityLifecycleEvents((ILogger)provider.GetService(typeof(ILogger)))));
        }
    }
}
