# Kernel 服务系统（@Service）

> 框架的服务化基座：以统一服务世界（`ServiceWorld`）管理所有子服务的构造、生命周期、轮询与作用域，并由 `GameApp`（静态外观，不含 MonoBehaviour）经 PlayerLoop 驱动。

`@Service` 是整个框架的服务基础设施。各功能服务的后端 handler 为继承 `ServiceBase` 的普通 C# 类，注册进容器实现契约（依赖通过 `[ServiceDependency(typeof(...))]` 特性声明）；静态门面（`AudioService`/`UIService` 等）是容器实例的公共缓存与高层封装；世界构建为两阶段——`GameServices.RegisterService<T>(scope, service)` 仅将服务入图，组合根统一调用 `GameServices.Default.InitializeAsync()` 按依赖图拓扑排序驱动全部 `OnInit`（缺失依赖/循环依赖 fail-fast，初始化顺序与注册顺序无关）；运行时（世界已初始化后）注册的服务立即初始化。非服务代码通过各服务的静态外观访问（如 `AudioService.Xxx()`、`UIService.Xxx()`、`AssetService.Xxx()`），动态服务查找统一走 `GameServices.GetRequiredService<T>()` 等静态方法。服务支持 App/Scene/Gameplay 三级作用域，跨作用域通过内联 3 槽绑定值类型 struct 实现 O(1) 查找（Gameplay > Scene > App 优先级），场景卸载时自动清理场景与玩法级服务。`ServiceWorld` 可实例化（`new ServiceWorld()`）——测试与沙盒场景可构造隔离世界，不触碰 `GameServices.Default`。

## 核心特性

