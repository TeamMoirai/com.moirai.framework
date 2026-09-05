using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    [FrameworkSetting("[框架]基础配置", "框架基础设置", int.MinValue)]
    public partial class GameAppSettings : FrameworkSettings<GameAppSettings>
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

        /// <!-- Utility -->
        private const string HELPER_GROUP = "框架工具 [Global Utility]";

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private VersionHandler m_VersionHandler = new DefaultVersionHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private SettingHandler m_SettingHandler = new DefaultSettingHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private StringHandler m_StringHandler = new DefaultStringHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private LogHandler m_LogHandler = new DefaultLogHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private ObjectHandler m_ObjectHandler = new UnityObjectHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private JsonHandler m_JsonHandler = new DefaultJsonHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private TweenHandler m_TweenHandler = new DefaultTweenHandler();

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

            // 框架工具
            StringUtility.Handler = Instance.m_StringHandler;
            VersionUtility.Handler = Instance.m_VersionHandler;
            LogUtility.Handler = Instance.m_LogHandler;
            LogUtility.EnableGlobalInterception();
            SettingUtility.Handler = Instance.m_SettingHandler;
            JsonUtility.Handler = Instance.m_JsonHandler;
            ObjectUtility.Handler = Instance.m_ObjectHandler;

            GameApp.Initialize();
            // 组合根：App 作用域服务创建、构建与流程启动
            InitializeAppServices().Forget();

            // 使用服务功能的工具
            TweenUtility.Handler = Instance.m_TweenHandler;

            LogUtility.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            LogUtility.Info("Unity Version: {0}", Application.unityVersion);
        }

        private static partial UniTaskVoid InitializeAppServices();
    }
}