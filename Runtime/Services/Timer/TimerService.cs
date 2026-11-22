using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;
using Moirai.Atropos.Debugger;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务外观（Facade）：全框架统一的静态计时器访问入口。
    /// </summary>
    /// <remarks>
    /// 默认后端 <see cref="DefaultTimerHandler"/> 是时间轮（<c>Delay</c> 系列）与帧计时（<c>WaitFrame</c> 系列）两条泳道的复合外观， <br />
    /// 可在 <see cref="TimerServiceSettings"/> 中替换。 <br />
    /// 全部 API 经 <c>Internal_PeekHandler()?.</c> 静默降级：未注册 / 未初始化时返回安全默认值。
    /// <c>Handler</c> 属性由 <c>ProviderHandlerHostGenerator</c> 源生成器生成（线程安全懒加载）。 <br />
    /// 本类是纯静态门面：不实现契约、不进容器，自报契约 <see cref="ITimerService"/> 的绑定键挂在后端实例上（绑定即注册）。 <br />
    /// 服务互调经 <c>Context.GetRequiredService&lt;ITimerService&gt;()</c> 解析到的是 <see cref="DefaultTimerHandler"/> 实例， <br />
    /// 门面只在它两侧做静态缓存与降级。
    /// </remarks>
    [ProviderHandlerHost(typeof(ITimerService), typeof(DefaultTimerHandler))]
    [UnityEngine.Scripting.Preserve]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static partial class TimerService
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>依赖契约实例（<see cref="OnInit"/> 经上下文解析，Phase 2 注入互调）。</summary>
        /// <summary>
        /// 从 <see cref="TimerServiceSettings"/> 解析计时器处理器。
        /// </summary>
        /// <remarks>
        /// 由生成的懒装配链调用（预热在入图期、懒加载在世界就绪后）；解析出的实例经容器登记契约绑定。
        /// </remarks>
        /// <returns>settings 中配置的后端；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static ITimerService GetHandlerFromSettings()
        {
            return TimerServiceSettings.TimerServiceHandler;
        }

        /// <summary>
        /// 容器编排（后端 OnInit 经 Manager 钩子调用）：注册调试面板。
        /// </summary>
        internal static void OnHandlerInitialized(IDebuggerService debugger)
        {
            debugger.RegisterDebuggerWindow("Profiler/Timer", new TimerServiceDebuggerWindow());
        }

        /// <summary>
        /// 容器收尾（后端 OnShutdown 经 Manager 钩子调用）：注销调试面板。
        /// </summary>
        internal static void OnHandlerShuttingDown(IDebuggerService debugger)
        {
            debugger?.UnregisterDebuggerWindow("Profiler/Timer");
        }

        #endregion

        #region 调度 API [SCHEDULING]

        /// <summary>
        /// 延迟 <paramref name="delaySeconds"/> 秒后回调。
        /// </summary>
        /// <param name="delaySeconds">延迟秒数。</param>
        /// <param name="onComplete">完成回调（热路径请用缓存方法组，禁止捕获闭包）。</param>
        /// <param name="isLooped">是否循环。</param>
        /// <param name="ignoreTimeScale">是否忽略时间缩放。</param>
        /// <param name="phase">触发阶段。</param>
        /// <returns>计时器句柄；无效时为 0。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Delay(float delaySeconds, Action onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.Delay(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>
        /// 延迟并每帧上报进度（0..1），无完成回调。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Delay(float delaySeconds, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.Delay(delaySeconds, (Action)null, onUpdate, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>
        /// 延迟后带参回调。
        /// </summary>
        public static ulong Delay<T>(float delaySeconds, Action<T> onComplete, T arg,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) where T : class =>
            Internal_PeekHandler()?.Delay(delaySeconds, onComplete, arg, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>
        /// 延迟并在到期前每帧上报进度（0..1）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Delay(float delaySeconds, Action onComplete, Action<float> onUpdate,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.Delay(delaySeconds, onComplete, onUpdate, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>
        /// 延迟（零分配函数指针绑定）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong DelayUnsafe(float delaySeconds, in TimerUnsafeBinding onComplete,
            bool isLooped = false, bool ignoreTimeScale = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.DelayUnsafe(delaySeconds, onComplete, isLooped, ignoreTimeScale, phase) ?? 0UL;

        /// <summary>
        /// 等待 N 帧后回调。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WaitFrame(int frames, Action onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.WaitFrame(frames, onComplete, isLooped, phase) ?? 0UL;

        /// <summary>
        /// 等待 N 帧，每帧回调累计帧数（1 起）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WaitFrame(int frames, Action<int> onUpdate,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.WaitFrame(frames, onUpdate, isLooped, phase) ?? 0UL;

        /// <summary>
        /// 帧等待（零分配函数指针绑定）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WaitFrameUnsafe(int frames, in TimerUnsafeBinding onComplete,
            bool isLooped = false, TimerPhase phase = TimerPhase.Update) =>
            Internal_PeekHandler()?.WaitFrameUnsafe(frames, onComplete, isLooped, phase) ?? 0UL;

        /// <summary>
        /// 暂停。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Pause(ulong timerHandle) => Internal_PeekHandler()?.Pause(timerHandle);

        /// <summary>
        /// 暂停全部计时器。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PauseAll() => Internal_PeekHandler()?.PauseAll();

        /// <summary>
        /// 恢复。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Resume(ulong timerHandle) => Internal_PeekHandler()?.Resume(timerHandle);

        /// <summary>
        /// 恢复全部计时器。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResumeAll() => Internal_PeekHandler()?.ResumeAll();

        /// <summary>
        /// 是否在运行（句柄有效且未暂停）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRunning(ulong timerHandle) => Internal_PeekHandler()?.IsRunning(timerHandle) ?? false;

        /// <summary>
        /// 剩余秒数（帧计时器返回 0）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetLeftTime(ulong timerHandle) => Internal_PeekHandler()?.GetLeftTime(timerHandle) ?? 0f;

        /// <summary>
        /// 剩余帧数（时间计时器返回 0）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetLeftFrames(ulong timerHandle) => Internal_PeekHandler()?.GetLeftFrames(timerHandle) ?? 0;

        /// <summary>
        /// 是否已结束（完成 / 取消 / 无效）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDone(ulong timerHandle) => Internal_PeekHandler()?.IsDone(timerHandle) ?? true;
        
        /// <summary>
        /// 是否已暂停。
        /// </summary>
        public static bool IsPaused(ulong timerHandle) => !IsRunning(timerHandle) && !IsDone(timerHandle);

        /// <summary>
        /// 已流逝量：时间计时器为秒，帧计时器为帧。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElapsed(ulong timerHandle) => Internal_PeekHandler()?.GetElapsed(timerHandle) ?? 0f;

        /// <summary>
        /// 总时长：时间计时器为秒，帧计时器为帧。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetDuration(ulong timerHandle) => Internal_PeekHandler()?.GetDuration(timerHandle) ?? 0f;

        /// <summary>
        /// 按原时长重启。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Restart(ulong timerHandle) => Internal_PeekHandler()?.Restart(timerHandle);

        /// <summary>
        /// 取消计时器。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Cancel(ulong timerHandle) => Internal_PeekHandler()?.Cancel(timerHandle);

        /// <summary>
        /// 取消全部计时器。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CancelAll() => Internal_PeekHandler()?.CancelAll();

        /// <summary>
        /// 等待计时器完成（默认后端为按槽位完成信号驱动，非每帧轮询）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask WaitAsync(ulong timerHandle, CancellationToken cancellationToken = default) =>
            Internal_PeekHandler()?.WaitAsync(timerHandle, cancellationToken) ?? UniTask.CompletedTask;

        #endregion

    }

    /// <summary>
    /// <c>ulong</c> 计时器句柄扩展。
    /// </summary>
    public static class TimerHandleExtensions
    {
        /// <summary>
        /// 取消计时器。
        /// </summary>
        public static void Cancel(this ulong timerHandle)
        {
            if (timerHandle != 0UL) TimerService.Cancel(timerHandle);
        }

        /// <summary>
        /// 句柄是否已结束。
        /// </summary>
        public static bool IsDone(this ulong timerHandle) => TimerService.IsDone(timerHandle);

        /// <summary>
        /// 剩余秒数（帧计时器返回 0）。
        /// </summary>
        public static float GetLeftTime(this ulong timerHandle) => TimerService.GetLeftTime(timerHandle);

        /// <summary>
        /// 剩余帧数（时间计时器返回 0）。
        /// </summary>
        public static int GetLeftFrames(this ulong timerHandle) => TimerService.GetLeftFrames(timerHandle);

        /// <summary>
        /// 暂停。
        /// </summary>
        public static void Pause(this ulong timerHandle)
        {
            if (timerHandle != 0UL) TimerService.Pause(timerHandle);
        }

        /// <summary>
        /// 恢复。
        /// </summary>
        public static void Resume(this ulong timerHandle)
        {
            if (timerHandle != 0UL) TimerService.Resume(timerHandle);
        }

        /// <summary>
        /// 等待计时器完成（按槽位完成信号驱动）。
        /// </summary>
        public static UniTask WaitAsync(this ulong timerHandle, CancellationToken cancellationToken = default)
            => TimerService.WaitAsync(timerHandle, cancellationToken);
    }
}
