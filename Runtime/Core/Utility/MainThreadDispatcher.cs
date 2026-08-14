using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 将操作/协程安全地调度到 Unity 主线程执行的线程安全调度器。
    /// 任务存储在无锁队列中，由主线程"泵"按序执行（播放模式为 <see cref="Update"/>，编辑模式为 EditorApplication.update）。
    /// </summary>
    /// <remarks>
    /// <para><b>线程契约</b>：<see cref="Post(Action)"/> 等静态方法可在任意线程调用，
    /// 入队路径完全不触碰 Unity API；任务始终在主线程泵内串行执行，单个任务的异常被隔离并记录，不会毒化后续任务。</para>
    /// <para><b>可等待操作的停机语义</b>：<c>PostAsync/SendAsync</c> 的挂起任务在调度器停机（<see cref="BeginShutdown"/>）时统一取消，
    /// 等待方会收到 <see cref="TaskCanceledException"/>，绝不会因闭包被丢弃而永久挂起。</para>
    /// <para><b>取消语义</b>：可等待 API 的 <c>CancellationToken</c> 取消的是"等待"；任务尚未执行时直接跳过执行，
    /// 已在执行中的任务会运行完毕但结果被放弃。任务自身抛出 <see cref="OperationCanceledException"/> 时统一按取消处理
    /// （<c>Task.IsCanceled == true</c>），主线程快速路径与排队路径语义一致。</para>
    /// <para><b>实例物化</b>：实例由 <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c> 在主线程尽早物化，
    /// 因此播放期间后台线程经 <c>Instance</c> 访问只命中静态字段的原子快速路径。
    /// 请勿在该物化时点之前启动会访问本类的后台线程。</para>
    /// <para><b>背压</b>：泵每帧受 <see cref="MAX_TIME_BUDGET_MS"/> 时间预算约束，超出部分推迟到下一帧；
    /// 积压持续超过 <see cref="BACKLOG_WARN_THRESHOLD"/> 会输出警告（生产速率高于消费速率的信号，见 <see cref="PendingCount"/>）。</para>
    /// <para><b>关闭语义</b>：应用退出时立即拒绝新任务并清空积压；之后的入队将被丢弃并告警。
    /// 编辑器退出播放模式后会自动恢复可用（兼容关闭 Domain Reload 的工作流）。</para>
    /// <para><b>注意</b>：<see cref="Post(IEnumerator)"/> 依赖 <c>StartCoroutine</c>，仅支持播放模式；
    /// 实例方法 <c>Enqueue/Dispatch</c> 族是对静态核心的兼容转发层（为 com.moirai.gameplay 的既有调用保留，约 123 处），
    /// 语义与对应静态方法完全一致 —— <b>新代码一律使用静态 <c>Post/Send</c> API</b>；待 gameplay 包迁移完毕后可将其标记 [Obsolete]。</para>
    /// </remarks>
    public class MainThreadDispatcher : SingletonMono_Persistent<MainThreadDispatcher>
    {
        #region 常量与静态状态 [Constants/State]

        /// <summary>泵单帧的时间预算（毫秒）。超出后剩余任务推迟到下一帧，避免突发积压造成单帧卡顿。</summary>
        public const double MAX_TIME_BUDGET_MS = 2.0;

        /// <summary>积压告警阈值：待执行任务数持续超过此值时输出警告。</summary>
        public const int BACKLOG_WARN_THRESHOLD = 1024;

        /// <summary>积压告警解除阈值（低于此值时复位告警，滞回避免抖动）。</summary>
        private const int BACKLOG_CLEAR_THRESHOLD = 256;

        /// <summary>积压采样掩码：每 256 次入队采样一次队列深度（ConcurrentQueue.Count 非零成本，避免高频路径每次读取）。
        /// 计数器 int 溢出不影响采样节奏：补码自增保持低 8 位连续跨越符号边界，采样周期严格保持 256。</summary>
        private const int BACKLOG_SAMPLE_MASK = 0xFF;

        private static readonly ConcurrentQueue<Action> s_PendingQueue = new ConcurrentQueue<Action>();
        private static readonly ProfilerMarker s_PumpMarker = new ProfilerMarker("MainThreadDispatcher.Pump");

        /// <summary>
        /// 挂起的可等待操作（PostAsync/SendAsync）取消句柄注册表。
        /// 停机时统一 <see cref="AwaiterHandle.Cancel"/>，保证等待方收到取消而非永久挂起；
        /// 任务正常完成后由闭包在 finally 中移除。
        /// </summary>
        private static readonly ConcurrentDictionary<AwaiterHandle, byte> s_PendingAwaiters = new ConcurrentDictionary<AwaiterHandle, byte>();

        private static int s_MainThreadId;       // 仅在主线程生命周期钩子中写入
        private static bool s_RejectNewWork;     // Volatile.Read/Write
        private static int s_EnqueueCounter;     // Interlocked.Increment
        private static bool s_BacklogWarned;     // Volatile.Read/Write（诊断标志，与 s_RejectNewWork 对齐标准）

        // s_ShuttingDown — 基类 SingletonMono<T> 的静态退出标记（声明于基类，控制 Instance getter 在退出期返回 null）；
        // 由 ResetStatics 显式复位，兼容关闭 Domain Reload 的编辑器工作流。

        /// <summary>当前线程是否为 Unity 主线程。</summary>
        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == s_MainThreadId;

        /// <summary>当前积压的待执行任务数（诊断用，勿用于业务逻辑）。</summary>
        public static int PendingCount => s_PendingQueue.Count;

        /// <summary>当前挂起的可等待操作数（诊断用，勿用于业务逻辑）。</summary>
        internal static int PendingAwaiterCount => s_PendingAwaiters.Count;

        #endregion

        #region 可等待操作取消句柄 [Awaiter Handle]

        /// <summary>
        /// 可等待操作的取消句柄（非泛型基类便于在停机时统一取消不同 T 类型的 TCS）。
        /// </summary>
        private abstract class AwaiterHandle
        {
            public abstract void Cancel(CancellationToken cancellationToken = default);
        }

        private sealed class AwaiterHandle<T> : AwaiterHandle
        {
            private readonly TaskCompletionSource<T> _tcs;
            private readonly CancellationToken _token; // 调用方令牌：停机取消时回传，便于调用方异常过滤器匹配

            public AwaiterHandle(TaskCompletionSource<T> tcs, CancellationToken token)
            {
                _tcs = tcs;
                _token = token;
            }

            public override void Cancel(CancellationToken cancellationToken = default)
                => _tcs.TrySetCanceled(cancellationToken != default ? cancellationToken : _token);
        }

        #endregion

        #region 生命周期 [Lifecycle]

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditorLifecycle()
        {
            ResetStatics();

            // 编辑模式下由 EditorApplication.update 驱动泵；退出播放后恢复可用（兼容关闭 Domain Reload）
            UnityEditor.EditorApplication.update -= EditorUpdatePump;
            UnityEditor.EditorApplication.update += EditorUpdatePump;
            UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
        }

        private static void EditorUpdatePump()
        {
            // 播放模式由实例的 Update 驱动，此处跳过避免同帧双泵
            if (Application.isPlaying) return;

            Pump();
        }

        private static void OnEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                ResetStatics();
            }
        }
