using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Plugins
{
    public interface IPlugin
    {
        PluginId Id { get; }
        UniTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
        UniTask StartAsync(CancellationToken cancellationToken);
        void Tick();
        UniTask StopAsync(CancellationToken cancellationToken);
        UniTask DisposeAsync();
    }
}
