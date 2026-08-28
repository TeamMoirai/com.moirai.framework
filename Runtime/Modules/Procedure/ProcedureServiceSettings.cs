using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("[服务]流程设置", "游戏流程状态机配置", -500)]
    public sealed partial class ProcedureServiceSettings : FrameworkSettings<ProcedureServiceSettings>
    {
        [HideInInspector]
        [SerializeField] private string[] m_AvailableProcedureTypeNames = null;
        
        [HideInInspector]
        [SerializeField] private string m_EntranceProcedureTypeName = null;

        private ProcedureBase _entranceProcedure = null;

        /// <summary>
        /// 获取当前流程。
        /// </summary>
        public static ProcedureBase CurrentProcedure => ProcedureService.CurrentProcedure;

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        public static float CurrentProcedureTime => ProcedureService.CurrentProcedureTime;

        /// <summary>
        /// 启动流程。
        /// </summary>
        public static async UniTask StartProcedure()
        {
            ProcedureBase[] procedures = new ProcedureBase[Instance.m_AvailableProcedureTypeNames.Length];
            for (int i = 0; i < Instance.m_AvailableProcedureTypeNames.Length; i++)
            {
                Type procedureType = AssemblyUtility.GetType(Instance.m_AvailableProcedureTypeNames[i]);
                if (procedureType == null)
                {
                    LogUtility.Error("Can not find procedure type '{0}'.", Instance.m_AvailableProcedureTypeNames[i]);
                    return;
                }

                procedures[i] = (ProcedureBase)Activator.CreateInstance(procedureType);
                if (procedures[i] == null)
                {
                    LogUtility.Error("Can not create procedure instance '{0}'.", Instance.m_AvailableProcedureTypeNames[i]);
                    return;
                }

                if (Instance.m_EntranceProcedureTypeName == Instance.m_AvailableProcedureTypeNames[i])
                {
                    Instance._entranceProcedure = procedures[i];
                }
            }

            if (Instance._entranceProcedure == null)
            {
                LogUtility.Error("Entrance procedure is invalid.");
                return;
            }

            ProcedureService.Initialize(procedures);

            await UniTask.Yield();

            ProcedureService.StartProcedure(Instance._entranceProcedure.GetType());
        }

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
            m_EntranceProcedureTypeName = procedureTypeNames.Single(x => x.Contains("ProcedureLaunch"));

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