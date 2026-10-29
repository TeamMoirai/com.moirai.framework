using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;

namespace Moirai.Atropos.Scenes.Internal
{
    internal sealed class SceneService : ISceneService
    {
        private sealed class Owner
        {
            internal ISceneLoadOperation Operation;
            internal bool Delivered;
            internal bool Cancelled;
            internal bool Unload;
        }

        private readonly ISceneBackend _backend;
        private readonly List<Owner> _inFlight = new List<Owner>();
        private bool _closing;

        internal SceneService(ISceneBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public async UniTask<ISceneLease> LoadAsync(ResourceKey key,
            UnityEngine.SceneManagement.LoadSceneParameters parameters, bool suspendActivation,
            CancellationToken cancellationToken = default)
        {
            if (_closing) throw new ObjectDisposedException(nameof(SceneService));
            ISceneLoadOperation operation = _backend.BeginLoad(key, parameters, suspendActivation);
            if (operation == null) throw new InvalidOperationException("Scene backend returned no operation.");
            var owner = new Owner { Operation = operation };
            _inFlight.Add(owner);
            ISceneLease lease;
            try
            {
                lease = await operation.Completion.AttachExternalCancellation(cancellationToken);
                owner.Delivered = true;
            }
            catch (OperationCanceledException)
            {
                owner.Cancelled = true;
                owner.Unload = true;
                CleanupLateAsync(owner).Forget();
                throw;
            }
            finally
            {
                if (owner.Delivered) _inFlight.Remove(owner);
            }
            return lease;
        }

        public UniTask UnloadAsync(ISceneLease lease, CancellationToken cancellationToken = default)
        {
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            return lease.UnloadAsync(cancellationToken);
        }

        internal async UniTask StopAsync()
        {
            _closing = true;
            var owners = _inFlight.ToArray();
            _inFlight.Clear();
            for (int i = 0; i < owners.Length; i++)
            {
                Owner owner = owners[i];
                owner.Cancelled = true;
                owner.Unload = true;
                try
                {
                    ISceneLease lease = await owner.Operation.Completion;
                    if (lease != null) await lease.UnloadAsync(CancellationToken.None);
                }
                catch (Exception) { }
            }
        }

        private async UniTask CleanupLateAsync(Owner owner)
        {
            try
            {
                ISceneLease lease = await owner.Operation.Completion;
                if (owner.Unload && lease != null) await lease.UnloadAsync(CancellationToken.None);
            }
            catch (Exception) { }
            finally { _inFlight.Remove(owner); }
        }
    }
}
