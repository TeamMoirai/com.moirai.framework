using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// PlayerPrefs 游戏配置辅助器。
    /// </summary>
    public class PlayerPrefsSettingHelper : SettingUtility.ISettingHelper
    {
        public int Count => -1;

        public void OnInit() { }

        public bool Load()
        {
            return true;
        }

        public bool Save()
        {
            PlayerPrefs.Save();
            return true;
        }

        public string[] GetAllSettingNames()
        {
            Log.Warning("GetAllSettingNames is not supported.");
            return null;
        }

        public void GetAllSettingNames(List<string> results)
        {
            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            Log.Warning("GetAllSettingNames is not supported.");
        }

        public bool HasSetting(string settingName)
        {
            return PlayerPrefs.HasKey(settingName);
        }

        public bool RemoveSetting(string settingName)
        {
            if (!PlayerPrefs.HasKey(settingName))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(settingName);
            return true;
        }

        public void RemoveAllSettings()
        {
            PlayerPrefs.DeleteAll();
        }

        public bool GetBool(string settingName)
        {
            return PlayerPrefs.GetInt(settingName) != 0;
        }

        public bool GetBool(string settingName, bool defaultValue)
        {
            return PlayerPrefs.GetInt(settingName, defaultValue ? 1 : 0) != 0;
        }

        public void SetBool(string settingName, bool value)
        {
            PlayerPrefs.SetInt(settingName, value ? 1 : 0);
        }

        public int GetInt(string settingName)
        {
            return PlayerPrefs.GetInt(settingName);
        }

        public int GetInt(string settingName, int defaultValue)
        {
            return PlayerPrefs.GetInt(settingName, defaultValue);
        }

        public void SetInt(string settingName, int value)
        {
            PlayerPrefs.SetInt(settingName, value);
        }

        public float GetFloat(string settingName)
        {
            return PlayerPrefs.GetFloat(settingName);
        }

        public float GetFloat(string settingName, float defaultValue)
        {
            return PlayerPrefs.GetFloat(settingName, defaultValue);
        }

        public void SetFloat(string settingName, float value)
        {
            PlayerPrefs.SetFloat(settingName, value);
        }

        public string GetString(string settingName)
        {
            return PlayerPrefs.GetString(settingName);
        }

        public string GetString(string settingName, string defaultValue)
        {
            return PlayerPrefs.GetString(settingName, defaultValue);
        }

        public void SetString(string settingName, string value)
        {
            PlayerPrefs.SetString(settingName, value);
        }
    }
}