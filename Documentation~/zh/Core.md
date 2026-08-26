# Core 服务系统（@Service）

> 框架的服务化基座：以统一服务世界（`ServiceWorld`）管理所有子服务的构造、生命周期、轮询与作用域，并由 `GameApp`（MonoBehaviour）驱动。

`@Service` 是整个框架的服务基础设施。所有功能服务（资源、UI、音频、计时器等）均为继承 `ServiceBase` 的普通 C# 类，依赖通过 `[ServiceDependency(typeof(...))]` 特性声明，由 `GameServices.RegisterService<T>(scope, service)` 统一注册并递归预注册依赖链（零反射）；非服务代码通过各服务的静态门面访问（如 `AudioService.Xxx()`、`UIService.Xxx()`、`ResourceService.Xxx()`），动态服务查找走 `GameApp.Services`（`IServiceProvider`）。服务支持 App/Scene/Gameplay 三级作用域，跨作用域通过 `ContractBindings` 值类型 struct 实现 O(1) 查找（Gameplay > Scene > App 优先级），场景卸载时自动清理场景与玩法级服务。

## 核心特性

- **统一服务世界**：`ServiceWorld` 持有 3-slot 固定数组（App/Scene/Gameplay），通过 `ContractBindings` 值类型 struct 实现 O(1) 跨作用域查找，无需父链遍历
- **特性声明依赖**：`[ServiceDependency(typeof(DepA), typeof(DepB))]` 单特性声明多依赖，编译期由 `ServiceDependencyAnalyzer`（MIRAI002/MIRAI003）校验类型实现 `IService`
- **递归预注册**：`RegisterWithDependencies` 按 [ServiceDependency] 声明序递归注册依赖（防重复 s_Registered 分桶表 + 防循环 s_InFlight 栈 fail-fast），被依赖服务先创建、先初始化
- **HandlerHost 静态门面**：12 个框架服务均为 `[HandlerHost] XxxService : ServiceBase` 静态门面 + 可序列化 `XxxHandler` 后端 + `XxxSettings`（`[SerializeReference]` + `[ProviderDropdown]`）选择后端实现
- **三级作用域**（`EServiceScopeKind.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 优先级查找
- **生命周期接口按需实现**：`IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable`、`IServiceGizmoDrawable`
- **`Priority` 优先级**控制轮询顺序（高优先先轮询、后关闭）
- **异步初始化**：实现 `IAsyncInitService` 的服务在注册后由容器驱动 `OnInitAsync()`
- **异步关闭**：实现 `IAsyncShutdownService` 的服务在 `ShutdownContainerAsync()` / `ShutdownAsync()` 中按逆注册序先异步关闭
- **运行时服务注册**：`GameServices.RegisterService<T>()` / `UnregisterService<T>()` 动态增删单个服务；显式契约重载 `RegisterService(scope, Type, instance)` 支持接口契约与同实例多契约绑定；迭代中调用默认延迟到本轮结束后执行（`EDeferMode.Defer`）
- **自注册 Mono 服务**：`ServiceMono<TScope>` 在 Awake 中自动注册、OnDestroy 中自动注销
- **作用域优先级常量**：`ServiceScopeOrder` 显式定义 App/Scene/Gameplay 排序优先级
- **服务事件**：`onServiceRegistered`/`onServiceUnregistered` 事件支持热替换通知
- **迭代安全**：轮询期间的注册/注销操作延迟到本轮结束后统一应用；轮询中请求的作用域销毁也延迟执行
- **Tick 异常分级策略**：编辑器与开发构建下记录后立即上抛（fail-fast，第一时间暴露缺陷）；发布构建下记录后隔离续跑（单服务故障不拖垮整帧）
- **主线程亲和守卫**：编辑器与开发构建下断言调用线程，发布版零开销
- **生命周期状态机**：每个服务跟踪 `EServiceState`（Created → Initialized → ShuttingDown → Disposed），关闭幂等
- **MonoBehaviour Tick 约束**：MonoBehaviour 服务不可实现 `IServiceTickable` 等 Tick 接口，应使用 Unity 自身的 Update/FixedUpdate/LateUpdate
- **服务基类内置查找**：`ServiceBase` / `ServiceMono<TScope>` 提供 `Require<T>()`、`TryGet<T>()`、`RequireApp<T>()`、`RequireScene<T>()`、`RequireGameplay<T>()` 等 protected 方法，无需注入 `IServiceProvider` 即可运行时查找依赖

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `IService` | 服务核心契约：`Priority`、`Scope`、`OnInit()`、`Shutdown()` |
| `ServiceBase` | 纯 C# 服务抽象基类；依赖通过 `[ServiceDependency]` 特性声明，由注册器递归预注册；内置 `Require<T>()` / `TryGet<T>()` / `RequireApp<T>()` 等运行时查找方法 |
| `ServiceMono<TScope>` | MonoBehaviour 服务基类（泛型作用域标记），Awake 自动注册、OnDestroy 自动注销；同样内置运行时查找方法 |
| `IServiceProvider` | 服务访问统一入口：`GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>()` / `GetRequiredServiceInScope<T>(scope)` / `TryGetServiceInScope<T>(scope)` |
| `ServiceWorld` | 统一服务世界：3-slot 固定作用域数组 + `ContractBindings` 值类型 struct O(1) 跨作用域查找；实现 `IServiceProvider` |
| `ServiceScope` | 单作用域注册表、轮询列表与迭代安全机制；注册/注销时同步 `ServiceWorld` 的 `ContractBindings` |
| `GameServices` | 静态门面：统一注册入口 `RegisterService<T>(scope, service, deferMode)` 与显式契约重载 `RegisterService(scope, Type, instance)`、注销、作用域管理（`ShutdownContainer`/`HasApp`/`HasScene`/`HasGameplay`）、默认工厂扩展点 `RegisterDefaultFactory`、轮询驱动、拦截器 |
| `ServiceDependencyAttribute` | 依赖声明特性：`[ServiceDependency(typeof(DepA), typeof(DepB))]`，声明顺序即依赖注册顺序；编译期 MIRAI002/MIRAI003 校验 |
| `EServiceScopeKind` | 服务作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `EServiceState` | 服务生命周期状态：`Created`、`Initialized`、`ShuttingDown`、`Disposed`（`ServiceBase.State` 属性） |
| `EDeferMode` | 迭代中注册/注销的延迟策略：`Defer`（延迟到本轮结束，默认）/ `Throw`（立即抛异常） |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | 轮询接口，方法签名 `Tick(float elapseSeconds, float realElapseSeconds)` 等（MonoBehaviour 服务不可实现） |
| `IServiceGizmoDrawable` | 编辑器 Gizmos 绘制接口 `OnDrawGizmos()` |
| `IAsyncInitService` | 异步初始化接口，实现 `OnInitAsync()` 的服务在注册后由容器驱动 |
| `IAsyncShutdownService` | 异步关闭接口，实现 `OnShutdownAsync()` 的服务在 `ShutdownContainerAsync()` 中按逆注册序异步关闭 |
| `FrameworkHandler` | 处理器基类（`[Serializable]`）：幂等 `Internal_Init`/`Internal_Shutdown` + 同步/异步生命周期回调；所有 XxxHandler 的基类 |
| `ServiceScopeOrder` | 作用域优先级常量（App=-10000, Scene=-5000, Gameplay=0） |
| `GameApp` | MonoBehaviour 入口（`[DefaultExecutionOrder(-1000)]`）；驱动 `GameAppSettings.Initiation` 与 `GameServices.Shutdown`；`GameApp.Services` 仅用于动态服务查找 |
| `GameAppMessageEvent` / `EMessageEventType` | 命名空间 `Moirai.Atropos.Events`，框架级池化事件（对焦/失焦/退出、SDK 回调） |

## 快速上手

```csharp
// 1. 业务代码通过静态门面访问框架服务
TimerService.AddTimer(() => Debug.Log("1s"), 1f);
UIService.ShowUI<MainWindow>();
ResourceService.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// 2. 定义自定义服务——依赖通过 [ServiceDependency] 特性声明
[ServiceDependency(typeof(TimerService))]
public class MyService : ServiceBase, IServiceTickable
{
    public override int Priority => 10;              // 高优先级先轮询