#endif

        /// <summary>
        /// 重置静态状态：捕获主线程 ID、清空积压队列与挂起注册表并恢复接受新任务。
        /// </summary>
        /// <remarks>在进入播放（SubsystemRegistration）、域重载与退出播放后调用，保证跨会话无脏状态。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_BacklogWarned = false;
            s_EnqueueCounter = 0;
            s_ShuttingDown = false; // 同步复位基类退出标记，避免关闭 Domain Reload 时 Instance 被旧标记拒绝

            CancelAllAwaiters(); // 异常退出（如编辑器崩溃恢复）未经 BeginShutdown 的场景：终结残留的可等待任务
            while (s_PendingQueue.TryDequeue(out _)) { } // 丢弃上一会话的残留任务

            // 最后恢复接受（BeginShutdown 的镜像顺序）：若提前置 false，清理期间入队的新任务会被排空循环静默丢弃
            Volatile.Write(ref s_RejectNewWork, false);
        }

        /// <summary>
        /// 播放启动时在主线程尽早物化实例，确保此后任何后台线程入队的任务都有消费者。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapOnPlay()
        {
            _ = Instance;
        }

        protected override void OnApplicationQuit()
        {
            base.OnApplicationQuit();

            BeginShutdown();
        }

        /// <inheritdoc/>
        protected override void Shutdown()
        {
            base.Shutdown();

            BeginShutdown();
        }

        /// <summary>
        /// 停止接受新任务，取消所有挂起的可等待操作并清空积压队列（幂等）。应用退出或实例被销毁时调用。
        /// </summary>
        internal static void BeginShutdown()
        {
            Volatile.Write(ref s_RejectNewWork, true);

            // 先取消挂起的可等待操作：被丢弃的闭包内的 TCS 若不终结，await 方将永久挂起
            CancelAllAwaiters();

            int dropped = 0;
            while (s_PendingQueue.TryDequeue(out _)) dropped++;

            if (dropped > 0)
            {
                Log.Warning(StringUtility.Format(
                    "MainThreadDispatcher shutdown: {0} pending action(s) dropped.", dropped));
            }
        }

        /// <summary>
        /// 取消并清空挂起的可等待操作注册表（无参 <see cref="AwaiterHandle.Cancel"/> 回传创建时的调用方令牌）。
        /// </summary>
        /// <remarks>BeginShutdown（正常停机）与 ResetStatics（异常退出恢复）共用，保证 TCS 永不悬挂。
        /// Cancel() 与调用方 CancellationToken 回调可能并发作用于同一 TCS——安全性依赖 <c>TrySetCanceled</c> 的幂等性（后到者返回 false，无副作用）。</remarks>
        private static void CancelAllAwaiters()
        {
            if (s_PendingAwaiters.IsEmpty) return;

            foreach (KeyValuePair<AwaiterHandle, byte> pair in s_PendingAwaiters)
            {
                pair.Key.Cancel();
            }
            s_PendingAwaiters.Clear();
        }

        #endregion

        #region 泵 [Pump]

        private void Update() => Pump();

        /// <summary>
        /// 在主线程上按入队顺序执行积压任务，直到队列清空或超出单帧时间预算。
        /// </summary>
        /// <remarks>任务在无锁队列外执行：慢任务不会阻塞生产者线程，后续任务推迟到下一次泵。</remarks>
        internal static void Pump()
        {
            if (Volatile.Read(ref s_RejectNewWork))
            {
                // 停机后排水：清理 BeginShutdown 的 drain 与并发 Post 之间竞态窗口内入队的滞留者
                int stragglers = 0;
                while (s_PendingQueue.TryDequeue(out _)) stragglers++;

                if (stragglers > 0)
                {
                    Log.Warning(StringUtility.Format(
                        "MainThreadDispatcher dropped {0} straggler action(s) after shutdown.", stragglers));
                }
                return;
            }

            if (s_PendingQueue.IsEmpty) return;

            s_PumpMarker.Begin();
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();

                while (s_PendingQueue.TryDequeue(out Action action))
                {
                    Execute(action);

                    if (s_PendingQueue.IsEmpty) break;

                    double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs >= MAX_TIME_BUDGET_MS) break; // 预算耗尽，余量推迟到下一帧
                }

                if (Volatile.Read(ref s_BacklogWarned) && s_PendingQueue.Count <= BACKLOG_CLEAR_THRESHOLD)
                {
                    Volatile.Write(ref s_BacklogWarned, false);
                }
            }
            finally
            {
                s_PumpMarker.End();
            }
        }

        private static void Execute(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(StringUtility.Format("MainThreadDispatcher action execution failed: {0}", ex));
            }
        }

        #endregion

        #region 下次主线程更新时执行 [Enqueue/OnNextUpdate]

        /// <summary>
        /// 将协程加入队列，在下次主线程泵时启动。
        /// </summary>
        /// <param name="routine">将在主线程执行的协程。</param>
        /// <remarks>任意线程可调用；仅支持播放模式（依赖 <c>StartCoroutine</c>）。<see cref="Instance"/> 延迟到泵内（主线程）才解析。</remarks>
        public static void Post(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));

            Post(() => Instance.StartCoroutine(routine));
        }

        /// <summary>
        /// 将操作加入队列，在下次主线程泵时执行。
        /// </summary>
        /// <param name="action">将在主线程执行的函数。</param>
        /// <remarks>任意线程可调用，入队路径不触碰任何 Unity API。停机后调用会被丢弃并告警。</remarks>
        public static void Post(Action action)
        {
            if (!TryPost(action))
            {
                Log.Warning("MainThreadDispatcher.Post rejected: dispatcher has shut down.");
            }
        }

        /// <summary>
        /// 尝试将操作加入队列。
        /// </summary>
        /// <returns>成功入队返回 true；调度器已停机返回 false（任务被拒绝，由调用方决定补救措施）。</returns>
        internal static bool TryPost(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (Volatile.Read(ref s_RejectNewWork)) return false;

            s_PendingQueue.Enqueue(action);

            // 采样式积压监控
            if ((Interlocked.Increment(ref s_EnqueueCounter) & BACKLOG_SAMPLE_MASK) == 0 &&
                !Volatile.Read(ref s_BacklogWarned) &&
                s_PendingQueue.Count > BACKLOG_WARN_THRESHOLD)
            {
                Volatile.Write(ref s_BacklogWarned, true);
                Log.Warning(StringUtility.Format(
                    "MainThreadDispatcher backlog exceeds {0}: producers are outpacing the main-thread pump.",
                    BACKLOG_WARN_THRESHOLD));
            }

            return true;
        }

        /// <summary>
        /// 将函数入队，使其在主线程上执行，并返回其完成时完成的任务。
        /// </summary>
        /// <param name="action">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待；任务尚未执行时直接跳过执行（见类备注"取消语义"）。</param>
        /// <returns>可以等待到操作完成的 <see cref="Task"/>；操作抛出的异常经该任务传播；停机/取消时以 <see cref="TaskCanceledException"/> 终结。</returns>
        public static Task PostAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new AwaiterHandle<bool>(tcs, cancellationToken);

            if (!TryBeginAwaiter(handle, tcs, cancellationToken, out CancellationTokenRegistration registration)) return tcs.Task;

            if (!TryPost(WrappedAction))
            {
                s_PendingAwaiters.TryRemove(handle, out _);
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken); // 调度器已停机：取消而非挂起
                return tcs.Task;
            }

            return tcs.Task;

            void WrappedAction()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken); // 取消先于执行：跳过执行
                        return;
                    }

                    action();
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException oce)
                {
                    tcs.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    s_PendingAwaiters.TryRemove(handle, out _);
                    registration.Dispose();
                }
            }
        }

        /// <summary>
        /// 将带返回值的函数入队，使其在主线程上执行，并返回其执行结果。
        /// </summary>
        /// <typeparam name="T">函数的返回值类型。</typeparam>
        /// <param name="func">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待；任务尚未执行时直接跳过执行（见类备注"取消语义"）。</param>
        /// <returns>以该函数执行结果完成的 <see cref="Task{T}"/>；异常经该任务传播；停机/取消时以 <see cref="TaskCanceledException"/> 终结。</returns>
        public static Task<T> PostAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new AwaiterHandle<T>(tcs, cancellationToken);

            if (!TryBeginAwaiter(handle, tcs, cancellationToken, out CancellationTokenRegistration registration)) return tcs.Task;

            if (!TryPost(WrappedAction))
            {
                s_PendingAwaiters.TryRemove(handle, out _);
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            return tcs.Task;

            void WrappedAction()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    tcs.TrySetResult(func());
                }
                catch (OperationCanceledException oce)
                {
                    tcs.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    s_PendingAwaiters.TryRemove(handle, out _);
                    registration.Dispose();
                }
            }
        }

        /// <summary>
        /// 将异步函数入队，使其在主线程上执行，并返回其完成时完成的任务。
        /// </summary>
        /// <param name="func">将在主线程执行的异步函数。</param>
        /// <param name="cancellationToken">取消等待；任务尚未执行时直接跳过执行（见类备注"取消语义"）。</param>
        /// <remarks>不使用 <c>ConfigureAwait(false)</c>：await 之后的逻辑经 UnitySynchronizationContext 驻留主线程。</remarks>
        public static Task PostAsync(Func<Task> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new AwaiterHandle<bool>(tcs, cancellationToken);

            if (!TryBeginAwaiter(handle, tcs, cancellationToken, out CancellationTokenRegistration registration)) return tcs.Task;

            if (!TryPost(WrappedAction))
            {
                s_PendingAwaiters.TryRemove(handle, out _);
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            return tcs.Task;

            async void WrappedAction()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    await func();
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException oce)
                {
                    tcs.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    s_PendingAwaiters.TryRemove(handle, out _);
                    registration.Dispose();
                }
            }
        }

        /// <summary>
        /// 将带返回值的异步函数入队，使其在主线程上执行，并返回其执行结果。
        /// </summary>
        /// <typeparam name="T">函数的返回值类型。</typeparam>
        /// <param name="func">将在主线程执行的异步函数。</param>
        /// <param name="cancellationToken">取消等待；任务尚未执行时直接跳过执行（见类备注"取消语义"）。</param>
        /// <remarks>不使用 <c>ConfigureAwait(false)</c>：await 之后的逻辑经 UnitySynchronizationContext 驻留主线程。</remarks>
        public static Task<T> PostAsync<T>(Func<Task<T>> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new AwaiterHandle<T>(tcs, cancellationToken);

            if (!TryBeginAwaiter(handle, tcs, cancellationToken, out CancellationTokenRegistration registration)) return tcs.Task;

            if (!TryPost(WrappedAction))
            {
                s_PendingAwaiters.TryRemove(handle, out _);
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            return tcs.Task;

            async void WrappedAction()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    tcs.TrySetResult(await func());
                }
                catch (OperationCanceledException oce)
                {
                    tcs.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    s_PendingAwaiters.TryRemove(handle, out _);
                    registration.Dispose();
                }
            }
        }

        /// <summary>
        /// 注册可等待操作的取消句柄：登记到停机注册表并挂接调用方令牌。
        /// </summary>
        /// <param name="registration">输出的令牌注册项（任务终结时由调用方 Dispose）。</param>
        /// <returns>已停机返回 false（TCS 已就地取消，调用方直接返回任务）。</returns>
        private static bool TryBeginAwaiter<T>(AwaiterHandle handle, TaskCompletionSource<T> tcs,
            CancellationToken cancellationToken, out CancellationTokenRegistration registration)
        {
            registration = default;

            if (Volatile.Read(ref s_RejectNewWork))
            {
                tcs.TrySetCanceled(cancellationToken); // 停机竞态预检：直接以取消终结，避免注册后又被丢弃
                return false;
            }

            s_PendingAwaiters.TryAdd(handle, 0);

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => handle.Cancel()); // Cancel() 回退到句柄存储的调用方令牌
            }

            return true;
        }

        // --- 兼容转发层：保持既有实例 API 不变（语义与对应静态方法一致） ---

        /// <inheritdoc cref="Post(IEnumerator)"/>
        public void Enqueue(IEnumerator routine) => Post(routine);

        /// <inheritdoc cref="Post(Action)"/>
        public void Enqueue(Action action) => Post(action);

        /// <inheritdoc cref="PostAsync(Action, CancellationToken)"/>
        public Task EnqueueAsync(Action action) => PostAsync(action);

        /// <inheritdoc cref="PostAsync{T}(Func{T}, CancellationToken)"/>
        public Task<T> EnqueueAsync<T>(Func<T> func) => PostAsync(func);

        /// <inheritdoc cref="PostAsync(Func{Task}, CancellationToken)"/>
        public Task EnqueueAsync(Func<Task> func) => PostAsync(func);

        /// <inheritdoc cref="PostAsync{T}(Func{Task{T}}, CancellationToken)"/>
        public Task<T> EnqueueAsync<T>(Func<Task<T>> func) => PostAsync(func);

        #endregion

        #region 同步请求到主线程 [Dispatch/SyncRequest]

        /// <summary>
        /// 在主线程上执行协程：已在主线程则立即启动，否则下次主线程泵时启动。
        /// </summary>
        /// <param name="routine">将在主线程执行的协程。</param>
        public static void Send(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));

            if (IsMainThread) Instance.StartCoroutine(routine);
            else Post(routine);
        }

        /// <summary>
        /// 在主线程上执行函数：已在主线程则同步执行（异常直接抛给调用方），否则下次主线程泵时执行（异常被隔离并记录）。
        /// </summary>
        /// <param name="action">将在主线程执行的函数。</param>
        public static void Send(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (IsMainThread) action();
            else Post(action);
        }

        /// <summary>
        /// 在主线程上执行函数并返回任务：已在主线程则同步执行，否则入队等待下次主线程泵。
        /// </summary>
        /// <param name="action">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待（仅后台入队路径生效；见类备注"取消语义"）。</param>
        /// <returns>可以等待到操作完成的 <see cref="Task"/>；异常经该任务传播。</returns>
        public static Task SendAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!IsMainThread) return PostAsync(action, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            WrappedAction();
            return tcs.Task;

            void WrappedAction()
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException oce)
                {
                    // 与 PostAsync 排队路径语义对齐：OCE → Task.IsCanceled，而非 IsFaulted
                    tcs.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }

        /// <summary>
        /// 在主线程上执行带返回值的函数并返回任务：已在主线程则同步执行，否则入队等待下次主线程泵。
        /// </summary>
        /// <typeparam name="T">函数的返回值类型。</typeparam>
        /// <param name="func">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待（仅后台入队路径生效；见类备注"取消语义"）。</param>
        /// <returns>以该函数执行结果完成的 <see cref="Task{T}"/>；异常经该任务传播。</returns>
        public static Task<T> SendAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            if (!IsMainThread) return PostAsync(func, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            WrappedAction();
            return tcs.Task;

            void WrappedAction()
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (OperationCanceledException oce)
                {
                    // 与 PostAsync 排队路径语义对齐：OCE → Task.IsCanceled，而非 IsFaulted
                    tcs.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }

        // --- 兼容转发层：保持既有实例 API 不变（语义与对应静态方法一致） ---

        /// <inheritdoc cref="Send(IEnumerator)"/>
        public void Dispatch(IEnumerator routine) => Send(routine);

        /// <inheritdoc cref="Send(Action)"/>
        public void Dispatch(Action action) => Send(action);

        /// <inheritdoc cref="SendAsync(Action, CancellationToken)"/>
        public Task DispatchAsync(Action action) => SendAsync(action);

        /// <inheritdoc cref="SendAsync{T}(Func{T}, CancellationToken)"/>
        public Task<T> DispatchAsync<T>(Func<T> func) => SendAsync(func);

        #endregion
    }
}