- **可实例化服务世界**：`ServiceWorld` 可 `new` 构造隔离世界（测试/沙盒），`GameServices` 静态外观仅是默认世界 `Default` 的投影；3 作用域固定序槽位（App/Scene/Gameplay，零排序）+ 内联 3 槽绑定值类型 struct O(1) 跨作用域查找
- **特性声明依赖 + 拓扑初始化**：`[ServiceDependency(typeof(DepA), typeof(DepB))]` 单特性声明多依赖，编译期由 `ServiceDependencyAnalyzer`（MA2001/MA2002）校验类型实现 `IService`；世界初始化按声明图 Kahn 拓扑排序统一驱动 OnInit（初始化顺序与注册顺序无关）
- **两阶段构建**：`Register` 仅入图（注册期不驱动 OnInit）；`Initialize()`/`InitializeAsync()` 提交第二阶段——缺失依赖与循环依赖在初始化期 fail-fast（错误消息含环成员）；世界已初始化后的运行时注册要求全部依赖已就绪（缺失即 fail-fast），通过后立即 OnInit——**不做拓扑插入**，新服务总是排在既有服务之后（实现 `IServiceInitializableAsync` 的服务禁止运行时注册）
- **ProviderHandlerHost 静态门面 + 绑定即注册**：框架服务均为 `static partial class XxxService` 门面（标记 `[ProviderHandlerHost(typeof(IXxxService), typeof(默认后端))]`——缓存宿主 + 绑定声明）+ 可序列化后端 `XxxHandler : ServiceBase, IXxxService`（注册实体，生命周期/轮询/诊断归属此层）+ `XxxSettings`（`[SerializeReference]` + `[ProviderDropdown]`）选择后端实现；门面缓存槽回填容器注册结果——槽位 ≡ 容器实例
- **三级作用域**（`EServiceScopeKind.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 优先级查找
- **生命周期能力接口按需实现**：`IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable`、`IServiceGizmoDrawable`、`IAsyncShutdownService`（均继承 `IService`）
- **`Priority` 优先级**控制轮询顺序（高优先先轮询、后关闭）。框架内置服务统一 ≤ -1000（见 `ServicePriorityOrder`），业务服务默认 0 及以上
- **异步关闭**：实现 `IAsyncShutdownService` 的服务在 `ShutdownContainerAsync()` / `ShutdownAsync()` 中按**逆激活序**（= 逆初始化序，非逆注册序）先执行 `OnShutdownAsync()`，再执行同步 `OnShutdown()`
- **运行时服务注册**：`GameServices.RegisterService<T>()` / `UnregisterService<T>()` 动态增删单个服务；显式契约重载 `RegisterService(scope, Type, instance)` 支持接口契约与同实例多契约绑定；迭代中调用默认延迟到本轮结束后执行（`EDeferMode.Defer`）
- **自注册 Mono 服务**：`ServiceMono<TScope>` 在 Awake 中自动注册、OnDestroy 中自动注销
- **作用域顺序**：容器按固定槽位序 App → Scene → Gameplay 逐段处理，与 `ServiceScopeOrder` 无关（后者只是对外常量表，容器不消费它排序）
- **拦截器**：`IServiceInterceptor` 在服务注册/注销/关闭与作用域帧边界（`OnBeforeScopeTick`/`OnAfterScopeTick`）插入横切逻辑；按 Priority 降序执行。除 `OnServiceRegistering`（否决通道）外，回调异常由容器就地记录并隔离——观察器缺陷不拖垮游戏循环
- **迭代安全**：轮询期间的注册/注销操作延迟到本轮结束后统一应用；轮询中请求的作用域销毁也延迟执行
- **Tick 异常分级策略**：编辑器与开发构建下记录后立即上抛（fail-fast，第一时间暴露缺陷）；发布构建下记录后隔离续跑（单服务故障不拖垮整帧）
- **主线程亲和守卫**：`GameServices` 的注册/注销/关闭/查找/轮询入口在编辑器与开发构建断言主线程；发布构建下该断言不参与编译（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`），零开销。隔离世界（`new ServiceWorld()`）自身不断言，可跨线程用于并行测试
- **生命周期状态机**：每个服务跟踪 `EServiceState`，装配与关停均幂等（见〈生命周期状态机〉）
- **MonoBehaviour 轮询约束**：MonoBehaviour 服务不可实现 `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` / `IServiceGizmoDrawable`——一律由 Unity 自身的 `Update`/`FixedUpdate`/`LateUpdate`/`OnDrawGizmos` 驱动，注册时即拒绝
- **统一查找入口**：动态服务查找统一走 `GameServices.GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>(out T)`，按 Gameplay > Scene > App 优先级返回最优服务

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `IService` | 服务核心契约：`Priority`、`Scope`、`OnInit()`、`OnShutdown()`。**必须由 `ServiceBase`（纯 C#）或 `ServiceMono<TScope>`（MonoBehaviour）派生**——容器经 `IServiceLifecycle` 驱动并读取状态，裸实现 `IService` 的类型在注册时即被拒 |
| `ServiceBase` | 纯 C# 服务抽象基类；依赖通过 `[ServiceDependency]` 特性声明，世界初始化时拓扑校验（生命周期状态机由容器经 `IServiceLifecycle` 唯一驱动，`State` 为只读投影）；内置服务 `Priority` 统一 ≤ -1000 |
| `ServiceMono<TScope>` | MonoBehaviour 服务基类（泛型作用域标记），Awake 自动注册、OnDestroy 自动注销；生命期钩子 `protected`，只由框架经状态机驱动 |
| `ServiceWorld` | 可实例化统一服务世界（`new ServiceWorld()` 构造隔离世界）：3 作用域固定序槽位 + 内联绑定值类型 O(1) 跨作用域查找；两阶段构建 `Register`/`Initialize(Async)`；关闭严格逆拓扑；初始化进行中禁止注销与关闭作用域（fail-fast）；自身不做线程断言 |
| `ServiceScope` | 单作用域注册表、轮询列表（lazy-sort + swap-remove）、迭代安全（延迟变更队列）与 Tick 异常熔断（按轮询类别独立计数，阈值取自所属世界） |
| `TopologySorter` | 内部 Kahn 拓扑排序器：同入度按注册序稳定出队；缺失依赖/循环依赖 fail-fast（错误消息含环成员） |
| `GameServices` | 静态外观（默认世界 `Default` 投影）：注册入口 `RegisterService<T>(scope, service, deferMode)` 与显式契约重载、注销、作用域管理（`ShutdownContainer`/`HasApp`/`HasScene`/`HasGameplay`）、服务后端懒链注册与预热（内部）、轮询驱动、帧边界拦截器 |
| `ServiceDependencyAttribute` | 依赖声明特性：`[ServiceDependency(typeof(DepA), typeof(DepB))]` 单特性多依赖；编译期 MA2001/MA2002 校验 + 初始化期拓扑排序 |
| `IServiceInitializableAsync` | 异步初始化能力接口（`UniTask OnInitAsync()`）；须在 `InitializeAsync` 前注册（运行时注册 fail-fast） |
| `IServiceInterceptor` | 拦截器接口：注册/注销/关闭回调 + 作用域帧边界（`OnBeforeScopeTick`/`OnAfterScopeTick`），按 Priority 降序；除 `OnServiceRegistering` 外异常被容器隔离 |
| `EServiceScopeKind` | 服务作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `EServiceState` | 服务生命周期状态（四个取值见〈生命周期状态机〉的状态表），事实源在宿主实例自持的 `State` |
| `EDeferMode` | 迭代中注册/注销的延迟策略：`Defer`（延迟到本轮结束，默认）/ `Throw`（立即抛异常） |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | 轮询能力接口（均继承 `IService`），方法签名 `Tick(float elapseSeconds, float realElapseSeconds)` 等（MonoBehaviour 服务不可实现） |
| `IServiceGizmoDrawable` | 编辑器 Gizmos 绘制能力接口（继承 `IService`）`OnDrawGizmos()`；MonoBehaviour 服务不可实现（Unity 已按魔法方法驱动，双份绘制） |
| `IAsyncShutdownService` | 异步关闭能力接口（继承 `IService`），实现 `OnShutdownAsync()` 的服务在 `ShutdownContainerAsync()` 中按逆激活序先异步关闭 |
| `HandlerBase` | 处理器与服务共同的生命期基座：`State` / `IsReady` / `WasInitialized` / `AllowsRevival` + `protected virtual OnInit`/`OnShutdown` |
| `LifecycleMachine` | 生命期转换语义的唯一实现（`internal static`）：就绪判据与 `Initialize`/`Shutdown` 两条幂等转换，宿主经 `IHost` 接缝接入 |
| `FrameworkHandler` | 工具处理器基类（`[Serializable]`，派生 `HandlerBase`）：`Internal_Init`/`Internal_Shutdown` 直驱、全程同步；服务后端不继承它 |
| `ServiceScopeOrder` | 作用域常量表（App=-10000, Scene=-5000, Gameplay=0）；**容器不消费它**——作用域顺序由固定槽位决定，轮询顺序由 `IService.Priority` 决定 |
| `ServicePriorityOrder` | 框架内置服务轮询优先级常量（全部 ≤ -1000，与业务服务分带） |
| `GameApp` | 静态外观入口（不含 MonoBehaviour）：由 `GameAppSettings.Initiation` 在 `BeforeSceneLoad` 初始化，向 `PlayerLoopDriver` 装配内置核心钩子以按帧驱动 `GameServices.Tick`，`Shutdown` 时调用 `GameServices.Shutdown`；协程/Gizmos/Pause 委托 `GameAppHost`，对焦/退出直订 `Application.focusChanged` / `Application.quitting` |
| `GameApp.onApplicationFocus` / `GameApp.onApplicationQuit` | 两个公共静态事件：对焦回执带 `bool` 真值（`true` 为回焦），退出回执无负载且先于 `Shutdown` 发出；逐项隔离，单项订户抛异常不截断其余。SDK 登录/支付等业务回调另由项目层定义 `EventBase<T>` 负载经 `EventManager` 投递 |

