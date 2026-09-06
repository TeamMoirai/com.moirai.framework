using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    [FrameworkSetting("[框架]基础配置", "框架基础设置", int.MinValue)]
    public sealed partial class GameAppSettings : FrameworkSettings<GameAppSettings>
    {
        [DisableInPlayMode]
        [Range(1, 300)]
        [SerializeField]
        internal int m_FrameRate = 120;

        [DisableInPlayMode]
        [Range(0f, 8f)]
        [SerializeField]
        internal float m_GameSpeed = 1f;

        [DisableInPlayMode]
        [SerializeField]
        internal bool m_RunInBackground = true;

        [DisableInPlayMode]
        [SerializeField]
        internal bool m_NeverSleep = true;

        /// <!-- Utilities -->
        private const string HELPER_GROUP = "框架工具 [Global Utilities]";

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private StringHandler m_StringHandler;
        internal static StringHandler StringHandler => Instance.m_StringHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private VersionHandler m_VersionHandler;
        internal static VersionHandler VersionHandler => Instance.m_VersionHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private SettingHandler m_SettingHandler;
        internal static SettingHandler SettingHandler => Instance.m_SettingHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private LogHandler m_LogHandler;
        internal static LogHandler LogHandler => Instance.m_LogHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private ObjectHandler m_ObjectHandler;
        internal static ObjectHandler ObjectHandler => Instance.m_ObjectHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private JsonHandler m_JsonHandler;
        internal static JsonHandler JsonHandler => Instance.m_JsonHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private TweenHandler m_TweenHandler;
        internal static TweenHandler TweenHandler => Instance.m_TweenHandler;

        private void Reset()
        {
            m_StringHandler = StringUtility.CreateDefaultHandler();
            m_VersionHandler = VersionUtility.CreateDefaultHandler();
            m_SettingHandler = SettingUtility.CreateDefaultHandler();
            m_LogHandler = LogUtility.CreateDefaultHandler();
            m_ObjectHandler = ObjectUtility.CreateDefaultHandler();
            m_JsonHandler = JsonUtility.CreateDefaultHandler();
            m_TweenHandler = TweenUtility.CreateDefaultHandler();
        }

        /// <summary>
        /// 游戏设置初始化
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Initiation()
        {
            // 系统设置
            ConverterUtility.ScreenDpi = Screen.dpi;
            if (ConverterUtility.ScreenDpi <= 0) ConverterUtility.ScreenDpi = 96; // default windows dpi

            Application.targetFrameRate = Instance.m_FrameRate;
            Time.timeScale = Instance.m_GameSpeed;
            Application.runInBackground = Instance.m_RunInBackground;
            Screen.sleepTimeout = Instance.m_NeverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            GameApp.Initialize();
            // 组合根：App 作用域服务创建、构建与流程启动
            InitializeAppServices().Forget();

            LogUtility.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            LogUtility.Info("Unity Version: {0}", Application.unityVersion);
        }

        private static partial UniTaskVoid InitializeAppServices();
    }
}