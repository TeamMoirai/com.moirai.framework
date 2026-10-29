using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Backends;

namespace Moirai.Atropos.Plugins.Internal
{
    internal sealed class ScopedPluginContext : IPluginContext
    {
        private readonly IAuthBackend _auth;
        private bool _closed;

        internal ScopedPluginContext(PluginId id, PluginCapability granted, IPluginServices services,
            CancellationToken lifetimeToken, IAuthBackend auth)
        {
            Id = id;
            GrantedCapabilities = granted;
            Services = services;
            LifetimeToken = lifetimeToken;
            _auth = auth;
        }

        public PluginId Id { get; }
        public PluginCapability GrantedCapabilities { get; }
        public IPluginServices Services { get; }
        public CancellationToken LifetimeToken { get; }

        internal IAuthBackend Auth
        {
            get
            {
                EnsureOpen();
                if ((GrantedCapabilities & PluginCapability.Auth) == 0)
                    throw new UnauthorizedAccessException("Plugin '" + Id + "' has no Auth capability.");
                if (_auth == null)
                    throw new InvalidOperationException("Auth backend is not configured.");
                return _auth;
            }
        }

        internal void Invalidate() => _closed = true;

        private void EnsureOpen()
        {
            if (_closed || LifetimeToken.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(ScopedPluginContext));
        }
    }
}
