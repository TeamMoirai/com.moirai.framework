using System;
using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程服务外观（Facade）。
    /// <para>统一的静态流程访问入口，通过替换 <see cref="Handler"/> 即可切换流程状态机后端。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 创建默认处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    /// <remarks>
    /// 组合根无序注册全部链上服务，世界初始化按 <c>[ServiceDependency]</c> 声明拓扑排序（依赖缺失/循环即 fail-fast）。
    /// 调试器依赖经 <see cref="DebuggerService"/> 声明显式建模——OnInit 注册调试面板要求 Debugger 拓扑先行。
    /// <para><b>依赖门槛意图</b>：<see cref="ResourceService"/> / <see cref="UIService"/> / <see cref="LocalizationService"/> /
    /// <see cref="TimerService"/> 四个依赖并非本服务自身消费，而是启动链的时序门槛——游戏侧启动流程
    /// （初始化资源包、闪屏 UI、多语言加载、计时驱动）要求这四者在流程 OnInit 前拓扑就绪，
    /// 缺失即世界初始化 fail-fast。不含这些服务的极简项目应移除对应声明（耦合点仅此一处）。</para>
    /// <para><b>未就绪契约</b>：查询类 API（<see cref="CurrentProcedure"/>、<see cref="HasProcedure"/> 等）
    /// 在处理器缺失或状态机未 <see cref="Initialize"/> 时静默降级为安全默认值；变更类 API 中
    /// <see cref="StartProcedure"/> / <see cref="ChangeState"/> 同样在未就绪时忽略并告警；
    /// <see cref="Initialize"/> / <see cref="RestartProcedure"/> 仅要求处理器在位（二者是引导/重建入口，
    /// 不依赖状态机已就绪）。后端直接调用仍会 fail-fast。</para>
    /// <para><b>切换广播</b>：<see cref="ProcedureChanged"/> 在切换完成（新流程 OnEnter 返回）后同步触发，
    /// 回调异常被逐订阅者隔离；回调内禁止同步 <see cref="StartProcedure"/> / <see cref="ChangeState"/>
    /// （处理器在广播期置位，重入即抛 <see cref="GameException"/>；OnEnter/OnLeave 内的合法嵌套切换不受影响）。</para>
    /// </remarks>
    [ServiceDependency(typeof(DebuggerService), typeof(ResourceService), typeof(UIService), typeof(LocalizationService), typeof(TimerService))]
    [HandlerHost(typeof(ProcedureServiceHandler))]
    public partial class ProcedureService : ServiceBase, IServiceTickable
    {
        private static readonly ProcedureBase[] EmptyProcedures = new ProcedureBase[0];
        private static readonly ProcedureTransitionRecord[] EmptyTransitions = new ProcedureTransitionRecord[0];

        /// <summary>Tick 懒加载重绑只告警一次（域重载后静态位自动复位，每个会话至多一次）。</summary>
        private static bool s_WarnedLazyRebind;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认流程处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册；
        /// 处理器实例由 <see cref="ProcedureServiceSettings"/> 经 [SerializeReference] 注入（Inspector 可拔插替换）。</para>
        /// </summary>
        /// <returns>默认流程处理器实例。</returns>
        private static ProcedureServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<ProcedureService>();
            return ProcedureServiceSettings.ProcedureServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.PROCEDURE;

        /// <summary>
        /// 初始化流程服务。由容器在构建期调用。
        /// <para>确保 <c>ProcedureService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
        /// 并向游戏内调试器注册调试面板（依赖组合根先注册 <see cref="DebuggerService"/>——外观未就绪时静默跳过）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
            DebuggerService.RegisterDebuggerWindow("Profiler/Procedure", new ProcedureServiceDebuggerWindow());
        }

        /// <summary>
        /// 关闭流程服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器轮询当前流程。
        /// <para><c>s_Handler</c> 静态字段会被域重载（编辑器内脚本编译）清空而服务实例仍在轮询，
        /// 此处经 <see cref="Handler"/> 属性懒加载重绑，避免流程状态机从此静默停摆。</para>
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            var handler = s_Handler;
            if (handler == null)
            {
                handler = Handler;
                if (!s_WarnedLazyRebind)
                {
                    s_WarnedLazyRebind = true;
                    LogUtility.Warning("ProcedureService.Tick 检测到处理器失联（多为编辑器域重载），已懒加载重绑；流程集为空，需重新启动流程。");
                }
            }

            handler.Tick(elapseSeconds, realElapseSeconds);
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 状态机是否已就绪（处理器在位且已 <see cref="Initialize"/>）。直接调用处理器时须先经此守卫——
        /// 外观查询/变更已内建降级，后端路径仍会 fail-fast。
        /// </summary>
        public static bool IsStateReady => s_Handler?.IsStateReady ?? false;

        /// <summary>
        /// 当前流程（处理器缺失或状态机未就绪时为 null）。
        /// </summary>
        public static ProcedureBase CurrentProcedure =>
            s_Handler != null && s_Handler.IsStateReady ? s_Handler.CurrentProcedure : null;

        /// <summary>
        /// 当前流程持续时间（处理器缺失或状态机未就绪时为 0）。
        /// </summary>
        public static float CurrentProcedureTime =>
            s_Handler != null && s_Handler.IsStateReady ? s_Handler.CurrentProcedureTime : 0f;

        /// <summary>
        /// 已注册的全部流程（未就绪时为空集）。
        /// </summary>
        public static IReadOnlyCollection<ProcedureBase> Procedures => s_Handler?.Procedures ?? EmptyProcedures;

        /// <summary>
        /// 最近的流程切换历史（时间升序；未就绪时为空集）。
        /// </summary>
        public static IReadOnlyList<ProcedureTransitionRecord> TransitionHistory =>
            s_Handler?.TransitionHistory ?? EmptyTransitions;

        /// <summary>
        /// 流程切换广播。在切换完成（新流程 OnEnter 返回）后同步触发；启动切换 From 为 null。
        /// <para>关停切换不广播（仅记入 <see cref="TransitionHistory"/>）；回调异常被逐订阅者隔离，不会中断状态机。
        /// 回调内禁止同步 <see cref="StartProcedure"/> / <see cref="ChangeState"/>（会抛 <see cref="GameException"/>）。</para>
        /// </summary>
        public static event Action<ProcedureTransitionRecord> ProcedureChanged;

        /// <summary>
        /// 由 <see cref="ProcedureServiceHandler.RecordTransition"/> 调用的内部广播入口——逐订阅者隔离异常。
        /// </summary>
        internal static void Internal_RaiseProcedureChanged(in ProcedureTransitionRecord record)
        {
            var handlers = ProcedureChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<ProcedureTransitionRecord> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(record);
                }
                catch (Exception ex)
                {
                    LogUtility.Error(StringUtility.Format("ProcedureChanged subscriber '{0}' threw: {1}",
                        handler.Method.DeclaringType?.FullName, ex));
                }
            }
        }

        #endregion

        #region 流程管理 [PROCEDURE MANAGEMENT]

        /// <summary>
        /// 初始化流程管理器（处理器缺失时忽略并告警；不要求状态机已就绪——本 API 即引导入口）。
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public static void Initialize(params ProcedureBase[] procedures)
        {
            if (TryGetHandler("Initialize", out var handler))
            {
                handler.Initialize(procedures);
            }
        }

        /// <summary>
        /// 开始流程（未就绪时忽略并告警）。
        /// </summary>
        /// <typeparam name="T">要开始的流程类型。</typeparam>
        public static void StartProcedure<T>() where T : ProcedureBase
        {
            if (TryGetReadyHandler("StartProcedure", out var handler))
            {
                handler.StartProcedure(typeof(T));
            }
        }

        /// <summary>
        /// 开始流程（未就绪时忽略并告警）。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public static void StartProcedure(Type procedureType)
        {
            if (TryGetReadyHandler("StartProcedure", out var handler))
            {
                handler.StartProcedure(procedureType);
            }
        }

        /// <summary>
        /// 是否存在流程（处理器缺失或状态机未就绪时为 false）。
        /// </summary>
        /// <typeparam name="T">要检查的流程类型。</typeparam>
        /// <returns>是否存在流程。</returns>
        public static bool HasProcedure<T>() where T : ProcedureBase =>
            s_Handler != null && s_Handler.IsStateReady && s_Handler.HasProcedure(typeof(T));

        /// <summary>
        /// 是否存在流程（处理器缺失或状态机未就绪时为 false）。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public static bool HasProcedure(Type procedureType) =>
            s_Handler != null && s_Handler.IsStateReady && s_Handler.HasProcedure(procedureType);

        /// <summary>
        /// 切换流程（未就绪时忽略并告警）。
        /// </summary>
        /// <typeparam name="T">要切换的流程类型。</typeparam>
        public static void ChangeState<T>() where T : ProcedureBase
        {
            if (TryGetReadyHandler("ChangeState", out var handler))
            {
                handler.ChangeState(typeof(T));
            }
        }

        /// <summary>
        /// 切换流程（未就绪时忽略并告警）。
        /// </summary>
        /// <param name="procedureType">要切换的状态类型。</param>
        public static void ChangeState(Type procedureType)
        {
            if (TryGetReadyHandler("ChangeState", out var handler))
            {
                handler.ChangeState(procedureType);
            }
        }

        /// <summary>
        /// 获取流程（处理器缺失或状态机未就绪时为 null）。
        /// </summary>
        /// <typeparam name="T">要获取的流程类型。</typeparam>
        /// <returns>要获取的流程。</returns>
        public static ProcedureBase GetProcedure<T>() where T : ProcedureBase =>
            s_Handler != null && s_Handler.IsStateReady ? s_Handler.GetProcedure(typeof(T)) : null;

        /// <summary>
        /// 获取流程（处理器缺失或状态机未就绪时为 null）。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        public static ProcedureBase GetProcedure(Type procedureType) =>
            s_Handler != null && s_Handler.IsStateReady ? s_Handler.GetProcedure(procedureType) : null;

        /// <summary>
        /// 重启流程。默认使用第一个流程作为启动流程（处理器缺失时忽略并告警；不要求状态机已就绪——
        /// 重启即重建入口）。
        /// </summary>
        /// <param name="procedures">新的流程。</param>
        /// <returns>是否重启成功。</returns>
        public static bool RestartProcedure(params ProcedureBase[] procedures)
        {
            if (!TryGetHandler("RestartProcedure", out var handler))
            {
                return false;
            }

            return handler.RestartProcedure(procedures);
        }

        #endregion

        #region 就绪诊断 [READINESS DIAGNOSTICS]

        /// <summary>
        /// 获取处理器；缺失时告警（引导时序错误须在控制台显式暴露，而非静默丢调用）。
        /// </summary>
        private static bool TryGetHandler(string apiName, out ProcedureServiceHandler handler)
        {
            handler = s_Handler;
            if (handler != null)
            {
                return true;
            }

            LogUtility.Warning(StringUtility.Format(
                "ProcedureService.{0} 被调用时流程服务未就绪（世界未初始化或已关闭），调用已忽略。", apiName));
            return false;
        }

        /// <summary>
        /// 获取已就绪的处理器（已 <see cref="Initialize"/>）；处理器缺失或状态机未初始化时告警并返回 false。
        /// <para><see cref="Initialize"/> / <see cref="RestartProcedure"/> 不走本守卫——二者是引导/重建入口。</para>
        /// </summary>
        private static bool TryGetReadyHandler(string apiName, out ProcedureServiceHandler handler)
        {
            if (!TryGetHandler(apiName, out handler))
            {
                return false;
            }

            if (!handler.IsStateReady)
            {
                LogUtility.Warning(StringUtility.Format(
                    "ProcedureService.{0} 被调用时状态机未初始化（需先 Initialize），调用已忽略。", apiName));
                return false;
            }

            return true;
        }

        #endregion
    }
}
