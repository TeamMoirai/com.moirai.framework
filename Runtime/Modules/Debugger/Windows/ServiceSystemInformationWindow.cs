using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 服务系统信息窗口（<see cref="GameServices"/> 作用域统计与服务清单）。
    /// </summary>
    public sealed class ServiceSystemInformationWindow : PollingDebuggerWindowBase
    {
        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化服务系统信息窗口的新实例。
        /// </summary>
        public ServiceSystemInformationWindow() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement summaryCard = AddSection(root, "Service System");

            List<GameServices.DiagnosticInfo> diagnostics = GameServices.GetDiagnosticInfo();
            int appCount = 0, sceneCount = 0, gameplayCount = 0;
            int updateCount = 0, fixedUpdateCount = 0, lateUpdateCount = 0, gizmoCount = 0;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                GameServices.DiagnosticInfo info = diagnostics[i];
                if (info.Scope == EServiceScopeKind.App) appCount++;
                else if (info.Scope == EServiceScopeKind.Scene) sceneCount++;
                else gameplayCount++;
                if (info.HasUpdate) updateCount++;
                if (info.HasFixedUpdate) fixedUpdateCount++;
                if (info.HasLateUpdate) lateUpdateCount++;
                if (info.HasGizmo) gizmoCount++;
            }

            AddRow(summaryCard, "Registered Services", diagnostics.Count.ToString());
            AddRow(summaryCard, "  App Scope", appCount.ToString());
            AddRow(summaryCard, "  Scene Scope", sceneCount.ToString());
            AddRow(summaryCard, "  Gameplay Scope", gameplayCount.ToString());
            AddRow(summaryCard, "Update", updateCount.ToString());
            AddRow(summaryCard, "FixedUpdate", fixedUpdateCount.ToString());
            AddRow(summaryCard, "LateUpdate", lateUpdateCount.ToString());
            AddRow(summaryCard, "Gizmo", gizmoCount.ToString());
            AddRow(summaryCard, "App Scope", GameServices.HasApp ? "Active" : "—");
            AddRow(summaryCard, "Scene Scope", GameServices.HasScene ? "Active" : "—");
            AddRow(summaryCard, "Gameplay Scope", GameServices.HasGameplay ? "Active" : "—");

            VisualElement listCard = AddSection(root, "Service List");
            for (int i = 0; i < diagnostics.Count; i++)
            {
                GameServices.DiagnosticInfo info = diagnostics[i];
                string title = StringUtility.Format("[{0}] {1}", ScopeToString(info.Scope), info.ContractType);
                string entry = StringUtility.Format("{0} (P:{1} [{2}])", info.ImplementationType, info.Priority, GetTickFlags(info));
                AddRow(listCard, title, entry);
            }
        }

        #endregion

        #region 私有 [PRIVATE]

        private static string GetTickFlags(GameServices.DiagnosticInfo info)
        {
            bool anyFlag = false;
            string result = string.Empty;
            if (info.HasUpdate)
            {
                result = "U";
                anyFlag = true;
            }

            if (info.HasFixedUpdate)
            {
                result = anyFlag ? StringUtility.Concat(result, " F") : "F";
                anyFlag = true;
            }

            if (info.HasLateUpdate)
            {
                result = anyFlag ? StringUtility.Concat(result, " L") : "L";
                anyFlag = true;
            }

            if (info.HasGizmo)
            {
                result = anyFlag ? StringUtility.Concat(result, " G") : "G";
                anyFlag = true;
            }

            return anyFlag ? result : "—";
        }

        private static string ScopeToString(EServiceScopeKind scope)
        {
            return scope switch
            {
                EServiceScopeKind.App => "App",
                EServiceScopeKind.Scene => "Scene",
                EServiceScopeKind.Gameplay => "Gameplay",
                _ => scope.ToString(),
            };
        }

        #endregion
    }
}
