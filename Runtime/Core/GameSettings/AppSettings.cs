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

        /// <!-- Modules -->
        private const string MODULE_GROUP = "游戏模块 [Game Modules]";

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IUpdateDriverModule), "Update Driver")]
        [SerializeField] private string m_UpdateDriverTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IResourceModule), "Resource Module")]
        [SerializeField] private string m_ResourceModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IDebuggerModule), "Debugger Module")]
        [SerializeField] private string m_DebuggerModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IFSMModule), "FSM Module")]
        [SerializeField] private string m_FSMModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IAudioModule), "Audio Module")]
        [SerializeField] private string m_AudioModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IObjectPoolModule), "ObjectPool Module")]
        [SerializeField] private string m_ObjectPoolModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IProcedureModule), "Procedure Module")]
        [SerializeField] private string m_ProcedureModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(ILocalizationModule), "Localization Module")]
        [SerializeField] private string m_LocalizationModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(ISceneModule), "Scene Module")]
        [SerializeField] private string m_SceneModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(ITimerModule), "Timer Module")]
        [SerializeField] private string m_TimerModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(IInputModule), "Input Module")]
        [SerializeField] private string m_InputModuleTypeName;

        [BoxGroup(MODULE_GROUP), HelperDropdown(typeof(ISaveModule), "Save Module")]
        [SerializeField] private string m_SaveModuleTypeName;

        /// <!-- Handler -->
        private const string HELPER_GROUP = "框架工具 [Global Handler]";

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
        [SerializeReference] private LogHandler m_LogHandler = new DefaultLogHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private ObjectHandler m_ObjectHandler = new UnityObjectHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
        [SerializeReference] private JsonHandler m_JsonHandler = new UnityJsonHandler();

        [BoxGroup(HELPER_GROUP), DisableInPlayMode]
        [ReferenceDropdown]
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

            m_UpdateDriverTypeName = typeof(UpdateDriverModule).FullName;
            m_ResourceModuleTypeName = typeof(ResourceModule).FullName;
            m_DebuggerModuleTypeName = typeof(DebuggerModule).FullName;
            m_FSMModuleTypeName = typeof(FSMModule).FullName;
            m_AudioModuleTypeName = typeof(AudioModule).FullName;
            m_ObjectPoolModuleTypeName = typeof(ObjectPoolModule).FullName;
            m_ProcedureModuleTypeName = typeof(ProcedureModule).FullName;
            m_LocalizationModuleTypeName = typeof(LocalizationModule).FullName;
            m_SceneModuleTypeName = typeof(SceneModule).FullName;
            m_TimerModuleTypeName = typeof(TimerModule).FullName;
            m_InputModuleTypeName = typeof(InputModule).FullName;
            m_SaveModuleTypeName = typeof(SaveModule).FullName;

            m_VersionHandler = new DefaultVersionHandler();
            m_SettingHandler = new DefaultSettingHandler();
            m_StringHandler = new DefaultStringHandler();
            m_LogHandler = new DefaultLogHandler();
            m_ObjectHandler = new UnityObjectHandler();
            m_JsonHandler = new UnityJsonHandler();
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
            SettingUtility.Handler = Instance.m_SettingHandler;
            JSONUtility.Handler = Instance.m_JsonHandler;
            ObjectUtility.Handler = Instance.m_ObjectHandler;

            // 将模块实现类型注册到 ModuleSystem
            ModuleSystem.RegisterModule<IUpdateDriverModule>(ResolveTypeOption<Module>(Instance.m_UpdateDriverTypeName));
            ModuleSystem.RegisterModule<IResourceModule>(ResolveTypeOption<Module>(Instance.m_ResourceModuleTypeName));
            ModuleSystem.RegisterModule<IDebuggerModule>(ResolveTypeOption<Module>(Instance.m_DebuggerModuleTypeName));
            ModuleSystem.RegisterModule<IFSMModule>(ResolveTypeOption<Module>(Instance.m_FSMModuleTypeName));
            ModuleSystem.RegisterModule<IAudioModule>(ResolveTypeOption<Module>(Instance.m_AudioModuleTypeName));
            ModuleSystem.RegisterModule<IObjectPoolModule>(ResolveTypeOption<Module>(Instance.m_ObjectPoolModuleTypeName));
            ModuleSystem.RegisterModule<IProcedureModule>(ResolveTypeOption<Module>(Instance.m_ProcedureModuleTypeName));
            ModuleSystem.RegisterModule<ILocalizationModule>(ResolveTypeOption<Module>(Instance.m_LocalizationModuleTypeName));
            ModuleSystem.RegisterModule<ISceneModule>(ResolveTypeOption<Module>(Instance.m_SceneModuleTypeName));
            ModuleSystem.RegisterModule<ITimerModule>(ResolveTypeOption<Module>(Instance.m_TimerModuleTypeName));
            ModuleSystem.RegisterModule<IInputModule>(ResolveTypeOption<Module>(Instance.m_InputModuleTypeName));
            ModuleSystem.RegisterModule<ISaveModule>(ResolveTypeOption<Module>(Instance.m_SaveModuleTypeName));

            // 使用模块功能的工具
            TweenUtility.Handler = Instance.m_TweenHandler;

            Log.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            Log.Info("Unity Version: {0}", Application.unityVersion);
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