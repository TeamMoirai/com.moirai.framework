using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Settings.Internal
{
    internal sealed class BufferedKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _values;
        private readonly ISettingsWriter _writer;
        private long _revision;
        private long _persisted;
        private bool _flushing;

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
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_values.Remove(key)) return false;
            _revision++;
            return true;
        }

        public void RemoveAll()
        {
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
            => Write(key, "s:" + (value ?? throw new ArgumentNullException(nameof(value))));

        public async UniTask<long> FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_flushing) throw new InvalidOperationException("A settings commit is already in progress.");
            if (!IsDirty) return _persisted;
            _flushing = true;
            long revision = _revision;
            var snapshot = new Dictionary<string, string>(_values, StringComparer.Ordinal);
            try
            {
                await _writer.CommitAsync(snapshot, cancellationToken);
                _persisted = revision;
                return revision;
            }
            finally { _flushing = false; }
        }
    }
}
