using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏配置模块。<br/>
    /// 功能特性：<br/>
    /// 支持用户隔离存储（通过用户ID自动生成复合键）
    /// </summary>
    /// <remarks>仅用于游戏配置，禁止用于游戏内容保存。</remarks>
    public static partial class SettingUtility
    {
        private static SettingHandler s_Handler = null;
        /// <summary>
        /// 获取/设置游戏配置处理器。
        /// </summary>
        public static SettingHandler Handler
        {
            get
            {
                if (s_Handler == null) Handler = new DefaultSettingHandler();
                return s_Handler;
            }
            set
            {
                if (s_Handler == value || value == null) return;

                s_Handler?.Internal_Shutdown();
                s_Handler = value;
                s_Handler.Internal_Init();
            }
        }

        /// <summary>
        /// 当前用户标识（用于生成用户隔离的存储键）。
        /// </summary>
        private static string s_UserId = "";

        /// <summary>
        /// 获取游戏配置项数量。
        /// </summary>
        public static int Count => Handler.Count;

        /// <summary>
        /// 加载游戏配置。
        /// </summary>
        /// <returns>是否加载游戏配置成功。</returns>
        public static bool Load() => Handler.Load();

        /// <summary>
        /// 保存游戏配置。
        /// </summary>
        /// <returns>是否保存游戏配置成功。</returns>
        public static bool Save() => Handler.Save();

        /// <summary>
        /// 获取所有游戏配置项的名称。
        /// </summary>
        /// <returns>所有游戏配置项的名称。</returns>
        public static string[] GetAllSettingNames() => Handler.GetAllSettingNames();

        /// <summary>
        /// 获取所有游戏配置项的名称。
        /// </summary>
        /// <param name="results">所有游戏配置项的名称。</param>
        public static void GetAllSettingNames(List<string> results) => Handler.GetAllSettingNames(results);

        /// <summary>
        /// 检查是否存在指定游戏配置项。
        /// </summary>
        /// <param name="settingName">要检查游戏配置项的名称。</param>
        /// <returns>指定的游戏配置项是否存在。</returns>
        public static bool HasSetting(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.HasSetting(settingName);
        }

        /// <summary>
        /// 移除指定游戏配置项。
        /// </summary>
        /// <param name="settingName">要移除游戏配置项的名称。</param>
        /// <returns>是否移除指定游戏配置项成功。</returns>
        public static bool RemoveSetting(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.RemoveSetting(settingName);
        }

        /// <summary>
        /// 清空所有游戏配置项。
        /// </summary>
        public static void RemoveAllSettings()
        {
            Handler.RemoveAllSettings();
        }

        /// <summary>
        /// 从指定游戏配置项中读取布尔值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <returns>读取的布尔值。</returns>
        public static bool GetBool(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetBool(settingName);
        }

        /// <summary>
        /// 从指定游戏配置项中读取布尔值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
        /// <returns>读取的布尔值。</returns>
        public static bool GetBool(string settingName, bool defaultValue)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetBool(settingName, defaultValue);
        }

        /// <summary>
        /// 向指定游戏配置项写入布尔值。
        /// </summary>
        /// <param name="settingName">要写入游戏配置项的名称。</param>
        /// <param name="value">要写入的布尔值。</param>
        public static void SetBool(string settingName, bool value)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            Handler.SetBool(settingName, value);
        }

        /// <summary>
        /// 从指定游戏配置项中读取整数值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <returns>读取的整数值。</returns>
        public static int GetInt(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetInt(settingName);
        }

        /// <summary>
        /// 从指定游戏配置项中读取整数值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
        /// <returns>读取的整数值。</returns>
        public static int GetInt(string settingName, int defaultValue)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetInt(settingName, defaultValue);
        }

        /// <summary>
        /// 向指定游戏配置项写入整数值。
        /// </summary>
        /// <param name="settingName">要写入游戏配置项的名称。</param>
        /// <param name="value">要写入的整数值。</param>
        public static void SetInt(string settingName, int value)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            Handler.SetInt(settingName, value);
        }

        /// <summary>
        /// 从指定游戏配置项中读取浮点数值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <returns>读取的浮点数值。</returns>
        public static float GetFloat(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetFloat(settingName);
        }

        /// <summary>
        /// 从指定游戏配置项中读取浮点数值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
        /// <returns>读取的浮点数值。</returns>
        public static float GetFloat(string settingName, float defaultValue)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetFloat(settingName, defaultValue);
        }

        /// <summary>
        /// 向指定游戏配置项写入浮点数值。
        /// </summary>
        /// <param name="settingName">要写入游戏配置项的名称。</param>
        /// <param name="value">要写入的浮点数值。</param>
        public static void SetFloat(string settingName, float value)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            Handler.SetFloat(settingName, value);
        }

        /// <summary>
        /// 从指定游戏配置项中读取字符串值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <returns>读取的字符串值。</returns>
        public static string GetString(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetString(settingName);
        }

        /// <summary>
        /// 从指定游戏配置项中读取字符串值。
        /// </summary>
        /// <param name="settingName">要获取游戏配置项的名称。</param>
        /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
        /// <returns>读取的字符串值。</returns>
        public static string GetString(string settingName, string defaultValue)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            return Handler.GetString(settingName, defaultValue);
        }

        /// <summary>
        /// 向指定游戏配置项写入字符串值。
        /// </summary>
        /// <param name="settingName">要写入游戏配置项的名称。</param>
        /// <param name="value">要写入的字符串值。</param>
        public static void SetString(string settingName, string value)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                throw new GameException("Setting name is invalid.");
            }

            Handler.SetString(settingName, value);
        }

        // 用户隔离支持方法 ---------------------------

        /// <summary>
        /// 生成用户隔离的复合键（格式：userId_key）。
        /// </summary>
        private static string GetUserKey(string key) => string.IsNullOrEmpty(key) ? key : $"{s_UserId}_{key}";

        /// <summary>
        /// 设置当前用户ID（用于键隔离）。
        /// </summary>
        /// <param name="id">用户唯一标识。</param>
        public static void SetUserId(string id) => s_UserId = id;

        /// <summary>
        /// 设置用户隔离的整数值。
        /// </summary>
        public static void SetUserInt(string key, int value) => SetInt(GetUserKey(key), value);

        /// <summary>
        /// 获取用户隔离的整数值。
        /// </summary>
        public static int GetUserInt(string key, int defaultValue) => GetInt(GetUserKey(key), defaultValue);

        /// <summary>
        /// 设置用户隔离的浮点数值。
        /// </summary>
        public static void SetUserFloat(string key, float value) => SetFloat(GetUserKey(key), value);

        /// <summary>
        /// 获取用户隔离的浮点数值。
        /// </summary>
        public static float GetUserFloat(string key, float defaultValue) => GetFloat(GetUserKey(key), defaultValue);

        /// <summary>
        /// 设置用户隔离的布尔值。
        /// </summary>
        public static void SetUserBool(string key, bool value) => SetBool(GetUserKey(key), value);

        /// <summary>
        /// 获取用户隔离的布尔值。
        /// </summary>
        public static bool GetUserBool(string key, bool defaultValue) => GetBool(GetUserKey(key), defaultValue);

        /// <summary>
        /// 设置用户隔离的字符串值。
        /// </summary>
        public static void SetUserString(string key, string value) => SetString(GetUserKey(key), value);

        /// <summary>
        /// 获取用户隔离的字符串值。
        /// </summary>
        public static string GetUserString(string key, string defaultValue) => GetString(GetUserKey(key), defaultValue);
    }
}