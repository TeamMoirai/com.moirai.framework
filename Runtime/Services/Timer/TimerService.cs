using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;
using Moirai.Atropos.Debugger;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务外观（Facade）——全框架统一的静态计时器访问入口。
    /// <para>默认后端 <see cref="DefaultTimerHandler"/> 是两条独立引擎泳道的复合外观：按秒的四级时间轮
    /// （<c>Delay</c> 系列，缩放 / 非缩放各一轮）与按帧的帧计时（<c>WaitFrame</c> 系列），
    /// 泳道号内嵌于句柄并按位路由；可在 <see cref="TimerServiceSettings"/> 中替换实现。</para>
    /// <para>公开 API：<c>Delay</c> / <c>DelayUnsafe</c> / <c>WaitFrame</c> / <c>WaitFrameUnsafe</c> /
    /// <c>Cancel</c> / <c>Pause</c> / <c>Resume</c> / <c>IsDone</c>。</para>
    /// <para>降级契约：全部外观 API 经 <c>s_Handler?.</c> 静默降级（未注册/未初始化时返回安全默认值），与全框架统一。</para>
    /// <para><c>Handler</c> 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [ServiceDependency(typeof(DebuggerService))]
    [HandlerHost(typeof(TimerServiceHandler))]
    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public partial class TimerService : ServiceBase, IServiceTickable, IServiceFixedTickable, IServiceLateTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>创建默认计时器处理器（settings 未配置时的代码兜底）。</summary>
        internal static TimerServiceHandler CreateDefaultHandler() => new DefaultTimerHandler();

        /// <summary>
        /// 从 <see cref="TimerServiceSettings"/> 解析计时器处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）。</para>
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static TimerServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<TimerService>();
            return TimerServiceSettings.TimerServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>初始化计时器服务。由容器在构建期调用。</summary>
        public override void OnInit()
        {
            _ = Handler;
            DebuggerService.RegisterDebuggerWindow("Profiler/Timer", new TimerServiceDebuggerWindow());
        }

        /// <summary>关闭计时器服务。由容器在关闭期调用。</summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>容器 Update 驱动——推进时间轮（未就绪时静默降级）。</summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        /// <summary>容器 FixedUpdate 驱动。</summary>
        public void FixedTick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.FixedTick(elapseSeconds, realElapseSeconds);

        /// <summary>容器 LateUpdate 驱动。</summary>
        public void LateTick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.LateTick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 调度 API [SCHEDULING]

        /// <summary>延迟 <paramref name="delaySeconds"/> 秒后回调。</summary>
        /// <param name="delaySeconds">延迟秒数。</param>
        /// <param name="onComplete">完成回调（热路径请用缓存方法组，禁止捕获闭包）。</param>
        /// <param name="isLooped">是否循环。</param>
        /// <param name="ignoreTimeScale">是否忽略时间缩放。</param>
        /// <param name="phase">触发阶段。</param>
        /// <returns>计时器句柄；无效时为 0。</returns>
        public static ulong Delay(float delaySeconds, Action onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.Delay(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>延迟并每帧上报进度（0..1），无完成回调。</summary>
        public static ulong Delay(float delaySeconds, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.Delay(delaySeconds, (Action)null, onUpdate, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>延迟后带参回调。</summary>
        public static ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) where T : class =>
            s_Handler?.Delay(delaySeconds, onComplete, arg, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>延迟并在到期前每帧上报进度（0..1）。</summary>
        public static ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.Delay(delaySeconds, onComplete, onUpdate, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>延迟（零分配函数指针绑定）。</summary>
        public static ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.DelayUnsafe(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>等待 N 帧后回调。</summary>
        public static ulong WaitFrame(int frames, Action onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.WaitFrame(frames, onComplete, isLooped, phase) ?? 0UL;

        /// <summary>等待 N 帧，每帧回调累计帧数（1 起）。</summary>
        public static ulong WaitFrame(int frames, Action<int> onUpdate,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.WaitFrame(frames, onUpdate, isLooped, phase) ?? 0UL;

        /// <summary>帧等待（零分配函数指针绑定）。</summary>
        public static ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update) =>
            s_Handler?.WaitFrameUnsafe(frames, onComplete, isLooped, phase) ?? 0UL;

        /// <summary>暂停。</summary>
        public static void Pause(ulong timerHandle) => s_Handler?.Pause(timerHandle);

        /// <summary>暂停全部计时器。</summary>
        public static void PauseAll() => s_Handler?.PauseAll();

        /// <summary>恢复。</summary>
        public static void Resume(ulong timerHandle) => s_Handler?.Resume(timerHandle);

        /// <summary>恢复全部计时器。</summary>
        public static void ResumeAll() => s_Handler?.ResumeAll();

        /// <summary>是否在运行（句柄有效且未暂停）。</summary>
        public static bool IsRunning(ulong timerHandle) => s_Handler?.IsRunning(timerHandle) ?? false;

        /// <summary>剩余秒数（帧计时器返回 0）。</summary>
        public static float GetLeftTime(ulong timerHandle) => s_Handler?.GetLeftTime(timerHandle) ?? 0f;

        /// <summary>剩余帧数（时间计时器返回 0）。</summary>
        public static int GetLeftFrames(ulong timerHandle) => s_Handler?.GetLeftFrames(timerHandle) ?? 0;

        /// <summary>是否已结束（完成 / 取消 / 无效）。</summary>
        public static bool IsDone(ulong timerHandle) => s_Handler?.IsDone(timerHandle) ?? true;
        
        /// <summary>是否已暂停。</summary>
        public static bool IsPaused(ulong timerHandle) => !IsRunning(timerHandle) && !IsDone(timerHandle);

        /// <summary>已流逝量：时间计时器为秒，帧计时器为帧。</summary>
        public static float GetElapsed(ulong timerHandle) => s_Handler?.GetElapsed(timerHandle) ?? 0f;

        /// <summary>总时长：时间计时器为秒，帧计时器为帧。</summary>
        public static float GetDuration(ulong timerHandle) => s_Handler?.GetDuration(timerHandle) ?? 0f;

        /// <summary>按原时长重启。</summary>
        public static void Restart(ulong timerHandle) => s_Handler?.Restart(timerHandle);

        /// <summary>取消计时器。</summary>
        public static void Cancel(ulong timerHandle) => s_Handler?.Cancel(timerHandle);

        /// <summary>取消全部计时器。</summary>
        public static void CancelAll() => s_Handler?.CancelAll();

        /// <summary>等待计时器完成（默认后端为按槽位完成信号驱动，非每帧轮询）。</summary>
        public static UniTask WaitAsync(ulong timerHandle, CancellationToken cancellationToken = default) =>
            s_Handler?.WaitAsync(timerHandle, cancellationToken) ?? UniTask.CompletedTask;

        #endregion
    }

    /// <summary>
    /// <c>ulong</c> 计时器句柄扩展。
    /// </summary>
    public static class TimerHandleExtensions
    {
        /// <summary>取消计时器。</summary>
        public static void Cancel(this ulong timerHandle)
        {
            if (timerHandle != 0UL) TimerService.Cancel(timerHandle);
        }

        /// <summary>句柄是否已结束。</summary>
        public static bool IsDone(this ulong timerHandle) => TimerService.IsDone(timerHandle);

        /// <summary>剩余秒数（帧计时器返回 0）。</summary>
        public static float GetLeftTime(this ulong timerHandle) => TimerService.GetLeftTime(timerHandle);

        /// <summary>剩余帧数（时间计时器返回 0）。</summary>
        public static int GetLeftFrames(this ulong timerHandle) => TimerService.GetLeftFrames(timerHandle);

        /// <summary>暂停。</summary>
        public static void Pause(this ulong timerHandle)
        {
            if (timerHandle != 0UL) TimerService.Pause(timerHandle);
        }

        /// <summary>恢复。</summary>
        public static void Resume(this ulong timerHandle)
        {
            if (timerHandle != 0UL) TimerService.Resume(timerHandle);
        }

        /// <summary>等待计时器完成（按槽位完成信号驱动）。</summary>
        public static UniTask WaitAsync(this ulong timerHandle, CancellationToken cancellationToken = default)
            => TimerService.WaitAsync(timerHandle, cancellationToken);
    }
}
