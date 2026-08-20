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
    public partial class AppSettings
    {
        [DisableInPlayMode, PropertyOrder(-999)]
        [ValueDropdown(nameof(GetLanguageOptions))]
        [SerializeField] private string m_EditorLanguage = Language.Unspecified.Name;
        private static IEnumerable<string> GetLanguageOptions() => Language.BuiltinLanguages.Select(lang => lang.Name);

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

        private partial void ResetServices()
        {
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
        }

        private static partial void RegisterServices()
        {
            GameServices.RegisterService<IUpdateDriverService>(ResolveTypeOption<ServiceBase>(Instance.m_UpdateDriverTypeName));
            GameServices.RegisterService<IResourceService>(ResolveTypeOption<ServiceBase>(Instance.m_ResourceServiceTypeName));
            GameServices.RegisterService<IDebuggerService>(ResolveTypeOption<ServiceBase>(Instance.m_DebuggerServiceTypeName));
            GameServices.RegisterService<IFSMService>(ResolveTypeOption<ServiceBase>(Instance.m_FSMServiceTypeName));
            GameServices.RegisterService<IAudioService>(ResolveTypeOption<ServiceBase>(Instance.m_AudioServiceTypeName));
            GameServices.RegisterService<IObjectPoolService>(ResolveTypeOption<ServiceBase>(Instance.m_ObjectPoolServiceTypeName));
            GameServices.RegisterService<IProcedureService>(ResolveTypeOption<ServiceBase>(Instance.m_ProcedureServiceTypeName));
            GameServices.RegisterService<ILocalizationService>(ResolveTypeOption<ServiceBase>(Instance.m_LocalizationServiceTypeName));
            GameServices.RegisterService<ISceneService>(ResolveTypeOption<ServiceBase>(Instance.m_SceneServiceTypeName));
            GameServices.RegisterService<ITimerService>(ResolveTypeOption<ServiceBase>(Instance.m_TimerServiceTypeName));
            GameServices.RegisterService<IInputService>(ResolveTypeOption<ServiceBase>(Instance.m_InputServiceTypeName));
            GameServices.RegisterService<ISaveService>(ResolveTypeOption<ServiceBase>(Instance.m_SaveServiceTypeName));
            GameServices.RegisterService<IUIService>(ResolveTypeOption<ServiceBase>(Instance.m_UIServiceTypeName));
        }
    }
}