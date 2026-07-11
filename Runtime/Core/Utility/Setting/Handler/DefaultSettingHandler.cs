using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认游戏配置处理器。
    /// </summary>
    [Serializable]
    public sealed partial class DefaultSettingHandler : SettingHandler
    {
        [SerializeField] private string m_SettingFileName = "Setting.dat";

        // 游戏配置存储文件路径
        private string _filePath;
        // 游戏配置
        private Settings _settings;
        // 游戏配置序列化器
        private Serializer _serializer;
        private bool _initialized;

        public override int Count => _settings?.Count ?? 0;

        /// <summary>
        /// 懒加载初始化。<br/>
        /// 构造函数不允许访问 Application.persistentDataPath
        /// </summary>
        private void EnsureInitialized()
        {
            if (_initialized) return;

            _initialized = true;
            _filePath = PathUtility.FormatToUnityPath(Path.Combine(Application.persistentDataPath, m_SettingFileName));
            _settings = new Settings();
            _serializer = new Serializer();
            _serializer.RegisterSerializeCallback(0, SerializeDefaultSettingCallback);
            _serializer.RegisterDeserializeCallback(0, DeserializeDefaultSettingCallback);

            Load();
        }

        private bool SerializeDefaultSettingCallback(Stream stream, Settings settings)
        {
            _settings.Serialize(stream);
            return true;
        }

        private Settings DeserializeDefaultSettingCallback(Stream stream)
        {
            _settings.Deserialize(stream);
            return _settings;
        }
        
        public override bool Load()
        {
            try
            {
                EnsureInitialized();

                if (!File.Exists(_filePath))
                {
                    return true;
                }

                using (FileStream fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
                {
                    _serializer.Deserialize(fileStream);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log.Warning("Load settings failure with exception '{0}'.", exception);
                return false;
            }
        }

        public override bool Save()
        {
            try
            {
                EnsureInitialized();

                using (FileStream fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write))
                {
                    return _serializer.Serialize(fileStream, _settings);
                }
            }
            catch (Exception exception)
            {
                Log.Warning("Save settings failure with exception '{0}'.", exception);
                return false;
            }
        }

        public override string[] GetAllSettingNames()
        {
            EnsureInitialized();
            return _settings.GetAllSettingNames();
        }

        public override void GetAllSettingNames(List<string> results)
        {
            EnsureInitialized();
            _settings.GetAllSettingNames(results);
        }

        public override bool HasSetting(string settingName)
        {
            EnsureInitialized();
            return _settings.HasSetting(settingName);
        }

        public override bool RemoveSetting(string settingName)
        {
            EnsureInitialized();
            return _settings.RemoveSetting(settingName);
        }

        public override void RemoveAllSettings()
        {
            EnsureInitialized();
            _settings.RemoveAllSettings();
        }

        public override bool GetBool(string settingName)
        {
            EnsureInitialized();
            return _settings.GetBool(settingName);
        }

        public override bool GetBool(string settingName, bool defaultValue)
        {
            EnsureInitialized();
            return _settings.GetBool(settingName, defaultValue);
        }

        public override void SetBool(string settingName, bool value)
        {
            EnsureInitialized();
            _settings.SetBool(settingName, value);
        }

        public override int GetInt(string settingName)
        {
            EnsureInitialized();
            return _settings.GetInt(settingName);
        }

        public override int GetInt(string settingName, int defaultValue)
        {
            EnsureInitialized();
            return _settings.GetInt(settingName, defaultValue);
        }

        public override void SetInt(string settingName, int value)
        {
            EnsureInitialized();
            _settings.SetInt(settingName, value);
        }

        public override float GetFloat(string settingName)
        {
            EnsureInitialized();
            return _settings.GetFloat(settingName);
        }

        public override float GetFloat(string settingName, float defaultValue)
        {
            EnsureInitialized();
            return _settings.GetFloat(settingName, defaultValue);
        }

        public override void SetFloat(string settingName, float value)
        {
            EnsureInitialized();
            _settings.SetFloat(settingName, value);
        }

        public override string GetString(string settingName)
        {
            EnsureInitialized();
            return _settings.GetString(settingName);
        }

        public override string GetString(string settingName, string defaultValue)
        {
            EnsureInitialized();
            return _settings.GetString(settingName, defaultValue);
        }

        public override void SetString(string settingName, string value)
        {
            EnsureInitialized();
            _settings.SetString(settingName, value);
        }
    }
}