    public override void OnInit()
    {
        TimerService.AddTimer(() => { /* 依赖已就绪，直接使用静态门面 */ }, 1f);
    }

    public override void Shutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 3. 注册到指定作用域——依赖链自动递归预注册（顺序无关）
GameServices.RegisterService(EServiceScopeKind.Gameplay, new MyService());

// 4. 关闭——服务按逆注册序（依赖方先）关闭
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## 进阶用法

### 生命周期与作用域

- `GameServices.RegisterService<T>(scope, service)` 统一注册入口：先按 `[ServiceDependency]` 声明序递归预注册依赖（依赖实例由默认工厂表创建，宿主可用 `RegisterDefaultFactory` 扩展），再注册当前服务并立即驱动 `OnInit()`；被依赖服务先于依赖方初始化。依赖声明始终从实现类型读取——以接口为契约注册时依赖链同样自动装配。
- `GameServices.Shutdown()` 按 Gameplay → Scene → App 逆序关闭全部作用域；`GameServices.ShutdownContainer(scope)` 只关闭指定作用域。
- `GameApp` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域。
- 同一契约可在不同作用域注册不同实现，`IServiceProvider` 查找顺序为 Gameplay > Scene > App（`ContractBindings.TryGetBest()`），可用于战斗内临时替换全局实现。
- 注册幂等：同一作用域重复注册同契约直接跳过（返回既有实例）；循环依赖在注册期即抛 `GameException`（fail-fast）。

### HandlerHost 服务架构

框架的 12 个内置服务（UpdateDriver/Resource/Debugger/Audio/GameObjectPool/Procedure/Localization/Scene/Timer/Save/UI/Input）统一采用三层结构：

| 层 | 形态 | 职责 |
|------|------|------|
| `XxxService : ServiceBase` | 静态门面，标记 `[HandlerHost(typeof(XxxHandler))]` + `[ServiceDependency(...)]` | 全部静态 API；`OnInit` 触发 Handler 懒加载，`Shutdown` 清空 Handler |
| `XxxHandler : FrameworkHandler` | 可序列化后端类 | 承载核心逻辑；替换后端无需改动调用方 |
| `XxxSettings : FrameworkSettings<XxxSettings>` | ScriptableObject 设置 | `[ProviderDropdown]` + `[SerializeReference]` 选择后端实现 |

业务代码一律调用静态门面（如 `AudioService.Play(...)`、`UIService.ShowUI<T>()`），不持有服务实例引用。自定义后端：继承 `XxxHandler` 覆写虚方法 → 在 `XxxSettings` 的 Provider 下拉框中切换。

### 生命周期状态机

每个服务通过 `ServiceBase.State`（`EServiceState`）跟踪生命周期状态：

| 状态 | 说明 |
|------|------|
| `Created` | 实例已创建并注册，尚未初始化 |
| `Initialized` | `OnInit()` 已调用，服务运行中 |
| `ShuttingDown` | 正在调用 `Shutdown()` |
| `Disposed` | 服务已完全关闭并移除 |

关闭幂等：对已销毁的服务再次调用关闭不会重复执行。

### 依赖声明 [DEPENDENCY DECLARATION]

服务依赖通过 `[ServiceDependency(typeof(...))]` 特性声明（单特性多类型，类似 `RequireComponent`），注册器据此递归预注册——依赖未注册时优先注册依赖，再注册当前服务：

```csharp
[ServiceDependency(typeof(ResourceService), typeof(TimerService))]
public sealed class UIService : ServiceBase, IServiceTickable
{
    public override void OnInit()
    {
        // 走到此处时 ResourceService/TimerService 已初始化完毕
        TimerService.AddTimer(() => { }, 1f);
    }

    public override void Shutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}
```

- 声明顺序即依赖注册顺序；所有依赖类型必须实现 `IService`，由 `ServiceDependencyAnalyzer`（MIRAI002/MIRAI003）在编译期校验
- 依赖实例由框架工厂表创建（零反射）；自定义服务的依赖若非框架内置类型，须先显式注册再注册依赖方
- 循环依赖在注册期即抛 `GameException`

需要运行时延迟解析时，使用 `ServiceBase` 内置的 `Require<T>()` / `TryGet<T>()`：

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        if (TryGet(out DebuggerService debugger)) // 可选依赖，未注册返回 false
        {
            debugger.Enable();
        }
    }
}
```

### 服务基类内置查找方法

`ServiceBase` 和 `ServiceMono<TScope>` 提供以下 `protected` 方法，无需注入 `IServiceProvider` 即可运行时查找依赖：

| 方法 | 说明 |
|------|------|
| `Require<T>()` | 跨作用域查找服务，未找到抛 `GameException`（Gameplay > Scene > App 优先级） |
| `TryGet<T>(out T)` | 跨作用域尝试查找服务，返回 bool |
| `RequireApp<T>()` | 仅在 App 作用域中查找，未找到抛 `GameException` |
| `RequireScene<T>()` | 仅在 Scene 作用域中查找，未找到抛 `GameException` |
| `RequireGameplay<T>()` | 仅在 Gameplay 作用域中查找，未找到抛 `GameException` |

### 组合根与内置服务注册

框架组合根极简：`GameAppSettings.InitializeAppServices()`（`AfterAssembliesLoaded` 阶段调用）仅显式注册流程链根服务：

```csharp
GameServices.RegisterService(EServiceScopeKind.App, new ProcedureService());
await ProcedureSettings.StartProcedure();
```

其余 11 个框架服务全部由 `[ServiceDependency]` 依赖链自动递归拉起——零反射、顺序无关、编译期类型安全。自定义服务的后端实现可在对应 `XxxSettings` 的 Inspector 中通过 Provider 下拉框替换。

### 异步初始化 [ASYNC INIT]

```csharp
public class MyResourceService : ServiceBase, IAsyncInitService
{
    public override void OnInit() { /* 同步快速设置 */ }

