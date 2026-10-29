using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;

namespace Moirai.Atropos.Settings.Internal
{
    internal sealed class BufferedKeyValueStore : IKeyValueStore, IAsyncStoppable, IAsyncDisposableService
    {
        private readonly Dictionary<string, string> _values;
        private readonly ISettingsWriter _writer;
        private readonly CancellationTokenSource _owner = new CancellationTokenSource();
        private long _revision;
        private long _persisted;
        private bool _closing;
        private UniTaskCompletionSource<Exception> _active;
        private UniTaskCompletionSource<Exception> _stopped;

        public BufferedKeyValueStore(IDictionary<string, string> loaded, ISettingsWriter writer)
        {
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            _values = new Dictionary<string, string>(loaded, StringComparer.Ordinal);
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public int Count => _values.Count;
        public bool IsDirty => _revision != _persisted;

        public bool Has(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return _values.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            EnsureOpen();
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_values.Remove(key)) return false;
            _revision++;
            return true;
        }

        public void RemoveAll()
        {
            EnsureOpen();
            if (_values.Count == 0) return;
            _values.Clear();
            _revision++;
        }

        public void GetAllKeys(List<string> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            foreach (string key in _values.Keys) results.Add(key);
        }

        private string Read(string key, string prefix)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_values.TryGetValue(key, out string value) ||
                !value.StartsWith(prefix, StringComparison.Ordinal)) return null;
            return value.Substring(prefix.Length);
        }

        private void Write(string key, string value)
        {
            EnsureOpen();
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (_values.TryGetValue(key, out string old) && old == value) return;
            _values[key] = value;
            _revision++;
        }

        public bool GetBool(string key, bool defaultValue = false)
            => bool.TryParse(Read(key, "b:"), out bool value) ? value : defaultValue;

        public int GetInt(string key, int defaultValue = 0)
            => int.TryParse(Read(key, "i:"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int value) ? value : defaultValue;

        public float GetFloat(string key, float defaultValue = 0f)
            => float.TryParse(Read(key, "f:"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out float value) ? value : defaultValue;

        public string GetString(string key, string defaultValue = "") => Read(key, "s:") ?? defaultValue;

        public void SetBool(string key, bool value) => Write(key, "b:" + value.ToString());

        public void SetInt(string key, int value)
            => Write(key, "i:" + value.ToString(CultureInfo.InvariantCulture));

        public void SetFloat(string key, float value)
            => Write(key, "f:" + value.ToString("R", CultureInfo.InvariantCulture));

        public void SetString(string key, string value)
        {
            EnsureOpen();
            Write(key, "s:" + (value ?? throw new ArgumentNullException(nameof(value))));
        }

        public UniTask<long> FlushAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            cancellationToken.ThrowIfCancellationRequested();
            if (_active != null) throw new InvalidOperationException("A settings commit is already in progress.");
            if (!IsDirty) return UniTask.FromResult(_persisted);
            long revision = _revision;
            var snapshot = new Dictionary<string, string>(_values, StringComparer.Ordinal);
            var done = new UniTaskCompletionSource<Exception>();
            _active = done;
            return CommitAsync(snapshot, revision, cancellationToken, done);
        }

        public UniTask StopAsync(CancellationToken cancellationToken)
        {
            if (_stopped == null)
            {
                _closing = true;
                _stopped = new UniTaskCompletionSource<Exception>();
                StopOwnedAsync(_active).Forget();
            }
            return ObserveStopAsync(_stopped, cancellationToken);
        }

        public UniTask DisposeAsync() => StopAsync(CancellationToken.None);

        private async UniTask<long> CommitAsync(IReadOnlyDictionary<string, string> snapshot,
            long revision, CancellationToken callerToken, UniTaskCompletionSource<Exception> done)
        {
            Exception failure = null;
            CancellationToken operationToken = default;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_owner.Token, callerToken))
                {
                    operationToken = linked.Token;
                    await _writer.CommitAsync(snapshot, operationToken);
                }
            }
            catch (Exception error) { failure = error; }

            await UniTask.SwitchToMainThread();
            if (failure == null) _persisted = revision;
            bool cancelledByStop = failure is OperationCanceledException cancelled &&
                cancelled.CancellationToken == operationToken && _closing &&
                _owner.IsCancellationRequested && !callerToken.IsCancellationRequested;
            _active = null;
            // 排干结果独立于公开 Flush，避免重复消费 writer 的单次 UniTask。
            done.TrySetResult(cancelledByStop ? null : failure);
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return revision;
        }

        private async UniTask StopOwnedAsync(UniTaskCompletionSource<Exception> active)
        {
            Exception failure = null;
            try { _owner.Cancel(); }
            catch (Exception error) { failure = error; }
            if (active != null)
            {
                Exception commitFailure = await active.Task;
                if (commitFailure != null)
                    failure = failure == null ? commitFailure : new AggregateException(failure, commitFailure);
            }
            await UniTask.SwitchToMainThread();
            try { _owner.Dispose(); }
            catch (Exception error)
            {
                failure = failure == null ? error : new AggregateException(failure, error);
            }
            _stopped.TrySetResult(failure);
        }

        private static async UniTask ObserveStopAsync(UniTaskCompletionSource<Exception> stopped,
            CancellationToken cancellationToken)
        {
            Exception failure = await stopped.Task.AttachExternalCancellation(cancellationToken);
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void EnsureOpen()
        {
            if (_closing) throw new ObjectDisposedException(nameof(BufferedKeyValueStore));
        }
    }
}
