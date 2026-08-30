using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
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
                LocalizationService.ChangeLanguage(value);
            }
        }

#endif

        /// <summary>
        /// 注册 App 作用域服务并启动游戏流程（Composition Root）。
        /// <para>① 手动按依赖链序显式注册全部链上服务——服务实例仅由手动注册创建，
        /// <see cref="ServiceDependencyAttribute"/> 声明在注册期做顺序校验（依赖未注册即 fail-fast）；</para>
        /// <para>② <see cref="ProcedureServiceSettings.StartProcedure"/> 启动流程状态机。</para>
        /// <para>未列入注册的服务（Audio/Scene/ObjectPool/Save/ConfigTable/Input/Debugger 等）
        /// 保持 opt-in：由各外观的 <c>CreateDefaultHandler</c> 懒加载路径自动注册（首次访问即生效）。</para>
        /// <para>由 <see cref="GameAppSettings.Initiation"/> 在 <c>AfterAssembliesLoaded</c> 阶段调用。</para>
        /// </summary>
        private static partial UniTaskVoid InitializeAppServices()
        {
            return InitializeCore();

            async UniTaskVoid InitializeCore()
            {
                // 依赖链序：UI 依赖 Resource+Timer，流程链根服务最后注册
                GameServices.RegisterService(EServiceScopeKind.App, new UpdateDriverService());
                GameServices.RegisterService(EServiceScopeKind.App, new ResourceService());
                GameServices.RegisterService(EServiceScopeKind.App, new TimerService());
                GameServices.RegisterService(EServiceScopeKind.App, new UIService());
                GameServices.RegisterService(EServiceScopeKind.App, new LocalizationService());
                GameServices.RegisterService(EServiceScopeKind.App, new ProcedureService());
                await ProcedureServiceSettings.StartProcedure();
            }
        }
    }
}
