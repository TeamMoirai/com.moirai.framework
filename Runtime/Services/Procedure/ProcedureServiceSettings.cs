using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
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

        [HideInInspector]
        [SerializeField] private string m_EntranceProcedureTypeName = null;

        /// <summary>
        /// 启动流程（引导入口，失败 fail-fast）。
        /// <para>流程服务未注册、类型解析失败或入口流程无效时抛出 <see cref="GameException"/>——
        /// 启动链配置错误属发布级缺陷，静默吞掉会让玩家面对永久黑屏；异常经 <c>Forget()</c> 转为
        /// <c>UniTaskScheduler.UnobservedTaskException</c> 输出错误日志，调用方不应捕获吞掉。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌（由 <see cref="ProcedureStarter"/> 传入宿主销毁令牌，
        /// 避免退出播放/应用后让帧续体撞上域拆除）。</param>
        public static async UniTask StartProcedure(CancellationToken cancellationToken = default)
        {
            if (!ProcedureService.IsValid)
            {
                throw new GameException(
                    "ProcedureService is not registered when StartProcedure is called — " +
                    "ensure GameApp has initialized the service world before ProcedureStarter awakes.");
            }

            ProcedureBase[] procedures = new ProcedureBase[Instance.m_AvailableProcedureTypeNames.Length];
            ProcedureBase entranceProcedure = null;
            for (int i = 0; i < Instance.m_AvailableProcedureTypeNames.Length; i++)
            {
                Type procedureType = AssemblyUtility.GetType(Instance.m_AvailableProcedureTypeNames[i]);
                if (procedureType == null)
                {
                    throw new GameException(StringUtility.Format(
                        "Can not find procedure type '{0}'.", Instance.m_AvailableProcedureTypeNames[i]));
                }

                procedures[i] = (ProcedureBase)Activator.CreateInstance(procedureType);
                if (procedures[i] == null)
                {
                    throw new GameException(StringUtility.Format(
                        "Can not create procedure instance '{0}'.", Instance.m_AvailableProcedureTypeNames[i]));
                }

                if (Instance.m_EntranceProcedureTypeName == Instance.m_AvailableProcedureTypeNames[i])
                {
                    entranceProcedure = procedures[i];
                }
            }

            if (entranceProcedure == null)
            {
                throw new GameException("Entrance procedure is invalid.");
            }

            ProcedureService.Initialize(procedures);

            // 让出一帧：流程 OnInit 内按框架约定经 MainThreadDispatcher 排队的场景对象访问在下一帧执行，
            // 须等其落地后再进入首个流程的 OnEnter
            await UniTask.Yield(cancellationToken);

            ProcedureService.StartProcedure(entranceProcedure.GetType());
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