    public async UniTask OnInitAsync()
    {
        await LoadCatalogAsync(); // 注册后由容器驱动
    }
}
```

处理器（`XxxHandler : FrameworkHandler`）同样支持异步生命周期：覆写 `OnInitAsync()` / `OnShutdownAsync()`，由 `GameAppSettings.Initiation` 在同步初始化后显式驱动。

### 服务事件 [SERVICE EVENTS]

```csharp
GameServices.onServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

GameServices.onServiceUnregistered += (service) =>
{
    Debug.Log($"Service unregistered: {service.GetType().Name}");
};
```

### MonoBehaviour 服务 [MONO SERVICE]

继承 `ServiceMono<TScope>`（`TScope` 为作用域标记：`AppScope` / `SceneScope` / `GameplayScope`），`Awake` 自动注册到对应作用域，`OnDestroy` 自动注销：

```csharp
public class MyMonoService : ServiceMono<AppScope>
{
    public override void OnInit() { /* Awake 注册后自动调用 */ }
    public override void Shutdown() { /* OnDestroy 注销前自动调用 */ }

    protected override void Update() { /* Unity 自身生命周期驱动 */ }
    // AppScope 自动 DontDestroyOnLoad；SceneScope/GameplayScope 随场景卸载自然销毁
}

// 直接挂载到场景对象即可——无需手动注册
```

依赖查找使用基类内置的 `Require<T>()` / `TryGet<T>()` 系列方法；同契约重复注册自动销毁多余 GameObject（幂等）。

> **注意**：MonoBehaviour 服务不可实现 `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable`——由 Unity 自身的 `Update()` / `FixedUpdate()` / `LateUpdate()` 驱动。

### 服务拦截器 [SERVICE INTERCEPTORS (AOP)]

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100;

    public void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds)
    {
        // 每次 Tick 前性能采样
    }

    public void OnServiceShutdown(IService service)
    {
        // Shutdown 前清理
    }
}

GameServices.AddInterceptor(new ProfilingInterceptor());
```

