using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 将操作/协程安全地调度到 Unity 主线程执行的线程安全调度器。
    /// </summary>
    /// <remarks>
    /// 任务存于无锁队列、由主线程泵按序执行（播放模式 <see cref="Update"/>，编辑模式 <see cref="UnityEditor.EditorApplication.update"/>）； <br />
    /// 静态 <c>Post/Send</c>（含 <see cref="Post(IEnumerator)"/>，依赖 <c>StartCoroutine</c>、仅播放模式）可任意线程调用，入队不触碰 Unity API，任务在主线程串行执行、 <br />
    /// 异常分级：开发档 Fatal 后上抛中止本次泵（fail-fast），发布档隔离续跑并对连续失败告警；新代码一律使用静态 API。 <br />
    /// 可等待 API（<c>PostAsync/SendAsync</c>）基于池化 <see cref="AutoResetUniTaskCompletionSource{T}"/>：稳态每次 2 次堆分配、主线程快速路径零分配； <br />
    /// 停机（<see cref="BeginShutdown"/>）统一取消挂起任务，等待方收到携带调用方令牌的 <see cref="OperationCanceledException"/>，不会永久挂起。
    /// <c>CancellationToken</c> 取消的是「等待」：任务未执行则跳过、执行中则运行完毕并放弃结果，任务自身抛 OCE 亦按取消处理；泵每帧受 <see cref="MAX_TIME_BUDGET_MS"/> 预算约束。
    /// 停机在主线程走优雅排水：已入队任务内联执行完毕（副作用照常完成，如存档落盘回执），等待方仍按取消终结，见 <see cref="BeginShutdown"/>。
    /// 队列无上限、入队永不阻塞或拒绝（停机除外），积压超 <see cref="BACKLOG_WARN_THRESHOLD"/> 仅告警，调用方须自行限流；实例在 <c>BeforeSceneLoad</c> 于主线程物化， <br />
    /// 勿在此之前启动访问本类的后台线程。编辑器暂停（<c>isPaused</c>）期间实例 <c>Update</c> 停摆、泵不运行——后台任务与等待方冻结至解除暂停。
    /// </remarks>
    public class MainThreadDispatcher : SingletonMono_Persistent<MainThreadDispatcher>
    {
        #region 常量与静态状态 [Constants/State]

        /// <summary>泵单帧的时间预算（毫秒）。超出后剩余任务推迟到下一帧，避免突发积压造成单帧卡顿。</summary>
        public const double MAX_TIME_BUDGET_MS = 2.0;

        /// <summary>积压告警阈值：待执行任务数持续超过此值时输出警告。</summary>
        public const int BACKLOG_WARN_THRESHOLD = 1024;

        /// <summary>积压告警解除阈值（低于此值时复位告警，滞回避免抖动）。
        /// 取触发阈值的 50%（惯例滞回比）：过高会悬挂告警（恢复后错失后续尖峰），过低会在 512-1024 波动区间反复触发。</summary>
        private const int BACKLOG_CLEAR_THRESHOLD = 512;

        /// <summary>积压采样掩码：每 256 次入队采样一次队列深度（ConcurrentQueue.Count 非零成本，避免高频路径每次读取）。
        /// 计数器 int 溢出不影响采样节奏：补码自增保持低 8 位连续跨越符号边界；非原子自增的并发丢失仅让采样点偶发后移，诊断用途可接受。</summary>
        private const int BACKLOG_SAMPLE_MASK = 0xFF;

        /// <summary>任务异常分级策略：开发期（编辑器 / Development Build）Fatal 后上抛中止本次泵（fail-fast），发布期隔离续跑。</summary>
        /// <remarks><c>const</c> 门控，发布构建裁掉死分支，零运行时成本。与 <see cref="PlayerLoopDriver"/> 的订户异常策略同构。</remarks>
        private const bool RETHROW_TASK_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>发布期连续失败告警阈值。调度器的队列任务无订户身份可摘（与 PlayerLoopDriver 熔断同值但仅告警诊断）。</summary>
        /// <remarks>计数在任一任务成功时归零，恰好达阈值告警一次（每个连续失败幕一条），避免毒任务逐帧刷爆日志。</remarks>
        private const int TASK_FAILURE_WARN_THRESHOLD = 300;

        private static readonly ConcurrentQueue<Action> s_PendingQueue = new ConcurrentQueue<Action>();
        private static readonly ProfilerMarker s_PumpMarker = new ProfilerMarker("MainThreadDispatcher.Pump");

        /// <summary>Stopwatch 刻度 → 毫秒换算系数（进程内恒定，静态缓存避免泵内逐任务除法）。</summary>
        private static readonly double s_MsPerTick = 1000.0 / Stopwatch.Frequency;

        /// <summary>挂起的可等待操作（PostAsync/SendAsync）取消句柄注册表。</summary>
        /// <remarks>
        /// 停机时统一 <see cref="AwaiterHandle.Cancel"/>，保证等待方收到取消而非永久挂起；任务正常完成后由闭包在 finally 中移除。
        /// </remarks>
        private static readonly ConcurrentDictionary<AwaiterHandle, byte> s_PendingAwaiters = new ConcurrentDictionary<AwaiterHandle, byte>();

        private static int s_MainThreadId;       // 仅在主线程生命周期钩子中写入
        private static bool s_RejectNewWork;     // Volatile.Read/Write
        private static int s_EnqueueCounter;     // 非原子自增：竞态只让采样点偶发后移，诊断用途可接受
        private static int s_ConsecutiveTaskFailures; // 仅泵（主线程）读写，普通 int 无需原子
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

        #region 可等待操作句柄 [Awaiter Handle]

        /// <summary>
        /// 可等待操作的取消句柄（非泛型基类便于在停机时统一取消不同 T 类型的完成源）。
        /// </summary>
        private abstract class AwaiterHandle
        {
            /// <summary>
            /// 以句柄存储的调用方令牌取消完成源。
            /// </summary>
            /// <remarks><b>必须保持非阻塞</b>：本方法会在 CancellationToken 回调线程上执行（可能是线程池线程），
            /// 而调用方任务终结时的 <c>registration.Dispose()</c>（按 .NET 契约会等待执行中的回调）可能在主线程等待其返回。 <br />
            /// 一旦加入重逻辑（锁、IO、同步等待），主线程将被拖住；此处仅允许 TrySetCanceled 级别的非阻塞操作。</remarks>
            public abstract void Cancel();

            /// <summary>
            /// 以指定令牌就地取消完成源（失败路径共用入口——令牌随调用携带，供基类引用的 <c>AbandonAwaiter</c> 等清扫路径调用）。
            /// </summary>
            /// <remarks>幂等由完成源 version 护栏仲裁（后到者无副作用）；非阻塞契约同 <see cref="Cancel"/>。</remarks>
            public abstract bool TrySetCanceled(CancellationToken cancellationToken);
        }

        private sealed class AwaiterHandle<T> : AwaiterHandle
        {
            /// <summary>池化完成源（AutoReset：await 消费后自动回池）。所有 TrySet* 经其 version 护栏仲裁：
            /// 源回收复用后，本句柄的陈旧 setter 自动失效，无需额外状态。</summary>
            private readonly AutoResetUniTaskCompletionSource<T> _source;
            private readonly CancellationToken _token; // 调用方令牌：停机取消时回传，便于调用方异常过滤器匹配

            public AwaiterHandle(AutoResetUniTaskCompletionSource<T> source, CancellationToken token)
            {
                _source = source;
                _token = token;
            }

            /// <summary>任务的唯一合法取用点——必须在任何 TrySet 之前捕获（源回收后 Task 属性失效）。</summary>
            public UniTask<T> Task => _source.Task;

            public bool TrySetResult(T value) => _source.TrySetResult(value);

            public bool TrySetException(Exception exception) => _source.TrySetException(exception);

            public override bool TrySetCanceled(CancellationToken cancellationToken = default)
                => _source.TrySetCanceled(cancellationToken != default ? cancellationToken : _token);

            public override void Cancel() => TrySetCanceled();
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
            s_ConsecutiveTaskFailures = 0;
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
        protected override void OnShutdown()
        {
            base.OnShutdown();

            BeginShutdown();
        }

        /// <summary>
        /// 停止接受新任务，取消所有挂起的可等待操作并排空积压队列（幂等）。应用退出或实例被销毁时调用。
        /// </summary>
        /// <remarks>
        /// 主线程停机走优雅排水：已入队任务内联执行完毕（副作用照常完成——存档落盘回执等停机收尾不被静默截杀），而非丢弃。 <br />
        /// 等待方语义不变：取消先于排水，排水执行的任务其完成源已被终结，version 护栏令 <c>TrySet*</c> 判负，等待方仍观察到一致的停机取消。 <br />
        /// 排水异常就地收敛（Fatal 记录后继续），单个毒任务不得截断停机收尾。非主线程停机（防御路径，正常生命周期不经过）
        /// 无法安全内联执行，积压任务丢弃并告警。本方法不假设与 <c>GameServices</c> 停机的先后次序——任一次序下副作用均不丢失。
        /// </remarks>
        internal static void BeginShutdown()
        {
            Volatile.Write(ref s_RejectNewWork, true);

            // 先取消挂起的可等待操作：等待方终结先于排水；被丢弃的闭包内的完成源若不终结，await 方将永久挂起
            CancelAllAwaiters();

            if (IsMainThread)
            {
                // 优雅排水：排空为止（无预算——停机收尾的任务量以服务 flush 入队为界，量小且必须完成）
                while (s_PendingQueue.TryDequeue(out Action action))
                {
                    try
                    {
                        Execute(action);
                    }
                    catch (Exception)
                    {
                        // 开发档的 rethrow 在此收敛：Fatal 已由 Execute 记录，排水必须继续
                    }
                }
            }
            else
            {
                int dropped = 0;
                while (s_PendingQueue.TryDequeue(out _)) dropped++;

                if (dropped > 0)
                {
                    LogUtility.Warning("MainThreadDispatcher shutdown: {0} pending action(s) dropped.", dropped);
                }
            }
        }

        /// <summary>
        /// 取消并清空挂起的可等待操作注册表（无参 <see cref="AwaiterHandle.Cancel"/> 回传创建时的调用方令牌）。
        /// </summary>
        /// <remarks>BeginShutdown（正常停机）与 ResetStatics（异常退出恢复）共用，保证完成源永不悬挂。
        /// Cancel() 与调用方 CancellationToken 回调可能并发作用于同一完成源——安全性依赖其 TrySetCanceled 的幂等性。 <br />
        /// 与 version 护栏（后到者/陈旧者返回 false，无副作用）。</remarks>
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
                if (s_PendingQueue.IsEmpty) return; // 停机常态：队列早已排空，免进排水循环

                // 停机后排水：清理 BeginShutdown 的 drain 与并发 Post 之间竞态窗口内入队的滞留者
                int stragglers = 0;
                while (s_PendingQueue.TryDequeue(out _)) stragglers++;

                if (stragglers > 0)
                {
                    // 与 BeginShutdown 的 dropped 日志同前缀同动词，运维检索时可归为同一停机事件族
                    LogUtility.Warning("MainThreadDispatcher shutdown: {0} straggler action(s) dropped (enqueued after drain).", stragglers);
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

                    double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * s_MsPerTick;
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

        /// <summary>
        /// 执行单个队列任务：Fatal 记录后按分级策略处置——开发档上抛（fail-fast，中止本次泵），发布档隔离续跑。
        /// </summary>
        /// <remarks>发布档的连续失败由 <see cref="RecordConsecutiveTaskFailure"/> 告警一次每幕，避免毒任务逐帧刷爆日志；
        /// 排水路径（<see cref="BeginShutdown"/>）在本方法外收敛 rethrow，保证停机收尾完整。</remarks>
        private static void Execute(Action action)
        {
            try
            {
                action?.Invoke();
                s_ConsecutiveTaskFailures = 0;
            }
            catch (Exception ex)
            {
                LogUtility.Fatal(ex);
                // 局部变量阻断编译期可达性折叠——避免发布构建下 throw 后的计数调用触发 CS0162（零运行时差异）
                bool rethrow = RETHROW_TASK_EXCEPTIONS;
                if (rethrow) throw;
                RecordConsecutiveTaskFailure();
            }
        }

        /// <summary>
        /// 发布档记录一次任务失败；连续失败达 <see cref="TASK_FAILURE_WARN_THRESHOLD"/> 时告警一次（任一任务成功即归零重计）。
        /// </summary>
        private static void RecordConsecutiveTaskFailure()
        {
            int failures = ++s_ConsecutiveTaskFailures;
            if (failures == TASK_FAILURE_WARN_THRESHOLD)
            {
                LogUtility.Warning(
                    "MainThreadDispatcher tasks have failed {0} time(s) consecutively; a producer may be poisoning the queue.",
                    TASK_FAILURE_WARN_THRESHOLD);
            }
        }

        #endregion

        #region 下次主线程更新时执行 [Enqueue/OnNextUpdate]

        /// <summary>
        /// 将协程加入队列，在下次主线程泵时启动。
        /// </summary>
        /// <param name="routine">将在主线程执行的协程。</param>
        /// <remarks>任意线程可调用；仅支持播放模式（依赖 <c>StartCoroutine</c>）。<see cref="SingletonMono{T}.Instance"/> 延迟到泵内（主线程）才解析。 <br />
        /// 经池化工作项 + 静态 lambda（编译器缓存）转发，稳态零闭包分配。</remarks>
        public static void Post(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));

            Post(routine, static r => Instance.StartCoroutine(r));
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
                LogUtility.Warning("MainThreadDispatcher.Post rejected: dispatcher has shut down.");
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

            // 采样式积压监控（计数非原子自增：纯诊断，竞态丢失只让采样点偶发后移）
            if ((++s_EnqueueCounter & BACKLOG_SAMPLE_MASK) == 0 &&
                !Volatile.Read(ref s_BacklogWarned) &&
                s_PendingQueue.Count > BACKLOG_WARN_THRESHOLD)
            {
                Volatile.Write(ref s_BacklogWarned, true);
                LogUtility.Warning("MainThreadDispatcher backlog exceeds {0}: producers are outpacing the main-thread pump.",
                    BACKLOG_WARN_THRESHOLD);
            }

            return true;
        }

        /// <summary>
        /// 将「状态 + 处理器」入队，在下次主线程泵时执行（状态化零闭包路径）。
        /// </summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="state">随队列携带的状态（工作项归还池时清空——引用类型状态不滞留）。</param>
        /// <param name="action">将在主线程执行的处理器。</param>
        /// <remarks>
        /// 工作项池化复用，处理器经静态 lambda / 方法组的编译器缓存后稳态零分配，值类型状态经泛型工作项传递零装箱。 <br />
        /// 任意线程可调用，入队不触碰任何 Unity API；停机后调用被丢弃并告警（工作项照常归还池）。
        /// </remarks>
        public static void Post<TState>(TState state, Action<TState> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            StateWorkItem<TState> item = StateWorkItem<TState>.Acquire(state, action);
            if (!TryPost(item.CachedExecute))
            {
                item.Release();
                LogUtility.Warning("MainThreadDispatcher.Post rejected: dispatcher has shut down.");
            }
        }

        /// <summary>
        /// 状态化工作项（池化）。
        /// </summary>
        /// <typeparam name="T">状态类型。</typeparam>
        /// <remarks>执行入口委托在实例构造时以方法组缓存——同一实例反复入队零委托分配；归还前清场，引用类型状态不跨任务滞留。</remarks>
        private sealed class StateWorkItem<T>
        {
            /// <summary>实例池（无锁队列；容量随历史峰值积压自然伸缩，与 PendingQueue 语义一致不设上限）。</summary>
            private static readonly ConcurrentQueue<StateWorkItem<T>> s_Pool = new ConcurrentQueue<StateWorkItem<T>>();

            /// <summary>携带状态。</summary>
            private T _state;

            /// <summary>主线程处理器。</summary>
            private Action<T> _handler;

            /// <summary>入队用执行入口（方法组缓存委托——泵直接调用本字段，无需每次构造闭包）。</summary>
            internal readonly Action CachedExecute;

            private StateWorkItem()
            {
                CachedExecute = Execute;
            }

            /// <summary>
            /// 取工作项并装载状态与处理器。
            /// </summary>
            /// <param name="state">携带状态。</param>
            /// <param name="handler">主线程处理器。</param>
            /// <returns>工作项实例。</returns>
            internal static StateWorkItem<T> Acquire(T state, Action<T> handler)
            {
                if (!s_Pool.TryDequeue(out StateWorkItem<T> item))
                {
                    item = new StateWorkItem<T>();
                }

                item._state = state;
                item._handler = handler;
                return item;
            }

            /// <summary>
            /// 泵内执行（先取引用再清场归还——归还后实例可能已被其他线程复用，状态读取必须先完成）。
            /// </summary>
            private void Execute()
            {
                Action<T> handler = _handler;
                T state = _state;
                Release();
                handler(state);
            }

            /// <summary>
            /// 清场并归还池（幂等语义由调用方保证：每实例每次入队生命周期内仅调用一次）。
            /// </summary>
            internal void Release()
            {
                _handler = null;
                _state = default;
                s_Pool.Enqueue(this);
            }
        }

        /// <summary>
        /// 将函数入队，使其在主线程上执行，并返回其完成时完成的任务。
        /// </summary>
        /// <param name="action">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待；任务尚未执行时直接跳过执行（见类备注"取消语义"）。</param>
        /// <returns>可以等待到操作完成的 <see cref="UniTask"/>；操作抛出的异常经该任务传播；停机/取消时以 <see cref="OperationCanceledException"/> 终结。</returns>
        public static UniTask PostAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var handle = new AwaiterHandle<bool>(AutoResetUniTaskCompletionSource<bool>.Create(), cancellationToken);
            UniTask<bool> coreTask = handle.Task; // 必须在任何 TrySet 之前捕获

            if (!TryBeginAwaiter(handle, cancellationToken, out CancellationTokenRegistration registration) ||
                !TryPost(WrappedAction))
            {
                AbandonAwaiter(handle, registration, cancellationToken); // 调度器已停机：取消而非挂起
            }

            return coreTask.AsUniTask();

            void WrappedAction()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        handle.TrySetCanceled(cancellationToken); // 取消先于执行：跳过执行
                        return;
                    }

                    action();
                    handle.TrySetResult(true);
                }
                catch (OperationCanceledException oce)
                {
                    handle.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    handle.TrySetException(ex);
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
        /// <returns>以该函数执行结果完成的 <see cref="UniTask{T}"/>；异常经该任务传播；停机/取消时以 <see cref="OperationCanceledException"/> 终结。</returns>
        public static UniTask<T> PostAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var handle = new AwaiterHandle<T>(AutoResetUniTaskCompletionSource<T>.Create(), cancellationToken);
            UniTask<T> coreTask = handle.Task; // 必须在任何 TrySet 之前捕获

            if (!TryBeginAwaiter(handle, cancellationToken, out CancellationTokenRegistration registration) ||
                !TryPost(WrappedAction))
            {
                AbandonAwaiter(handle, registration, cancellationToken);
            }

            return coreTask;

            void WrappedAction()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        handle.TrySetCanceled(cancellationToken);
                        return;
                    }

                    handle.TrySetResult(func());
                }
                catch (OperationCanceledException oce)
                {
                    handle.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    handle.TrySetException(ex);
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
        public static UniTask PostAsync(Func<UniTask> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var handle = new AwaiterHandle<bool>(AutoResetUniTaskCompletionSource<bool>.Create(), cancellationToken);
            UniTask<bool> coreTask = handle.Task; // 必须在任何 TrySet 之前捕获

            if (!TryBeginAwaiter(handle, cancellationToken, out CancellationTokenRegistration registration) ||
                !TryPost(WrappedAction))
            {
                AbandonAwaiter(handle, registration, cancellationToken);
                return coreTask.AsUniTask();
            }

            return coreTask.AsUniTask();

            // 队列签名为 Action：薄包装经 UniTaskVoid.Forget() 转发，未捕获异常由 UniTaskScheduler 集中发布。
            // try/catch/finally 不可省略——异常必须路由进完成源，PublishUnobservedTaskException 不会代劳，勿把逻辑移出 try 块。
            void WrappedAction() => RunInner().Forget();

            async UniTaskVoid RunInner()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        handle.TrySetCanceled(cancellationToken);
                        return;
                    }

                    await func();
                    handle.TrySetResult(true);
                }
                catch (OperationCanceledException oce)
                {
                    handle.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    handle.TrySetException(ex);
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
        public static UniTask<T> PostAsync<T>(Func<UniTask<T>> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var handle = new AwaiterHandle<T>(AutoResetUniTaskCompletionSource<T>.Create(), cancellationToken);
            UniTask<T> coreTask = handle.Task; // 必须在任何 TrySet 之前捕获

            if (!TryBeginAwaiter(handle, cancellationToken, out CancellationTokenRegistration registration) ||
                !TryPost(WrappedAction))
            {
                AbandonAwaiter(handle, registration, cancellationToken);
                return coreTask;
            }

            return coreTask;

            // 队列签名为 Action：薄包装经 UniTaskVoid.Forget() 转发，未捕获异常由 UniTaskScheduler 集中发布。
            // try/catch/finally 不可省略——异常必须路由进完成源，PublishUnobservedTaskException 不会代劳，勿把逻辑移出 try 块。
            void WrappedAction() => RunInner().Forget();

            async UniTaskVoid RunInner()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        handle.TrySetCanceled(cancellationToken);
                        return;
                    }

                    handle.TrySetResult(await func());
                }
                catch (OperationCanceledException oce)
                {
                    handle.TrySetCanceled(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    handle.TrySetException(ex);
                }
                finally
                {
                    s_PendingAwaiters.TryRemove(handle, out _);
                    registration.Dispose();
                }
            }
        }

        /// <summary>
        /// 遗弃可等待操作：摘出停机注册表、释放令牌注册项并就地取消完成源（四个 <c>PostAsync</c> 重载共用的失败路径）。
        /// </summary>
        /// <remarks>三步均为幂等安全操作：与 <see cref="BeginShutdown"/> / 令牌回调并发时由完成源 version 护栏仲裁，后到者无副作用。</remarks>
        private static void AbandonAwaiter(AwaiterHandle handle, CancellationTokenRegistration registration, CancellationToken token)
        {
            s_PendingAwaiters.TryRemove(handle, out _);
            registration.Dispose();
            handle.TrySetCanceled(token);
        }

        /// <summary>
        /// 注册可等待操作的取消句柄：登记到停机注册表并挂接调用方令牌。
        /// </summary>
        /// <param name="registration">输出的令牌注册项（任务终结时由调用方 Dispose）。</param>
        /// <returns>已停机返回 false（由调用方就地取消完成源并直接返回任务）。</returns>
        /// <remarks>
        /// 本方法（①登记注册表）与调用方随后的入队（②TryPost）之间存在非原子窗口：若 <see cref="BeginShutdown"/> 恰在两者之间运行，
        /// 句柄已被 <see cref="CancelAllAwaiters"/> 取消且注册表被 Clear，此时 ② 失败，调用方清理路径的 TryRemove / TrySetCanceled 均为幂等空操作，最终状态仍为"已取消"。 <br />
        /// 该窗口是设计上接受的良性竞态，安全性依赖 TrySetCanceled 幂等 + 完成源 version 护栏。
        /// </remarks>
        private static bool TryBeginAwaiter(AwaiterHandle handle, CancellationToken cancellationToken,
            out CancellationTokenRegistration registration)
        {
            registration = default; // default 注册项的 Dispose() 是文档化 no-op，调用方失败路径无需条件判断

            if (Volatile.Read(ref s_RejectNewWork)) return false; // 停机竞态预检

            s_PendingAwaiters.TryAdd(handle, 0);

            if (cancellationToken.CanBeCanceled)
            {
                // 状态重载（netstandard2.1 可用）+ 静态 lambda（编译器缓存）：注册路径零闭包分配
                registration = cancellationToken.Register(static h => ((AwaiterHandle)h).Cancel(), handle);
            }

            return true;
        }

        #endregion

        #region 同步请求到主线程 [Dispatch/SyncRequest]

        /// <summary>
        /// 在主线程上执行协程：已在主线程则立即启动，否则下次主线程泵时启动。
        /// </summary>
        /// <param name="routine">将在主线程执行的协程。</param>
        /// <remarks>应用退出窗口（<c>s_ShuttingDown</c>）下 <see cref="SingletonMono{T}.Instance"/> 为 null——协程丢弃并告警，不抛 NRE。</remarks>
        public static void Send(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));

            if (!IsMainThread)
            {
                Post(routine);
                return;
            }

            var inst = Instance; // 单次取用，避免检查与使用之间的竞态
            if (inst == null)
            {
                LogUtility.Warning("MainThreadDispatcher.Send(IEnumerator): instance unavailable during shutdown, coroutine dropped.");
                return;
            }

            inst.StartCoroutine(routine);
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
        /// 在主线程上执行「状态 + 处理器」：已在主线程则同步执行（异常直接抛给调用方），否则下次主线程泵时执行（异常被隔离并记录）。
        /// </summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="state">随调用携带的状态（排队路径下工作项归还池时清空——引用类型状态不滞留）。</param>
        /// <param name="action">将在主线程执行的处理器。</param>
        /// <remarks>
        /// 主线程快速路径直调处理器，零分配；后台路径复用 <see cref="Post{TState}(TState, Action{TState})"/> 的池化工作项， <br />
        /// 处理器经静态 lambda / 方法组的编译器缓存后稳态零分配，值类型状态经泛型工作项传递零装箱。任意线程可调用。
        /// </remarks>
        public static void Send<TState>(TState state, Action<TState> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (IsMainThread)
            {
                action(state);
                return;
            }

            Post(state, action);
        }

        /// <summary>
        /// 在主线程上执行函数并返回任务：已在主线程则同步执行，否则入队等待下次主线程泵。
        /// </summary>
        /// <param name="action">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待（仅后台入队路径生效；见类备注"取消语义"）。</param>
        /// <returns>可以等待到操作完成的 <see cref="UniTask"/>；异常经该任务传播。</returns>
        /// <remarks>本方法拦截 <b>所有</b> <see cref="OperationCanceledException"/>（无论其令牌来源，包括用户代码深层抛出的无关令牌）
        /// 并统一映射为取消——与排队路径保持一致。若需区分令牌来源，请在 action 内部自行捕获处理。</remarks>
        public static UniTask SendAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!IsMainThread) return PostAsync(action, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return CanceledUniTask(cancellationToken);

            try
            {
                action();
                return UniTask.CompletedTask; // 成功快速路径：零分配
            }
            catch (OperationCanceledException oce)
            {
                // 与 PostAsync 排队路径语义对齐：OCE → 取消，而非 Faulted
                return CanceledUniTask(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
            }
            catch (Exception ex)
            {
                return FaultedUniTask(ex);
            }
        }

        /// <summary>
        /// 在主线程上执行带返回值的函数并返回任务：已在主线程则同步执行，否则入队等待下次主线程泵。
        /// </summary>
        /// <typeparam name="T">函数的返回值类型。</typeparam>
        /// <param name="func">将在主线程执行的函数。</param>
        /// <param name="cancellationToken">取消等待（仅后台入队路径生效；见类备注"取消语义"）。</param>
        /// <returns>以该函数执行结果完成的 <see cref="UniTask{T}"/>；异常经该任务传播。</returns>
        /// <remarks>本方法拦截 <b>所有</b> <see cref="OperationCanceledException"/>（无论其令牌来源，包括用户代码深层抛出的无关令牌）
        /// 并统一映射为取消——与排队路径保持一致。若需区分令牌来源，请在 func 内部自行捕获处理。</remarks>
        public static UniTask<T> SendAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            if (!IsMainThread) return PostAsync(func, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return CanceledUniTask<T>(cancellationToken);

            try
            {
                return UniTask.FromResult(func()); // 成功快速路径：值直存结构体，零分配
            }
            catch (OperationCanceledException oce)
            {
                return CanceledUniTask<T>(oce.CancellationToken != default ? oce.CancellationToken : cancellationToken);
            }
            catch (Exception ex)
            {
                return FaultedUniTask<T>(ex);
            }
        }

        /// <summary>
        /// 已取消的 <see cref="UniTask"/>（池化源工厂，冷路径）。
        /// </summary>
        private static UniTask CanceledUniTask(CancellationToken token)
        {
            AutoResetUniTaskCompletionSource source = AutoResetUniTaskCompletionSource.CreateFromCanceled(token, out short token2);
            return new UniTask(source, token2);
        }

        /// <summary>
        /// 已失败的 <see cref="UniTask"/>（池化源工厂，冷路径）。
        /// </summary>
        private static UniTask FaultedUniTask(Exception exception)
        {
            AutoResetUniTaskCompletionSource source = AutoResetUniTaskCompletionSource.CreateFromException(exception, out short token2);
            return new UniTask(source, token2);
        }

        /// <summary>
        /// 已取消的 <see cref="UniTask{T}"/>（池化源工厂，冷路径）。
        /// </summary>
        private static UniTask<T> CanceledUniTask<T>(CancellationToken token)
        {
            AutoResetUniTaskCompletionSource<T> source = AutoResetUniTaskCompletionSource<T>.CreateFromCanceled(token, out short token2);
            return new UniTask<T>(source, token2);
        }

        /// <summary>
        /// 已失败的 <see cref="UniTask{T}"/>（池化源工厂，冷路径）。
        /// </summary>
        private static UniTask<T> FaultedUniTask<T>(Exception exception)
        {
            AutoResetUniTaskCompletionSource<T> source = AutoResetUniTaskCompletionSource<T>.CreateFromException(exception, out short token2);
            return new UniTask<T>(source, token2);
        }

        #endregion
    }
}
