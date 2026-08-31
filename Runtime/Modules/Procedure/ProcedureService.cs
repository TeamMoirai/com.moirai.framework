using System;
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
    /// 组合根手动按依赖链序注册本服务及全部链上服务（Resource/Timer/UI/Localization），
    /// <see cref="GameServices.RegisterService{T}"/> 在注册期按本类 <c>[ServiceDependency]</c> 声明校验依赖就绪
    /// （UI/Timer→Resource、Audio/Scene/ObjectPool 亦传递依赖 Resource）。
    /// </remarks>
    [ServiceDependency(typeof(ResourceService), typeof(UIService), typeof(LocalizationService), typeof(TimerService))]
    [HandlerHost(typeof(ProcedureServiceHandler))]
    public partial class ProcedureService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认流程处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册；
        /// 重入路径下 <c>s_Handler</c> 已就绪时直接返回，避免重复实例化。</para>
        /// </summary>
        /// <returns>默认流程处理器实例。</returns>
        private static ProcedureServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<ProcedureService>();
            return new DefaultProcedureHandler();
        }

        public override int Priority => -2;

        /// <summary>
        /// 初始化流程服务。由容器在构建期调用。
        /// <para>确保 <c>ProcedureService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
        /// 并向游戏内调试器注册调试面板（依赖组合根先注册 <see cref="DebuggerService"/>——外观未就绪时静默跳过）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
            DebuggerService.RegisterDebuggerWindow("Profiler/Procedure", new ProcedureServiceDebugView());
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
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            Handler.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 流程管理 [PROCEDURE MANAGEMENT]

        /// <summary>
        /// 获取当前流程。
        /// </summary>
        public static ProcedureBase CurrentProcedure => Handler.CurrentProcedure;

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        public static float CurrentProcedureTime => Handler.CurrentProcedureTime;

        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public static void Initialize(params ProcedureBase[] procedures) =>
            Handler.Initialize(procedures);

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <typeparam name="T">要开始的流程类型。</typeparam>
        public static void StartProcedure<T>() where T : ProcedureBase =>
            Handler.StartProcedure(typeof(T));

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public static void StartProcedure(Type procedureType) =>
            Handler.StartProcedure(procedureType);

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <typeparam name="T">要检查的流程类型。</typeparam>
        /// <returns>是否存在流程。</returns>
        public static bool HasProcedure<T>() where T : ProcedureBase =>
            Handler.HasProcedure(typeof(T));

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public static bool HasProcedure(Type procedureType) =>
            Handler.HasProcedure(procedureType);

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <typeparam name="T">要切换的流程类型。</typeparam>
        public static void ChangeState<T>() where T : ProcedureBase =>
            Handler.ChangeState(typeof(T));

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <param name="procedureType">要切换的状态类型。</param>
        public static void ChangeState(Type procedureType) =>
            Handler.ChangeState(procedureType);

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <typeparam name="T">要获取的流程类型。</typeparam>
        /// <returns>要获取的流程。</returns>
        public static ProcedureBase GetProcedure<T>() where T : ProcedureBase =>
            Handler.GetProcedure(typeof(T));

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        public static ProcedureBase GetProcedure(Type procedureType) =>
            Handler.GetProcedure(procedureType);

        /// <summary>
        /// 重启流程。
        /// <remarks>默认使用第一个流程作为启动流程。</remarks>
        /// </summary>
        /// <param name="procedures">新的流程。</param>
        /// <returns>是否重启成功。</returns>
        public static bool RestartProcedure(params ProcedureBase[] procedures) =>
            Handler.RestartProcedure(procedures);

        #endregion
    }
}
