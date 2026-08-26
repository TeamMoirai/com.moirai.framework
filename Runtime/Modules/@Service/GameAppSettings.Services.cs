using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Procedure;
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
        /// <para>① <see cref="GameServices.RegisterService"/> 注册流程服务——其余框架服务由
        /// <see cref="ServiceDependencyAttribute"/> 声明的依赖链自动递归预注册（零反射、顺序无关）；</para>
        /// <para>② <see cref="ProcedureSettings.StartProcedure"/> 启动流程状态机。</para>
        /// <para>由 <see cref="GameAppSettings.Initiation"/> 在 <c>AfterAssembliesLoaded</c> 阶段调用。</para>
        /// </summary>
        private static partial UniTaskVoid InitializeAppServices()
        {
            return InitializeCore();

            async UniTaskVoid InitializeCore()
            {
                // 仅显式注册流程链根服务；UpdateDriver/Resource/Audio/Scene/Timer/UI 等由依赖链拉起
                GameServices.RegisterService(EServiceScopeKind.App, new ProcedureService());
                await ProcedureSettings.StartProcedure();
            }
        }
    }
}
