using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Debugger;
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

        /// <summary>
        /// 获取或设置编辑器语言（仅编辑器内有效）。
        /// </summary>
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

        /// <summary>
        /// 重置服务相关设置。
        /// </summary>
        private partial void ResetAppServices()
        {
            m_UpdateDriverTypeName = typeof(UpdateDriverService).FullName;
            m_ResourceServiceTypeName = typeof(ResourceService).FullName;
            m_DebuggerServiceTypeName = typeof(DebuggerService).FullName;
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

        /// <summary>
        /// 创建并构建 App 作用域服务容器，随后启动游戏流程（Composition Root）。
        /// <para>① <see cref="GameServices.BuildContainer"/> 创建容器并存储描述符；</para>
        /// <para>② <see cref="ServiceContainer.BuildAsync"/> 按拓扑序创建实例、构造注入、OnInit、OnInitAsync；</para>
        /// <para>③ <see cref="ProcedureSettings.StartProcedure"/> 启动流程状态机。</para>
        /// <para>由 <see cref="AppSettings.Initiation"/> 在 <c>AfterAssembliesLoaded</c> 阶段调用。</para>
        /// </summary>
        private static partial UniTaskVoid InitializeAppServices()
        {
            return InitializeCore();

            async UniTaskVoid InitializeCore()
            {
                GameServices.BuildContainer(EServiceScopeKind.App, BuildServiceCollection(), parent: null);
                await GameServices.AppContainer.BuildAsync();
                await ProcedureSettings.StartProcedure();
            }
        }

        #region 组合根 [COMPOSITION ROOT]

        /// <summary>
        /// 创建 App 作用域服务注册集合。
        /// </summary>
        private static ServiceCollection BuildServiceCollection()
        {
            var collection = new ServiceCollection();

            RegisterServiceFromInspector(collection, typeof(IUpdateDriverService), Instance.m_UpdateDriverTypeName);
            RegisterServiceFromInspector(collection, typeof(IResourceService), Instance.m_ResourceServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(IDebuggerService), Instance.m_DebuggerServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(IAudioService), Instance.m_AudioServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(IObjectPoolService), Instance.m_ObjectPoolServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(IProcedureService), Instance.m_ProcedureServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(ILocalizationService), Instance.m_LocalizationServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(ISceneService), Instance.m_SceneServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(ITimerService), Instance.m_TimerServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(IInputService), Instance.m_InputServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(ISaveService), Instance.m_SaveServiceTypeName);
            RegisterServiceFromInspector(collection, typeof(IUIService), Instance.m_UIServiceTypeName);

            return collection;
        }

        /// <summary>
        /// 从 Inspector 类型名字符串解析并注册服务到集合。
        /// </summary>
        private static void RegisterServiceFromInspector(ServiceCollection collection, Type interfaceType, string implTypeName)
        {
            if (string.IsNullOrEmpty(implTypeName))
            {
                LogUtility.Warning("Service implementation type for '{0}' is not configured.",
                    interfaceType.FullName);
                return;
            }

            var implType = AssemblyUtility.GetType(implTypeName);
            if (implType == null)
            {
                LogUtility.Error("Cannot resolve type '{0}' for service '{1}'.",
                    implTypeName, interfaceType.FullName);
                return;
            }

            collection.Register(interfaceType, implType, EServiceScopeKind.App);
        }

        #endregion
    }
}
