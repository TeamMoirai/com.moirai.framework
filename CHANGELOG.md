# Changelog

本项目的所有重要变更都会记录在此文件中。

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。
已发布版本的完整对比见 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases)。

## [Unreleased]

### Added

- `PlayerLoopDriver`：`OnDrawGizmos` / `OnDrawGizmosSelected` 静态事件表，Gizmos 订阅从宿主实例事件迁出，宿主销毁不再丢订阅。
- `GameAppHost`：框架唯一的轻量 MonoBehaviour 宿主（`SingletonMono_Persistent`），承接协程与 `OnDrawGizmos(Selected)` / `OnApplicationPause` 三类只能在 MonoBehaviour 上派发的消息，统一转发到 `PlayerLoopDriver` 静态表。
- `PlayerLoopDriver` 注册/注销接入主线程 fail-fast 断言，并在类型文档中写明线程契约。
- `Tests/EditorMode/Core/PlayerLoop/PlayerLoopDriverTests.cs`：驱动架构验收测试。
- `IServiceLifecycle` 新增 `StateInternal` 只读口，使"能否被注册"与"能否被读到状态"成为同一个类型判据（此前两处各写一遍分支、已经漂移）。
- `Tests/EditorMode/Service/Kernel/GameServicesTest.cs`：内核审阅缺陷回归 10 格——多契约部分注销、`Registered` 契约粒度、观察器异常隔离、否决通道不留痕迹、裸 `IService` 与 Mono+Gizmo 注册拒绝、初始化期注销/关作用域拒绝、途中 Dispose 不宣告已初始化。
- `Timer`：按帧计时公开面 `WaitFrame`（完成回调 / 每帧累计帧数两种重载）与 `WaitFrameUnsafe`——1.0.2 的 `Timer` 只有按秒计时，帧等待此前只能靠已删除的 `Schedulers`。
- `Timer`：`WaitAsync(handle, CancellationToken)`，等待指定计时器完成；由已删除的 `SchedulerHandle.WaitAsync` 扩展接管。
- `Timer`：零分配函数指针绑定 `TimerUnsafeBinding` 与 `DelayUnsafe`（自已删除的 `SchedulerUnsafeBinding` 迁入），并新增 `TimerPhase` 枚举（`TimerTypes.cs`）用于选择触发阶段。
- `Timer`：`GetLeftFrames` / `GetElapsed` / `GetDuration` / `IsDone` / `IsPaused` / `PauseAll` / `ResumeAll` / `CancelAll`，以及 `ulong` 句柄扩展方法。
- `Timer`：两泳道各自独立的预热容量——`m_WheelInitialCapacity`（默认 1024）与 `m_FrameInitialCapacity`（默认 256），取代原单字段 `m_InitialCapacity`。
- `Tests/EditorMode/Service/Timer/DefaultTimerHandlerArchitectureTests.cs`：双引擎架构验收测试——泳道句柄互不串台、槽位复用 ABA、Fixed/Late 延后触发、Unsafe 绑定、进度比值、跨泳道真实并发峰值、`WaitAsync` 完成/取消/已完成/多等待者。
- `Tests/EditorMode/Service/Timer/DefaultTimerHandlerArchitectureTests.cs` 增补：关停后句柄操作与再注册的降级契约、引擎级 `Shutdown` 双关停幂等、两泳道各自预热容量、循环型 Fixed/Late 跨帧再触发、进度列表中间项摘除不影响其余项。
- `Tests/EditorMode/Core/PlayerLoop/PlayerLoopDriverTests.cs` 增补：正优先级之后注册普通 Handler 的执行顺序、后台线程注册 fail-fast（`s_MainThreadId` 未被钩子捕获时由用例反射补齐，避免断言被"未捕获即放行"分支静默跳过）。

### Changed

