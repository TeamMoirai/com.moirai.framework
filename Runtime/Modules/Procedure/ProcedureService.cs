using System;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程服务门面（Facade）。
    /// <para>统一的静态流程访问入口，通过替换 <see cref="Handler"/> 即可切换流程状态机后端。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 创建默认处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(ProcedureHandler))]
    public partial class ProcedureService : ServiceBase, IServiceTickable
    {
        public override int Priority => -2;

        #region 处理器 [HANDLER]

        /// <summary>
        /// 创建默认流程处理器。
        /// </summary>
        /// <returns>默认流程处理器实例。</returns>
        private static ProcedureHandler CreateDefaultHandler()
        {
            return new ProcedureHandler();
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化流程服务。由容器在构建期调用。
        /// <para>确保 <c>ProcedureService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭流程服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器轮询当前流程。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 流程管理 [PROCEDURE MANAGEMENT]

        /// <summary>
        /// 获取当前流程。
        /// </summary>
        public static ProcedureBase CurrentProcedure => s_Handler?.CurrentProcedure;

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        public static float CurrentProcedureTime => s_Handler?.CurrentProcedureTime ?? 0f;

        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public static void Initialize(params ProcedureBase[] procedures) =>
            s_Handler?.Initialize(procedures);

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <typeparam name="T">要开始的流程类型。</typeparam>
        public static void StartProcedure<T>() where T : ProcedureBase =>
            s_Handler?.StartProcedure(typeof(T));

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public static void StartProcedure(Type procedureType) =>
            s_Handler?.StartProcedure(procedureType);

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <typeparam name="T">要检查的流程类型。</typeparam>
        /// <returns>是否存在流程。</returns>
        public static bool HasProcedure<T>() where T : ProcedureBase =>
            s_Handler != null && s_Handler.HasProcedure(typeof(T));

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public static bool HasProcedure(Type procedureType) =>
            s_Handler != null && s_Handler.HasProcedure(procedureType);

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <typeparam name="T">要切换的流程类型。</typeparam>
        public static void ChangeState<T>() where T : ProcedureBase =>
            s_Handler?.ChangeState(typeof(T));

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <param name="procedureType">要切换的状态类型。</param>
        public static void ChangeState(Type procedureType) =>
            s_Handler?.ChangeState(procedureType);

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <typeparam name="T">要获取的流程类型。</typeparam>
        /// <returns>要获取的流程。</returns>
        public static ProcedureBase GetProcedure<T>() where T : ProcedureBase =>
            s_Handler?.GetProcedure(typeof(T));

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        public static ProcedureBase GetProcedure(Type procedureType) =>
            s_Handler?.GetProcedure(procedureType);

        /// <summary>
        /// 重启流程。
        /// <remarks>默认使用第一个流程作为启动流程。</remarks>
        /// </summary>
        /// <param name="procedures">新的流程。</param>
        /// <returns>是否重启成功。</returns>
        public static bool RestartProcedure(params ProcedureBase[] procedures) =>
            s_Handler?.RestartProcedure(procedures) ?? false;

        #endregion
    }
}
