using System;
using System.Collections.Generic;
using System.Linq;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Input;
using Moirai.Atropos.Localization;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Save;
using Moirai.Atropos.Scene;
using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;
using Moirai.Atropos.UpdateDriver;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    [FrameworkSetting("游戏基础配置", "自动生成组件绑定代码设置", -999)]
    public class AppSettings : FrameworkSettings<AppSettings>
    {
        [DisableInPlayMode]
        [ValueDropdown(nameof(GetLanguageOptions))]
        [SerializeField] private string m_EditorLanguage = Language.Unspecified.Name;
        private static IEnumerable<string> GetLanguageOptions() => Language.BuiltinLanguages.Select(lang => lang.Name);

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

        /// <!-- Services -->
        private const string SERVICE_GROUP = "游戏服务 [Game Services]";

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IUpdateDriverService), "Update Driver")]
        [SerializeField] private string m_UpdateDriverTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IResourceService), "Resource Service")]
        [SerializeField] private string m_ResourceServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IDebuggerService), "Debugger Service")]
        [SerializeField] private string m_DebuggerServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IFSMService), "FSM Service")]
        [SerializeField] private string m_FSMServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IAudioService), "Audio Service")]
        [SerializeField] private string m_AudioServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IObjectPoolService), "ObjectPool Service")]
        [SerializeField] private string m_ObjectPoolServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IProcedureService), "Procedure Service")]
        [SerializeField] private string m_ProcedureServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(ILocalizationService), "Localization Service")]
        [SerializeField] private string m_LocalizationServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(ISceneService), "Scene Service")]
        [SerializeField] private string m_SceneServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(ITimerService), "Timer Service")]
        [SerializeField] private string m_TimerServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IInputService), "Input Service")]
        [SerializeField] private string m_InputServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(ISaveService), "Save Service")]
        [SerializeField] private string m_SaveServiceTypeName;

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [HelperDropdown(typeof(IUIService), "UI Service")]
        [SerializeField] private string m_UIServiceTypeName;

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

#if UNITY_EDITOR

        /// <summary>获取或设置编辑器语言（仅编辑器内有效）。</summary>
        public static string EditorLanguage
        {
            get => Instance.m_EditorLanguage;
            set
            {
                if (Instance.m_EditorLanguage == value) return;

                Instance.m_EditorLanguage = value;
                GameApp.Localization?.ChangeLanguage(value);
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

            m_UpdateDriverTypeName = typeof(UpdateDriverService).FullName;
            m_ResourceServiceTypeName = typeof(ResourceService).FullName;
            m_DebuggerServiceTypeName = typeof(DebuggerService).FullName;
            m_FSMServiceTypeName = typeof(FSMService).FullName;
            m_AudioServiceTypeName = typeof(AudioService).FullName;
            m_ObjectPoolServiceTypeName = typeof(ObjectPoolService).FullName;
            m_ProcedureServiceTypeName = typeof(ProcedureService).FullName;
            m_LocalizationServiceTypeName = typeof(LocalizationService).FullName;
            m_SceneServiceTypeName = typeof(SceneService).FullName;
            m_TimerServiceTypeName = typeof(TimerService).FullName;
            m_InputServiceTypeName = typeof(InputService).FullName;
            m_SaveServiceTypeName = typeof(SaveService).FullName;
            m_UIServiceTypeName = typeof(UIService).FullName;

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

            // 将服务实现类型注册到 ServiceSystem
            ServiceSystem.RegisterService<IUpdateDriverService>(ResolveTypeOption<Service>(Instance.m_UpdateDriverTypeName));
            ServiceSystem.RegisterService<IResourceService>(ResolveTypeOption<Service>(Instance.m_ResourceServiceTypeName));
            ServiceSystem.RegisterService<IDebuggerService>(ResolveTypeOption<Service>(Instance.m_DebuggerServiceTypeName));
            ServiceSystem.RegisterService<IFSMService>(ResolveTypeOption<Service>(Instance.m_FSMServiceTypeName));
            ServiceSystem.RegisterService<IAudioService>(ResolveTypeOption<Service>(Instance.m_AudioServiceTypeName));
            ServiceSystem.RegisterService<IObjectPoolService>(ResolveTypeOption<Service>(Instance.m_ObjectPoolServiceTypeName));
            ServiceSystem.RegisterService<IProcedureService>(ResolveTypeOption<Service>(Instance.m_ProcedureServiceTypeName));
            ServiceSystem.RegisterService<ILocalizationService>(ResolveTypeOption<Service>(Instance.m_LocalizationServiceTypeName));
            ServiceSystem.RegisterService<ISceneService>(ResolveTypeOption<Service>(Instance.m_SceneServiceTypeName));
            ServiceSystem.RegisterService<ITimerService>(ResolveTypeOption<Service>(Instance.m_TimerServiceTypeName));
            ServiceSystem.RegisterService<IInputService>(ResolveTypeOption<Service>(Instance.m_InputServiceTypeName));
            ServiceSystem.RegisterService<ISaveService>(ResolveTypeOption<Service>(Instance.m_SaveServiceTypeName));
            ServiceSystem.RegisterService<IUIService>(ResolveTypeOption<Service>(Instance.m_UIServiceTypeName));

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
    }
}