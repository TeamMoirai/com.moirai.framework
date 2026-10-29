using System;

namespace Moirai.Atropos.Kernel.Events
{
    public interface IEventBus
    {
        IDisposable Subscribe<T>(Action<T> handler) where T : class;
        void Publish<T>(T eventData) where T : class;
    }
}
