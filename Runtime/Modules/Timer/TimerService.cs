using System;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务外观（Facade）。
    /// <para>统一的静态计时器访问入口，通过替换 <see cref="Handler"/> 即可在不同计时器后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="TimerServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(TimerServiceHandler))]
    public partial class TimerService : ServiceBase, IServiceTickable
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="TimerServiceSettings"/> 创建默认计时器处理器。
        /// </summary>
        /// <returns>默认计时器处理器实例。</returns>
        private static TimerServiceHandler CreateDefaultHandler()
        {
            return TimerServiceSettings.TimerServiceHandler;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化计时器服务。由容器在构建期调用。
        /// <para>确保 <c>TimerService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭计时器服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器推进时间轮。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 计时器管理 [TIMER MANAGEMENT]

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <param name="args">传参。(避免闭包)</param>
        /// <returns>计时器Id。</returns>
        public static ulong AddTimer(TimerCallback callback, float time, bool isLoop = false, bool isUnscaled = false, params object[] args) =>
            s_Handler?.AddTimer(callback, time, isLoop, isUnscaled, args) ?? 0UL;

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <returns>计时器Id。</returns>
        public static ulong AddTimer(Action callback, float time, bool isLoop = false, bool isUnscaled = false) =>
            s_Handler?.AddTimer(callback, time, isLoop, isUnscaled) ?? 0UL;

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
            s_Handler?.AddTimer(callback, arg, time, isLoop, isUnscaled) ?? 0UL;

        /// <summary>
        /// 暂停计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void Stop(ulong timerId) => s_Handler?.Stop(timerId);

        /// <summary>
        /// 恢复计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void Resume(ulong timerId) => s_Handler?.Resume(timerId);

        /// <summary>
        /// 重启计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void Restart(ulong timerId) => s_Handler?.Restart(timerId);

        /// <summary>
        /// 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public static void RemoveTimer(ulong timerId) => s_Handler?.RemoveTimer(timerId);

        /// <summary>
        /// 预热计时器容量。
        /// </summary>
        /// <param name="capacity">容量。</param>
        public static void Prewarm(int capacity) => s_Handler?.Prewarm(capacity);

        /// <summary>
        /// 获取计时器统计。
        /// </summary>
        /// <param name="activeCount">活跃的数量。</param>
        /// <param name="poolCapacity">池容量。</param>
        /// <param name="peakActiveCount">峰值活跃数量。</param>
        /// <param name="freeCount">空闲数量。</param>
        public static void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            if (s_Handler == null)
            {
                activeCount = 0;
                poolCapacity = 0;
                peakActiveCount = 0;
                freeCount = 0;
                return;
            }

            s_Handler.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
        }

        /// <summary>
        /// 获取所有计时器调试信息。
        /// </summary>
        /// <param name="results">结果缓冲区。</param>
        /// <returns>填充的数量。</returns>
        public static int GetAllTimers(TimerDebugInfo[] results) =>
            s_Handler?.GetAllTimers(results) ?? 0;

        #endregion
    }
}
