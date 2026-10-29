using System;

namespace Moirai.Atropos.Kernel.Composition
{
    public readonly struct ServiceDescriptor
    {
        public ServiceDescriptor(Type contract, Type[] dependencies, ServiceLifetime lifetime,
            Func<IServiceProvider, object> factory)
        {
            Contract = contract;
            Dependencies = dependencies;
            Lifetime = lifetime;
            Factory = factory;
        }

        public Type Contract { get; }
        public Type[] Dependencies { get; }
        public ServiceLifetime Lifetime { get; }
        public Func<IServiceProvider, object> Factory { get; }
    }
}