## 快速上手

```csharp
// 1. 业务代码通过静态外观访问框架服务
TimerService.Delay(1f, () => Debug.Log("1s"));
UIService.ShowUI<MainWindow>("main");
AssetService.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// 2. 定义自定义服务——依赖通过 [ServiceDependency] 特性声明，OnInit 中经上下文解析注入
[ServiceDependency(typeof(ITimerService))]
public class MyService : ServiceBase, IServiceTickable
{
    private ITimerService _timer;

    public override int Priority => 10;              // 高优先级先轮询

    protected override void OnInit()
    {
        // 走到此处时依赖已初始化完毕——解析契约实例到字段，服务互调一律用它
        _timer = Context.GetRequiredService<ITimerService>();
        _timer.Delay(1f, OnTimerDone);
    }

    private void OnTimerDone() { }

    protected override void OnShutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 3. 两阶段构建：注册顺序无关（拓扑排序保证依赖先行），Initialize 统一驱动 OnInit
//    框架服务不在这里注册——组合根按 [ProviderHandlerHost] 宿主预热，运行期首次访问走同一条懒链入容器
GameServices.RegisterService(EServiceScopeKind.Gameplay, new MyService());
GameServices.Default.Initialize();

// 4. 关闭——按逆初始化序（依赖方先）关闭
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## 进阶用法

### 生命周期与作用域

- `GameServices.RegisterService<T>(scope, service)` 统一注册入口（两阶段第一阶段：仅入图，不驱动 OnInit）。世界未初始化时依赖校验推迟到 `Initialize(Async)` 的拓扑排序期统一执行（缺失依赖/循环依赖 fail-fast，错误消息含环成员）；世界已初始化后的运行时注册要求全部依赖已就绪，通过校验后立即驱动 `OnInit()`。依赖声明始终从实现类型读取——以接口为契约注册时依赖校验同样生效。
- `GameServices.Shutdown()` 按 Gameplay → Scene → App 逆序关闭全部作用域；`GameServices.ShutdownContainer(scope)` 只关闭指定作用域。
- `GameApp` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域。
- 同一契约可在不同作用域注册不同实现，`GameServices` 查找顺序为 Gameplay > Scene > App（跨作用域绑定值类型 `TryGetBest()`），可用于战斗内临时替换全局实现。
- 注册幂等：同一作用域重复注册同契约直接跳过（返回既有实例）；循环依赖在世界初始化拓扑排序期抛 `GameException`（fail-fast，错误消息含环成员）。
- 注销粒度是**服务**而非契约：同一实例以多个契约注册时，注销其中任一契约会把该实例整体摘出作用域（其余契约随之失效）。
- 初始化进行中（`ServiceWorld.IsInitializing`）禁止 `UnregisterService` 与 `ShutdownContainer`——挂起图正被按索引推进的循环消费，中途摘除会让被注销者仍被 `OnInit`（且不记激活序 → 无 `OnShutdown`）、并让其后服务因索引位移被跳过。这类操作请放到初始化完成之后。

### ProviderHandlerHost 服务架构

框架的内置服务（Asset/Debugger/Audio/ObjectPool/Procedure/Localization/Scene/Timer/Save/UI/Input/ConfigTable）统一采用三层结构：

| 层 | 形态 | 职责 |
|------|------|------|
| `static partial class XxxService` | 静态门面，标记 `[ProviderHandlerHost(typeof(IXxxService), typeof(默认后端))]`（缓存宿主 + 绑定声明） | 全部静态 API（`s_Handler?.` 转发热路径）；容器实例的公共缓存 + 高层封装（跨后端编排、高频入口组合）；`RequireHandler` fail-fast 写路径 |
| `XxxHandler : ServiceBase, IXxxService` | 可序列化后端类，**注册实体** | 直接实现契约承载核心逻辑；`[ServiceDependency]`/`Priority`/轮询/诊断归属此层；`OnShutdown` 末尾调门面缓存失效钩子 |
| `XxxSettings : FrameworkSettings<XxxSettings>` | ScriptableObject 设置 | `[ProviderDropdown]` + `[SerializeReference]` 选择后端实现 |

**绑定即注册**：懒加载链解析出后端实例后注册进容器（契约 = `IXxxService`），缓存回填注册结果——**门面槽位 ≡ 容器登记实例**是构造性不变量；组合根 `RegisterAll` 对 static 服务宿主发射 `WarmHandler` 预热（settings 解析 → 注册 → 回填），运行期首次访问静态 API 走同一懒链，但只在**世界已初始化或初始化在途**的窗口内放行（未建/未初始化/已关闭 → 抛 `GameException`，见「懒加载装配窗口」）。槽位失效由写侧推送：后端注销/销毁经失效钩子同步清槽，世界关停时内核再按装配期登记的回调逐个落槽——读侧不回查容器，`IsValid` 随之翻转。

**消费政策三车道**（谁是消费方决定走哪条通道）：

| 消费方 | 通道 |
|---|---|
| 游戏业务代码 | 门面静态 API（产品面，方便优先） |
| 框架内部服务互调 | `Context.GetRequiredService<T>()` 契约注入（解析到真实现） |
| 测试 / 编辑器工具 | 门面 `Handler` setter（注销旧 → 注册新 → 回填）+ `Internal_Peek/UseHandler` 接缝 |

> **门面槽 ≡ App 级注册实例**。作用域遮蔽（Scene/Gameplay 同契约覆盖）只影响契约解析通道（`GetService<T>` 按 Gameplay > Scene > App 取最优），门面静态 API 永远指向 App 级那份——两通道语义差异是设计定案。

读写路径分级容错：读路径 `s_Handler?.` 优雅降级（未就绪返回默认值，不抛）；写路径 `RequireHandler` 不触发懒加载、未就绪抛 `GameException`（fail-fast）。

默认处理器经 `[ProviderHandlerHost]` 第二构造参数声明，由 `ProviderHandlerHostGenerator` 生成 `CreateDefaultHandler` 兜底工厂（非法声明编译期报 MA1003：接口契约的后端须继承 `ServiceBase`，`FrameworkHandler` 派生**不再放行**——服务侧只有一条经容器的装配路径，生成代码不再按实例类型运行期分派）；懒加载优先读 settings，未配置回退声明默认——**不按已安装包自动优选**，切换后端一律在 settings 里显式选择（引擎宏分派的宿主如 `InputService` 保留手写工厂）。自定义后端：继承家族基座覆写 → 在 `XxxSettings` 的 Provider 下拉框中切换。空壳基座（无共享实现的 `XxxServiceHandler`）已删除；有真实共享实现或被插件继承的家族基座保留（`AssetRecordHostHandler` 链、`SaveServiceHandler`、`MiddlewareAudioHandler`、`LocalizationServiceHandler`、`UIServiceHandler`）。

**UI 双轨多缓存例外（终态口径）**：`UGUIHandler`/`UITKHandler : UIServiceHandler : ServiceBase, IUIService, IServiceTickable` 由组合根 `UIService.RegisterEnabledHandlers()` 按目录关停档降序注册（契约 = 各自具体类型，宿主轨最先注册），容器反序拆除 = 档位升序收口（宿主轨最后拆根）；门面 `UIService` 是**多缓存**（每启用后端一槽）+ 跨轨开关分派（`RequireOwningTrack`）+ 共享栈排序（`SharedLedger`）。`IUIService` 定位为后端能力契约 + 门面多缓存引用类型，**不承诺容器可解析**；UI 消费方（Input/Procedure 等）直订门面静态事件、调门面静态开窗——终态而非过渡态。

### 生命周期状态机

生命期转换与就绪判据只有一处实现：`LifecycleMachine`（`internal static`）。
`ServiceBase` 与 `FrameworkHandler` 经共同基座 `HandlerBase` 接入，`ServiceMono<TScope>` 自己实现同一枚 `LifecycleMachine.IHost` 接缝。

每个宿主自己持有状态位（`EServiceState`）：`ServiceBase` 与 `FrameworkHandler` 用共同基座的 `HandlerBase.State`，`ServiceMono<TScope>` 不派生该基座、自持同名 `State`。三处都只由 `LifecycleMachine` 经 `IHost` 接缝写入，对外只读；容器侧读的是同一枚状态，没有第二份账：

| 状态 | 说明 |
|------|------|
| `Created` | 实例已创建并注册，尚未初始化 |
| `Initialized` | `OnInit()` 已调用，服务运行中 |
| `ShuttingDown` | 正在调用 `OnShutdown()` |
| `Disposed` | 服务已完全关闭并移除 |

| 宿主 | 入口 | 驱动者 |
|------|------|------|
| `ServiceBase`（服务后端） | `IServiceLifecycle.Initialize()` / `Destroy()` | 容器按 `[ServiceDependency]` 拓扑序驱动 |
| `FrameworkHandler`（工具处理器） | `Internal_Init()` / `Internal_Shutdown()` | `[ProviderHandlerHost]` 生成代码直驱 |
| `ServiceMono<TScope>`（Mono 服务） | `Awake` 注册、`OnDestroy` 注销 | 注册即入容器，逐帧生命周期归 Unity |

- **就绪判据**：严格 `== Initialized`（`LifecycleMachine.IsReady` 单点），关停中与已关闭都不算就绪
- **装配过没有**：类侧基座另有单向锁存 `HandlerBase.WasInitialized`，只在机器落 `Initialized` 那一刻武装、关停期仍可读。机器先落 `ShuttingDown` 才驱动 `OnShutdown`，故关停体里的编排收尾（摘订阅、注销调试窗）挂 `IsReady` 恒假、只能挂锁存——两个问题分开答，就绪判据不放宽
- **复活口径**：`AllowsRevival` 由宿主自报，容器与工具后端允许，`ServiceMono<TScope>` 拒绝（Mono 实例没有复活来源）
- **关停钩子**：`OnShutdown` 抛错由机器捕获、经 `LogUtility.Error` 记录，实例照常落 `Disposed`；`OnInit` 异常原样上抛
- **直驱前置**：停在 `Created`（从未装配成功）的处理器不跑 `OnShutdown`，容器侧无此前置——两处不一致是刻意的

### 依赖声明

服务依赖通过 `[ServiceDependency(typeof(...))]` 特性声明（单特性多类型，类似 `RequireComponent`）。世界初始化的拓扑排序据此排序并校验——**初始化顺序由声明决定，与注册顺序无关**：

```csharp
[ServiceDependency(typeof(IAssetService), typeof(ITimerService))]
public sealed class MyUIService : ServiceBase, IServiceTickable
{
    private IAssetService _assets;
    private ITimerService _timer;