五个拦截点，默认空实现：

| 方法 | 时机 |
|------|------|
| `OnServiceRegistering` | `OnInit()` 前——可抛异常拒绝注册 |
| `OnServiceRegistered` | `OnInit()` 后、状态切换为 `Initialized` 后 |
| `OnServiceUnregistered` | `Shutdown()` 已调用并从注册表移除后 |
| `OnServiceTick` | 每次 `Tick()` 调用前（仅 Update 路径） |
| `OnServiceShutdown` | `Shutdown()` 调用前 |

多个拦截器按 `Priority` 降序执行。`GameServices.Shutdown()` 时清空全部拦截器。

### AOT 安全的延迟解析

`Func<T>` 注入依赖 `MakeGenericMethod`，IL2CPP 下存在裁剪风险。框架服务查找全部走 `RuntimeTypeHandle` 键的 `ContractBindings` 值类型表——零反射、零装箱，天然 AOT 安全：

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        // 运行时延迟解析：泛型方法直接调用，无 MakeGenericMethod 路径
        var stats = Require<StatsService>();
    }
}
```

### 运行时服务注册 [RUNTIME SERVICE REGISTRATION]

动态增删单个服务（Mod 系统、DLC 热加载等场景）：

```csharp
// 运行时注册——立即驱动 OnInit，依赖链自动递归预注册
GameServices.RegisterService(EServiceScopeKind.Gameplay, new BuffService());

