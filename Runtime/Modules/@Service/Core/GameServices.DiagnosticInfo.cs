using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class GameServices
    {
        /// <summary>
        /// 服务诊断信息。供编辑器窗口和调试器组件展示已注册服务状态。
        /// </summary>
        internal struct DiagnosticInfo
        {
            public string InterfaceType;
            public string ImplementationType;
            public EServiceScopeKind Scope;
            public int Priority;
            public bool HasUpdate;
            public bool HasFixedUpdate;
            public bool HasLateUpdate;
            public bool HasGizmo;
        }

        #region 诊断信息收集 [DIAGNOSTIC COLLECTION]

        /// <summary>
        /// 收集全部活跃容器（App → Scene → Gameplay）内已注册服务的诊断信息。
        /// </summary>
        internal static List<DiagnosticInfo> GetDiagnosticInfo()
        {
            var result = new List<DiagnosticInfo>();
            AppContainer?.CollectDiagnosticInfo(result);
            SceneContainer?.CollectDiagnosticInfo(result);
            GameplayContainer?.CollectDiagnosticInfo(result);
            return result;
        }

        #endregion
    }
}