    protected override void OnInit()
    {
        // 走到此处时依赖已按声明图初始化完毕——解析契约实例到字段
        _assets = Context.GetRequiredService<IAssetService>();
        _timer = Context.GetRequiredService<ITimerService>();
        _timer.Delay(1f, OnTimerDone);
    }

    private void OnTimerDone() { }

    protected override void OnShutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}
```

- 依赖声明是无序集合（全部依赖就绪后才轮到本服务）；所有依赖类型必须实现 `IService`，由 `ServiceDependencyAnalyzer`（MA2001/MA2002）在编译期校验
- 服务实例仅由手动注册创建（框架不隐式实例化）。世界未初始化时依赖校验推迟到 `Initialize(Async)` 的拓扑排序期；世界已初始化后的运行时注册则要求依赖当场已就绪，否则注册立即抛 `GameException`
- 缺失依赖与循环依赖均抛 `GameException`（fail-fast，循环错误消息含环成员）：前者在拓扑排序期或运行时注册期，后者只可能在拓扑排序期

需要运行时延迟解析时，统一使用 `GameServices` 的静态查找方法：

```csharp
public class BattleService : ServiceBase
{
    protected override void OnInit()
    {
        if (GameServices.TryGetService(out DebuggerService debugger)) // 可选依赖，未注册返回 false
        {
            debugger.Enable();
        }
    }
}
```

### 统一服务查找

动态服务查找的唯一入口是 `GameServices` 静态外观——服务类内部与非服务代码使用同一组方法，无需注入任何提供者：

| 方法 | 说明 |
|------|------|
| `GetRequiredService<T>()` | 跨作用域查找服务，未找到抛 `GameException`（Gameplay > Scene > App 优先级）；容器未构建时同样抛出 |
| `GetService<T>()` | 跨作用域查找服务，未找到返回 null |
| `TryGetService<T>(out T)` | 跨作用域尝试查找服务，返回 bool |

### 组合根与内置服务注册

框架组合根：`GameAppSettings.InitializeAppServices()`（`BeforeSceneLoad` 阶段调用）注册全部内置 App 服务，随后一次提交第二阶段：

```csharp
// 注册清单由 BuiltinServiceRegistrationGenerator 生成：对每个 static 服务宿主发射 WarmHandler 预热
// （settings 解析 → 注册进容器 → 缓存回填）
BuiltinServiceRegistration.RegisterAll(GameServices.Default);   // 仅入图，顺序无关