// 显式契约注册——以接口为契约键，依赖声明仍从实现类型读取
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), new BuffService());

// 多契约绑定——同一实例依次以多个契约注册，只初始化/关闭一次
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), buff);
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(BuffService), buff);

// 运行时注销——立即驱动 Shutdown；注销后可以全新实例重新注册同契约
GameServices.UnregisterService<BuffService>(EServiceScopeKind.Gameplay);
```

> 迭代中（Tick）调用默认延迟到本轮迭代结束后执行（`EDeferMode.Defer`）；传入 `EDeferMode.Throw` 则立即抛出异常（fail-fast）。RegisterService 的契约类型 = `typeof(T)` 具体类型，解析也必须用同一类型 `GetRequiredService<T>()`。

### 默认工厂扩展点 [DEFAULT FACTORY EXTENSION]

框架内置服务的默认工厂表位于 `GameServices.Factories.cs`。宿主工程可为自有服务贡献工厂，
使 `[ServiceDependency]` 依赖链跨程序集自动装配：

```csharp
// 贡献默认工厂（重复注册同类型会 fail-fast 抛 GameException）
GameServices.RegisterDefaultFactory(typeof(QuestService), static () => new QuestService());

// 之后任何服务声明 [ServiceDependency(typeof(QuestService))] 即可自动预注册
```

### 异步关闭 [ASYNC SHUTDOWN]

实现 `IAsyncShutdownService` 的服务在异步关闭管线中按逆注册序先执行 `OnShutdownAsync()`，再执行同步 `Shutdown()`：

```csharp
public class ResourceService : ServiceBase, IAsyncShutdownService
{
    public async UniTask OnShutdownAsync()
    {
        await UnloadAllAssetsAsync(); // 异步卸载资源
    }

    public override void Shutdown() { /* 同步清理 */ }
}

// 异步关闭单个作用域
await GameServices.ShutdownContainerAsync(EServiceScopeKind.Gameplay);

