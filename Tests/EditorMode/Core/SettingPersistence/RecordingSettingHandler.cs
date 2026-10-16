using System.Collections.Generic;

namespace Core.SettingPersistence
{
    /// <summary>只记数不落盘的配置后端：把 checkpoint 政策变成可断言的计数，用例因此不碰 persistentDataPath。</summary>
    /// <remarks>
    /// 缓解口径同 <c>L10nProbeHandler</c>：internal 且不带 <c>[Serializable]</c>——ProviderDropdown 走
    /// <c>TypeCache.GetTypesDerivedFrom</c> 且只收录带该特性的派生，替身因此不进生产资产的下拉。
    /// 本类只编进 <c>Moirai.Atropos.Tests.EditorMode</c>：PlayMode 与 Player 装配看不见它，落盘判据一律排 EditMode。
    /// </remarks>
    internal sealed class RecordingSettingHandler : Moirai.Atropos.SettingHandler
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        internal int SaveCount;
        internal int LoadCount;

        /// <summary>取回写过的键：直接走字典，不经 <see cref="GetString(string)"/> 的缺键路径。</summary>
        internal bool TryGetStored(string key, out string value) => _values.TryGetValue(key, out value);

        public override int Count => _values.Count;

        public override bool Load()
        {
            LoadCount++;
            return true;
        }

        public override bool Save()
        {
            SaveCount++;
            return true;
        }

        public override string[] GetAllSettingNames() => new List<string>(_values.Keys).ToArray();

        public override void GetAllSettingNames(List<string> results)
        {
            results.Clear();
            results.AddRange(_values.Keys);
        }

        public override bool HasSetting(string settingName) => _values.ContainsKey(settingName);

        public override bool RemoveSetting(string settingName) => _values.Remove(settingName);

        public override void RemoveAllSettings() => _values.Clear();

        public override bool GetBool(string settingName) => int.Parse(Fetch(settingName)) != 0;

        public override bool GetBool(string settingName, bool defaultValue) =>
            _values.TryGetValue(settingName, out var value) ? int.Parse(value) != 0 : defaultValue;

        public override void SetBool(string settingName, bool value) => _values[settingName] = value ? "1" : "0";

        public override int GetInt(string settingName) => int.Parse(Fetch(settingName));

        public override int GetInt(string settingName, int defaultValue) =>
            _values.TryGetValue(settingName, out var value) ? int.Parse(value) : defaultValue;

        public override void SetInt(string settingName, int value) => _values[settingName] = value.ToString();

        public override float GetFloat(string settingName) => float.Parse(Fetch(settingName));

        public override float GetFloat(string settingName, float defaultValue) =>
            _values.TryGetValue(settingName, out var value) ? float.Parse(value) : defaultValue;

        public override void SetFloat(string settingName, float value) => _values[settingName] = value.ToString();

        public override string GetString(string settingName) => Fetch(settingName);

        public override string GetString(string settingName, string defaultValue) =>
            _values.TryGetValue(settingName, out var value) ? value : defaultValue;

        public override void SetString(string settingName, string value) => _values[settingName] = value;

        private string Fetch(string settingName) => _values[settingName];
    }
}