// UIService 双轨多缓存：handler 按目录关停档降序注册（契约 = 各自具体类型，宿主轨最先），容器反序拆除 = 档位升序收口
UIService.RegisterEnabledHandlers();

await GameServices.Default.InitializeAsync();                    // 按 [ServiceDependency] 拓扑序统一驱动 OnInit
```

内置服务进清单的来路是 `[ProviderHandlerHost]` 的接口契约宿主：源生成器收集 static 宿主，生成的程序集级 `BuiltinServiceRegistration.RegisterAll(ServiceWorld)` 逐个发射 `WarmHandler` 预热（settings 解析出的后端经容器登记契约绑定，与运行期懒链同一条装配序列）。项目侧自建服务**不进清单**——在自己的装配点（如 `GameApp.onServicesComposing` 订阅方）显式调用 `GameServices.RegisterService`（实例由项目创建，作用域与轮询能力随声明），或把门面挂 `[ProviderHandlerHost]` 走 settings 后端选型。`[ServiceDependency]` 声明在世界初始化的拓扑排序期校验并排序（缺失依赖 fail-fast，与注册顺序无关）；接口目标须为某宿主自报的契约接口，特例注册的服务（UIService 双轨）以具体后端类型声明。组合根尚未跑到就被访问服务时，静态 API 的懒链**判红而不自举**（见「懒加载装配窗口」）。自定义服务的后端实现可在对应 `XxxSettings` 的 Inspector 中通过 Provider 下拉框替换。

### 后端异步生命周期

服务后端（`XxxHandler : ServiceBase`）的异步生命周期由容器承接：实现 `IServiceInitializableAsync` 的后端在世界 `InitializeAsync()` 中按拓扑序驱动 `OnInitAsync()`（禁止运行时注册）；实现 `IAsyncShutdownService` 的后端在异步关闭中按逆激活序先执行 `OnShutdownAsync()`。工具处理器（`FrameworkHandler` 派生，如 `LogHandler`/`JsonHandler`）生命周期全部同步（`Internal_Init`/`Internal_Shutdown` 直驱），不挂异步钩子。

### 服务事件

服务生命周期通知统一经 `IServiceInterceptor`：

```csharp
public sealed class ServiceAuditInterceptor : IServiceInterceptor
{
    public void OnServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope) =>
        Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");

    public void OnServiceUnregistered(IService service) =>
        Debug.Log($"Service unregistered: {service.GetType().Name}");
}

