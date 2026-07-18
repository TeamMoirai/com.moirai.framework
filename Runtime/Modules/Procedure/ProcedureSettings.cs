using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.FSM;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("流程设置", "游戏流程状态机配置", -490)]
    public sealed partial class ProcedureSettings : FrameworkSettings<ProcedureSettings>
    {
        [HideInInspector]
        [SerializeField] private string[] m_AvailableProcedureTypeNames = null;
        
        [HideInInspector]
        [SerializeField] private string m_EntranceProcedureTypeName = null;

        private IProcedureModule _procedureModule = null;
        private ProcedureBase _entranceProcedure = null;
        
        /// <summary>
        /// 获取当前流程。
        /// </summary>
        public static ProcedureBase CurrentProcedure
        {
            get
            {
                if (Instance._procedureModule == null)
                {
                    return null;
                }

                return Instance._procedureModule.CurrentProcedure;
            }
        }

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        public static float CurrentProcedureTime
        {
            get
            {
                if (Instance._procedureModule == null)
                {
                    return 0f;
                }

                return Instance._procedureModule.CurrentProcedureTime;
            }
        }

        /// <summary>
        /// 启动流程。
        /// </summary>
        public static async UniTaskVoid StartProcedure()
        {
            if (Instance._procedureModule == null)
            {
                Instance._procedureModule = ModuleSystem.GetModule<IProcedureModule>();
            }

            if (Instance._procedureModule == null)
            {
                Log.Fatal("Procedure manager is invalid.");
                return;
            }

            ProcedureBase[] procedures = new ProcedureBase[Instance.m_AvailableProcedureTypeNames.Length];
            for (int i = 0; i < Instance.m_AvailableProcedureTypeNames.Length; i++)
            {
                Type procedureType = AssemblyUtility.GetType(Instance.m_AvailableProcedureTypeNames[i]);
                if (procedureType == null)
                {
                    Log.Error("Can not find procedure type '{0}'.", Instance.m_AvailableProcedureTypeNames[i]);
                    return;
                }

                procedures[i] = (ProcedureBase)Activator.CreateInstance(procedureType);
                if (procedures[i] == null)
                {
                    Log.Error("Can not create procedure instance '{0}'.", Instance.m_AvailableProcedureTypeNames[i]);
                    return;
                }

                if (Instance.m_EntranceProcedureTypeName == Instance.m_AvailableProcedureTypeNames[i])
                {
                    Instance._entranceProcedure = procedures[i];
                }
            }

            if (Instance._entranceProcedure == null)
            {
                Log.Error("Entrance procedure is invalid.");
                return;
            }

            Instance._procedureModule.Initialize(ModuleSystem.GetModule<IFSMModule>(), procedures);

            await UniTask.Yield();

            Instance._procedureModule.StartProcedure(Instance._entranceProcedure.GetType());
        }

#if UNITY_EDITOR

        /// <summary>
        /// 编辑器侧订阅：设置被重置时刷新 Inspector 缓存状态。
        /// </summary>
        internal event Action SettingsReset;

        protected internal override void Reset()
        {
            // 设置默认值
            var procedureTypeNames = GetProcedureTypeNames();
            m_AvailableProcedureTypeNames = procedureTypeNames;
            m_EntranceProcedureTypeName = procedureTypeNames.Single(x => x.Contains("ProcedureLaunch"));

            SettingsReset?.Invoke();
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