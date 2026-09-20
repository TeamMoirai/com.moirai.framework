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
        [SerializeField] private int m_FrameRate = 120;

        [DisableInPlayMode]
        [Range(0f, 8f)]
        [SerializeField] private float m_GameSpeed = 1f;

        [DisableInPlayMode]
        [SerializeField] private bool m_RunInBackground = true;

        [DisableInPlayMode]
        [SerializeField] private bool m_NeverSleep = true;

        /// <!-- Utilities -->
        private const string HELPER_GROUP = "框架工具 [Global Utilities]";

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private StringHandler m_StringHandler = StringUtility.CreateDefaultHandler();
        internal static StringHandler StringHandler => Instance.m_StringHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private VersionHandler m_VersionHandler = VersionUtility.CreateDefaultHandler();
        internal static VersionHandler VersionHandler => Instance.m_VersionHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private SettingHandler m_SettingHandler = SettingUtility.CreateDefaultHandler();
        internal static SettingHandler SettingHandler => Instance.m_SettingHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private LogHandler m_LogHandler = LogUtility.CreateDefaultHandler();
        internal static LogHandler LogHandler => Instance.m_LogHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private ObjectHandler m_ObjectHandler = ObjectUtility.CreateDefaultHandler();
        internal static ObjectHandler ObjectHandler => Instance.m_ObjectHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private JsonHandler m_JsonHandler = JsonUtility.CreateDefaultHandler();
        internal static JsonHandler JsonHandler => Instance.m_JsonHandler;

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ProviderDropdown]
        [SerializeReference] private TweenHandler m_TweenHandler = TweenUtility.CreateDefaultHandler();
        internal static TweenHandler TweenHandler => Instance.m_TweenHandler;

        /// <summary>
        /// 游戏设置初始化
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initiation()
        {
            // 系统设置
            ConverterUtility.ScreenDpi = Screen.dpi;
            if (ConverterUtility.ScreenDpi <= 0) ConverterUtility.ScreenDpi = 96; // default windows dpi

            Application.targetFrameRate = Instance.m_FrameRate;
            Time.timeScale = Instance.m_GameSpeed;
            Application.runInBackground = Instance.m_RunInBackground;
            Screen.sleepTimeout = Instance.m_NeverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            if (GameApp.AutoBoot) GameApp.Boot();
        }

        /// <summary>
        /// 组合根：注册内置 App 服务并驱动世界初始化。由 <see cref="GameApp.Boot"/> 调用，
        /// internal 而非 private 是为让启动入口收敛在 GameApp 一处（项目经 <c>AutoBoot</c> 可推迟到那时机）。
        /// </summary>
        internal static partial UniTaskVoid InitializeAppServices();
    }
}