// 注册：GameServices.AddInterceptor(new ServiceAuditInterceptor());
```

### MonoBehaviour 服务

继承 `ServiceMono<TScope>`（`TScope` 为作用域标记：`AppScope` / `SceneScope` / `GameplayScope`），`Awake` 自动注册到对应作用域，`OnDestroy` 自动注销：

```csharp
public class MyMonoService : ServiceMono<AppScope>
{
    protected override void OnInit() { /* Awake 注册后自动调用 */ }
    protected override void OnShutdown() { /* OnDestroy 注销前自动调用 */ }

    private void Update() { /* Unity 自身生命周期驱动（基类没有对应的可覆写成员） */ }
    // AppScope 自动 DontDestroyOnLoad；SceneScope/GameplayScope 随场景卸载自然销毁
}

// 直接挂载到场景对象即可——无需手动注册
```

依赖查找使用 `GameServices.GetRequiredService<T>()` / `TryGetService<T>()`；同契约重复注册自动销毁多余 GameObject（幂等）。

> **注意**：MonoBehaviour 服务不可实现 `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` / `IServiceGizmoDrawable`——分别由 Unity 自身的 `Update()` / `FixedUpdate()` / `LateUpdate()` / `OnDrawGizmos()` 驱动；实现其中任一个都会在注册时抛 `GameException`（Gizmo 尤其没有退路：Unity 对组件上的 `OnDrawGizmos` 魔法方法是无条件调用的，容器再驱动一次就是双份绘制）。

### 服务拦截器（AOP）

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100;

    public void OnBeforeScopeTick(EServiceScopeKind scope, float elapseSeconds, float realElapseSeconds)
    {
        // 本作用域一帧轮询开始——采样窗口起点
    }

    public void OnAfterScopeTick(EServiceScopeKind scope, float elapseSeconds, float realElapseSeconds)
    {
        // 本作用域一帧轮询结束——采样窗口终点
    }

    public void OnServiceShutdown(IService service)
    {
        // 该服务 OnShutdown() 前
    }
}

GameServices.AddInterceptor(new ProfilingInterceptor());
```

六个拦截点，全部默认空实现：

