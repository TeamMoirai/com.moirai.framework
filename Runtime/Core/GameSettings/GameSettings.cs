using System;
using System.Collections.Generic;
using System.Linq;
using Moirai.Atropos.Localization;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    [FrameworkSetting("游戏基础配置", "自动生成组件绑定代码设置", -999)]
    public class GameSettings : FrameworkSettings<GameSettings>
    {
        [DisableInPlayMode]
        [ValueDropdown(nameof(GetLanguageOptions))]
        [SerializeField] private string m_EditorLanguage = "Unspecified";
        private static IEnumerable<string> GetLanguageOptions() => Language.BuiltinLanguages.Select(lang => lang.Name);

        private const string HELPER_GROUP = "Global Helpers";

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [LabelText("Version Helper")]
        [ValueDropdown(nameof(GetVersionHelperTypes))]
        [SerializeField] private string m_VersionHelperTypeName = typeof(DefaultVersionHelper).FullName;
        private static IEnumerable<string> GetVersionHelperTypes() =>
            GetTypeOptions(typeof(VersionUtility.IVersionHelper), typeof(DefaultVersionHelper).FullName);

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [LabelText("Format Helper")]
        [ValueDropdown(nameof(GetFormatHelperTypes))]
        [SerializeField] private string m_FormatHelperTypeName = typeof(DefaultFormatHelper).FullName;
        private static IEnumerable<string> GetFormatHelperTypes() =>
            GetTypeOptions(typeof(TextUtility.IFormatHelper), typeof(DefaultFormatHelper).FullName);

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [LabelText("Log Helper")]
        [ValueDropdown(nameof(GetLogHelperTypes))]
        [SerializeField] private string m_LogHelperTypeName = typeof(DefaultLogHelper).FullName;
        private static IEnumerable<string> GetLogHelperTypes() =>
            GetTypeOptions(typeof(LogUtility.ILogHelper), typeof(DefaultLogHelper).FullName);

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [LabelText("Object Helper")]
        [ValueDropdown(nameof(GetObjectHelperTypes))]
        [SerializeField] private string m_ObjectHelperTypeName = typeof(UnityObjectHelper).FullName;
        private static IEnumerable<string> GetObjectHelperTypes() =>
            GetTypeOptions(typeof(ObjectUtility.IObjectHelper), typeof(UnityObjectHelper).FullName);

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [LabelText("Json Helper")]
        [ValueDropdown(nameof(GetJsonHelperTypes))]
        [SerializeField] private string m_JsonHelperTypeName = typeof(UnityJsonHelper).FullName;
        private static IEnumerable<string> GetJsonHelperTypes() =>
            GetTypeOptions(typeof(JSONUtility.IJsonHelper), typeof(UnityJsonHelper).FullName);

        [DisableInPlayMode]
        [Range(1, 300)]
        [SerializeField] private int m_FrameRate = 120;

        [DisableInPlayMode]
        [Range(0f, 8f)]
        [SerializeField] private float m_GameSpeed = 1f;

        [DisableInPlayMode]
        [SerializeField] private bool m_RunInBackground = true;

        [DisableInPlayMode]
        [SerializeField] private bool m_NeverSleep = true;

        private static float s_GameSpeedBeforePause = 1f;
        private const int DEFAULT_DPI = 96;  // default windows dpi

#if UNITY_EDITOR

        /// <summary>获取或设置编辑器语言（仅编辑器内有效）。</summary>
        public static string EditorLanguage
        {
            get => Instance.m_EditorLanguage;
            set
            {
                if (Instance.m_EditorLanguage == value) return;

                Instance.m_EditorLanguage = value;
                GameModule.Localization?.ChangeLanguage(value);
            }
        }

#endif

        /// <summary>获取或设置游戏帧率。</summary>
        public static int FrameRate
        {
            get => Instance.m_FrameRate;
            set => Application.targetFrameRate = Instance.m_FrameRate = value;
        }

        /// <summary>获取或设置游戏速度。</summary>
        public static float GameSpeed
        {
            get => Instance.m_GameSpeed;
            set => UnityEngine.Time.timeScale = Instance.m_GameSpeed = value >= 0f ? value : 0f;
        }

        /// <summary>获取游戏是否暂停。</summary>
        public static bool IsGamePaused => Instance.m_GameSpeed <= 0f;

        /// <summary>获取是否正常游戏速度。</summary>
        public static bool IsNormalGameSpeed => Math.Abs(Instance.m_GameSpeed - 1f) < 0.01f;

        /// <summary>获取或设置是否允许后台运行。</summary>
        public static bool RunInBackground
        {
            get => Instance.m_RunInBackground;
            set => Application.runInBackground = Instance.m_RunInBackground = value;
        }

        /// <summary>获取或设置是否禁止休眠。</summary>
        public static bool NeverSleep
        {
            get => Instance.m_NeverSleep;
            set
            {
                Instance.m_NeverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        public static void InitSettings()
        {
            TextUtility.SetFormatHelper(ResolveTypeOption<TextUtility.IFormatHelper>(Instance.m_FormatHelperTypeName));
            VersionUtility.SetVersionHelper(ResolveTypeOption<VersionUtility.IVersionHelper>(Instance.m_VersionHelperTypeName));
            LogUtility.SetLogHelper(ResolveTypeOption<LogUtility.ILogHelper>(Instance.m_LogHelperTypeName));

            Log.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            Log.Info("Unity Version: {0}", Application.unityVersion);

            ObjectUtility.SetObjectHelper(ResolveTypeOption<ObjectUtility.IObjectHelper>(Instance.m_ObjectHelperTypeName));
            JSONUtility.SetJsonHelper(ResolveTypeOption<JSONUtility.IJsonHelper>(Instance.m_JsonHelperTypeName));
            ConverterUtility.ScreenDpi = Screen.dpi;
            if (ConverterUtility.ScreenDpi <= 0)
            {
                ConverterUtility.ScreenDpi = DEFAULT_DPI;
            }

            Application.targetFrameRate = Instance.m_FrameRate;
            Time.timeScale = Instance.m_GameSpeed;
            Application.runInBackground = Instance.m_RunInBackground;
            Screen.sleepTimeout = Instance.m_NeverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public static void PauseGame()
        {
            if (IsGamePaused)
            {
                return;
            }

            s_GameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public static void ResumeGame()
        {
            if (!IsGamePaused) return;

            GameSpeed = s_GameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
        /// </summary>
        public static void ResetNormalGameSpeed()
        {
            if (IsNormalGameSpeed) return;

            GameSpeed = 1f;
        }
    }
}