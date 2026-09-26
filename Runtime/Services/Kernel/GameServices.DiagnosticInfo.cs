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
            public string ContractType;
            public string ImplementationType;
            public EServiceScopeKind Scope;
            public int Priority;
            public bool HasUpdate;
            public bool HasFixedUpdate;
            public bool HasLateUpdate;
            public bool HasGizmo;

            /// <summary>
            /// 轮询耗时均值（毫秒；自上次 <see cref="ResetPollStatistics"/> 起累计）。
            /// 仅编辑器/开发构建非零。
            /// </summary>
            public float PollAvgMs;

            /// <summary>
            /// 轮询耗时峰值（毫秒；统计窗口内单次最大值）。
            /// </summary>
            public float PollPeakMs;

            /// <summary>
            /// 统计窗口内的轮询采样次数。
            /// </summary>
            public int PollSamples;
        }

        #region 诊断信息收集 [DIAGNOSTIC COLLECTION]

        /// <summary>
        /// 收集全部活跃作用域内已注册服务的诊断信息。
        /// </summary>
        internal static List<DiagnosticInfo> GetDiagnosticInfo()
        {
            var result = new List<DiagnosticInfo>();
            s_World?.CollectDiagnosticInfo(result);
            return result;
        }

        /// <summary>
        /// 清零全部服务的轮询耗时统计（不影响失败计数与熔断状态）。用于诊断窗口切换观察时间窗。
        /// </summary>
        public static void ResetPollStatistics()
        {
            EnsureMainThread();
            s_World?.ResetPollStatistics();
        }

        #endregion
    }
}