| 方法 | 时机 | 异常处置 |
|------|------|------|
| `OnServiceRegistering` | 契约句柄入表**前** | 上抛——唯一的否决通道，抛出即拒绝注册且注册表不留痕迹 |
| `OnServiceRegistered` | `OnInit()` 完成、状态切到 `Initialized` 之后 | 容器就地记录并隔离 |
| `OnServiceShutdown` | 该服务 `OnShutdown()` 调用前（此刻状态仍为 `Initialized`） | 容器就地记录并隔离 |
| `OnServiceUnregistered` | `OnShutdown()` 已调用且已从注册表移除 | 容器就地记录并隔离 |
| `OnBeforeScopeTick` | 该作用域本帧全部服务 Tick 之前 | 容器就地记录并隔离 |
| `OnAfterScopeTick` | 该作用域本帧全部服务 Tick 之后 | 容器就地记录并隔离 |

- **粒度是契约**：同一实例以 N 个契约注册会收到 N 次 `OnServiceRegistering`/`OnServiceRegistered`，`contractType` 参数是注册契约而非实现类型。待初始化阶段附加的契约，其 `OnServiceRegistered` 推迟到 `OnInit()` 之后统一发出——收到 Registered 即可用。
- **没有逐服务 Tick 回调**：轮询通知以「作用域一帧」为边界；逐服务耗时监控走编辑器诊断旁表（`PollAvgMs` / `PollPeakMs`，编译期门控）。
- 多个拦截器按 `Priority` 降序执行（同值保持添加序）；`GameServices.Shutdown()` 时清空全部拦截器。
- **观察器不得拖垮宿主**：除否决通道外，任何拦截器抛出的异常都由容器捕获并记录为 Error，后续拦截器与被观察的服务照常执行。

### AOT 安全的延迟解析

`Func<T>` 注入依赖 `MakeGenericMethod`，IL2CPP 下存在裁剪风险。框架服务查找全部走 `RuntimeTypeHandle` 键的内联绑定值类型表——零反射、零装箱，天然 AOT 安全：

```csharp
public class BattleService : ServiceBase
{
    protected override void OnInit()
    {
        // 运行时延迟解析：泛型方法直接调用，无 MakeGenericMethod 路径
        var stats = GameServices.GetRequiredService<StatsService>();
    }
}
```

### 运行时服务注册

动态增删单个服务（Mod 系统、DLC 热加载等场景）：

```csharp
// 运行时注册——依赖必须已初始化就绪（缺失即 fail-fast），通过后立即驱动 OnInit
GameServices.RegisterService(EServiceScopeKind.Gameplay, new BuffService());

// 显式契约注册——以接口为契约键，依赖声明仍从实现类型读取
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), new BuffService());

// 多契约绑定——同一实例依次以多个契约注册，只初始化/关闭一次
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), buff);
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(BuffService), buff);

// 运行时注销——立即驱动 OnShutdown；注销后可以全新实例重新注册同契约
// （多契约实例：注销任一契约即摘走整个实例，其余契约随之失效）
GameServices.UnregisterService<BuffService>(EServiceScopeKind.Gameplay);
```

> `EDeferMode` 只对**世界已初始化**的运行时路径生效：迭代中（Tick 内）调用默认延迟到本轮迭代结束后执行（`EDeferMode.Defer`），传入 `EDeferMode.Throw` 则立即抛异常（fail-fast）。世界尚未初始化时服务只是入挂起图、不参与轮询列表，不存在迭代冲突，该参数被忽略。RegisterService 的契约类型 = `typeof(T)` 具体类型，解析也必须用同一类型 `GetRequiredService<T>()`。初始化进行中两类变更操作（注销、关闭作用域）一律 fail-fast。

#### 重复契约策略

同作用域内已占用契约再次显式注册**不同实例**时，按 `GameServices.DuplicateContractPolicy` 处置：

| 策略 | 行为 | 默认 |
|------|------|------|
| `EDuplicateContractPolicy.Skip` | 静默丢弃新实例并返回既有实例 | 发布构建 |
| `EDuplicateContractPolicy.Warn` | 记录警告后丢弃新实例——意外抢占契约不再静默 | 编辑器/开发构建 |
| `EDuplicateContractPolicy.Throw` | 抛出 `GameException`（fail-fast） | 显式配置 |

```csharp
// 排查期开启强校验
GameServices.DuplicateContractPolicy = EDuplicateContractPolicy.Throw;
```

> 同实例重复注册始终幂等返回既有实例；已占用契约再次注册**同一实例**的其他契约走多契约绑定路径，不受本策略影响。`Warn`/`Skip` 下**被丢弃的那个实例不会被框架关闭**——它从未入图，框架无从接管其生命周期。

### 懒加载装配窗口

