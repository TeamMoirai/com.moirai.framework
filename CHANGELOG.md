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
- **`Documentation~/zh|en/Core.md` 与代码脱节**（双语同步修正）：`IService.Shutdown()` 实为 `OnShutdown()`（含示例，照抄不能编译）；异步关闭顺序是逆激活序而非逆注册序；拦截器表列出不存在的 `OnServiceTick`；引用已删除的 `RegisterWithDependencies` 与不属于组合根的 `ProcedureServiceSettings.StartProcedure`；内置服务计数 11/12 实为 13；`ServiceScopeOrder` 其实不被容器消费；依赖校验时机仍是两阶段之前的旧描述。

### Deprecated

- 无。

## [1.0.2] - 2026-09-12

见 [1.0.2 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.2)。

## [1.0.1] - 2026-09-01

见 [1.0.1 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.1)。

## [1.0.0] - 2026-08-20

首个正式版本。见 [1.0.0 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.0)。
