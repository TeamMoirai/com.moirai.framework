using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// PlayerPrefs 游戏配置处理器。
    /// </summary>
    [Serializable]
    public sealed class PlayerPrefsSettingHandler : SettingHandler
    {
        public override int Count => -1;

        public override bool Load()
        {
            return true;
        }

        public override bool Save()
        {
            PlayerPrefs.Save();
            return true;
        }

        public override string[] GetAllSettingNames()
        {
            Log.Warning("GetAllSettingNames is not supported.");
            return null;
        }

        public override void GetAllSettingNames(List<string> results)
        {
            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            Log.Warning("GetAllSettingNames is not supported.");
        }

        public override bool HasSetting(string settingName)
        {
            return PlayerPrefs.HasKey(settingName);
        }

        public override bool RemoveSetting(string settingName)
        {
            if (!PlayerPrefs.HasKey(settingName))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(settingName);
            return true;
        }

        public override void RemoveAllSettings()
        {
            PlayerPrefs.DeleteAll();
        }

        public override bool GetBool(string settingName)
        {
            return PlayerPrefs.GetInt(settingName) != 0;
        }

        public override bool GetBool(string settingName, bool defaultValue)
        {
            return PlayerPrefs.GetInt(settingName, defaultValue ? 1 : 0) != 0;
        }

        public override void SetBool(string settingName, bool value)
        {
            PlayerPrefs.SetInt(settingName, value ? 1 : 0);
        }

        public override int GetInt(string settingName)
        {
            return PlayerPrefs.GetInt(settingName);
        }

        public override int GetInt(string settingName, int defaultValue)
        {
            return PlayerPrefs.GetInt(settingName, defaultValue);
        }

        public override void SetInt(string settingName, int value)
        {
            PlayerPrefs.SetInt(settingName, value);
        }

        public override float GetFloat(string settingName)
        {
            return PlayerPrefs.GetFloat(settingName);
        }

        public override float GetFloat(string settingName, float defaultValue)
        {
            return PlayerPrefs.GetFloat(settingName, defaultValue);
        }

        public override void SetFloat(string settingName, float value)
        {
            PlayerPrefs.SetFloat(settingName, value);
        }

        public override string GetString(string settingName)
        {
            return PlayerPrefs.GetString(settingName);
        }

        public override string GetString(string settingName, string defaultValue)
        {
            return PlayerPrefs.GetString(settingName, defaultValue);
        }

        public override void SetString(string settingName, string value)
        {
            PlayerPrefs.SetString(settingName, value);
        }
    }
}