服务后端有两条注册路径，**同形而权限不同**：组合根 `WarmHandler` 预热（免装配闸，先入图、拓扑统一驱动）与运行期访问静态 API 的懒链（settings 解析（null 回退声明默认工厂）→ 注册进容器（契约 = `IXxxService`）→ 注册结果回填门面缓存）。懒链只在**世界已初始化或初始化在途**的窗口内放行：

```csharp
// 世界就绪后首次访问——懒链解析后端、注册进 App 作用域并回填缓存槽
ObjectPoolService.Spawn(...);
```

- 闸在工厂之前：世界未建立、已建立但尚未初始化、`GameApp` 已关闭三种情形一律抛 `GameException`（正文带门面名），不再静默返回未初始化的后端——预热缺失属启动序问题，判红比半装配易查
- 走的是标准 `RegisterService` 路径，依赖校验规则不变：世界已初始化时依赖必须当场就绪（否则抛 `GameException`）
- 组合根已对全部内置服务宿主预热；项目侧自建服务经门面懒加载，须在世界初始化之后访问

### 异步关闭

实现 `IAsyncShutdownService` 的服务在异步关闭管线中按**逆激活序**（= 逆初始化序）先执行 `OnShutdownAsync()`，再执行同步 `OnShutdown()`：

```csharp
public class BundleCacheService : ServiceBase, IAsyncShutdownService
{
    public async UniTask OnShutdownAsync()
    {
        await UnloadAllBundlesAsync(); // 异步卸载资源
    }

    protected override void OnShutdown() { /* 同步清理 */ }
}

// 异步关闭单个作用域
await GameServices.ShutdownContainerAsync(EServiceScopeKind.Gameplay);

// 异步关闭全部作用域
await GameServices.ShutdownAsync();
```

### 运行时调试

在运行时调试器（DebuggerComp）的 Service System 窗口中可查看已注册服务的接口、实现、作用域、优先级与 Tick 接口实现情况（数据来自 `GameServices.GetDiagnosticInfo()`），以及各作用域的活跃状态（`HasApp` / `HasScene` / `HasGameplay`）。

编辑器与开发构建还会统计每个服务的轮询耗时——均值 `PollAvgMs` / 峰值 `PollPeakMs` / 采样数 `PollSamples` 随诊断信息一并返回；调用 `GameServices.ResetPollStatistics()` 可清零统计窗口。发布构建不采集（零开销）。

## 注意事项

- `GameServices` 仅允许主线程调用（编辑器/开发构建由断言强制，发布构建不参与编译）；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Post`/`Send` 切回主线程。隔离世界（`new ServiceWorld()`）不做该断言，可跨线程用于并行测试。
- 消费政策三车道（详见「ProviderHandlerHost 服务架构」）：游戏业务代码走门面静态 API（如 `AudioService.Play(...)`、`UIService.ShowUI<T>()`）；框架服务互调走 `Context` 契约注入；动态服务查找统一走 `GameServices.GetRequiredService<T>()` / `TryGetService<T>()`。
- `GameServices.GetRequiredService<T>()` 未注册时抛出 `GameException`；`GetService<T>()` 返回 null；`TryGetService<T>()` 返回 bool。
- 同一作用域重复注册同契约幂等跳过（返回既有实例），嵌套依赖链重复注册免疫；以不同实例抢占已占用契约按 `DuplicateContractPolicy` 处置（开发默认告警、发布静默、可配 Throw）。注意 `Warn`/`Skip` 下**被丢弃的那个实例不会被框架关闭**——它从未入图，框架无从接管其生命周期。
- 单个服务在轮询中抛异常：编辑器与开发构建记录后立即上抛（fail-fast）；发布构建记录后隔离续跑，不影响同帧其他服务。同一服务在同一轮询类别连续失败达到阈值（默认 300）即被摘除出该轮询列表并汇总告警一次（熔断）；服务条目保留、重新注册即完全重置。阈值挂在世界上（`ServiceWorld.TickFailureTripThreshold`，框架内部可调，不对外暴露），隔离世界各持一份、互不污染。
- 服务必须派生 `ServiceBase` 或 `ServiceMono<TScope>`：容器经 `IServiceLifecycle` 驱动并读取状态，裸实现 `IService` 的类型在注册时即被拒（其状态永远读不到 `Initialized`，声明它为依赖的服务会误报"依赖未就绪"）。
- 循环依赖在世界初始化的拓扑排序期抛 `GameException`（错误消息含环成员）；注册期不做环检测（两阶段下注册顺序与依赖链无关）。
- 初始化进行中（`ServiceWorld.IsInitializing`）不得注销服务或关闭作用域，两者均抛 `GameException`；等 `Initialize`/`InitializeAsync` 返回后再做。
- MonoBehaviour 服务不可实现 `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` / `IServiceGizmoDrawable`——使用 Unity 自身的对应生命周期。
- 编辑器下退出 Play 模式时 `GameApp` 会自动调用 `GameServices.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回文档索引](Index.md) · [主 README](../README.md) · [Timer](Timer.md) · [GameApp](GameApp.md) · [Singleton](Singleton.md)