// 异步关闭全部作用域
await GameServices.ShutdownAsync();
```

### 运行时调试

在运行时调试器（DebuggerComp）的 Service System 窗口中可查看已注册服务的接口、实现、作用域、优先级与 Tick 接口实现情况（数据来自 `GameServices.GetDiagnosticInfo()`），以及各作用域的活跃状态（`HasApp` / `HasScene` / `HasGameplay`）。

## 注意事项

- `GameServices` 与 `IServiceProvider` 仅允许主线程调用；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Post`/`Send` 切回主线程。
- 业务代码一律通过静态门面访问框架服务（如 `AudioService.Play(...)`、`UIService.ShowUI<T>()`）；`GameApp.Services`（`IServiceProvider`）仅用于 Gameplay/Scene 作用域动态服务查找。
- `GetRequiredService<T>()` 未注册时抛出 `GameException`；`GetService<T>()` 返回 null；`TryGetService<T>()` 返回 bool。
- 同一作用域重复注册同契约幂等跳过（返回既有实例），嵌套依赖链重复注册免疫。
- 单个服务在轮询中抛异常：编辑器与开发构建记录后立即上抛（fail-fast）；发布构建记录后隔离续跑，不影响同帧其他服务。
- 循环依赖在 `RegisterWithDependencies` 注册期即被检测并抛出异常（fail-fast）。
- MonoBehaviour 服务不可实现 `IServiceTickable` 等 Tick 接口——使用 Unity 自身的 Update 生命周期。
- 编辑器下退出 Play 模式时 `GameApp` 会自动调用 `GameServices.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回主 README](../../README.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)

---

## 处理器宿主（HandlerHost）

> 通过 `[HandlerHost]` 特性 + 源生成器，为静态工具类自动生成线程安全的 Handler 属性与懒加载机制。

框架的 7 个工具门面（`LogUtility`、`SettingUtility`、`VersionUtility`、`JsonUtility`、`ObjectUtility`、`StringUtility`、`TweenUtility`）均采用统一的处理器宿主模式：

- **`[HandlerHost(typeof(XxxHandler))]`** 标记 `static partial class`，源生成器自动生成 `Handler` 属性（`volatile` + `Interlocked` 线程安全 get/set）
- **`FrameworkHandler`** 是所有处理器抽象基类的统一基类，提供 `Internal_Init()` / `Internal_Shutdown()` 幂等生命周期和 `OnInit()` / `OnShutdown()` 虚方法回调
- 用户在 partial 类中提供 `private static XxxHandler CreateDefaultHandler()` 工厂方法，首次访问 `Handler` 时自动调用
- 未提供 `CreateDefaultHandler` 时，编译器报 **MIRAI001** 警告（IDE 提供快速修复生成空方法）；`Handler.get` 在未显式设置时抛出 `InvalidOperationException`（运行时托底）
- `Handler` 赋值 `null` 抛出 `ArgumentNullException`（fail-fast）
- `s_Handler` 字段为 `private`，partial 同类可直接访问

### 使用方式

```csharp
[HandlerHost(typeof(LogHandler))]
public static partial class LogUtility
{
    private static LogHandler CreateDefaultHandler()
    {
#if ZLOGGER_INSTALLED
        return new ZLoggerHandler();
#else
        return new DefaultLogHandler();
#endif
    }

    // ... facade 方法通过 Handler 调用
    public static void Info(string msg) => Handler.Log(/* ... */);
}
```

### 源生成器输出

源生成器为每个标记 `[HandlerHost]` 的类生成 `{ClassName}.g.cs`，包含：

| 成员 | 说明 |
|------|------|
| `s_Handler` | `private static volatile` 处理器字段 |
| `s_DefaultFactory` | `private static Func<T>` = `CreateDefaultHandler`（方法存在时生成） |
| `Handler` | `public static` 属性：get 懒加载（Interlocked），set 替换并关闭旧处理器 |
| `Handler.set` | 初始化新处理器 → `Interlocked.Exchange` → 旧处理器 `Internal_Shutdown()` |

### 处理器继承体系

```
FrameworkHandler (abstract)
├── OnInit() / OnShutdown()  — 虚方法回调
├── Internal_Init() / Internal_Shutdown()  — 幂等生命周期入口
├── LogHandler : FrameworkHandler
├── SettingHandler : FrameworkHandler
├── VersionHandler : FrameworkHandler
├── JsonHandler : FrameworkHandler
├── ObjectHandler : FrameworkHandler
├── StringHandler : FrameworkHandler
├── TweenHandler : FrameworkHandler  (重写 Internal_Init 注册 TweenManager)
└── InputHandler : FrameworkHandler
```
