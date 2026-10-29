using System;

namespace Moirai.Atropos.Kernel.Composition
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ServiceAttribute : Attribute
    {
        public ServiceAttribute(Type contract) { Contract = contract; }
        public Type Contract { get; }
        public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Root;
    }
}
