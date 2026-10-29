using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Assets.Internal
{
    internal sealed class ResourceService : IResourceService, IAsyncInitializable, IAsyncStoppable,
        IAsyncDisposableService
    {
        private interface IRecord
        {
            bool IsExpired(double now);
            void OnReleased(ResourceLeaseOption option, double now);
            UniTask ShutdownAsync(CancellationToken cancellationToken);
        }

        private sealed class Record<T> : IRecord where T : UnityEngine.Object
        {
            internal readonly SharedAssetLoad<T> Load;
            internal readonly LeaseSet<T> Leases;
            internal double ExpiresAt;
            internal bool WaitingForExpiry;

            private readonly double _keepAliveSeconds;

            internal Record(SharedAssetLoad<T> load, int capacity, double keepAliveSeconds,
                Action<ResourceLeaseOption> released)
            {
                Load = load;
                _keepAliveSeconds = keepAliveSeconds;
                Leases = new LeaseSet<T>(capacity, released);
            }

            public void OnReleased(ResourceLeaseOption option, double now)
            {
                WaitingForExpiry = true;
                ExpiresAt = option == ResourceLeaseOption.None ? now
                    : option == ResourceLeaseOption.Pin ? double.MaxValue : now + _keepAliveSeconds;
            }

            public bool IsExpired(double now)
            {
                return WaitingForExpiry && Leases.Count == 0 && now >= ExpiresAt;
            }

            public async UniTask ShutdownAsync(CancellationToken cancellationToken)
            {
                await Load.ShutdownAsync(cancellationToken);
                Leases.Invalidate();
            }
        }

        private sealed class SceneLease : ISceneLease
        {
            private IScenePayload _payload;
            private bool _disposed;

            internal SceneLease(IScenePayload payload) { _payload = payload; }

            public UnityEngine.SceneManagement.Scene Scene => _payload.Scene;
            public bool IsActivated => _payload.IsActivated;

            public UniTask ActivateAsync(CancellationToken cancellationToken)
                => _payload.ActivateAsync(cancellationToken);

            public async UniTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                IScenePayload payload = _payload;
                _payload = null;
                if (payload != null) await payload.DisposeAsync();
            }
        }

        private readonly IAssetLoader _loader;
        private readonly ResourceOptions _options;
        private readonly IClock _clock;
        private readonly IPlayerLoop _loop;
        private readonly CancellationTokenSource _owner = new CancellationTokenSource();
        private readonly Dictionary<string, IRecord> _records = new Dictionary<string, IRecord>(StringComparer.Ordinal);
        private IDisposable _maintenance;
        private bool _running;
        private bool _stopping;

        internal ResourceService(IAssetLoader loader, ResourceOptions options, IClock clock, IPlayerLoop loop)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        }

        private static string Id(ResourceKey key, Type type) => key.ToString() + "|" + type.FullName;

        public UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _running = true;
            _maintenance = _loop.AddUpdateListener(Maintain);
            return UniTask.CompletedTask;
        }

        public bool TryGetAsset<T>(ResourceKey key, out T asset) where T : UnityEngine.Object
        {
            EnsureRunning();
            if (_records.TryGetValue(Id(key, typeof(T)), out IRecord record))
            {
                T cached = ((Record<T>)record).Leases.Asset;
                if (cached != null)
                {
                    asset = cached;
                    return true;
                }
            }
            asset = null;
            return false;
        }

        public async UniTask<ResourceLease<T>> AcquireLeaseAsync<T>(ResourceKey key,
            ResourceLeaseOption option = ResourceLeaseOption.None,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureRunning();
            string id = Id(key, typeof(T));
            if (!_records.TryGetValue(id, out IRecord existing))
            {
                var load = new SharedAssetLoad<T>(_loader, key, _owner.Token);
                var created = new Record<T>(load, _options.LeaseCapacity, _options.KeepAliveSeconds,
                    released => OnReleased(id, released));
                _records.Add(id, created);
                existing = created;
            }
            var record = (Record<T>)existing;
            IAssetPayload<T> payload;
            try { payload = await record.Load.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (
                !_owner.IsCancellationRequested && cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _records.Remove(id);
                await record.ShutdownAsync(CancellationToken.None);
                throw;
            }
            await UniTask.SwitchToMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            if (record.Leases.Asset == null) record.Leases.Initialize(payload.Asset);
            record.WaitingForExpiry = false;
            return record.Leases.Acquire(option);
        }

        public async UniTask<ISceneLease> LoadSceneAsync(ResourceKey key,
            UnityEngine.SceneManagement.LoadSceneParameters parameters, bool activateOnLoad,
            CancellationToken cancellationToken = default)
        {
            EnsureRunning();
            IScenePayload payload = await _loader.LoadSceneAsync(key, parameters, activateOnLoad, _owner.Token);
            await UniTask.SwitchToMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            return new SceneLease(payload);
        }

        private void OnReleased(string id, ResourceLeaseOption option)
        {
            if (!_records.TryGetValue(id, out IRecord record)) return;
            record.OnReleased(option, _clock.RealtimeSinceStartup);
        }

        private void Maintain()
        {
            double now = _clock.RealtimeSinceStartup;
            int evicted = 0;
            List<string> expired = null;
            foreach (KeyValuePair<string, IRecord> pair in _records)
            {
                if (evicted >= _options.MaxEvictionsPerUpdate) break;
                if (!pair.Value.IsExpired(now)) continue;
                (expired ?? (expired = new List<string>())).Add(pair.Key);
                evicted++;
            }
            if (expired == null) return;
            foreach (string id in expired)
            {
                if (!_records.TryGetValue(id, out IRecord record)) continue;
                _records.Remove(id);
                record.ShutdownAsync(CancellationToken.None).Forget();
            }
        }

        public async UniTask StopAsync(CancellationToken cancellationToken)
        {
            if (!_stopping)
            {
                _stopping = true;
                _running = false;
                IDisposable maintenance = _maintenance;
                _maintenance = null;
                maintenance?.Dispose();
                try { _owner.Cancel(); }
                catch (Exception) { }
            }
            var records = new List<IRecord>(_records.Values);
            _records.Clear();
            var failures = new List<Exception>();
            for (int i = 0; i < records.Count; i++)
            {
                try { await records[i].ShutdownAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count != 0) throw new AggregateException(failures);
        }

        public UniTask DisposeAsync() => StopAsync(CancellationToken.None);

        private void EnsureRunning()
        {
            if (!_running || _stopping) throw new ObjectDisposedException(nameof(ResourceService));
        }
    }
}
