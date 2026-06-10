using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class SettingUtility
    {
        /// <summary>
        /// 游戏配置辅助器接口。
        /// </summary>
        public interface ISettingHelper
        {
            /// <summary>
            /// 获取游戏配置项数量。
            /// </summary>
            int Count { get; }

            /// <summary>
            /// 初始化游戏配置辅助器。
            /// </summary>
            void OnInit();

            /// <summary>
            /// 加载游戏配置。
            /// </summary>
            /// <returns>是否加载游戏配置成功。</returns>
            bool Load();

            /// <summary>
            /// 保存游戏配置。
            /// </summary>
            /// <returns>是否保存游戏配置成功。</returns>
            bool Save();

            /// <summary>
            /// 获取所有游戏配置项的名称。
            /// </summary>
            /// <returns>所有游戏配置项的名称。</returns>
            string[] GetAllSettingNames();

            /// <summary>
            /// 获取所有游戏配置项的名称。
            /// </summary>
            /// <param name="results">所有游戏配置项的名称。</param>
            void GetAllSettingNames(List<string> results);

            /// <summary>
            /// 检查是否存在指定游戏配置项。
            /// </summary>
            /// <param name="settingName">要检查游戏配置项的名称。</param>
            /// <returns>指定的游戏配置项是否存在。</returns>
            bool HasSetting(string settingName);

            /// <summary>
            /// 移除指定游戏配置项。
            /// </summary>
            /// <param name="settingName">要移除游戏配置项的名称。</param>
            /// <returns>是否移除指定游戏配置项成功。</returns>
            bool RemoveSetting(string settingName);

            /// <summary>
            /// 清空所有游戏配置项。
            /// </summary>
            void RemoveAllSettings();

            /// <summary>
            /// 从指定游戏配置项中读取布尔值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <returns>读取的布尔值。</returns>
            bool GetBool(string settingName);

            /// <summary>
            /// 从指定游戏配置项中读取布尔值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
            /// <returns>读取的布尔值。</returns>
            bool GetBool(string settingName, bool defaultValue);

            /// <summary>
            /// 向指定游戏配置项写入布尔值。
            /// </summary>
            /// <param name="settingName">要写入游戏配置项的名称。</param>
            /// <param name="value">要写入的布尔值。</param>
            void SetBool(string settingName, bool value);

            /// <summary>
            /// 从指定游戏配置项中读取整数值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <returns>读取的整数值。</returns>
            int GetInt(string settingName);

            /// <summary>
            /// 从指定游戏配置项中读取整数值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
            /// <returns>读取的整数值。</returns>
            int GetInt(string settingName, int defaultValue);

            /// <summary>
            /// 向指定游戏配置项写入整数值。
            /// </summary>
            /// <param name="settingName">要写入游戏配置项的名称。</param>
            /// <param name="value">要写入的整数值。</param>
            void SetInt(string settingName, int value);

            /// <summary>
            /// 从指定游戏配置项中读取浮点数值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <returns>读取的浮点数值。</returns>
            float GetFloat(string settingName);

            /// <summary>
            /// 从指定游戏配置项中读取浮点数值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
            /// <returns>读取的浮点数值。</returns>
            float GetFloat(string settingName, float defaultValue);

            /// <summary>
            /// 向指定游戏配置项写入浮点数值。
            /// </summary>
            /// <param name="settingName">要写入游戏配置项的名称。</param>
            /// <param name="value">要写入的浮点数值。</param>
            void SetFloat(string settingName, float value);

            /// <summary>
            /// 从指定游戏配置项中读取字符串值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <returns>读取的字符串值。</returns>
            string GetString(string settingName);

            /// <summary>
            /// 从指定游戏配置项中读取字符串值。
            /// </summary>
            /// <param name="settingName">要获取游戏配置项的名称。</param>
            /// <param name="defaultValue">当指定的游戏配置项不存在时，返回此默认值。</param>
            /// <returns>读取的字符串值。</returns>
            string GetString(string settingName, string defaultValue);

            /// <summary>
            /// 向指定游戏配置项写入字符串值。
            /// </summary>
            /// <param name="settingName">要写入游戏配置项的名称。</param>
            /// <param name="value">要写入的字符串值。</param>
            void SetString(string settingName, string value);
        }
    }
}
