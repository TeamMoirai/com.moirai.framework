using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Plugins.Internal
{
    internal sealed class PluginHost
    {
        private readonly IPluginCatalog _catalog;
        private readonly IPluginServices _services;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _started;
        private bool _closing;

        private sealed class Entry
        {
            internal readonly IPlugin Plugin;
            internal readonly PluginWorkScope Scope = new PluginWorkScope();
            internal Entry(IPlugin plugin) { Plugin = plugin; }
        }

        internal sealed class Context : IPluginContext
        {
            internal Context(PluginId id, PluginCapability granted, IPluginServices services,
                CancellationToken token)
            {
                Id = id;
                GrantedCapabilities = granted;
                Services = services;
                LifetimeToken = token;
            }

            public PluginId Id { get; }
            public PluginCapability GrantedCapabilities { get; }
            public IPluginServices Services { get; }
            public CancellationToken LifetimeToken { get; }
        }

        internal PluginHost(IPluginCatalog catalog, IPluginServices services)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        internal async UniTask StartAsync(CancellationToken cancellationToken)
        {
            if (_closing) throw new ObjectDisposedException(nameof(PluginHost));
            if (_started) return;
            var created = new List<Entry>();
            try
            {
                foreach (Func<IPlugin> factory in _catalog.Factories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IPlugin plugin = factory == null ? null : factory();
                    if (plugin == null) throw new InvalidOperationException("A plugin factory returned null.");
                    var entry = new Entry(plugin);
                    created.Add(entry);
                    var context = new Context(plugin.Id, _catalog.GrantedCapabilities,
                        _services, entry.Scope.Token);
                    await plugin.InitializeAsync(context, cancellationToken);
                    await plugin.StartAsync(cancellationToken);
                }
                _entries.AddRange(created);
                _started = true;
            }
            catch (Exception failure)
            {
                for (int i = created.Count - 1; i >= 0; i--)
                {
                    try { await created[i].Plugin.StopAsync(CancellationToken.None); }
                    catch (Exception) { }
                    try { await created[i].Plugin.DisposeAsync(); }
                    catch (Exception) { }
                    await created[i].Scope.ReleaseAfterDrainAsync();
                }
                throw;
            }
        }

        internal async UniTask StopAsync(CancellationToken callerToken)
        {
            _closing = true;
            try { _lifetime.Cancel(); }
            catch (Exception) { }
            var failures = new List<Exception>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                try { await entry.Scope.StopAsync(callerToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure) { failures.Add(failure); }
                try { await entry.Plugin.StopAsync(CancellationToken.None); }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure) { failures.Add(failure); }
                try { await entry.Plugin.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            _entries.Clear();
            if (failures.Count != 0) throw new AggregateException(failures);
        }

        internal void Tick()
        {
            if (!_started || _closing) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                try { _entries[i].Plugin.Tick(); }
                catch (Exception failure)
                {
                    _services.Logger.Log(LogLevel.Error, nameof(PluginHost),
                        "Plugin tick failed: " + _entries[i].Plugin.Id, failure);
                }
            }
        }

        internal PluginWorkScope ScopeOf(IPlugin plugin)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (ReferenceEquals(_entries[i].Plugin, plugin)) return _entries[i].Scope;
            throw new InvalidOperationException("Plugin is not hosted: " + plugin.Id);
        }
    }
}
