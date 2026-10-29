using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Settings
{
    public interface IKeyValueStore
    {
        int Count { get; }
        bool IsDirty { get; }
        bool Has(string key);
        bool Remove(string key);
        void RemoveAll();
        void GetAllKeys(List<string> results);
        bool GetBool(string key, bool defaultValue = false);
        int GetInt(string key, int defaultValue = 0);
        float GetFloat(string key, float defaultValue = 0f);
        string GetString(string key, string defaultValue = "");
        void SetBool(string key, bool value);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        void SetString(string key, string value);
        UniTask<long> FlushAsync(CancellationToken cancellationToken);
    }
}
