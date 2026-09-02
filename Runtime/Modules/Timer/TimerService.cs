using System;
using Unity.IL2CPP.CompilerServices;
using Moirai.Atropos.Debugger;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务外观（Facade）。
    /// <para>统一的静态计时器访问入口，通过替换 <see cref="Handler"/> 即可在不同计时器后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="TimerServiceSettings"/> 创建处理器实例。</para>
    /// <para>外观方法一律经 Handler 属性转发：服务未就绪时按需初始化，设置资产不可用或默认工厂缺失时抛出异常（fail-fast），不静默降级。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(TimerServiceHandler))]
    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public partial class TimerService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="TimerServiceSettings"/> 创建默认计时器处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认计时器处理器实例。</returns>
        private static TimerServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<TimerService>();
            return TimerServiceSettings.TimerServiceHandler;
        }

        /// <summary>
        /// 初始化计时器服务。由容器在构建期调用。
        /// <para>确保 <c>TimerService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
        /// 并向游戏内调试器注册调试面板（依赖组合根先注册 <see cref="DebuggerService"/>——外观未就绪时静默跳过）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
            DebuggerService.RegisterDebuggerWindow("Profiler/Timer", new TimerServiceDebugView());
        }

        /// <summary>
        /// 关闭计时器服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器推进时间轮。
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

        #region 计时器管理 [TIMER MANAGEMENT]

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <returns>计时器Id。</returns>
        public static ulong AddTimer(Action callback, float time, bool isLoop = false, bool isUnscaled = false) =>
            Handler.AddTimer(callback, time, isLoop, isUnscaled);

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="arg">传参。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <returns>计时器Id。</returns>
        public static ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class =>
            Handler.AddTimer(callback, arg, time, isLoop, isUnscaled);

        /// <summary>
        /// 暂停计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void Stop(ulong timerId) => Handler.Stop(timerId);

        /// <summary>
        /// 恢复计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void Resume(ulong timerId) => Handler.Resume(timerId);

        /// <summary>
        /// 查询计时器是否处于运行状态。
        /// </summary>
        /// <param name="timerHandle">计时器句柄。</param>
        /// <returns>句柄有效且未暂停时返回 true；句柄失效返回 false。</returns>
        public static bool IsRunning(ulong timerHandle) => Handler.IsRunning(timerHandle);

        /// <summary>
        /// 查询计时器剩余时间。
        /// </summary>
        /// <param name="timerHandle">计时器句柄。</param>
        /// <returns>剩余秒数（已暂停的计时器返回其记录的剩余时间）；句柄失效返回 0。</returns>
        public static float GetLeftTime(ulong timerHandle) => Handler.GetLeftTime(timerHandle);

        /// <summary>
        /// 重启计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void Restart(ulong timerId) => Handler.Restart(timerId);

        /// <summary>
        /// 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void RemoveTimer(ulong timerId) => Handler.RemoveTimer(timerId);

        #endregion
    }
}
