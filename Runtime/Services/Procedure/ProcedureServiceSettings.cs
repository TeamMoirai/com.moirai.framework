using System;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("[服务]流程设置", "游戏流程状态机配置", -500)]
    public sealed partial class ProcedureServiceSettings : FrameworkSettings<ProcedureServiceSettings>
    {
        [Tooltip("可拔插替换的流程状态机后端")]
        [ProviderDropdown]
        [SerializeReference] private ProcedureServiceHandler m_ProcedureServiceHandler = ProcedureService.CreateDefaultHandler();
        /// <summary>当前流程处理器实例。</summary>
        public static ProcedureServiceHandler ProcedureServiceHandler => Instance.m_ProcedureServiceHandler;

        [HideInInspector]
        [SerializeField] private string[] m_AvailableProcedureTypeNames = null;
        internal static string[] AvailableProcedureTypeNames => Instance.m_AvailableProcedureTypeNames;

        [HideInInspector]
        [SerializeField] private string m_EntranceProcedureTypeName = null;
        internal static string EntranceProcedureTypeName => Instance.m_EntranceProcedureTypeName;

#if UNITY_EDITOR

        /// <summary>
        /// 编辑器侧订阅：设置被重置时刷新 Inspector 缓存状态。
        /// </summary>
        internal event Action onSettingsReset;

        private void Reset()
        {
            // 设置默认值
            var procedureTypeNames = GetProcedureTypeNames();
            m_AvailableProcedureTypeNames = procedureTypeNames;
            // 优先约定入口 ProcedureLaunch；缺失时回退第一个可用流程（Reset 不可抛异常中断资产重置）
            m_EntranceProcedureTypeName = procedureTypeNames.FirstOrDefault(x => x.Contains("ProcedureLaunch"))
                ?? procedureTypeNames.FirstOrDefault();

            onSettingsReset?.Invoke();
        }

        private static string[] GetProcedureTypeNames()
        {
            return AssemblyUtility.GetRuntimeTypes(typeof(ProcedureBase))
                .Where(t => Attribute.IsDefined(t, typeof(ProcedureLauncherAttribute)))
                .Select(t => t.FullName)
                .ToArray();
        }

#endif
    }
}