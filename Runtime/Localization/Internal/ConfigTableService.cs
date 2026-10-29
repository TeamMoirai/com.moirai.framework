using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.L10n.Internal
{
    internal sealed class ConfigTableService : IConfigTableService
    {
        private readonly IConfigTableSource _source;
        private readonly Dictionary<Type, object> _loaded = new Dictionary<Type, object>();
        private readonly Dictionary<Type, UniTaskCompletionSource<object>> _loading =
            new Dictionary<Type, UniTaskCompletionSource<object>>();

        internal ConfigTableService(IConfigTableSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool TryGetTable<T>(out T table) where T : class
        {
            if (_loaded.TryGetValue(typeof(T), out object value))
            {
                table = (T)value;
                return true;
            }
            table = null;
            return false;
        }

        public async UniTask<T> GetTableAsync<T>(CancellationToken cancellationToken = default)
            where T : class
        {
            if (TryGetTable(out T cached)) return cached;
            if (_loading.TryGetValue(typeof(T), out UniTaskCompletionSource<object> shared))
            {
                object result = await shared.Task.AttachExternalCancellation(cancellationToken);
                return (T)result;
            }
            var completion = new UniTaskCompletionSource<object>();
            _loading.Add(typeof(T), completion);
            LoadOwnedAsync<T>(completion).Forget(failure => completion.TrySetException(failure));
            object loaded = await completion.Task.AttachExternalCancellation(cancellationToken);
            return (T)loaded;
        }

        private async UniTask LoadOwnedAsync<T>(UniTaskCompletionSource<object> completion)
            where T : class
        {
            try
            {
                T table = await _source.LoadAsync<T>(CancellationToken.None);
                if (table == null) throw new InvalidOperationException("Config table is missing: " + typeof(T));
                _loaded[typeof(T)] = table;
                completion.TrySetResult(table);
            }
            catch (Exception failure)
            {
                completion.TrySetException(failure);
            }
            finally
            {
                _loading.Remove(typeof(T));
            }
        }
    }
}
