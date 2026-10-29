namespace Moirai.Atropos.Kernel.Composition
{
    public interface IServiceModule
    {
        void Register(IServiceRegistrar registrar);
    }
}
