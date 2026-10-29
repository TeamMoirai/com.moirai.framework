using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Assets.Internal
{
    internal sealed class SharedAssetLoad<T> where T : UnityEngine.Object
    {
        private readonly UniTaskCompletionSource<IAssetPayload<T>> _ready =
            new UniTaskCompletionSource<IAssetPayload<T>>();
        private readonly UniTaskCompletionSource<bool> _drained =
            new UniTaskCompletionSource<bool>();
        private readonly CancellationTokenSource _owner;
        private readonly UniTask _running;
        private IAssetPayload<T> _payload;
        private Exception _cancellationFailure;
        private bool _closing;

        internal SharedAssetLoad(IAssetLoader loader, ResourceKey key, CancellationToken serviceToken)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            _owner = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            _running = RunAsync(loader, key);
        }

        internal UniTask<IAssetPayload<T>> WaitAsync(CancellationToken callerToken)
            => _ready.Task.AttachExternalCancellation(callerToken);

        private async UniTask RunAsync(IAssetLoader loader, ResourceKey key)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                _owner.Token.ThrowIfCancellationRequested();
                IAssetPayload<T> loaded = await loader.LoadAssetAsync<T>(key, _owner.Token);
                await UniTask.SwitchToMainThread();
                _payload = loaded;
                if (_closing || _owner.IsCancellationRequested)
                {
                    await loaded.DisposeAsync();
                    await UniTask.SwitchToMainThread();
                    _payload = null;
                    _ready.TrySetCanceled(_owner.Token);
                }
                else _ready.TrySetResult(loaded);
            }
            catch (OperationCanceledException cancelled) when (_owner.IsCancellationRequested &&
                cancelled.CancellationToken == _owner.Token)
            {
                await UniTask.SwitchToMainThread();
                _ready.TrySetCanceled(cancelled.CancellationToken);
            }
            catch (Exception failure)
            {
                await UniTask.SwitchToMainThread();
                _ready.TrySetException(failure);
            }
        }

        internal UniTask ShutdownAsync(CancellationToken callerToken)
        {
            if (!_closing)
            {
                _closing = true;
                try { _owner.Cancel(); }
                catch (Exception failure) { _cancellationFailure = failure; }
                DrainAsync().Forget();
            }
            return _drained.Task.AttachExternalCancellation(callerToken);
        }

        private async UniTask DrainAsync()
        {
            Exception failure = _cancellationFailure;
            try
            {
                await _running;
                try { await _ready.Task; }
                catch (OperationCanceledException cancelled) when (_owner.IsCancellationRequested &&
                    cancelled.CancellationToken == _owner.Token) { }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
                await UniTask.SwitchToMainThread();
                if (_payload != null)
                {
                    await _payload.DisposeAsync();
                    await UniTask.SwitchToMainThread();
                    _payload = null;
                }
            }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            finally
            {
                await UniTask.SwitchToMainThread();
                _owner.Dispose();
            }
            if (failure == null) _drained.TrySetResult(true);
            else _drained.TrySetException(failure);
        }
    }
}