- **`GameApp` 不再含任何 MonoBehaviour 成员**：1.0.2 里嵌套的 `GameApp.MainBehaviour` 宿主（连同 `s_Entity` / `s_Behaviour`）被移除，协程与引擎事件一律经 `GameAppHost`。
- **宿主 GameObject 改名**（相对已发布的 1.0.2）：`[UpdateDriver]` → `[GameAppHost]`。
- **帧时钟采样时点**：`GameTime.StartFrame()` 上移到 `DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` 各阶段入口。此前接口 `IUpdateHandler` 读到的是上一帧的 `deltaTime`（采样排在回调循环之后），现在 Handler 与 Action 回调读到同一帧的快照。
- **驱动中注册/注销的延迟缓冲按阶段隔离**：此前 `AddLateUpdateCallback` 在驱动中被调用会被兜底注册成 Update 回调；`Register(单阶段接口)` 在驱动中被调用会被"升级"注册进该对象实现的其余阶段。
- `PlayerLoopDriver` 内部：六份按阶段复制的注册表（数组 + 计数 + 延迟缓冲 + 容量增长 + 去重 + 注销搬移）收敛为 `HandlerSlot<T>` / `CallbackSlot`，三份插入排序合一；同一收敛也消除了"新增一个阶段就得复制一遍"的出错面。
- **服务必须派生 `ServiceBase` 或 `ServiceMono<TScope>`**：裸实现 `IService` 的类型在 `ServiceWorld.Register` 处即抛 `GameException`。此前会被正常接受，但其状态永远读不到 `Initialized`，任何声明它为依赖的服务在运行时注册时都会误报"依赖未初始化"。
- **初始化进行中的 `UnregisterService` / `ShutdownContainer` 改为 fail-fast**：挂起图正被按索引推进的循环消费，中途摘除属于静默损坏（见 Fixed），不存在需要保留的合法用法；等 `Initialize`/`InitializeAsync` 返回后再做。
- **拦截器异常处置分级**：除 `OnServiceRegistering`（唯一的否决通道，抛出即拒绝注册且不被隔离）外，其余回调异常一律由容器记录为 Error 并继续——观察器缺陷不再打断整帧轮询，也不吃掉同轮其它拦截器的回调。
- **`OnServiceRegistered` 的粒度与参数**：改由容器按注册契约逐个发出（一实例绑 N 契约收 N 次），`contractType` 是注册契约而非实现类型；待初始化阶段附加的契约不再早于 `OnInit` 上报。`OnServiceShutdown` 时机不变，但此刻服务状态仍为 `Initialized`（转换在回调返回后）。
- **主线程断言真正门控**：`EnsureMainThread` 包进 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`，并接入查找（`GetService` / `GetRequiredService` / `TryGetService`）与轮询入口。发布构建下断言不参与编译、被内联为零开销——此前依赖 `UnityEngine.Assertions.Assert` 是否被裁剪，文档"发布版零开销"并无依据。隔离世界 `ServiceWorld` 明确不断言（并行测试是它的既定用途）。
- **Tick 熔断阈值改由世界持有**：`ServiceScope.s_TickFailureTripThreshold`（进程级 static）→ `ServiceWorld.TickFailureTripThreshold`（世界级），与 `DuplicateContractPolicy` 同构；隔离世界各持一份，并行测试不再互相污染。仍为框架内部可调，不对外暴露。
- **`State` 写入端收紧**：`ServiceBase.State` / `ServiceMono<TScope>.State` 由 `internal set` 收为 `private set`，生命周期转换只剩 `IServiceLifecycle` 一处（编译器强制）。随之删除零调用者的 `GameServices.SetState` 与 `ServiceMonoMarker` 标记接口。
- **注销粒度明确为服务而非契约**：多契约实例注销任一契约即整体摘出作用域，其余契约随之失效（此前两阶段语义不一致）。
- **`Timer` 处理器拆为两条独立引擎泳道**：`DefaultTimerHandler` 现为 `WheelTimerEngine`（按秒，四级时间轮，缩放 / 非缩放各一轮）与 `FrameTimerEngine`（按帧递减）的复合外观，两引擎各持独立分页槽位池与句柄命名空间、互不知晓，因此引擎内部不存在任何跨后端分支。句柄位布局 `[版本(32b) | 泳道(3b) | 槽位+1(21b)]`：泳道号内嵌于句柄，外观按位路由，外来泳道的句柄一律解析失败并安全降级（fail-closed）。
- **`Timer` 公开 API 改名（破坏性）**：`AddTimer` / `AddTimer<T>` / `Stop`（暂停语义）/ `RemoveTimer` → `Delay` / `Delay<T>` / `Pause` / `Cancel`。**不提供 `[Obsolete]` 别名**——旧 `AddTimer(Action callback, float time)` 与新 `Delay(float delaySeconds, Action onComplete)` 的实参顺序相反，别名无法纯转发。`Resume` / `Restart` / `IsRunning` / `GetLeftTime` 的名称与语义不变。
- **`Timer.WaitAsync` 由每帧轮询改为按槽位完成信号驱动**：完成 / 取消在引擎本阶段 `Tick` 末尾统一排空唤醒，避免 await 续跑在槽位释放调用栈内同步重入；`Shutdown` 时同步排空，防止 awaiter 永久挂起。同一句柄只有首个 await 走信号，后续 await 退回轮询（成本回到旧行为）。
- **`Timer` 统计的并发峰值改为跨泳道真实值**：`GetStatistics` 的 `peakActiveCount` 由复合层在每次创建后采样「两泳道活跃数之和」的最大值，不再等于两引擎各自峰值相加（那是高估）。
- **`GameApp` 三段心跳改走核心钩子**：`Tick` / `FixedTick` / `LateTick` 从用户 Action 回调表迁至 `PlayerLoopDriver.SetCore*Callback`，先于本阶段全部用户订户执行且永不参与熔断。此前它们落在无优先级的 `AddUpdateCallback` 表里，执行位置取决于注册时机——比 `GameApp.Initialize` 更早注册的项目订户一旦持续抛出，整层服务的轮询就被连带截断。

### Fixed

- **`GameApp.AddOnApplicationPauseListener` 单独使用时永不触发**：此前只有宿主因协程 / Gizmos 等原因被创建后才会挂上 Pause 转发。
- **订阅方抛异常会永久卡死驱动器**：`Drive*` 缺少 `finally`，异常路径下 `s_IsDriving` 残留为 `true`，此后所有注册滞留在延迟缓冲且当帧不提交。
- **多契约服务在世界初始化前注销单个契约会复活该服务**：`ServiceWorld.UntrackPending` 只摘一个契约键、`ServiceScope.UnregisterDeferred` 却按条目摘掉实例的全部句柄，实例因此残留在挂起图里。随后 `Initialize` 会对这个"已注销"的服务再驱动一次 `OnInit`，而条目已删导致不记激活序——`OnShutdown` 永不执行，`OnInit` 拿到的资源泄漏。
- **`OnServiceRegistered` 上报的是实现类型**：事件由服务自身经 `IServiceLifecycle` 发出，只知道自己 `GetType()`，于是以接口为契约注册时拦截器收到成对不上的 `Registering(IFace)` → `Registered(Impl)`；同一实例再绑其他契约时又会在 `OnInit` 之前提前收到 `Registered`。
- **拦截器异常打断轮询帧**：`OnBeforeScopeTick` 抛出会让 `ServiceWorld.Tick` 直接失败，该作用域本帧的 `scope.Tick` 与后续作用域全部不执行；`OnServiceRegistered` / `OnServiceShutdown` / `OnServiceUnregistered` 同样能把初始化与关闭流程半途打断。
- **否决注册后残留半注册条目**：`OnServiceRegistering` 原先在契约句柄写入注册表之后才发出，拦截器抛异常"拒绝注册"时绑定已经留下，且与附加契约路径（先通知后写入）顺序相反。
- **MonoBehaviour 服务可实现 `IServiceGizmoDrawable`**：注册守卫只挡三类 Tick，Gizmo 漏网。Unity 对组件上的 `OnDrawGizmos` 魔法方法是无条件调用的，容器再驱动一次即编辑器下双份绘制。
- **`OnInit` 途中被关闭会被重新判为就绪**：转换在 `OnInit()` 返回后无条件 `State = Initialized`，把外部 Dispose 路径推进的 `ShuttingDown`/`Disposed` 盖回去，已关闭的服务重新被 `IsServiceReady` 判为就绪。
- **初始化进行中注销服务 / 关闭作用域静默损坏挂起图**：被注销者因循环持有局部引用仍被 `OnInit`（且不记激活序 → 永不 `OnShutdown`），其余服务因 `List` 摘除的索引位移被跳过；被关闭作用域的服务则留成幽灵拓扑节点，稍后被重新初始化。现统一 fail-fast（见 Changed）。
- **世界在初始化途中被 Dispose 后仍宣告已初始化**：`CompleteInitialization` 无条件置 `_initialized`，与被置位的 `_disposed` 并存。
- **`Timer` 文档与代码脱节**（双语同步修正）：`Timer.md` 声称旧名「以 `[Obsolete]` 别名保留」而代码中一个都不存在（且 `AddTimerUnsafe` 从未存在于代码），照此写代码会编译失败；`Core.md` 三处示例仍调用已移除的 `TimerService.AddTimer(...)` 且实参顺序与新 `Delay` 相反；`Debugger.md` 的 Timer 面板引用不存在的 `TimerServiceDebugView`（实为 `TimerServiceDebuggerWindow`，注册路径 `Profiler/Timer`）；`Index.md` 与 `TimerService` 类型注释仍只描述「四级时间轮」单引擎，未体现双泳道。
- **`Timer` 关停后使用句柄或再注册会抛 `NullReferenceException`**：`Shutdown` 只置空页数组，未清 `_slotCapacity` / `_freeCount` / `_pageCount`，于是过期句柄穿过 `GetSlotIndex` 的首道范围判定、去解引用已置空的 `_pages`；`AcquireSlot` 同样在残留 `_freeCount` 下直读空的自由栈，故"关停后再 `Delay`"也是抛而非返回 0 句柄。现计数随 `Shutdown` 归零、`AcquireSlot` 见已关停即按"无槽位"降级，`DefaultTimerHandler` 另在 `OnShutdown` 摘掉泳道路由表并让三类 `Tick` 与 `PauseAll` / `ResumeAll` / `CancelAll` 整体空转；`Shutdown` 自身亦幂等（重复调用、未 `Init` 即调用均安全）。经服务门面（`s_Handler` 先置空）的路径本来安全，受影响的是自持 handler 引用的测试与自定义宿主。
- **`PlayerLoopDriver` 优先级插入不对称**：非 `IPlayerLoopPriority` 对象被无条件尾部追加，故 `Register(Priority = 5)` 之后再注册的普通 Handler 会跑到 +5 之后，违背「数字小者先跑」。现按有效优先级（未实现者计 0）判定：仅当追加后仍满足升序才走 O(1) 尾部追加，否则整表稳定排序插入。
- **`Timer` 调度失败诊断不对称**：只有 `Delay(Action)` 与 `Delay<T>` 两个重载在编辑器下告警，带进度的 `Delay`、`DelayUnsafe` 以及帧泳道全部重载（含 `frames <= 0` 与槽位耗尽）静默返 0 句柄。现两引擎统一为 `[Conditional("UNITY_EDITOR")]` 告警——发布构建连整条调用与实参求值一并摘除，运行期零开销。
- **`PlayerLoopDriver.EnsureMainThread` 未随内核一并门控**：内核版已包进 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`，驱动器版仍在 20 个注册/注销入口无条件编译断言，与「发布构建下断言不参与编译、零开销」的记载不符。现补齐同一约定。
- **`Timer` 时间轮的延后触发列表与进度列表存在线性扫描**：Fixed/Late 入列用 `List<ulong>.Contains` 去重、释放用线性摘除，进度列表亦线性移除——同帧大量 Fixed/Late 或带进度计时器到期即 O(N²)。现延后列表改以槽位归属位（`1 << 6` / `1 << 7`）作去重判据、并在快照排空时清除（否则循环型只会触发一次），不在列的槽位一次位判即返回；进度列表按帧泳道同法记录列表下标，做 O(1) swap-remove。
- **`Documentation~/zh|en/Core.md` 与代码脱节**（双语同步修正）：`IService.Shutdown()` 实为 `OnShutdown()`（含示例，照抄不能编译）；异步关闭顺序是逆激活序而非逆注册序；拦截器表列出不存在的 `OnServiceTick`；引用已删除的 `RegisterWithDependencies` 与不属于组合根的 `ProcedureServiceSettings.StartProcedure`；内置服务计数 11/12 实为 13；`ServiceScopeOrder` 其实不被容器消费；依赖校验时机仍是两阶段之前的旧描述。
- **`FrameworkSettings<T>.Instance` 加载失败时栈溢出**：资产缺失的**打包分支**原先调 `LogUtility.Error`，而 `LogUtility.Handler` 的懒加载要经 `GetHandlerFromSettings()` → `GameAppSettings.LogHandler` 回读**同一个**设置资产——此刻 `s_Instance` 仍为 null，于是"报错说资产缺失"这一步再次进入本 getter 无限递归（`StackOverflowException` 不可捕获，进程直接被带走）。编辑器分支走 `LoadSettingSO`，其中本就用裸 `Debug.*`，故编辑器下从不复现、也从未被发现；触发与否还取决于 `LogUtility` 是否已被更早的钩子解析过，表现为偶发。现改裸 `Debug.LogError`，并在类型注释中写明该路径禁用可插拔日志链路的约束。
- **`PlayerLoopDriver` 订户异常截断整阶段，并能永久冻结服务层心跳**：三个 `Drive*` 只有 `finally`、没有隔离，一个订户抛出后同阶段其余订户与全部 Action 回调当帧不再执行。内核早有逐服务 `try/catch` + `TickFailureTripThreshold` 熔断，这条防御线在上一层断掉了。现按内核同一约定分级（开发期 `Error` 记录后上抛、发布期隔离续跑），并按 `FailureTripThreshold`（默认 300，与内核同值）做连续失败熔断摘出——成功一次即归零，间歇性故障不会被累计成熔断；两档构建下都摘除，故熔断在编辑器亦观察得到。核心钩子永不熔断；`ApplicationQuit` / `Destroy` 两类一次性清理广播改为逐项调用且开发构建也不上抛（截断清理等于漏掉后续每一项的释放动作）。`focusChanged` / pause / gizmos 三张表仍是裸多播调用，单项抛出会截断其后的订户（已记入文档，未改）。既有 `PlayerLoopDriverTests` 断言的行为不变。
- **已 `Destroy` 的 `MonoBehaviour` 型 Handler 抛 `MissingReferenceException` 未被挡**（只记文档，未改代码）：`Drive` 原先的 `handlers[i]?.` 与改用显式 `null` 判定后一样，走的都是 C# 引用比较而非 Unity 的伪造 null 重载，挡不住已销毁组件——须在 `OnDestroy` 里自行 `Unregister`。
- **启动链对配置资产缺失仍无守卫**（本轮有意未采纳）：`GameAppSettings.Initiation` 直接解引用 `Instance.m_FrameRate`，资产缺失时 NRE 中断这个 `[RuntimeInitializeOnLoadMethod]`——PlayerLoop 不注入、服务组合根不执行；`GameAppSettings` 七个处理器出口写成 `Instance.m_XHandler`，其抛出会穿过 `GetHandlerFromSettings() ?? CreateDefaultHandler()` 兜底链（`??` 不吞异常），使日志/字符串等 Utility 连退回代码默认值的机会都没有。上一条栈溢出已修（`caf44d0c`），故当前表现是打印一行 `Could not find GameAppSettings at path '...'` 加一段 NRE 栈，而非进程直接被带走。
- **`PlayerLoop` 注入的自愈校验从未执行过一次**：`VerifyInjection` 挂在 `AfterAssembliesLoaded` 且首行即 `if (!s_Injected) return;`，而唯一置位方 `EnsureInjected()` 只在 `GameAppSettings.Initiation`（`BeforeSceneLoad`）内被调用——校验严格早于注入点，每次启动都在"尚未注入"的状态下直接返回，补插与告警一次也没跑过。于是第三方（Entities 等）在 `BeforeSceneLoad` 基于默认循环重建 PlayerLoop、抹掉三个 Moirai 标记时，`s_Injected` 仍为 true 且永不重试，整框架静默不 Tick，运行期不留任何线索（这恰是该校验本该防住的场景）。现校验改挂 `AfterSceneLoad`，覆盖 `BeforeSceneLoad` 及其之前的重建；更晚的重建仍按文档在完成后调 `PlayerLoopInjector.Reinject()`。随之删掉最低 2022.3 下恒真的 `#if UNITY_2020_1_OR_NEWER` 死分支，并修正把两个相位写反的记载（`GameAppSettings_Services` 注释与本文件文档，双语）。新增 `SelfHealCheck_RunsStrictlyAfterInjectionPhase`：反射取两者的 `[RuntimeInitializeOnLoadMethod]` 相位并断言"自愈必须严格晚于注入"——正是此前被破坏的那条不变量。
- **`GameApp.Shutdown` 把整条 PlayerLoop 盖回引擎默认，连带拆掉 UniTask**：`PlayerLoopDriver.Shutdown` 收尾调的是 `RestoreDefault()`，而 UniTask 的 `PlayerLoopHelper` 只注入一次、不会自行回来——默认循环一盖回去，它排队的 `await` 就永不续跑。问题在于 `Shutdown` 并不总意味着进程结束：调试器 `OperationsWindow` 的 `Shutdown (Restart)` 是 `GameApp.Shutdown()` 之后紧接 `LoadScene(0)`，`Shutdown (None)` 则是关掉框架继续跑，两者都会让 UniTask 在本次会话余下时间里永久报废。退出流程同理——`GameServices` 自己写明优雅退出应在 `OnApplicationQuit` 之前走 `ShutdownAsync()`，而那条链路上的 await 只要晚于 `Shutdown` 一步，它依赖的 Pump 就已经被我们自己拆掉了。现改用**早已存在、却一直没被调用**的 `RemoveMoiraiSystems()`：只摘本框架三个标记，第三方注入原样保留。顺带解掉文档中记作已知妥协的「禁用域重载时退出 Play 会让编辑模式 UniTask 停摆到下次域重载」。`RestoreDefault()` 保留，仅供调试窗的显式复原按钮，并在注释里标明其破坏性。

### Removed

- **`Moirai.Atropos.Schedulers` 命名空间整体删除**：`Scheduler` / `SchedulerHandle` / `SchedulerUnsafeBinding` / `SchedulerUnsafeBinding<T>` / `SchedulerExtensions`（含 `WaitAsync`）/ `TickFrame`，以及 `IScheduled` 接口、`SchedulerRunner` 组件、`FrameCounter` / `Timer` / `SchedulerRegistry` 等模型，连同 `Editor/Schedulers/` 的调度器调试窗口。迁移映射：按秒延时 → `TimerService.Delay`；按帧等待 → `TimerService.WaitFrame`；逐帧订阅 → `PlayerLoopDriver.Register(IUpdateHandler)` 或 `AddUpdateCallback`；等待完成 → `TimerService.WaitAsync`；零分配函数指针绑定 → `TimerUnsafeBinding` 配 `DelayUnsafe` / `WaitFrameUnsafe`。

### Deprecated

- 无。

## [1.0.2] - 2026-09-12

见 [1.0.2 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.2)。

## [1.0.1] - 2026-09-01

见 [1.0.1 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.1)。

## [1.0.0] - 2026-08-20

首个正式版本。见 [1.0.0 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.0)。
