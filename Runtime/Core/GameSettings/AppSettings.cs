using System;
using Moirai.Atropos.Localization;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    [FrameworkSetting("游戏基础配置", "自动生成组件绑定代码设置", -999)]
    public partial class AppSettings : FrameworkSettings<AppSettings>
    {
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

        /// <!-- Handler -->
        private const string HELPER_GROUP = "框架工具 [Global Handler]";

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private VersionHandler m_VersionHandler = new DefaultVersionHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private SettingHandler m_SettingHandler = new DefaultSettingHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private StringHandler m_StringHandler = new DefaultStringHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private LogHandler m_LogHandler = new DefaultLogHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private ObjectHandler m_ObjectHandler = new UnityObjectHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private JsonHandler m_JsonHandler = new DefaultJsonHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [HelperDropdown]
        [SerializeReference] private TweenHandler m_TweenHandler = new DefaultTweenHandler();

        private static float s_GameSpeedBeforePause = 1f;
        private const int DEFAULT_DPI = 96;  // default windows dpi

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
            set => Time.timeScale = Instance.m_GameSpeed = value >= 0f ? value : 0f;
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
            m_FrameRate = 120;
            m_GameSpeed = 1f;
            m_RunInBackground = true;
            m_NeverSleep = true;

            ResetServices();

            m_VersionHandler = new DefaultVersionHandler();
            m_SettingHandler = new DefaultSettingHandler();
            m_StringHandler = new DefaultStringHandler();
            m_LogHandler = new DefaultLogHandler();
            m_ObjectHandler = new UnityObjectHandler();
            m_JsonHandler = new DefaultJsonHandler();
            m_TweenHandler = new DefaultTweenHandler();
        }

        /// <summary>
        /// 游戏设置初始化
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Initiation()
        {
            // 系统设置
            ConverterUtility.ScreenDpi = Screen.dpi;
            if (ConverterUtility.ScreenDpi <= 0) ConverterUtility.ScreenDpi = DEFAULT_DPI;

            Application.targetFrameRate = Instance.m_FrameRate;
            Time.timeScale = Instance.m_GameSpeed;
            Application.runInBackground = Instance.m_RunInBackground;
            Screen.sleepTimeout = Instance.m_NeverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            // 框架工具
            StringUtility.Handler = Instance.m_StringHandler;
            VersionUtility.Handler = Instance.m_VersionHandler;
            LogUtility.Handler = Instance.m_LogHandler;
            LogUtility.EnableGlobalInterception();
            SettingUtility.Handler = Instance.m_SettingHandler;
            JsonUtility.Handler = Instance.m_JsonHandler;
            ObjectUtility.Handler = Instance.m_ObjectHandler;

            // 组合根：创建 ServiceCollection → 注册所有 App 作用域服务 → 构建 App 容器
            // （容器仅存储描述符，实例在 GameApp.Awake 中按拓扑序异步创建）
            BuildAppContainer();

            // 使用服务功能的工具
            TweenUtility.Handler = Instance.m_TweenHandler;

            LogUtility.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            LogUtility.Info("Unity Version: {0}", Application.unityVersion);
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

        private partial void ResetServices();
        private static partial void BuildAppContainer() ;
    }
}