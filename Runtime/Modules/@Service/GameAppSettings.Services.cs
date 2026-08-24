using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Input;
using Moirai.Atropos.Localization;
using Moirai.Atropos.GameObjectPool;
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
    public partial class GameAppSettings
    {
        [DisableInPlayMode, PropertyOrder(-999)]
        [ValueDropdown(nameof(GetLanguageOptions))]
        [SerializeField] private string m_EditorLanguage = Language.Unspecified.Name;
        private static IEnumerable<string> GetLanguageOptions() => Language.BuiltinLanguages.Select(lang => lang.Name);

        /// <!-- Services -->
        private const string SERVICE_GROUP = "游戏服务 [Game Services]";

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IUpdateDriverService), "Update Driver")]
        [SerializeField] private string m_UpdateDriverTypeName;
        private static readonly Type s_UpdateDriverService = typeof(UpdateDriverService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IResourceService), "Resource Service")]
        [SerializeField] private string m_ResourceServiceTypeName;
        private static readonly Type s_ResourceService = typeof(ResourceService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IDebuggerService), "Debugger Service")]
        [SerializeField] private string m_DebuggerServiceTypeName;
        private static readonly Type s_DebuggerService = typeof(DebuggerService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IAudioService), "Audio Service")]
        [SerializeField] private string m_AudioServiceTypeName;
        private static readonly Type s_AudioService = typeof(AudioService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IGameObjectPoolService), "GameObjectPool Service")]
        [SerializeField] private string m_GameObjectPoolServiceTypeName;
        private static readonly Type s_GameObjectPoolService = typeof(GameObjectPoolService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IProcedureService), "Procedure Service")]
        [SerializeField] private string m_ProcedureServiceTypeName;
        private static readonly Type s_ProcedureService = typeof(ProcedureService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(ILocalizationService), "Localization Service")]
        [SerializeField] private string m_LocalizationServiceTypeName;
        private static readonly Type s_LocalizationService = typeof(LocalizationService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(ISceneService), "Scene Service")]
        [SerializeField] private string m_SceneServiceTypeName;
        private static readonly Type s_SceneService = typeof(SceneService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(ITimerService), "Timer Service")]
        [SerializeField] private string m_TimerServiceTypeName;
        private static readonly Type s_TimerService = typeof(TimerService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IInputService), "Input Service")]
        [SerializeField] private string m_InputServiceTypeName;
        private static readonly Type s_InputService = typeof(InputService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(ISaveService), "Save Service")]
        [SerializeField] private string m_SaveServiceTypeName;
        private static readonly Type s_SaveService = typeof(SaveService);

        [BoxGroup(SERVICE_GROUP), DisableInPlayMode]
        [ProviderDropdown(typeof(IUIService), "UI Service")]
        [SerializeField] private string m_UIServiceTypeName;
        private static readonly Type s_UIService = typeof(UIService);


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
            m_UpdateDriverTypeName = s_UpdateDriverService.FullName;
            m_ResourceServiceTypeName = s_ResourceService.FullName;
            m_DebuggerServiceTypeName = s_DebuggerService.FullName;
            m_AudioServiceTypeName = s_AudioService.FullName;
            m_GameObjectPoolServiceTypeName = s_GameObjectPoolService.FullName;
            m_ProcedureServiceTypeName = s_ProcedureService.FullName;
            m_LocalizationServiceTypeName = s_LocalizationService.FullName;
            m_SceneServiceTypeName = s_SceneService.FullName;
            m_TimerServiceTypeName = s_TimerService.FullName;
            m_InputServiceTypeName = s_InputService.FullName;
            m_SaveServiceTypeName = s_SaveService.FullName;
            m_UIServiceTypeName = s_UIService.FullName;
        }

        /// <summary>
        /// 创建并构建 App 作用域服务，随后启动游戏流程（Composition Root）。
        /// <para>① <see cref="GameServices.BuildAsync"/> 按拓扑序创建实例、构造注入、OnInit、OnInitAsync；</para>
        /// <para>② <see cref="ProcedureSettings.StartProcedure"/> 启动流程状态机。</para>
        /// <para>由 <see cref="GameAppSettings.Initiation"/> 在 <c>AfterAssembliesLoaded</c> 阶段调用。</para>
        /// </summary>
        private static partial UniTaskVoid InitializeAppServices()
        {
            return InitializeCore();

            async UniTaskVoid InitializeCore()
            {
                await GameServices.BuildAsync(EServiceScopeKind.App, BuildServiceCollection());
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

            collection.Register(typeof(IUpdateDriverService), ResolveType(Instance.m_UpdateDriverTypeName, s_UpdateDriverService), EServiceScopeKind.App);
            collection.Register(typeof(IResourceService), ResolveType(Instance.m_ResourceServiceTypeName, s_ResourceService), EServiceScopeKind.App);
            collection.Register(typeof(IDebuggerService), ResolveType(Instance.m_DebuggerServiceTypeName, s_DebuggerService), EServiceScopeKind.App);
            collection.Register(typeof(IAudioService), ResolveType(Instance.m_AudioServiceTypeName, s_AudioService), EServiceScopeKind.App);
            collection.Register(typeof(IGameObjectPoolService), ResolveType(Instance.m_GameObjectPoolServiceTypeName, s_GameObjectPoolService), EServiceScopeKind.App);
            collection.Register(typeof(IProcedureService), ResolveType(Instance.m_ProcedureServiceTypeName, s_ProcedureService), EServiceScopeKind.App);
            collection.Register(typeof(ILocalizationService), ResolveType(Instance.m_LocalizationServiceTypeName, s_LocalizationService), EServiceScopeKind.App);
            collection.Register(typeof(ISceneService), ResolveType(Instance.m_SceneServiceTypeName, s_SceneService), EServiceScopeKind.App);
            collection.Register(typeof(ITimerService), ResolveType(Instance.m_TimerServiceTypeName, s_TimerService), EServiceScopeKind.App);
            collection.Register(typeof(IInputService), ResolveType(Instance.m_InputServiceTypeName, s_InputService), EServiceScopeKind.App);
            collection.Register(typeof(ISaveService), ResolveType(Instance.m_SaveServiceTypeName, s_SaveService), EServiceScopeKind.App);
            collection.Register(typeof(IUIService), ResolveType(Instance.m_UIServiceTypeName, s_UIService), EServiceScopeKind.App);

            return collection;
        }

        /// <summary>
        /// 根据类型全名解析并返回类型。当配置类型无效时自动回退到备用类型，不抛异常。
        /// </summary>
        /// <param name="implTypeName">
        /// 实现类的完整类型名称（包含命名空间）。
        /// 为 <see langword="null"/> 或空白时直接使用 <paramref name="fallbackType"/>。
        /// </param>
        /// <param name="fallbackType">当 <paramref name="implTypeName"/> 指定的类型不存在时使用的回退类型。</param>
        /// <returns>解析到的 <see cref="Type"/>，保证非 null（最坏情况返回 <paramref name="fallbackType"/>）。</returns>
        public static Type ResolveType(string implTypeName, Type fallbackType)
        {
            var resolvedTypeName = string.IsNullOrWhiteSpace(implTypeName) ? fallbackType.FullName : implTypeName;
            var instanceType = AssemblyUtility.GetType(resolvedTypeName);

            if (instanceType != null) return instanceType;

            if (!string.Equals(resolvedTypeName, fallbackType.FullName, StringComparison.Ordinal))
            {
                LogUtility.Fatal("Could not load type '{0}'. Falling back to {1}.", resolvedTypeName, fallbackType.FullName);
            }

            return fallbackType;
        }

        #endregion
    }
}
