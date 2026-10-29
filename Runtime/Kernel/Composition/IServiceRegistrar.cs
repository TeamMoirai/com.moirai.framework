namespace Moirai.Atropos.Kernel.Composition
{
    public interface IServiceRegistrar
    {
        void Add(in ServiceDescriptor descriptor);
    }
}
