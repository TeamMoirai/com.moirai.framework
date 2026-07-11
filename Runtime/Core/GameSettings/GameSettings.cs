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
        [SerializeField] private string m_EditorLanguage = Language.Unspecified.Name;
        private static IEnumerable<string> GetLanguageOptions() => Language.BuiltinLanguages.Select(lang => lang.Name);

        private const string HELPER_GROUP = "Global Helpers";

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private VersionHandler m_VersionHandler = new DefaultVersionHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private SettingHandler m_SettingHandler = new DefaultSettingHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private StringHandler m_StringHandler = new DefaultStringHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private LogHandler m_LogHandler = new DefaultLogHelper();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private ObjectHandler m_ObjectHandler = new UnityObjectHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private JsonHandler m_JsonHandler = new UnityJsonHandler();

        [DisableInPlayMode]
        [Range(1, 300)]
        [SerializeField] private int m_FrameRate;

        [DisableInPlayMode]
        [Range(0f, 8f)]
        [SerializeField] private float m_GameSpeed;

        [DisableInPlayMode]
        [SerializeField] private bool m_RunInBackground;

        [DisableInPlayMode]
        [SerializeField] private bool m_NeverSleep;

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

        protected internal override void Reset()
        {
            m_EditorLanguage = Language.Unspecified.Name;

            m_VersionHandler = new DefaultVersionHandler();
            m_SettingHandler = new DefaultSettingHandler();
            m_StringHandler = new DefaultStringHandler();
            m_LogHandler = new DefaultLogHelper();
            m_ObjectHandler = new UnityObjectHandler();
            m_JsonHandler = new UnityJsonHandler();

            m_FrameRate = 120;
            m_GameSpeed = 1f;
            m_RunInBackground = true;
            m_NeverSleep = true;
        }

        /// <summary>
        /// 游戏设置初始化
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initiation()
        {
            StringUtility.Handler = Instance.m_StringHandler;
            VersionUtility.Handler = Instance.m_VersionHandler;
            LogUtility.Handler = Instance.m_LogHandler;
            SettingUtility.Handler = Instance.m_SettingHandler;

            Log.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            Log.Info("Unity Version: {0}", Application.unityVersion);

            JSONUtility.Handler = Instance.m_JsonHandler;
            ObjectUtility.Handler = Instance.m_ObjectHandler;

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