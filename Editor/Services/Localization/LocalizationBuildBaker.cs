using System.IO;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 渠道默认语言烘焙器：把 <c>LocalizationBuildConfig</c> 写进 <c>Assets/Resources/</c>，随包分发。
    /// <para>多渠道出包各自带默认语言，但不改写任何被版本管理的源资产——
    /// 烘焙只有一个目录约定（<see cref="ASSET_PATH"/>），由出包方决定是否把产物纳入版本控制。
    /// 玩家侧读取见 <c>LocalizationService.GetBakedChannelLanguage()</c>（仅播放器生效）。</para>
    /// </summary>
    public static class LocalizationBuildBaker
    {
        /// <summary>烘焙产物路径（<c>Resources.Load("LocalizationBuildConfig")</c> 的映射位置）。</summary>
        public const string ASSET_PATH = "Assets/Resources/LocalizationBuildConfig.asset";

        /// <summary>烘焙目标语言（语言 Name 或 Code，须为内置语言）。</summary>
        /// <param name="nameOrCode">内置语言 Name 或 Code（解析失败直接异常，不让错值静默进包）。</param>
        /// <returns>已写入磁盘的烘焙资产。</returns>
        public static LocalizationBuildConfig BakeLanguage(string nameOrCode)
        {
            if (!LocalizationService.TryGetBuiltInLanguage(nameOrCode, out var language))
            {
                throw new System.ArgumentException($"Not a built-in language: '{nameOrCode}'.", nameof(nameOrCode));
            }

            var config = LoadOrCreate();
            config.SetLanguageCode(language.Code);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>清除烘焙（删除产物；不传渠道语言的构建经此回到系统语言检测）。</summary>
        /// <returns>是否删除了产物。</returns>
        public static bool ClearBaked()
        {
            var existed = AssetDatabase.LoadAssetAtPath<LocalizationBuildConfig>(ASSET_PATH) != null;
            if (existed) AssetDatabase.DeleteAsset(ASSET_PATH);
            return existed;
        }

        /// <summary>当前烘焙值（未烘焙返回 <c>null</c>）。</summary>
        public static string GetBakedLanguageCode()
        {
            var config = AssetDatabase.LoadAssetAtPath<LocalizationBuildConfig>(ASSET_PATH);
            return config != null ? config.LanguageCode : null;
        }

        private static LocalizationBuildConfig LoadOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<LocalizationBuildConfig>(ASSET_PATH);
            if (config != null) return config;

            var directory = Path.GetDirectoryName(ASSET_PATH);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            config = ScriptableObject.CreateInstance<LocalizationBuildConfig>();
            AssetDatabase.CreateAsset(config, ASSET_PATH);
            return config;
        }
    }
}
